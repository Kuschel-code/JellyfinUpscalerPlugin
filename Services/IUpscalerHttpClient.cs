using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Low-level HTTP communication with the AI service. Owns retry/back-off
    /// policy and JPEG/JSON marshalling. Caches no state.
    /// </summary>
    public interface IUpscalerHttpClient
    {
        HttpClient GetClient();
        Task<bool> CheckHealthAsync(string baseUrl, CancellationToken ct);
        Task<ServiceStatus?> GetServiceStatusAsync(string baseUrl, CancellationToken ct);
        Task<byte[]?> UpscaleImageAsync(string baseUrl, byte[] imageData, int scale, CancellationToken ct);
        Task<bool> DownloadModelAsync(string baseUrl, string modelName, CancellationToken ct);
        Task<bool> LoadModelAsync(string baseUrl, string modelName, bool useGpu, int gpuDeviceId, CancellationToken ct);
    }

    public class UpscalerHttpClient : IUpscalerHttpClient, IDisposable
    {
        private readonly IHttpClientFactory? _httpClientFactory;
        private readonly HttpClient _fallbackClient;
        private readonly ILogger<UpscalerHttpClient> _logger;
        private volatile bool _disposed;

        public UpscalerHttpClient(ILogger<UpscalerHttpClient> logger, IHttpClientFactory? httpClientFactory = null)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _fallbackClient = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 10
            })
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
        }

        public HttpClient GetClient()
            => _httpClientFactory != null ? _httpClientFactory.CreateClient("AiUpscaler") : _fallbackClient;

        public async Task<bool> CheckHealthAsync(string baseUrl, CancellationToken ct)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await GetClient().GetAsync($"{baseUrl}/health", cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("AI Service health check failed at {Url}: {Message}", baseUrl, ex.Message);
                return false;
            }
        }

        public async Task<ServiceStatus?> GetServiceStatusAsync(string baseUrl, CancellationToken ct)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await GetClient().GetAsync($"{baseUrl}/status", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cts.Token);
                    return JsonSerializer.Deserialize<ServiceStatus>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get AI service status from {Url}", baseUrl);
            }
            return null;
        }

        public async Task<byte[]?> UpscaleImageAsync(string baseUrl, byte[] imageData, int scale, CancellationToken ct)
        {
            if (imageData == null || imageData.Length == 0)
            {
                _logger.LogWarning("UpscaleImageAsync called with empty image data");
                return null;
            }

            const int maxRetries = 2;
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using var content = new MultipartFormDataContent();
                    using var imageContent = new ByteArrayContent(imageData);
                    imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    content.Add(imageContent, "file", "frame.png");
                    content.Add(new StringContent(scale.ToString()), "scale");

                    if (attempt == 0)
                    {
                        _logger.LogDebug("Sending image ({Size} bytes) to AI service for {Scale}x upscaling", imageData.Length, scale);
                    }
                    else
                    {
                        _logger.LogDebug("Retry {Attempt}/{MaxRetries} for upscaling", attempt, maxRetries);
                    }

                    using var response = await GetClient().PostAsync($"{baseUrl}/upscale", content, ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadAsByteArrayAsync(ct);
                        _logger.LogDebug("Received upscaled image ({Size} bytes)", result.Length);
                        return result;
                    }

                    var error = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogError("AI service upscaling failed: {StatusCode} - {Error}", response.StatusCode, error);
                    if ((int)response.StatusCode < 500) break;
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning("Upscaling request was cancelled");
                    break;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "HTTP error communicating with AI service at {Url} (attempt {Attempt})", baseUrl, attempt + 1);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error during upscaling");
                    break;
                }

                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1 << attempt), ct);
                }
            }
            return null;
        }

        public async Task<bool> DownloadModelAsync(string baseUrl, string modelName, CancellationToken ct)
            => await PostModelAsync(baseUrl, "/models/download", modelName, useGpu: null, gpuDeviceId: null, label: "download", ct);

        public async Task<bool> LoadModelAsync(string baseUrl, string modelName, bool useGpu, int gpuDeviceId, CancellationToken ct)
            => await PostModelAsync(baseUrl, "/models/load", modelName, useGpu, gpuDeviceId, label: "load", ct);

        private async Task<bool> PostModelAsync(string baseUrl, string path, string modelName, bool? useGpu, int? gpuDeviceId, string label, CancellationToken ct)
        {
            const int maxRetries = 1;
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using var content = new MultipartFormDataContent();
                    content.Add(new StringContent(modelName), "model_name");
                    if (useGpu.HasValue)     content.Add(new StringContent(useGpu.Value.ToString().ToLower()), "use_gpu");
                    if (gpuDeviceId.HasValue) content.Add(new StringContent(gpuDeviceId.Value.ToString()), "gpu_device_id");

                    using var response = await GetClient().PostAsync($"{baseUrl}{path}", content, ct);
                    if (response.IsSuccessStatusCode) return true;
                    if ((int)response.StatusCode < 500) return false;
                }
                catch (TaskCanceledException) { break; }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "Transient error during model {Label} {Model} (attempt {Attempt})", label, modelName, attempt + 1);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to {Label} model {Model}", label, modelName);
                    return false;
                }

                if (attempt < maxRetries)
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }

            _logger.LogError("All attempts to {Label} model {Model} failed", label, modelName);
            return false;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _fallbackClient?.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
