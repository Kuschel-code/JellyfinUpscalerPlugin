using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Common.Configuration;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Model Manager - v1.5.2 Docker-based implementation
    /// Delegates model management to the external Docker AI service.
    /// </summary>
    public class ModelManager : IDisposable
    {
        private readonly ILogger<ModelManager> _logger;
        private readonly IApplicationPaths _appPaths;
        private bool _disposed;

        /// <summary>
        /// Available model configurations - matches Docker AI service.
        /// </summary>
        public static readonly Dictionary<string, ModelInfo> AvailableModels = new()
        {
            // === Real-ESRGAN (GPU-accelerated ONNX models) ===
            ["realesrgan-x4"] = new ModelInfo 
            { 
                Name = "Real-ESRGAN x4 (Best Quality)", 
                Scale = 4, 
                Description = "Best quality 4x for photos & anime (67MB ONNX, GPU-accelerated)"
            },
            ["realesrgan-x4-256"] = new ModelInfo 
            { 
                Name = "Real-ESRGAN x4 (Low VRAM)", 
                Scale = 4, 
                Description = "Optimized for 256px tiles, better for low VRAM GPUs"
            },
            
            // === Fast Models (OpenCV, CPU) ===
            ["fsrcnn-x2"] = new ModelInfo 
            { 
                Name = "FSRCNN x2 (Fast)", 
                Scale = 2, 
                Description = "Very fast 2x upscaling, good for real-time"
            },
            ["fsrcnn-x3"] = new ModelInfo 
            { 
                Name = "FSRCNN x3 (Fast)", 
                Scale = 3, 
                Description = "Fast 3x upscaling"
            },
            ["fsrcnn-x4"] = new ModelInfo 
            { 
                Name = "FSRCNN x4 (Fast)", 
                Scale = 4, 
                Description = "Fast 4x upscaling, lower quality but quick"
            },
            ["espcn-x2"] = new ModelInfo 
            { 
                Name = "ESPCN x2 (Fastest)", 
                Scale = 2, 
                Description = "Fastest model, minimal quality improvement"
            },
            ["espcn-x3"] = new ModelInfo 
            { 
                Name = "ESPCN x3 (Fastest)", 
                Scale = 3, 
                Description = "Fastest 3x model"
            },
            ["espcn-x4"] = new ModelInfo 
            { 
                Name = "ESPCN x4 (Fastest)", 
                Scale = 4, 
                Description = "Fastest 4x model"
            },
            
            // === Quality Models (OpenCV) ===
            ["lapsrn-x2"] = new ModelInfo 
            { 
                Name = "LapSRN x2 (Quality)", 
                Scale = 2, 
                Description = "Good quality 2x upscaling"
            },
            ["lapsrn-x4"] = new ModelInfo 
            { 
                Name = "LapSRN x4 (Quality)", 
                Scale = 4, 
                Description = "Good quality 4x upscaling"
            },
            ["lapsrn-x8"] = new ModelInfo 
            { 
                Name = "LapSRN x8 (Quality)", 
                Scale = 8, 
                Description = "Extreme 8x upscaling"
            },
            ["edsr-x2"] = new ModelInfo 
            { 
                Name = "EDSR x2 (Best OpenCV)", 
                Scale = 2, 
                Description = "Best quality 2x with OpenCV, requires more compute"
            },
            ["edsr-x3"] = new ModelInfo 
            { 
                Name = "EDSR x3 (Best OpenCV)", 
                Scale = 3, 
                Description = "Best quality 3x with OpenCV"
            },
            ["edsr-x4"] = new ModelInfo 
            { 
                Name = "EDSR x4 (Best OpenCV)", 
                Scale = 4, 
                Description = "Best quality 4x with OpenCV, slowest but best"
            }
        };

        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public ModelManager(
            ILogger<ModelManager> logger,
            IApplicationPaths appPaths)
        {
            _logger = logger;
            _appPaths = appPaths;
            
            _logger.LogInformation("ModelManager v1.5.2 initialized - Docker-based model management");
        }

        /// <summary>
        /// Get list of available models.
        /// </summary>
        public IReadOnlyDictionary<string, ModelInfo> GetAvailableModels()
        {
            return AvailableModels;
        }

        /// <summary>
        /// Request model download from the AI service.
        /// </summary>
        public async Task<bool> DownloadModelAsync(string modelName, HttpUpscalerService httpUpscaler, CancellationToken cancellationToken = default)
        {
            if (!AvailableModels.ContainsKey(modelName))
            {
                _logger.LogWarning("Unknown model requested: {Model}", modelName);
                return false;
            }

            try
            {
                _logger.LogInformation("Requesting model download: {Model}", modelName);
                return await httpUpscaler.DownloadModelAsync(modelName, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download model {Model}", modelName);
                return false;
            }
        }

        /// <summary>
        /// Request model load in the AI service.
        /// </summary>
        public async Task<bool> LoadModelAsync(string modelName, HttpUpscalerService httpUpscaler, bool useGpu = true, CancellationToken cancellationToken = default)
        {
            if (!AvailableModels.ContainsKey(modelName))
            {
                _logger.LogWarning("Unknown model requested: {Model}", modelName);
                return false;
            }

            try
            {
                _logger.LogInformation("Requesting model load: {Model}, GPU: {UseGpu}", modelName, useGpu);
                return await httpUpscaler.LoadModelAsync(modelName, useGpu, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load model {Model}", modelName);
                return false;
            }
        }

        /// <summary>
        /// Get the models directory path.
        /// </summary>
        public string GetModelsPath()
        {
            return Path.Combine(_appPaths.PluginsPath, "AI Upscaler Plugin", "models");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Model information.
    /// </summary>
    public class ModelInfo
    {
        public string Name { get; set; } = string.Empty;
        public int Scale { get; set; } = 2;
        public string Description { get; set; } = string.Empty;
    }
}
