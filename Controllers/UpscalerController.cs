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
using MediaBrowser.Controller.Entities;
using Image = SixLabors.ImageSharp.Image;

namespace JellyfinUpscalerPlugin.Controllers
{
    /// <summary>
    /// AI Upscaler API Controller
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
            return Ok(new List<object>
            {
                new { id = "realesrgan", name = "Real-ESRGAN", description = "Best Overall Quality (Anime/Photo)", scale = new[] { 2, 3, 4 } },
                new { id = "esrgan-pro", name = "ESRGAN Pro", description = "Optimized for Movies & TV Shows", scale = new[] { 2, 4 } },
                new { id = "swinir", name = "SwinIR", description = "State-of-the-art Transformer based", scale = new[] { 2, 4, 8 } },
                new { id = "srcnn-light", name = "SRCNN Light", description = "Fast processing for low-end hardware", scale = new[] { 2, 3 } },
                new { id = "waifu2x", name = "Waifu2x", description = "Classic Anime upscaling", scale = new[] { 2 } },
                new { id = "hat", name = "HAT", description = "High Detail Enhancement", scale = new[] { 2, 4 } },
                new { id = "edsr", name = "EDSR", description = "Precise Super-Resolution", scale = new[] { 2, 3, 4 } },
                new { id = "vdsr", name = "VDSR", description = "Deep Learning approach", scale = new[] { 2, 3, 4 } },
                new { id = "rdn", name = "RDN", description = "Enhanced Texture Detail", scale = new[] { 2, 4 } },
                new { id = "srresnet", name = "SRResNet", description = "Balanced Performance", scale = new[] { 2, 4 } },
                new { id = "carn", name = "CARN", description = "Compact & Fast", scale = new[] { 2, 3, 4 } },
                new { id = "rrdbnet", name = "RRDBNet", description = "High Fidelity Quality", scale = new[] { 2, 4 } },
                new { id = "drln", name = "DRLN", description = "Advanced Noise Reduction", scale = new[] { 2, 4 } },
                new { id = "fsrcnn", name = "FSRCNN", description = "Lightweight Real-time capable", scale = new[] { 2, 3, 4 } }
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
            return Ok(config);
        }

        [HttpPost("test")]
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
            var version = assembly.GetName().Version?.ToString(3) ?? "1.4.1";

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
                var item = _libraryManager.GetItemById(itemId);
                if (item == null) return NotFound(new { message = "Item not found" });

                var imagePath = item.GetImagePath(ImageType.Primary, 0);
                if (string.IsNullOrEmpty(imagePath))
                {
                    var images = item.GetImages(ImageType.Primary).ToList();
                    if (images.Count == 0) return BadRequest(new { message = "No image available" });
                    imagePath = images[0].Path;
                }

                if (!File.Exists(imagePath)) return NotFound(new { message = "Image file not found" });

                byte[] originalData = await File.ReadAllBytesAsync(imagePath);
                
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
        [Consumes(MediaTypeNames.Application.Json)]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> ProcessVideo([FromBody] VideoProcessRequest request)
        {
            try
            {
                var options = new VideoProcessingOptions
                {
                    Model = request.Model ?? "auto",
                    Scale = request.Scale ?? 2,
                    Quality = request.Quality ?? "medium"
                };
                
                var result = await _videoProcessor.ProcessVideoAsync(request.InputPath, request.OutputPath, options);
                
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
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> ProcessItem(string itemId, [FromQuery] string? model = null, [FromQuery] int? scale = null)
        {
            try
            {
                var item = _libraryManager.GetItemById(itemId);
                if (item == null) return NotFound(new { message = "Item not found" });

                var config = Plugin.Instance?.Configuration;
                var options = new VideoProcessingOptions
                {
                    Model = model ?? config?.Model ?? "auto",
                    ScaleFactor = scale ?? config?.ScaleFactor ?? 2,
                    QualityLevel = config?.QualityLevel ?? "medium"
                };

                var outputPath = Path.Combine(
                    Path.GetDirectoryName(item.Path) ?? "",
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
        [Consumes("application/octet-stream")]
        [Produces("application/octet-stream")]
        public async Task<ActionResult> UpscaleImage([FromQuery] string model = "realesrgan", [FromQuery] int scale = 2)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                await Request.Body.CopyToAsync(memoryStream);
                var inputImage = memoryStream.ToArray();
                var upscaledImage = await _upscalerCore.UpscaleImageAsync(inputImage, model, scale);
                return File(upscaledImage, "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Image upscaling failed");
                return StatusCode(500);
            }
        }

        [HttpPost("preprocess")]
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

        [HttpGet("fallback")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetFallbackStatus()
        {
            try
            {
                return Ok(_benchmarkService.GetFallbackStatus());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get fallback status");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
    }
}
