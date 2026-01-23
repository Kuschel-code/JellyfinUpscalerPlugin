using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using JellyfinUpscalerPlugin.Models;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Core upscaling engine - v1.4.9.5 Docker-based implementation
    /// Delegates AI processing to the external Docker AI service via HTTP.
    /// </summary>
    public class UpscalerCore : IDisposable
    {
        private readonly ILogger<UpscalerCore> _logger;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly IFileSystem _fileSystem;
        private readonly IApplicationPaths _appPaths;
        private readonly HttpUpscalerService _httpUpscaler;
        
        // Hardware detection cache with thread safety
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _hardwareCache = new();
        private static DateTime _lastHardwareCheck = DateTime.MinValue;
        
        // Performance monitoring
        private readonly Dictionary<string, PerformanceMetrics> _performanceMetrics = new();
        
        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();
        private bool _disposed;
        
        public UpscalerCore(
            ILogger<UpscalerCore> logger,
            IMediaEncoder mediaEncoder,
            IFileSystem fileSystem,
            IApplicationPaths appPaths,
            HttpUpscalerService httpUpscaler)
        {
            _logger = logger;
            _mediaEncoder = mediaEncoder;
            _fileSystem = fileSystem;
            _appPaths = appPaths;
            _httpUpscaler = httpUpscaler;
            
            _logger.LogInformation("UpscalerCore v1.4.9.5 initialized - Docker-based AI processing");
        }

        /// <summary>
        /// Check if the AI upscaling service is available.
        /// </summary>
        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return await _httpUpscaler.IsServiceAvailableAsync(cancellationToken);
        }

        /// <summary>
        /// Upscale an image using the Docker AI service.
        /// </summary>
        /// <param name="imageData">Raw image bytes</param>
        /// <param name="model">Model name (optional)</param>
        /// <param name="scale">Scale factor (2 or 4)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Upscaled image bytes</returns>
        public async Task<byte[]> UpscaleImageAsync(byte[] imageData, string model = "auto", int scale = 2, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                _logger.LogDebug("Starting image upscale: {Size} bytes, scale={Scale}", imageData.Length, scale);
                
                var result = await _httpUpscaler.UpscaleImageAsync(imageData, scale, cancellationToken);
                
                if (result == null || result.Length == 0)
                {
                    _logger.LogWarning("Upscaling returned empty result, using fallback resize");
                    return await FallbackResizeAsync(imageData, scale);
                }
                
                stopwatch.Stop();
                _logger.LogInformation("Image upscaled: {InputSize} -> {OutputSize} bytes in {Time}ms",
                    imageData.Length, result.Length, stopwatch.ElapsedMilliseconds);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI upscaling failed, using fallback resize");
                return await FallbackResizeAsync(imageData, scale);
            }
        }

        /// <summary>
        /// Fallback resize using ImageSharp when AI service is unavailable.
        /// </summary>
        private async Task<byte[]> FallbackResizeAsync(byte[] imageData, int scale)
        {
            try
            {
                using var image = Image.Load(imageData);
                var newWidth = image.Width * scale;
                var newHeight = image.Height * scale;
                
                image.Mutate(x => x.Resize(newWidth, newHeight, KnownResamplers.Lanczos3));
                
                using var outputStream = new MemoryStream();
                await image.SaveAsPngAsync(outputStream);
                return outputStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallback resize also failed");
                return imageData; // Return original as last resort
            }
        }

        /// <summary>
        /// Detect available hardware capabilities.
        /// </summary>
        public async Task<HardwareProfile> DetectHardwareAsync()
        {
            var profile = new HardwareProfile
            {
                DetectionTime = DateTime.UtcNow
            };

            try
            {
                var status = await _httpUpscaler.GetServiceStatusAsync();
                
                if (status != null)
                {
                    profile.CudaAvailable = status.AvailableProviders.Any(p => 
                        p.Contains("CUDA", StringComparison.OrdinalIgnoreCase) ||
                        p.Contains("TensorRT", StringComparison.OrdinalIgnoreCase));
                    
                    profile.DirectMlAvailable = status.AvailableProviders.Any(p => 
                        p.Contains("DirectML", StringComparison.OrdinalIgnoreCase));
                    
                    profile.ServiceAvailable = true;
                }
                else
                {
                    profile.ServiceAvailable = false;
                    _logger.LogWarning("Could not connect to AI service for hardware detection");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hardware detection failed");
                profile.ServiceAvailable = false;
            }

            profile.CpuCores = Environment.ProcessorCount;
            return profile;
        }

        /// <summary>
        /// Get service status summary.
        /// </summary>
        public async Task<ServiceStatus?> GetServiceStatusAsync()
        {
            return await _httpUpscaler.GetServiceStatusAsync();
        }

        /// <summary>
        /// Get current performance metrics.
        /// </summary>
        public Dictionary<string, object> GetPerformanceMetrics()
        {
            return new Dictionary<string, object>
            {
                ["service_url"] = Config.AiServiceUrl,
                ["metrics_count"] = _performanceMetrics.Count
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpUpscaler?.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Performance metrics for upscaling operations.
    /// </summary>
    public class PerformanceMetrics
    {
        public int TotalOperations { get; set; }
        public double AverageTimeMs { get; set; }
        public double TotalTimeMs { get; set; }
        public DateTime LastOperation { get; set; }
    }
}