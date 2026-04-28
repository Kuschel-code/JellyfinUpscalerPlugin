using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Tracks the AI service's currently-loaded model and serializes
    /// switch operations via a SemaphoreSlim. Re-uses the existing model
    /// when possible to avoid redundant download + load roundtrips.
    /// </summary>
    public interface IModelLifecycleManager
    {
        Task<bool> EnsureModelLoadedAsync(string modelName, CancellationToken ct);
    }

    public class SingleModelLifecycleManager : IModelLifecycleManager
    {
        private readonly ILogger<SingleModelLifecycleManager> _logger;
        private readonly IUpscalerHttpClient _http;
        private readonly IServiceUrlProvider _urls;
        private readonly SemaphoreSlim _loadGate = new(1, 1);
        private volatile string? _currentModel;

        public SingleModelLifecycleManager(ILogger<SingleModelLifecycleManager> logger,
                                           IUpscalerHttpClient http,
                                           IServiceUrlProvider urls)
        {
            _logger = logger;
            _http = http;
            _urls = urls;
        }

        public async Task<bool> EnsureModelLoadedAsync(string modelName, CancellationToken ct)
        {
            if (string.Equals(_currentModel, modelName, StringComparison.Ordinal))
            {
                return true;
            }

            await _loadGate.WaitAsync(ct);
            try
            {
                if (string.Equals(_currentModel, modelName, StringComparison.Ordinal))
                {
                    return true;
                }

                var baseUrl = _urls.GetServiceUrl();

                try
                {
                    var status = await _http.GetServiceStatusAsync(baseUrl, ct);
                    if (status?.CurrentModel == modelName)
                    {
                        _currentModel = modelName;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not check service status, proceeding to load model");
                }

                _logger.LogInformation("Switching AI model to: {Model}", modelName);

                var downloaded = await _http.DownloadModelAsync(baseUrl, modelName, ct);
                if (!downloaded)
                {
                    _logger.LogWarning("Failed to download model {Model}, attempting load anyway", modelName);
                }

                var config = Plugin.Instance?.Configuration;
                var useGpu = config?.HardwareAcceleration ?? true;
                var gpuDeviceId = config?.GpuDeviceIndex ?? 0;

                var loaded = await _http.LoadModelAsync(baseUrl, modelName, useGpu, gpuDeviceId, ct);
                if (loaded)
                {
                    _currentModel = modelName;
                    _logger.LogInformation("Model {Model} loaded successfully", modelName);
                }
                else
                {
                    _logger.LogError("Failed to load model {Model}", modelName);
                }
                return loaded;
            }
            finally
            {
                _loadGate.Release();
            }
        }
    }
}
