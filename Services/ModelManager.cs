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
    /// </summary>
    public class ModelManager
    {
        private readonly ILogger<ModelManager> _logger;
        private readonly IApplicationPaths _appPaths;
        private readonly HttpClient _httpClient;
        
        // Base URL for model downloads (Placeholder - needs a real repo or release URL)
        // Using a reliable source or a placeholder that the user can configure/override is best.
        // For now, I will use a placeholder URL that would need to be updated with the actual hosting location.
        private const string ModelBaseUrl = "https://github.com/Kuscheltier/JellyfinUpscalerPlugin/releases/download/models-v1.0/"; 
        
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
            _logger.LogInformation($"Model {modelName} not found locally. Attempting download...");
            var downloaded = await DownloadModelAsync(modelName, filePath, cancellationToken);
            
            return downloaded ? filePath : null;
        }

        public async Task<bool> IsModelAvailableAsync(string modelName)
        {
             var filePath = Path.Combine(_modelsPath, $"{modelName}.onnx");
             return File.Exists(filePath);
        }

        public async Task<bool> DownloadModelAsync(string modelName, string destinationPath, CancellationToken cancellationToken)
        {
            try
            {
                var url = $"{ModelBaseUrl}{modelName}.onnx";
                _logger.LogInformation($"Downloading model {modelName} from {url}...");

                // Ensure directory exists
                var dir = Path.GetDirectoryName(destinationPath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError($"Failed to download model {modelName}. Status: {response.StatusCode}");
                        return false;
                    }

                    using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await stream.CopyToAsync(fileStream, cancellationToken);
                    }
                }

                _logger.LogInformation($"Successfully downloaded model {modelName} to {destinationPath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,($"Failed to download model {modelName}"));
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
    }
}
