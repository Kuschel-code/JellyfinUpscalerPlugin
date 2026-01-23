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
    /// This replaces local ONNX processing to avoid native DLL issues in Jellyfin plugins.
    /// </summary>
    public class HttpUpscalerService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<HttpUpscalerService> _logger;
        private readonly string _serviceUrl;
        private bool _disposed;

        public HttpUpscalerService(ILogger<HttpUpscalerService> logger)
        {
            _logger = logger;
            
            // Get service URL from plugin configuration or use default
            var config = Plugin.Instance?.Configuration;
            _serviceUrl = config?.AiServiceUrl ?? "http://localhost:5000";
            
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_serviceUrl),
                Timeout = TimeSpan.FromMinutes(5) // Long timeout for upscaling
            };
            
            _logger.LogInformation("HttpUpscalerService initialized with URL: {Url}", _serviceUrl);
        }

        /// <summary>
        /// Check if the AI service is available and healthy.
        /// </summary>
        public async Task<bool> IsServiceAvailableAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync("/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogDebug("AI Service health check: {Response}", content);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI Service health check failed");
            }
            return false;
        }

        /// <summary>
        /// Get the current status of the AI service.
        /// </summary>
        public async Task<ServiceStatus?> GetServiceStatusAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync("/status", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    return JsonSerializer.Deserialize<ServiceStatus>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get AI service status");
            }
            return null;
        }

        /// <summary>
        /// Upscale an image using the AI service.
        /// </summary>
        /// <param name="imageData">Raw image bytes (PNG, JPEG, etc.)</param>
        /// <param name="scale">Upscaling factor (2 or 4)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Upscaled image bytes or null on failure</returns>
        public async Task<byte[]?> UpscaleImageAsync(byte[] imageData, int scale = 2, CancellationToken cancellationToken = default)
        {
            if (imageData == null || imageData.Length == 0)
            {
                _logger.LogWarning("UpscaleImageAsync called with empty image data");
                return null;
            }

            try
            {
                using var content = new MultipartFormDataContent();
                
                // Add image file
                using var imageContent = new ByteArrayContent(imageData);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                content.Add(imageContent, "file", "frame.png");
                
                // Add scale parameter
                content.Add(new StringContent(scale.ToString()), "scale");

                _logger.LogDebug("Sending image ({Size} bytes) to AI service for {Scale}x upscaling", 
                    imageData.Length, scale);

                var response = await _httpClient.PostAsync("/upscale", content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    _logger.LogDebug("Received upscaled image ({Size} bytes)", result.Length);
                    return result;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("AI service upscaling failed: {StatusCode} - {Error}", 
                        response.StatusCode, error);
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Upscaling request was cancelled");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error communicating with AI service");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during upscaling");
            }

            return null;
        }

        /// <summary>
        /// Request the AI service to download a model.
        /// </summary>
        public async Task<bool> DownloadModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            try
            {
                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(modelName), "model_name");

                var response = await _httpClient.PostAsync("/models/download", content, cancellationToken);
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
        public async Task<bool> LoadModelAsync(string modelName, bool useGpu = true, CancellationToken cancellationToken = default)
        {
            try
            {
                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(modelName), "model_name");
                content.Add(new StringContent(useGpu.ToString().ToLower()), "use_gpu");

                var response = await _httpClient.PostAsync("/models/load", content, cancellationToken);
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
                _httpClient?.Dispose();
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
