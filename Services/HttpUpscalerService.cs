using System;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Thin facade composing four single-responsibility services:
    /// <see cref="IServiceUrlProvider"/>, <see cref="IUpscalerHttpClient"/>,
    /// <see cref="IServiceHealthMonitor"/>, and <see cref="IModelLifecycleManager"/>.
    /// Public API is preserved so existing callers (VideoProcessor, UpscalerCore,
    /// HardwareBenchmarkService, scheduled tasks, ModelManager) keep compiling.
    /// </summary>
    public class HttpUpscalerService : IDisposable
    {
        private readonly IServiceUrlProvider _urls;
        private readonly IUpscalerHttpClient _http;
        private readonly IServiceHealthMonitor _health;
        private readonly IModelLifecycleManager _lifecycle;
        private readonly UpscalerHttpClient? _ownedHttpClient;
        private volatile bool _disposed;

        /// <summary>
        /// Backward-compatible constructor used by tests and historical DI bootstrap.
        /// Internally builds the four collaborators; safe to keep using.
        /// </summary>
        public HttpUpscalerService(ILogger<HttpUpscalerService> logger, IHttpClientFactory? httpClientFactory = null)
        {
            var urlProvider = new ConfigUrlProvider(NullLogger<ConfigUrlProvider>.Instance);
            var httpClient = new UpscalerHttpClient(NullLogger<UpscalerHttpClient>.Instance, httpClientFactory);
            var monitor = new CachedHealthMonitor(NullLogger<CachedHealthMonitor>.Instance, httpClient, urlProvider);
            var lifecycle = new SingleModelLifecycleManager(NullLogger<SingleModelLifecycleManager>.Instance, httpClient, urlProvider);

            _urls = urlProvider;
            _http = httpClient;
            _ownedHttpClient = httpClient;
            _health = monitor;
            _lifecycle = lifecycle;

            logger.LogInformation("HttpUpscalerService initialized (composed)");
        }

        /// <summary>
        /// DI-friendly constructor. Used by <see cref="PluginServiceRegistrator"/>
        /// when all four collaborators are registered separately.
        /// </summary>
        public HttpUpscalerService(ILogger<HttpUpscalerService> logger,
                                   IServiceUrlProvider urls,
                                   IUpscalerHttpClient http,
                                   IServiceHealthMonitor health,
                                   IModelLifecycleManager lifecycle)
        {
            _urls = urls;
            _http = http;
            _health = health;
            _lifecycle = lifecycle;
            _ownedHttpClient = null;

            logger.LogInformation("HttpUpscalerService initialized (DI-injected)");
        }

        public Task<bool> IsServiceAvailableAsync(CancellationToken ct = default)
            => _health.IsServiceAvailableAsync(ct);

        public void InvalidateHealthCache()
            => _health.InvalidateCache();

        public Task<ServiceStatus?> GetServiceStatusAsync(CancellationToken ct = default)
            => _http.GetServiceStatusAsync(_urls.GetServiceUrl(), ct);

        public Task<bool> EnsureModelLoadedAsync(string modelName, CancellationToken ct = default)
            => _lifecycle.EnsureModelLoadedAsync(modelName, ct);

        public Task<byte[]?> UpscaleImageAsync(byte[] imageData, int scale = 2, CancellationToken ct = default)
            => _http.UpscaleImageAsync(_urls.GetServiceUrl(), imageData, scale, ct);

        public Task<bool> DownloadModelAsync(string modelName, CancellationToken ct = default)
            => _http.DownloadModelAsync(_urls.GetServiceUrl(), modelName, ct);

        public Task<bool> LoadModelAsync(string modelName, bool useGpu = true, int gpuDeviceId = 0, CancellationToken ct = default)
            => _http.LoadModelAsync(_urls.GetServiceUrl(), modelName, useGpu, gpuDeviceId, ct);

        public void Dispose()
        {
            if (!_disposed)
            {
                _ownedHttpClient?.Dispose();
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
        [JsonPropertyName("current_model")]
        public string? CurrentModel { get; set; }
        [JsonPropertyName("available_providers")]
        public string[] AvailableProviders { get; set; } = Array.Empty<string>();

        [JsonPropertyName("using_gpu")]
        public bool UsingGpu { get; set; }

        [JsonPropertyName("loaded_models")]
        public string[] LoadedModels { get; set; } = Array.Empty<string>();

        [JsonPropertyName("processing_count")]
        public int ProcessingCount { get; set; }

        [JsonPropertyName("max_concurrent")]
        public int MaxConcurrent { get; set; }

        [JsonPropertyName("input_frames")]
        public int InputFrames { get; set; } = 1;
    }
}
