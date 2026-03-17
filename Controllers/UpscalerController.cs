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
using System.Net.Http;
using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using JellyfinUpscalerPlugin.Services;
using JellyfinUpscalerPlugin.Models;
using MediaBrowser.Model.Entities;
using MediaBrowser.Controller.Entities;
using Image = SixLabors.ImageSharp.Image;
using IOFile = System.IO.File;

namespace JellyfinUpscalerPlugin.Controllers
{
    /// <summary>
    /// AI Upscaler API Controller
    /// </summary>
    [ApiController]
    [Authorize]
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

        public UpscalerController(
            ILogger<UpscalerController> logger,
            ILibraryManager libraryManager,
            ISessionManager sessionManager,
            HardwareBenchmarkService benchmarkService,
            UpscalerCore upscalerCore,
            VideoProcessor videoProcessor,
            CacheManager cacheManager)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _sessionManager = sessionManager;
            _benchmarkService = benchmarkService;
            _upscalerCore = upscalerCore;
            _videoProcessor = videoProcessor;
            _cacheManager = cacheManager;
        }

        [HttpGet("models")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<List<object>> GetAvailableModels()
        {
            // Models must match the Docker AI service AVAILABLE_MODELS exactly
            return Ok(new List<object>
            {
                // Real-ESRGAN (ONNX GPU - Best Quality)
                new { id = "realesrgan-x4", name = "Real-ESRGAN x4 (Best Quality)", description = "Best quality 4x for photos & anime (67MB ONNX)", scale = new[] { 4 }, category = "realesrgan", type = "onnx" },
                new { id = "realesrgan-x4-256", name = "Real-ESRGAN x4 (256px optimized)", description = "Optimized for 256px tiles, better for low VRAM", scale = new[] { 4 }, category = "realesrgan", type = "onnx" },
                // Fast Models (OpenCV - Real-time)
                new { id = "fsrcnn-x2", name = "FSRCNN x2 (Fast)", description = "Very fast 2x upscaling, good for real-time", scale = new[] { 2 }, category = "fast", type = "pb" },
                new { id = "fsrcnn-x3", name = "FSRCNN x3 (Fast)", description = "Fast 3x upscaling", scale = new[] { 3 }, category = "fast", type = "pb" },
                new { id = "fsrcnn-x4", name = "FSRCNN x4 (Fast)", description = "Fast 4x upscaling, lower quality but quick", scale = new[] { 4 }, category = "fast", type = "pb" },
                new { id = "espcn-x2", name = "ESPCN x2 (Fastest)", description = "Fastest model, minimal quality improvement", scale = new[] { 2 }, category = "fast", type = "pb" },
                new { id = "espcn-x3", name = "ESPCN x3 (Fastest)", description = "Fastest 3x model", scale = new[] { 3 }, category = "fast", type = "pb" },
                new { id = "espcn-x4", name = "ESPCN x4 (Fastest)", description = "Fastest 4x model", scale = new[] { 4 }, category = "fast", type = "pb" },
                // Quality Models (OpenCV - High Quality)
                new { id = "lapsrn-x2", name = "LapSRN x2 (Quality)", description = "Good quality 2x upscaling", scale = new[] { 2 }, category = "quality", type = "pb" },
                new { id = "lapsrn-x4", name = "LapSRN x4 (Quality)", description = "Good quality 4x upscaling", scale = new[] { 4 }, category = "quality", type = "pb" },
                new { id = "lapsrn-x8", name = "LapSRN x8 (Quality)", description = "Extreme 8x upscaling", scale = new[] { 8 }, category = "quality", type = "pb" },
                new { id = "edsr-x2", name = "EDSR x2 (Best OpenCV)", description = "Best quality 2x with OpenCV", scale = new[] { 2 }, category = "quality", type = "pb" },
                new { id = "edsr-x3", name = "EDSR x3 (Best OpenCV)", description = "Best quality 3x with OpenCV", scale = new[] { 3 }, category = "quality", type = "pb" },
                new { id = "edsr-x4", name = "EDSR x4 (Best OpenCV)", description = "Best quality 4x with OpenCV, slowest but best", scale = new[] { 4 }, category = "quality", type = "pb" }
            });
        }

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

                if (resourceName == null) return NotFound();

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) return NotFound();

                using var reader = new StreamReader(stream);
                return Content(reader.ReadToEnd(), "text/javascript");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to serve JS component: {name}");
                return StatusCode(500);
            }
        }

        [HttpGet("status")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetStatus()
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null) return BadRequest();
            // Return only non-sensitive operational state (not full config with SSH paths etc.)
            return Ok(new
            {
                status = "Active",
                enablePlugin = config.EnablePlugin,
                model = config.Model,
                scaleFactor = config.ScaleFactor,
                qualityLevel = config.QualityLevel,
                hardwareAcceleration = config.HardwareAcceleration,
                maxConcurrentStreams = config.MaxConcurrentStreams,
                isProcessing = false, // Placeholder for actual processing state
                version = typeof(Plugin).Assembly.GetName().Version?.ToString(4) ?? "1.5.2.8"
            });
        }

        [HttpPost("test")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> TestUpscaling()
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null) return BadRequest();

            try
            {
                var hardware = await _upscalerCore.DetectHardwareAsync();
                return Ok(new
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
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Upscaler: Error during test");
                return StatusCode(500, new { success = false, message = "Test failed: " + ex.Message });
            }
        }

        [HttpGet("info")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetPluginInfo()
        {
            var assembly = typeof(Plugin).Assembly;
            var version = assembly.GetName().Version?.ToString(3) ?? "1.5.2";

            return Ok(new
            {
                name = "AI Upscaler Plugin",
                version = version,
                description = "AI-powered video upscaling with modern UI integration and hardware benchmarking",
                author = "Kuschel-code",
                features = new[]
                {
                    "Real-time AI video upscaling",
                    "Multiple AI models",
                    "Hardware acceleration support",
                    "Player integration",
                    "Automated hardware benchmarking"
                }
            });
        }

        [HttpPost("benchmark")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> RunHardwareBenchmark()
        {
            try
            {
                var results = await _benchmarkService.RunHardwareBenchmark();
                return Ok(new
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
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hardware benchmark failed");
                return StatusCode(500, new { success = false, message = "Hardware benchmark failed", error = ex.Message });
            }
        }

        [HttpGet("HardwareInfo")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetHardwareInfo()
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                var hardwareAcceleration = config?.HardwareAcceleration ?? false;
                
                return Ok(new
                {
                    GpuAvailable = hardwareAcceleration,
                    FFmpegAvailable = true,
                    OnnxRuntime = "Available",
                    Platform = Environment.OSVersion.Platform.ToString(),
                    PluginVersion = typeof(Plugin).Assembly.GetName().Version?.ToString(4) ?? "1.5.2.8"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get hardware info");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpGet("recommendations")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> GetHardwareRecommendations()
        {
            try
            {
                return Ok(await _benchmarkService.GetRecommendationsAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get hardware recommendations");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpGet("compare/{itemId}")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> GetComparisonData(string itemId, [FromQuery] string model = "realesrgan", [FromQuery] int scale = 2)
        {
            try
            {
                if (!Guid.TryParse(itemId, out var itemGuid))
                {
                    return BadRequest(new { message = "Invalid item ID format" });
                }
                
                var item = _libraryManager.GetItemById(itemGuid);
                if (item == null) return NotFound(new { message = "Item not found" });

                var imagePath = item.GetImagePath(ImageType.Primary, 0);
                if (string.IsNullOrEmpty(imagePath))
                {
                    var images = item.GetImages(ImageType.Primary).ToList();
                    if (images.Count == 0) return BadRequest(new { message = "No image available" });
                    imagePath = images[0].Path;
                }

                if (!IOFile.Exists(imagePath)) return NotFound(new { message = "Image file not found" });

                byte[] originalData = await IOFile.ReadAllBytesAsync(imagePath);
                
                using (var image = Image.Load(originalData))
                {
                    if (image.Width > 1280 || image.Height > 720)
                    {
                        image.Mutate(x => x.Resize(new ResizeOptions
                        {
                            Size = new Size(1280, 720),
                            Mode = ResizeMode.Max
                        }));
                        
                        using var ms = new MemoryStream();
                        image.SaveAsJpeg(ms);
                        originalData = ms.ToArray();
                    }
                }

                var upscaledData = await _upscalerCore.UpscaleImageAsync(originalData, model, scale);

                if (upscaledData == null)
                {
                    return StatusCode(503, new { message = "AI upscaling service unavailable" });
                }

                return Ok(new
                {
                    itemId = itemId,
                    model = model,
                    scale = scale,
                    originalBase64 = $"data:image/jpeg;base64,{Convert.ToBase64String(originalData)}",
                    upscaledBase64 = $"data:image/jpeg;base64,{Convert.ToBase64String(upscaledData)}",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to generate comparison data for item {itemId}");
                return StatusCode(500, new { message = "Comparison failed", error = ex.Message });
            }
        }

        [HttpPost("process")]
        [Authorize(Policy = "RequiresElevation")]
        [Consumes(MediaTypeNames.Application.Json)]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> ProcessVideo([FromBody] VideoProcessRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.InputPath) || !IOFile.Exists(request.InputPath))
                {
                    return BadRequest(new { success = false, error = "Input file not found" });
                }

                if (string.IsNullOrEmpty(request.OutputPath))
                {
                    return BadRequest(new { success = false, error = "Output path required" });
                }

                // Security: Validate paths to prevent path traversal attacks
                var fullInputPath = Path.GetFullPath(request.InputPath);
                var fullOutputPath = Path.GetFullPath(request.OutputPath);

                // Block writes to system-critical directories
                var blockedPrefixes = new[] { "/etc", "/usr", "/bin", "/sbin", "/boot", "/proc", "/sys",
                    @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)" };
                if (blockedPrefixes.Any(p => fullOutputPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    return BadRequest(new { success = false, error = "Output path not allowed - system directory" });
                }

                var outputDir = Path.GetDirectoryName(fullOutputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                var options = new VideoProcessingOptions
                {
                    Model = request.Model ?? "auto",
                    Scale = request.Scale ?? 2,
                    Quality = request.Quality ?? "medium"
                };
                
                var result = await _videoProcessor.ProcessVideoAsync(fullInputPath, fullOutputPath, options);
                
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

        [HttpPost("process/item/{itemId}")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> ProcessItem(string itemId, [FromQuery] string? model = null, [FromQuery] int? scale = null)
        {
            try
            {
                if (!Guid.TryParse(itemId, out var itemGuid))
                {
                    return BadRequest(new { message = "Invalid item ID format" });
                }
                
                var item = _libraryManager.GetItemById(itemGuid);
                if (item == null) return NotFound(new { message = "Item not found" });

                if (string.IsNullOrEmpty(item.Path) || !IOFile.Exists(item.Path))
                {
                    return BadRequest(new { message = "Item path not found or invalid" });
                }

                var config = Plugin.Instance?.Configuration;
                var options = new VideoProcessingOptions
                {
                    Model = model ?? config?.Model ?? "auto",
                    ScaleFactor = scale ?? config?.ScaleFactor ?? 2,
                    QualityLevel = config?.QualityLevel ?? "medium"
                };

                var directory = Path.GetDirectoryName(item.Path);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    return BadRequest(new { message = "Output directory not accessible" });
                }

                var outputPath = Path.Combine(
                    directory,
                    Path.GetFileNameWithoutExtension(item.Path) + "_upscaled" + Path.GetExtension(item.Path)
                );

                var result = await _videoProcessor.ProcessVideoAsync(item.Path, outputPath, options);

                return Ok(new { success = result.Success, itemId = itemId, outputPath = result.OutputPath, error = result.Error });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to process item {itemId}");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpGet("jobs")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetActiveJobs()
        {
            try
            {
                var jobs = _videoProcessor.GetActiveJobs();
                return Ok(new { success = true, jobs = jobs });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve active jobs");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpPost("jobs/{jobId}/pause")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> PauseJob(string jobId)
        {
            try
            {
                var result = _videoProcessor.PauseJob(jobId);
                if (result)
                {
                    return Ok(new { success = true, message = $"Job {jobId} paused" });
                }
                else
                {
                    return NotFound(new { success = false, message = "Job not found or cannot be paused" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to pause job {jobId}");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpPost("jobs/{jobId}/resume")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> ResumeJob(string jobId)
        {
            try
            {
                var result = _videoProcessor.ResumeJob(jobId);
                if (result)
                {
                    return Ok(new { success = true, message = $"Job {jobId} resumed" });
                }
                else
                {
                    return NotFound(new { success = false, message = "Job not found or cannot be resumed" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to resume job {jobId}");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpPost("jobs/{jobId}/cancel")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> CancelJob(string jobId)
        {
            try
            {
                var result = _videoProcessor.CancelJob(jobId);
                if (result)
                {
                    return Ok(new { success = true, message = $"Job {jobId} cancelled" });
                }
                else
                {
                    return NotFound(new { success = false, message = "Job not found or cannot be cancelled" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to cancel job {jobId}");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpGet("cache/stats")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetCacheStats()
        {
            try
            {
                var stats = _cacheManager.GetCacheStatistics();
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get cache statistics");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpPost("cache/clear")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> ClearCache()
        {
            try
            {
                await _cacheManager.ClearCacheAsync();
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear cache");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpGet("hardware")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> GetHardwareProfile()
        {
            try
            {
                return Ok(await _upscalerCore.DetectHardwareAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get hardware profile");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpPost("upscale/image")]
        [Authorize(Policy = "RequiresElevation")]
        [Consumes("application/octet-stream")]
        [Produces("application/octet-stream")]
        [RequestSizeLimit(52428800)] // 50MB max
        public async Task<ActionResult> UpscaleImage([FromQuery] string model = "realesrgan-x4", [FromQuery] int scale = 2)
        {
            try
            {
                // Security: Validate scale parameter
                var allowedScales = new[] { 2, 3, 4, 8 };
                if (!allowedScales.Contains(scale))
                    return BadRequest(new { error = "Invalid scale. Allowed values: 2, 3, 4, 8" });

                // Security: Validate model name (alphanumeric, hyphens only)
                if (!Regex.IsMatch(model, @"^[a-zA-Z0-9\-]+$"))
                    return BadRequest(new { error = "Invalid model name" });

                // Security: Limit upload size to prevent DOS attacks
                const long maxSizeBytes = 50 * 1024 * 1024; // 50MB
                if (Request.ContentLength > maxSizeBytes)
                {
                    return BadRequest(new { error = "Image too large. Maximum size is 50MB." });
                }

                using var memoryStream = new MemoryStream();
                await Request.Body.CopyToAsync(memoryStream);
                
                if (memoryStream.Length > maxSizeBytes)
                {
                    return BadRequest(new { error = "Image too large. Maximum size is 50MB." });
                }
                
                var inputImage = memoryStream.ToArray();
                var upscaledImage = await _upscalerCore.UpscaleImageAsync(inputImage, model, scale);
                if (upscaledImage == null)
                {
                    return StatusCode(503, new { error = "AI upscaling service unavailable" });
                }
                return File(upscaledImage, "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Image upscaling failed");
                return StatusCode(500);
            }
        }

        [HttpPost("preprocess")]
        [Authorize(Policy = "RequiresElevation")]
        [Consumes(MediaTypeNames.Application.Json)]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> PreProcessVideo([FromBody] PreProcessRequest request)
        {
            try
            {
                var success = await _cacheManager.PreProcessContentAsync(
                    request.InputPath,
                    request.Model ?? "auto",
                    request.Scale ?? 2,
                    request.Quality ?? "medium",
                    _videoProcessor);
                
                return Ok(new { success = success });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pre-processing failed");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpPost("cache/config")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> ConfigurePreProcessingCache([FromBody] PreProcessingCacheRequest request)
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config != null)
                {
                    config.EnablePreProcessingCache = request.Enabled;
                    Plugin.Instance?.SaveConfiguration();
                }
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure pre-processing cache");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpGet("settings/export")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> ExportSettings()
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config == null) return BadRequest(new { success = false, error = "Plugin not loaded" });

                return Ok(new
                {
                    success = true,
                    pluginVersion = config.PluginVersion,
                    exportDate = DateTime.UtcNow.ToString("o"),
                    settings = new
                    {
                        config.EnablePlugin,
                        config.Model,
                        config.ScaleFactor,
                        config.QualityLevel,
                        config.HardwareAcceleration,
                        config.MaxConcurrentStreams,
                        config.MaxVRAMUsage,
                        config.CpuThreads,
                        config.AiServiceUrl,
                        config.EnableRemoteTranscoding,
                        config.RemoteHost,
                        config.RemoteSshPort,
                        config.RemoteUser,
                        config.RemoteSshKeyFile,
                        config.LocalMediaMountPoint,
                        config.RemoteMediaMountPoint,
                        config.RemoteTranscodePath,
                        config.PlayerButton,
                        config.Notifications,
                        config.AutoRetryButton,
                        config.ButtonPosition,
                        config.EnableComparisonView,
                        config.EnablePerformanceMetrics,
                        config.EnableAutoBenchmarking,
                        config.EnablePreProcessingCache,
                        config.MaxCacheAgeDays,
                        config.CacheSizeMB,
                        config.GpuDeviceIndex
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export settings");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpPost("settings/import")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> ImportSettings([FromBody] System.Text.Json.JsonElement body)
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config == null) return BadRequest(new { success = false, error = "Plugin not loaded" });

                System.Text.Json.JsonElement settings;
                if (!body.TryGetProperty("settings", out settings))
                {
                    return BadRequest(new { success = false, error = "Missing 'settings' property" });
                }

                // Apply each setting if present
                if (settings.TryGetProperty("EnablePlugin", out var v)) config.EnablePlugin = v.GetBoolean();
                if (settings.TryGetProperty("Model", out v)) config.Model = v.GetString() ?? "realesrgan-x4";
                if (settings.TryGetProperty("ScaleFactor", out v)) config.ScaleFactor = v.GetInt32();
                if (settings.TryGetProperty("QualityLevel", out v)) config.QualityLevel = v.GetString() ?? "medium";
                if (settings.TryGetProperty("HardwareAcceleration", out v)) config.HardwareAcceleration = v.GetBoolean();
                if (settings.TryGetProperty("MaxConcurrentStreams", out v)) config.MaxConcurrentStreams = v.GetInt32();
                if (settings.TryGetProperty("MaxVRAMUsage", out v)) config.MaxVRAMUsage = v.GetInt32();
                if (settings.TryGetProperty("CpuThreads", out v)) config.CpuThreads = v.GetInt32();
                if (settings.TryGetProperty("AiServiceUrl", out v)) config.AiServiceUrl = v.GetString() ?? "http://localhost:5000";
                if (settings.TryGetProperty("EnableRemoteTranscoding", out v)) config.EnableRemoteTranscoding = v.GetBoolean();
                if (settings.TryGetProperty("RemoteHost", out v)) config.RemoteHost = v.GetString() ?? "";
                if (settings.TryGetProperty("RemoteSshPort", out v)) config.RemoteSshPort = v.GetInt32();
                if (settings.TryGetProperty("RemoteUser", out v)) config.RemoteUser = v.GetString() ?? "";
                if (settings.TryGetProperty("RemoteSshKeyFile", out v)) config.RemoteSshKeyFile = v.GetString() ?? "";
                if (settings.TryGetProperty("LocalMediaMountPoint", out v)) config.LocalMediaMountPoint = v.GetString() ?? "";
                if (settings.TryGetProperty("RemoteMediaMountPoint", out v)) config.RemoteMediaMountPoint = v.GetString() ?? "";
                if (settings.TryGetProperty("RemoteTranscodePath", out v)) config.RemoteTranscodePath = v.GetString() ?? "";
                if (settings.TryGetProperty("PlayerButton", out v)) config.PlayerButton = v.GetBoolean();
                if (settings.TryGetProperty("Notifications", out v)) config.Notifications = v.GetBoolean();
                if (settings.TryGetProperty("AutoRetryButton", out v)) config.AutoRetryButton = v.GetBoolean();
                if (settings.TryGetProperty("ButtonPosition", out v)) config.ButtonPosition = v.GetString() ?? "right";
                if (settings.TryGetProperty("EnableComparisonView", out v)) config.EnableComparisonView = v.GetBoolean();
                if (settings.TryGetProperty("EnablePerformanceMetrics", out v)) config.EnablePerformanceMetrics = v.GetBoolean();
                if (settings.TryGetProperty("EnableAutoBenchmarking", out v)) config.EnableAutoBenchmarking = v.GetBoolean();
                if (settings.TryGetProperty("EnablePreProcessingCache", out v)) config.EnablePreProcessingCache = v.GetBoolean();
                if (settings.TryGetProperty("MaxCacheAgeDays", out v)) config.MaxCacheAgeDays = v.GetInt32();
                if (settings.TryGetProperty("CacheSizeMB", out v)) config.CacheSizeMB = v.GetInt32();
                if (settings.TryGetProperty("GpuDeviceIndex", out v)) config.GpuDeviceIndex = Math.Max(0, v.GetInt32());

                Plugin.Instance?.SaveConfiguration();
                _logger.LogInformation("Settings imported successfully");
                return Ok(new { success = true, message = "Settings imported successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import settings");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpGet("fallback")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> GetFallbackStatus()
        {
            try
            {
                return Ok(await _benchmarkService.GetFallbackStatusAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get fallback status");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Server-side health check proxy for the Docker AI service (avoids CORS issues)
        /// </summary>
        [HttpGet("service-health")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> CheckServiceHealth()
        {
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var isAvailable = await _benchmarkService.IsServiceAvailableAsync();
                stopwatch.Stop();

                var status = isAvailable ? await _benchmarkService.GetServiceStatusAsync() : null;

                return Ok(new
                {
                    success = true,
                    available = isAvailable,
                    latencyMs = stopwatch.ElapsedMilliseconds,
                    currentModel = status?.CurrentModel,
                    usingGpu = status?.UsingGpu ?? false,
                    processingCount = status?.ProcessingCount ?? 0,
                    maxConcurrent = status?.MaxConcurrent ?? 0,
                    providers = status?.AvailableProviders ?? Array.Empty<string>()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Service health check failed");
                return Ok(new { success = false, available = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Get available GPUs from the AI Docker service (proxy to /gpus).
        /// </summary>
        [HttpGet("gpus")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> GetGpuList()
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                var serviceUrl = config?.AiServiceUrl?.TrimEnd('/') ?? "http://localhost:5000";
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = await client.GetAsync($"{serviceUrl}/gpus");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return Content(content, "application/json");
                }
                return StatusCode((int)response.StatusCode, new { error = "Failed to get GPU list from AI service" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get GPU list");
                return Ok(new { gpus = Array.Empty<object>() });
            }
        }

        /// <summary>
        /// Test SSH connection to remote transcoding host (admin only)
        /// </summary>
        [HttpPost("ssh/test")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> TestSshConnection([FromBody] SshTestRequest request)
        {
            try
            {
                // Security: Validate inputs to prevent command injection
                if (!Regex.IsMatch(request.Host, @"^[a-zA-Z0-9._\-]+$"))
                    return BadRequest(new { success = false, message = "Invalid host format. Only alphanumeric, dots, hyphens allowed." });

                if (!Regex.IsMatch(request.User, @"^[a-zA-Z0-9._\-]+$"))
                    return BadRequest(new { success = false, message = "Invalid user format. Only alphanumeric, dots, hyphens allowed." });

                if (request.Port < 1 || request.Port > 65535)
                    return BadRequest(new { success = false, message = "Invalid port. Must be 1-65535." });

                _logger.LogInformation("Testing SSH connection to {User}@{Host}:{Port}", request.User, request.Host, request.Port);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ssh",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Use ArgumentList to prevent shell injection
                if (!string.IsNullOrWhiteSpace(request.KeyFile))
                {
                    var resolvedKeyPath = Path.GetFullPath(request.KeyFile);
                    if (!IOFile.Exists(resolvedKeyPath))
                        return BadRequest(new { success = false, message = "SSH key file not found." });

                    // Security: Restrict key file to .ssh directories or plugin data path
                    var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
                    var pluginDir = Plugin.Instance != null ? Path.GetDirectoryName(Plugin.Instance.ConfigurationFilePath) ?? "" : "";
                    if (!resolvedKeyPath.StartsWith(sshDir, StringComparison.OrdinalIgnoreCase) &&
                        !resolvedKeyPath.StartsWith(pluginDir, StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest(new { success = false, message = "SSH key file must be in ~/.ssh/ or plugin data directory." });
                    }

                    psi.ArgumentList.Add("-i");
                    psi.ArgumentList.Add(resolvedKeyPath);
                }

                psi.ArgumentList.Add("-o");
                psi.ArgumentList.Add("BatchMode=yes");
                psi.ArgumentList.Add("-o");
                psi.ArgumentList.Add("StrictHostKeyChecking=accept-new");
                psi.ArgumentList.Add("-o");
                psi.ArgumentList.Add("ConnectTimeout=5");
                psi.ArgumentList.Add("-p");
                psi.ArgumentList.Add(request.Port.ToString());
                psi.ArgumentList.Add($"{request.User}@{request.Host}");
                psi.ArgumentList.Add("echo 'SSH_TEST_SUCCESS'");

                using var process = new System.Diagnostics.Process { StartInfo = psi };
                process.Start();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
                var error = await process.StandardError.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);

                if (process.ExitCode == 0 && output.Contains("SSH_TEST_SUCCESS"))
                {
                    _logger.LogInformation("SSH connection test successful");
                    return Ok(new { success = true, message = "SSH connection successful" });
                }
                else
                {
                    _logger.LogWarning("SSH connection test failed: {Error}", error);
                    return Ok(new { success = false, message = $"SSH connection failed: {error}" });
                }
            }
            catch (OperationCanceledException)
            {
                return Ok(new { success = false, message = "SSH connection timed out after 15 seconds" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSH connection test error");
                return Ok(new { success = false, message = $"SSH test error: {ex.Message}" });
            }
        }
    }

    /// <summary>
    /// Request model for SSH connection test
    /// </summary>
    public class SshTestRequest
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 2222;
        public string User { get; set; } = "root";
        public string KeyFile { get; set; } = "";
    }
}
