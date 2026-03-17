using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// HTTP-based upscaler service that communicates with the AI Upscaler Docker container.
    /// v1.5.2.8 - Health caching, retry logic, multi-GPU support.
    /// </summary>
    public class HttpUpscalerService : IDisposable
    {
        private readonly IHttpClientFactory? _httpClientFactory;
        private readonly HttpClient _fallbackClient;
        private readonly ILogger<HttpUpscalerService> _logger;
        private bool _disposed;

        // Health check cache (30 seconds) with thread-safety lock
        private bool? _cachedHealthResult;
        private DateTime _healthCacheExpiry = DateTime.MinValue;
        private static readonly TimeSpan HealthCacheDuration = TimeSpan.FromSeconds(30);
        private readonly object _healthLock = new();

        public HttpUpscalerService(ILogger<HttpUpscalerService> logger, IHttpClientFactory? httpClientFactory = null)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;

            // Fallback HttpClient if IHttpClientFactory is not available
            _fallbackClient = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5), // DNS refresh
                MaxConnectionsPerServer = 10
            })
            {
                Timeout = TimeSpan.FromMinutes(5)
            };

            _logger.LogInformation("HttpUpscalerService v1.5.2.8 initialized");
        }

        private HttpClient GetClient()
        {
            if (_httpClientFactory != null)
            {
                return _httpClientFactory.CreateClient("AiUpscaler");
            }
            return _fallbackClient;
        }

        private string GetServiceUrl()
        {
            var config = Plugin.Instance?.Configuration;
            var url = config?.AiServiceUrl ?? "http://localhost:5000";
            return url.TrimEnd('/');
        }

        /// <summary>
        /// Check if the AI service is available and healthy.
        /// </summary>
        public async Task<bool> IsServiceAvailableAsync(CancellationToken cancellationToken = default)
        {
            // Return cached result if still valid (thread-safe read)
            lock (_healthLock)
            {
                if (_cachedHealthResult.HasValue && DateTime.UtcNow < _healthCacheExpiry)
                {
                    return _cachedHealthResult.Value;
                }
            }

            var baseUrl = GetServiceUrl();
            bool result = false;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                var response = await GetClient().GetAsync($"{baseUrl}/health", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("AI Service health check OK at {Url}", baseUrl);
                    result = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("AI Service health check failed at {Url}: {Message}", baseUrl, ex.Message);
            }

            lock (_healthLock)
            {
                _cachedHealthResult = result;
                _healthCacheExpiry = DateTime.UtcNow + HealthCacheDuration;
            }
            return result;
        }

        /// <summary>
        /// Get the current status of the AI service.
        /// </summary>
        public async Task<ServiceStatus?> GetServiceStatusAsync(CancellationToken cancellationToken = default)
        {
            var baseUrl = GetServiceUrl();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                var response = await GetClient().GetAsync($"{baseUrl}/status", cts.Token);
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

        /// <summary>
        /// Upscale an image using the AI service.
        /// </summary>
        public async Task<byte[]?> UpscaleImageAsync(byte[] imageData, int scale = 2, CancellationToken cancellationToken = default)
        {
            if (imageData == null || imageData.Length == 0)
            {
                _logger.LogWarning("UpscaleImageAsync called with empty image data");
                return null;
            }

            var baseUrl = GetServiceUrl();
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

                    var response = await GetClient().PostAsync($"{baseUrl}/upscale", content, cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                        _logger.LogDebug("Received upscaled image ({Size} bytes)", result.Length);
                        return result;
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogError("AI service upscaling failed: {StatusCode} - {Error}", response.StatusCode, error);
                        // Don't retry on 4xx client errors
                        if ((int)response.StatusCode < 500) break;
                    }
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning("Upscaling request was cancelled");
                    break; // Don't retry on cancellation
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
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }

            return null;
        }

        /// <summary>
        /// Request the AI service to download a model.
        /// </summary>
        public async Task<bool> DownloadModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            var baseUrl = GetServiceUrl();
            try
            {
                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(modelName), "model_name");
                var response = await GetClient().PostAsync($"{baseUrl}/models/download", content, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download model {Model}", modelName);
                return false;
            }
        }

        /// <summary>
        /// Request the AI service to load a model.
        /// </summary>
        public async Task<bool> LoadModelAsync(string modelName, bool useGpu = true, int gpuDeviceId = 0, CancellationToken cancellationToken = default)
        {
            var baseUrl = GetServiceUrl();
            try
            {
                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(modelName), "model_name");
                content.Add(new StringContent(useGpu.ToString().ToLower()), "use_gpu");
                content.Add(new StringContent(gpuDeviceId.ToString()), "gpu_device_id");
                var response = await GetClient().PostAsync($"{baseUrl}/models/load", content, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load model {Model}", modelName);
                return false;
            }
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

    /// <summary>
    /// AI service status response model.
    /// </summary>
    public class ServiceStatus
    {
        public string Status { get; set; } = string.Empty;
        public string? CurrentModel { get; set; }
        public string[] AvailableProviders { get; set; } = Array.Empty<string>();
        public bool UsingGpu { get; set; }
        public string[] LoadedModels { get; set; } = Array.Empty<string>();
        public int ProcessingCount { get; set; }
        public int MaxConcurrent { get; set; }
    }
}
