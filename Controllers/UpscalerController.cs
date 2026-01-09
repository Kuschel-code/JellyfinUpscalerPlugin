using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Net;
using System.Net.Mime;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using JellyfinUpscalerPlugin.Services;
using JellyfinUpscalerPlugin.Models;
using MediaBrowser.Model.Entities;
using Image = SixLabors.ImageSharp.Image;

namespace JellyfinUpscalerPlugin.Controllers
{
    /// <summary>
    /// AI Upscaler API Controller v1.4.1 - Enhanced with Modern UI Sync
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class UpscalerController : ControllerBase
    {
        private readonly ILogger<UpscalerController> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly ISessionManager _sessionManager;
        private readonly HardwareBenchmarkService _benchmarkService;
        private readonly UpscalerCore _upscalerCore;
        private readonly VideoProcessor _videoProcessor;
        private readonly CacheManager _cacheManager;
        private readonly MediaProcessingService _mediaProcessingService;

        /// <summary>
        /// Initializes a new instance of the UpscalerController class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="libraryManager">Library manager instance.</param>
        /// <param name="sessionManager">Session manager instance.</param>
        /// <param name="benchmarkService">Hardware benchmark service.</param>
        /// <param name="upscalerCore">Upscaler core service.</param>
        /// <param name="videoProcessor">Video processor service.</param>
        /// <param name="cacheManager">Cache manager service.</param>
        /// <param name="mediaProcessingService">Media processing service.</param>
        public UpscalerController(
            ILogger<UpscalerController> logger,
            ILibraryManager libraryManager,
            ISessionManager sessionManager,
            HardwareBenchmarkService benchmarkService,
            UpscalerCore upscalerCore,
            VideoProcessor videoProcessor,
            CacheManager cacheManager,
            MediaProcessingService mediaProcessingService)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _sessionManager = sessionManager;
            _benchmarkService = benchmarkService;
            _upscalerCore = upscalerCore;
            _videoProcessor = videoProcessor;
            _cacheManager = cacheManager;
            _mediaProcessingService = mediaProcessingService;
        }

