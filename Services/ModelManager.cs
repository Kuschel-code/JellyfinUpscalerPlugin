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
            // === General Purpose ===
            ["realesrgan-x4plus"] = new ModelInfo 
            { 
                Name = "Real-ESRGAN x4+ (Best Quality)", 
                Scale = 4, 
                Description = "Best quality 4x upscaling for photos and real-world images"
            },
            ["realesrgan-x4"] = new ModelInfo 
            { 
                Name = "Real-ESRGAN x4", 
                Scale = 4, 
                Description = "High-quality 4x upscaling for real-world images"
            },
            ["realesrgan-x2"] = new ModelInfo 
            { 
                Name = "Real-ESRGAN x2", 
                Scale = 2, 
                Description = "High-quality 2x upscaling for real-world images"
            },
            
            // === Anime/Cartoon ===
            ["realesrgan-anime"] = new ModelInfo 
            { 
                Name = "Real-ESRGAN Anime x4", 
                Scale = 4, 
                Description = "Optimized for anime and cartoon content"
            },
            ["waifu2x-anime"] = new ModelInfo 
            { 
                Name = "Waifu2x Anime x2", 
                Scale = 2, 
                Description = "Classic anime upscaler, good for illustrations"
            },
            
            // === Fast Models ===
            ["fsrcnn-x2"] = new ModelInfo 
            { 
                Name = "FSRCNN x2 (Fast)", 
                Scale = 2, 
                Description = "Very fast 2x upscaling, good for real-time"
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
            
            // === Quality Models ===
            ["bsrgan-x4"] = new ModelInfo 
            { 
                Name = "BSRGAN x4 (Degraded Images)", 
                Scale = 4, 
                Description = "Best for old, compressed, or degraded images"
            },
            ["swinir-x4"] = new ModelInfo 
            { 
                Name = "SwinIR x4 (Transformer)", 
                Scale = 4, 
                Description = "Transformer-based model, high quality but slow"
            },
            
            // === Custom ===
            ["claude-opus-4-6"] = new ModelInfo 
            { 
                Name = "Claude Opus 4-6", 
                Scale = 4, 
                Description = "Experimental Claude Opus 4-6 Model"
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
