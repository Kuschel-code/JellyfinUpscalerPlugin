using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Common.Configuration;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Manages AI models, including verification and downloading.
    /// Implements IDisposable to properly clean up HttpClient resources.
    /// </summary>
    public class ModelManager : IDisposable
    {
        private readonly ILogger<ModelManager> _logger;
        private readonly IApplicationPaths _appPaths;
        private readonly HttpClient _httpClient;
        private bool _disposed;
        
        // Default model download URL - can be overridden in plugin configuration
        private const string DefaultModelBaseUrl = "https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/models-v1.0/";
        
        private readonly string _modelsPath;
        private readonly ConcurrentDictionary<string, bool> _modelAvailability = new();

        public ModelManager(ILogger<ModelManager> logger, IApplicationPaths appPaths)
        {
            _logger = logger;
            _appPaths = appPaths;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(10); // Allow long downloads

            _modelsPath = Path.Combine(_appPaths.PluginConfigurationsPath, "JellyfinUpscaler", "models");
            if (!Directory.Exists(_modelsPath))
            {
                Directory.CreateDirectory(_modelsPath);
            }
        }

        /// <summary>
        /// Gets the configured model base URL from plugin settings, or uses the default.
        /// </summary>
        private string GetModelBaseUrl()
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config != null && !string.IsNullOrWhiteSpace(config.ModelDownloadUrl))
                {
                    return config.ModelDownloadUrl.TrimEnd('/') + "/";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read model URL from config, using default.");
            }
            return DefaultModelBaseUrl;
        }

        public string GetModelsPath()
        {
            return _modelsPath;
        }

        public async Task<string?> GetModelPathAsync(string modelName, CancellationToken cancellationToken = default)
        {
            var fileName = $"{modelName}.onnx";
            var filePath = Path.Combine(_modelsPath, fileName);

            if (File.Exists(filePath))
            {
                return filePath;
            }

            // If not found, attempt download
            _logger.LogInformation("Model {ModelName} not found locally. Attempting download...", modelName);
            var downloaded = await DownloadModelAsync(modelName, filePath, cancellationToken);
            
            return downloaded ? filePath : null;
        }

        public Task<bool> IsModelAvailableAsync(string modelName)
        {
             var filePath = Path.Combine(_modelsPath, $"{modelName}.onnx");
             return Task.FromResult(File.Exists(filePath));
        }

        public async Task<bool> DownloadModelAsync(string modelName, string destinationPath, CancellationToken cancellationToken)
        {
            try
            {
                var baseUrl = GetModelBaseUrl();
                var url = $"{baseUrl}{modelName}.onnx";
                _logger.LogInformation("Downloading model {ModelName} from {Url}...", modelName, url);

                // Ensure directory exists
                var dir = Path.GetDirectoryName(destinationPath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError("Failed to download model {ModelName}. Status: {StatusCode}", modelName, response.StatusCode);
                        return false;
                    }

                    using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await stream.CopyToAsync(fileStream, cancellationToken);
                    }
                }

                _logger.LogInformation("Successfully downloaded model {ModelName} to {DestinationPath}", modelName, destinationPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download model {ModelName}", modelName);
                // Clean up partial file
                if (File.Exists(destinationPath))
                {
                    try { File.Delete(destinationPath); } catch { }
                }
                return false;
            }
        }
        
        public IEnumerable<string> GetAvailableModels()
        {
            if (Directory.Exists(_modelsPath))
            {
                var files = Directory.GetFiles(_modelsPath, "*.onnx");
                foreach (var file in files)
                {
                    yield return Path.GetFileNameWithoutExtension(file);
                }
            }
        }

        /// <summary>
        /// Disposes the HttpClient to prevent memory leaks.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}