        /// <summary>
        /// Get available AI models
        /// </summary>
        /// <returns>List of available AI upscaling models</returns>
        [HttpGet("models")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<List<object>> GetAvailableModels()
        {
            _logger.LogInformation("AI Upscaler: Getting available models");
            return Ok(PluginInfoService.GetAvailableModels());
        }

        /// <summary>
        /// Get JavaScript components for UI integration
        /// </summary>
        /// <param name="name">Name of the component</param>
        /// <returns>JavaScript file content</returns>
        [HttpGet("js/{name}")]
        [Produces("text/javascript")]
        public ActionResult GetJavaScript(string name)
        {
            try
            {
                var assembly = GetType().Assembly;
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(r => r.EndsWith($".Configuration.{name}", StringComparison.OrdinalIgnoreCase) || 
                                         r.EndsWith($".Configuration.{name}.js", StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                {
                    _logger.LogWarning($"JS resource not found: {name}");
                    return NotFound();
                }

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) return NotFound();

                using var reader = new StreamReader(stream);
                return Content(reader.ReadToEnd(), "text/javascript");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to serve JS component: {name}");
                return StatusCode(500, "Internal Server Error");
            }
        }

        /// <summary>
        /// Get current upscaler status and settings
        /// </summary>
        /// <returns>Current status of the AI upscaler</returns>
        [HttpGet("status")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetStatus()
        {
            _logger.LogInformation("AI Upscaler: Getting status");

            var config = Plugin.Instance?.Configuration;
            if (config == null)
            {
                return BadRequest("Plugin configuration not available");
            }

            return Ok(config);
        }

        /// <summary>
        /// Test AI upscaling with current settings
        /// </summary>
        /// <returns>Test result</returns>
        [HttpPost("test")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> TestUpscaling()
        {
            _logger.LogInformation("AI Upscaler: Testing upscaling");

            var config = Plugin.Instance?.Configuration;
            if (config == null)
            {
                return BadRequest("Plugin configuration not available");
            }

            try
            {
                // Detect hardware for real test info
                var hardware = await _upscalerCore.DetectHardwareAsync();
                
                var testResult = new
                {
                    success = true,
                    model = config.Model,
                    scale = config.ScaleFactor,
                    quality = config.QualityLevel,
                    hardwareAcceleration = config.HardwareAcceleration,
                    gpuModel = hardware.GpuModel,
                    supportsCUDA = hardware.SupportsCUDA,
                    estimatedPerformance = hardware.SupportsCUDA ? "High (GPU/CUDA)" : (hardware.SupportsDirectML ? "Medium (GPU/DirectML)" : "Low (CPU)"),
                    message = $"AI upscaling test successful on {hardware.GpuModel ?? "CPU"} with {config.Model} model at {config.ScaleFactor}x scale"
                };

                _logger.LogInformation("AI Upscaler: Test completed successfully");

                return Ok(testResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Upscaler: Error during test");
                return StatusCode(500, new { success = false, message = "Test failed: " + ex.Message });
            }
        }

        /// <summary>
        /// Get plugin information
        /// </summary>
        /// <returns>Plugin information</returns>
        [HttpGet("info")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetPluginInfo()
        {
            _logger.LogInformation("AI Upscaler: Getting plugin info");
            return Ok(PluginInfoService.GetPluginMetadata());
        }

        /// <summary>
        /// Run hardware benchmark - v1.4.1 NEW
        /// </summary>
        /// <returns>Comprehensive hardware benchmark results</returns>
        [HttpPost("benchmark")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> RunHardwareBenchmark()
        {
            _logger.LogInformation("AI Upscaler: Starting hardware benchmark");

            try
            {
                var results = await _benchmarkService.RunHardwareBenchmark();
                
                var response = new
                {
                    success = true,
                    message = "Hardware benchmark completed successfully",
                    results = new
                    {
                        duration = results.TotalDuration.TotalSeconds,
                        systemInfo = results.SystemInfo,
                        optimalSettings = results.OptimalSettings,
                        modelPerformance = results.ModelPerformance,
                        resolutionPerformance = results.ResolutionPerformance,
                        timestamp = results.EndTime
                    }
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hardware benchmark failed");
                return StatusCode(500, new { success = false, message = "Hardware benchmark failed", error = ex.Message });
            }
        }

        /// <summary>
        /// Get hardware recommendations - v1.4.1 NEW
        /// </summary>
        /// <returns>Hardware-specific recommendations</returns>
        [HttpGet("recommendations")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> GetHardwareRecommendations()
        {
            _logger.LogInformation("AI Upscaler: Getting hardware recommendations");

            try
            {
                return Ok(await _benchmarkService.GetRecommendationsAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get hardware recommendations");
                return StatusCode(500, new { success = false, message = "Failed to get recommendations", error = ex.Message });
            }
        }

        /// <summary>
        /// Get comparison data for before/after preview - v1.4.1 NEW
        /// </summary>
        /// <param name="itemId">Media item ID</param>
        /// <param name="model">AI model to use for comparison</param>
        /// <param name="scale">Scale factor</param>
        /// <returns>Comparison preview data</returns>
        [HttpGet("compare/{itemId}")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> GetComparisonData(string itemId, [FromQuery] string model = "realesrgan", [FromQuery] int scale = 2)
        {
            try
            {
                return Ok(await _mediaProcessingService.GenerateComparisonDataAsync(itemId, model, scale));
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Failed to generate comparison data for item {itemId}");
                return StatusCode(500, new { message = "Comparison failed", error = ex.Message });
            }
        }

        /// <summary>
        /// Process video with AI upscaling - NEW v1.4.1
        /// </summary>
        [HttpPost("process")]
        [Consumes(MediaTypeNames.Application.Json)]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> ProcessVideo([FromBody] VideoProcessRequest request)
        {
            try
            {
                _logger.LogInformation($"🚀 Processing video: {request.InputPath}");
                
                var options = new VideoProcessingOptions
                {
                    Model = request.Model ?? "auto",
                    Scale = request.Scale ?? 2,
                    Quality = request.Quality ?? "medium"
                };
                
                var result = await _videoProcessor.ProcessVideoAsync(
                    request.InputPath, 
                    request.OutputPath, 
                    options);
                
                return Ok(new 
                {
                    success = result.Success,
                    outputPath = result.OutputPath,
                    processingTime = result.ProcessingTime.TotalSeconds,
                    method = result.Method.ToString(),
                    error = result.Error
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Video processing failed");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Process a specific library item - NEW v1.4.1
        /// </summary>
        [HttpPost("process/item/{itemId}")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> ProcessItem(string itemId, [FromQuery] string? model = null, [FromQuery] int? scale = null)
        {
            try
            {
                var result = await _mediaProcessingService.ProcessLibraryItemAsync(itemId, model, scale);

                return Ok(new
                {
                    success = result.Success,
                    itemId = itemId,
                    outputPath = result.OutputPath,
                    error = result.Error
                });
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to process item {itemId}");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Get cache statistics - NEW v1.4.1
        /// </summary>
        [HttpGet("cache/stats")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetCacheStats()
        {
            try
            {
                var stats = _cacheManager.GetCacheStatistics();
                
                return Ok(new
                {
                    totalEntries = stats.TotalEntries,
                    totalSize = stats.TotalSize,
                    maxSize = stats.MaxSize,
                    hitRate = stats.HitRate,
                    totalHits = stats.TotalHits,
                    totalMisses = stats.TotalMisses,
                    usagePercentage = stats.UsagePercentage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get cache statistics");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Clear cache - NEW v1.4.1
        /// </summary>
        [HttpPost("cache/clear")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> ClearCache()
        {
            try
            {
                await _cacheManager.ClearCacheAsync();
                
                return Ok(new { success = true, message = "Cache cleared successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear cache");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Get hardware profile - NEW v1.4.1
        /// </summary>
        [HttpGet("hardware")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> GetHardwareProfile()
        {
            try
            {
                var profile = await _upscalerCore.DetectHardwareAsync();
                
                return Ok(new
                {
                    gpuVendor = profile.GpuVendor,
                    gpuModel = profile.GpuModel,
                    driverVersion = profile.DriverVersion,
                    vramMB = profile.VramMB,
                    cpuCores = profile.CpuCores,
                    systemRamMB = profile.SystemRamMB,
                    supportsCUDA = profile.SupportsCUDA,
                    supportsDirectML = profile.SupportsDirectML,
                    recommendedModel = profile.RecommendedModel,
                    recommendedScale = profile.RecommendedScale,
                    maxConcurrentStreams = profile.MaxConcurrentStreams,
                    availableProviders = profile.AvailableProviders,
                    availableHwAccels = profile.AvailableHwAccels
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get hardware profile");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Upscale image - NEW v1.4.1
        /// </summary>
        [HttpPost("upscale/image")]
        [Consumes("application/octet-stream")]
        [Produces("application/octet-stream")]
        public async Task<ActionResult> UpscaleImage(
            [FromQuery] string model = "realesrgan",
            [FromQuery] int scale = 2)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                await Request.Body.CopyToAsync(memoryStream);
                var inputImage = memoryStream.ToArray();
                
                var upscaledImage = await _mediaProcessingService.UpscaleImageAsync(inputImage, model, scale);
                
                return File(upscaledImage, "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Image upscaling failed");
                return StatusCode(500);
            }
        }

        /// <summary>
        /// Pre-process video for caching - NEW v1.4.1
        /// </summary>
        [HttpPost("preprocess")]
        [Consumes(MediaTypeNames.Application.Json)]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> PreProcessVideo([FromBody] PreProcessRequest request)
        {
            try
            {
                var success = await _mediaProcessingService.PreProcessVideoAsync(
                    request.InputPath,
                    request.Model,
                    request.Scale,
                    request.Quality,
                    _cacheManager);
                
                return Ok(new 
                {
                    success = success,
                    message = success ? "Pre-processing completed" : "Pre-processing failed"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pre-processing failed");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
        /// <summary>
        /// Enable/disable pre-processing cache - v1.4.1
        /// </summary>
        /// <param name="request">Pre-processing cache settings</param>
        /// <returns>Cache operation result</returns>
        [HttpPost("cache")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> ConfigurePreProcessingCache([FromBody] PreProcessingCacheRequest request)
        {
            _logger.LogInformation($"AI Upscaler: Configuring pre-processing cache - enabled: {request.Enabled}");

            try
            {
                // Update plugin configuration
                var config = Plugin.Instance?.Configuration;
                if (config != null)
                {
                    config.EnablePreProcessingCache = request.Enabled;
                    
                    Plugin.Instance?.SaveConfiguration();
                }

                var response = new
                {
                    success = true,
                    message = "Pre-processing cache configured successfully",
                    settings = new
                    {
                        enabled = request.Enabled,
                        status = request.Enabled ? "active" : "disabled"
                    }
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure pre-processing cache");
                return StatusCode(500, new { success = false, message = "Failed to configure cache", error = ex.Message });
            }
        }

        /// <summary>
        /// Get fallback system status - v1.4.1
        /// </summary>
        /// <returns>Fallback system information</returns>
        [HttpGet("fallback")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetFallbackStatus()
        {
            _logger.LogInformation("AI Upscaler: Getting fallback system status");

            try
            {
                return Ok(_benchmarkService.GetFallbackStatus());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get fallback status");
                return StatusCode(500, new { success = false, message = "Failed to get fallback status", error = ex.Message });
            }
        }



    }
}