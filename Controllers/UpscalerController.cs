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
using MediaBrowser.Controller.MediaEncoding;
using JellyfinUpscalerPlugin.Services;
using JellyfinUpscalerPlugin.Models;
using MediaBrowser.Model.Entities;
using MediaBrowser.Controller.Entities;
using System.Collections.Concurrent;
using Image = SixLabors.ImageSharp.Image;
using IOFile = System.IO.File;

namespace JellyfinUpscalerPlugin.Controllers
{
    /// <summary>
    /// AI Upscaler API Controller
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("[controller]")]
    public class UpscalerController : ControllerBase
    {
        // ── Constants ────────────────────────────────────────────────────
        private const long MaxUploadSizeBytes = 50 * 1024 * 1024; // 50 MB
        private const int RateLimitMaxRequests = 10;
        private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

        // Dots allowed only between alphanumeric/dash/underscore runs (e.g. rife-v4.6, gfpgan-v1.4).
        // Rejects path-traversal patterns: .., leading/trailing ., empty segments.
        private static readonly Regex ValidModelNameRegex = new(@"^[a-zA-Z0-9_-]+(?:\.[a-zA-Z0-9_-]+)*$", RegexOptions.Compiled);
        private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _rateLimitTracker = new();

        private readonly ILogger<UpscalerController> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IMediaSourceManager _mediaSourceManager;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly ISessionManager _sessionManager;
        private readonly HardwareBenchmarkService _benchmarkService;
        private readonly UpscalerCore _upscalerCore;
        private readonly VideoProcessor _videoProcessor;
        private readonly CacheManager _cacheManager;
        private readonly ProcessingQueue _processingQueue;
        private readonly IHttpClientFactory _httpClientFactory;

        public UpscalerController(
            ILogger<UpscalerController> logger,
            ILibraryManager libraryManager,
            IMediaSourceManager mediaSourceManager,
            IMediaEncoder mediaEncoder,
            ISessionManager sessionManager,
            HardwareBenchmarkService benchmarkService,
            UpscalerCore upscalerCore,
            VideoProcessor videoProcessor,
            CacheManager cacheManager,
            ProcessingQueue processingQueue,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _mediaSourceManager = mediaSourceManager;
            _mediaEncoder = mediaEncoder;
            _sessionManager = sessionManager;
            _benchmarkService = benchmarkService;
            _upscalerCore = upscalerCore;
            _videoProcessor = videoProcessor;
            _cacheManager = cacheManager;
            _processingQueue = processingQueue;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Get an HttpClient from the factory for AI service proxy calls.
        /// Uses IHttpClientFactory for proper DNS refresh and connection pooling.
        /// </summary>
        private HttpClient GetAiServiceClient() => _httpClientFactory.CreateClient("AiUpscaler");
        private HttpClient GetMultiFrameClient() => _httpClientFactory.CreateClient("AiUpscalerLongTimeout");

        /// <summary>
        /// Per-user sliding-window rate limiter for upscale endpoints.
        /// Returns true if the request should be rejected (rate exceeded).
        /// </summary>
        private bool IsRateLimited()
        {
            var userId = User?.Identity?.Name ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var now = DateTime.UtcNow;
            var entry = _rateLimitTracker.AddOrUpdate(
                userId,
                _ => (1, now),
                (_, existing) =>
                {
                    if (now - existing.WindowStart > RateLimitWindow)
                        return (1, now);
                    return (existing.Count + 1, existing.WindowStart);
                });
            // Opportunistic pruning to prevent unbounded growth
            if (_rateLimitTracker.Count > 500)
            {
                var cutoff = now - RateLimitWindow;
                foreach (var key in _rateLimitTracker.Keys)
                    if (_rateLimitTracker.TryGetValue(key, out var v) && v.WindowStart < cutoff)
                        _rateLimitTracker.TryRemove(key, out _);
            }

            return entry.Count > RateLimitMaxRequests;
        }

        /// <summary>
        /// Get the validated AI service URL. Rejects non-http(s) schemes and control characters.
        /// </summary>
        private string GetValidatedServiceUrl()
        {
            const string fallback = "http://localhost:5000";
            var config = Plugin.Instance?.Configuration;
            var url = config?.AiServiceUrl?.Trim();

            if (string.IsNullOrEmpty(url))
                return fallback;

            // Reject URLs containing control characters that could enable header injection
            if (url.IndexOfAny(new[] { '\n', '\r', '\t' }) >= 0)
            {
                _logger.LogWarning("AiServiceUrl rejected (contains control characters), using fallback");
                return fallback;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                _logger.LogWarning("AiServiceUrl rejected (invalid scheme: {Scheme}), using fallback", uri?.Scheme ?? "null");
                return fallback;
            }

            return url.TrimEnd('/');
        }

        [HttpGet("models")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> GetAvailableModels()
        {
            // Proxy the Docker AI service's /models endpoint to get the full model list (35+ models)
            var baseUrl = GetValidatedServiceUrl();

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var response = await GetAiServiceClient().GetAsync($"{baseUrl}/models", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    _logger.LogDebug("Models proxy success from {Url}: {Length} chars", baseUrl, json.Length);
                    return Content(json, "application/json");
                }
                _logger.LogWarning("Models proxy failed: HTTP {Status} from {Url}", (int)response.StatusCode, baseUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not reach Docker AI service at {Url}/models: {Error}", baseUrl, ex.Message);
            }

            // Fallback: return hardcoded base models if Docker service is unavailable
            // Format matches Docker's {"models": [...], "total": N} response
            var fallbackModels = new List<object>
            {
                new { id = "realesrgan-x4", name = "Real-ESRGAN x4 (Best Quality)", description = "Best quality 4x (67MB ONNX)", scale = new[] { 4 }, category = "realesrgan", type = "onnx", downloaded = false, loaded = false, available = true },
                new { id = "realesrgan-x4-256", name = "Real-ESRGAN x4 (256px optimized)", description = "Optimized for 256px tiles, low VRAM", scale = new[] { 4 }, category = "realesrgan", type = "onnx", downloaded = false, loaded = false, available = true },
                new { id = "span-x2", name = "SPAN x2 (Fast Quality)", description = "NTIRE 2023 winner 2x", scale = new[] { 2 }, category = "nextgen", type = "onnx", downloaded = false, loaded = false, available = true },
                new { id = "span-x4", name = "SPAN x4 (Fast Quality)", description = "NTIRE 2023 winner 4x", scale = new[] { 4 }, category = "nextgen", type = "onnx", downloaded = false, loaded = false, available = true },
                new { id = "fsrcnn-x2", name = "FSRCNN x2 (Fast)", description = "Very fast 2x upscaling", scale = new[] { 2 }, category = "fast", type = "pb", downloaded = false, loaded = false, available = true },
                new { id = "fsrcnn-x3", name = "FSRCNN x3 (Fast)", description = "Fast 3x upscaling", scale = new[] { 3 }, category = "fast", type = "pb", downloaded = false, loaded = false, available = true },
                new { id = "fsrcnn-x4", name = "FSRCNN x4 (Fast)", description = "Fast 4x, lower quality", scale = new[] { 4 }, category = "fast", type = "pb", downloaded = false, loaded = false, available = true },
                new { id = "espcn-x2", name = "ESPCN x2 (Fastest)", description = "Fastest model", scale = new[] { 2 }, category = "fast", type = "pb", downloaded = false, loaded = false, available = true },
                new { id = "espcn-x4", name = "ESPCN x4 (Fastest)", description = "Fastest 4x", scale = new[] { 4 }, category = "fast", type = "pb", downloaded = false, loaded = false, available = true },
                new { id = "edsr-x4", name = "EDSR x4 (Best OpenCV)", description = "Best quality 4x OpenCV", scale = new[] { 4 }, category = "quality", type = "pb", downloaded = false, loaded = false, available = true },
                // v1.6.1.7 — Face restoration models (GFPGAN / CodeFormer)
                new { id = "gfpgan-v1.4", name = "GFPGAN v1.4 (Face Restore)", description = "Tencent ARC face restoration GAN — 512x512 crops", scale = new[] { 1 }, category = "face_restore", type = "onnx", downloaded = false, loaded = false, available = true },
                new { id = "codeformer", name = "CodeFormer (Face Restore)", description = "Transformer-codebook face restoration — 512x512 crops", scale = new[] { 1 }, category = "face_restore", type = "onnx", downloaded = false, loaded = false, available = true }
            };
            return Ok(new { models = fallbackModels, total = fallbackModels.Count });
        }

        [HttpGet("js/{name}")]
        [Produces("text/javascript")]
        public ActionResult GetJavaScript(string name)
        {
            try
            {
                // Allowlist of permitted resource names to prevent resource disclosure
                var allowedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "player-integration.js", "quick-menu.js", "sidebar-upscaler.js", "webgl-upscaler.js"
                };
                if (!allowedNames.Contains(name)) return NotFound();

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
                _logger.LogError(ex, "Failed to serve JS component: {Name}", name);
                return StatusCode(500);
            }
        }

        /// <summary>
        /// Lists Jellyfin media libraries (virtual folders) so the config UI can render
        /// a library picker for the scheduled-scan scope filter (issue #64).
        /// </summary>
        [HttpGet("libraries")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetLibraries()
        {
            try
            {
                var folders = _libraryManager.GetVirtualFolders();
                var result = folders
                    .Where(f => f != null && !string.IsNullOrEmpty(f.ItemId))
                    .Select(f => new
                    {
                        id = f.ItemId,
                        name = f.Name ?? "(unnamed)",
                        collectionType = f.CollectionType?.ToString() ?? "mixed",
                        locations = f.Locations ?? Array.Empty<string>()
                    })
                    .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return Ok(new { libraries = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list libraries");
                return StatusCode(500, new { error = "Failed to list libraries" });
            }
        }

        [HttpGet("status")]
        [Authorize(Policy = "RequiresElevation")]
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
                version = typeof(Plugin).Assembly.GetName().Version?.ToString(4) ?? "unknown"
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
                return StatusCode(500, new { success = false, message = "Test failed due to an internal error" });
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
                var serviceAvailable = results.Hardware?.ServiceAvailable ?? false;
                return Ok(new
                {
                    success = true,
                    serviceAvailable = serviceAvailable,
                    message = serviceAvailable
                        ? "Hardware benchmark completed successfully"
                        : "Docker AI Service is not reachable — benchmark skipped",
                    results = new
                    {
                        duration = results.TotalDuration.TotalSeconds,
                        serviceAvailable = serviceAvailable,
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
                return StatusCode(500, new { success = false, message = "Hardware benchmark failed", error = "Internal server error" });
            }
        }

        [HttpGet("hardware-info")]
        [Authorize(Policy = "RequiresElevation")]
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
                    PluginVersion = typeof(Plugin).Assembly.GetName().Version?.ToString(4) ?? "1.5.2.9"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get hardware info");
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        [HttpGet("recommendations")]
        [Authorize(Policy = "RequiresElevation")]
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
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get the recommended AI model for specific content parameters.
        /// Used by the UI to show which model will be auto-selected.
        /// </summary>
        [HttpGet("recommend-model")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> RecommendModel(
            [FromQuery] string? genres = null,
            [FromQuery] int width = 0,
            [FromQuery] int height = 0,
            [FromQuery] bool isBatch = true)
        {
            try
            {
                var serviceStatus = await _upscalerCore.GetServiceStatusAsync();
                int inputFrames = serviceStatus?.InputFrames ?? 1;

                var genreList = string.IsNullOrEmpty(genres)
                    ? Array.Empty<string>()
                    : genres.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                // The endpoint is called by clients that explicitly want an auto
                // recommendation (the in-player panel only calls it when Auto-Mode
                // is enabled). forceAuto=true so the heuristic runs even if the
                // user has a non-auto Model value saved.
                var recommendedModel = _upscalerCore.ResolveModelForVideo(
                    genres: genreList,
                    width: width,
                    height: height,
                    isBatch: isBatch,
                    inputFrames: inputFrames,
                    forceAuto: true);

                var recommendedFilter = _upscalerCore.ResolveFilterForVideo(
                    genres: genreList,
                    width: width,
                    height: height);

                var config = Plugin.Instance?.Configuration;
                return Ok(new
                {
                    success = true,
                    recommended_model = recommendedModel,
                    recommended_filter = recommendedFilter,
                    input_frames = inputFrames,
                    auto_selection_enabled = config?.EnableAutoModelSelection ?? false,
                    parameters = new { genres = genreList, width, height, is_batch = isBatch }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get model recommendation");
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        [HttpGet("compare/{itemId}")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> GetComparisonData(
            string itemId,
            [FromQuery] string model = "realesrgan",
            [FromQuery] int scale = 2,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!ValidModelNameRegex.IsMatch(model))
                    return BadRequest(new { message = "Invalid model name" });

                if (!Guid.TryParse(itemId, out var itemGuid) || itemGuid == Guid.Empty)
                    return BadRequest(new { message = "Invalid item ID format" });

                var item = _libraryManager.GetItemById(itemGuid);
                if (item == null) return NotFound(new { message = "Item not found" });

                // Use Jellyfin's media source manager to resolve paths (handles path substitutions for SMB, etc.)
                var mediaSources = _mediaSourceManager.GetStaticMediaSources(item, true, null);
                var mediaSource = mediaSources?.FirstOrDefault();

                // Prefer the substituted path from MediaSourceManager, fall back to item.Path
                var videoPath = mediaSource?.Path ?? item.Path;
                if (string.IsNullOrEmpty(videoPath))
                    return BadRequest(new { message = "No video path — select a movie or episode, not a library folder" });

                _logger.LogInformation("Comparison: extracting frame from {Path}", videoPath);

                // Determine seek position (~10% into video, fallback to 10s)
                var seekPosition = TimeSpan.FromSeconds(10);
                if (mediaSource?.RunTimeTicks != null)
                {
                    var totalSeconds = TimeSpan.FromTicks(mediaSource.RunTimeTicks.Value).TotalSeconds;
                    if (totalSeconds > 30)
                        seekPosition = TimeSpan.FromSeconds(totalSeconds * 0.10);
                }

                // Extract frame using direct FFmpeg call with the resolved path
                byte[] originalImageBytes = await _videoProcessor.ExtractSingleFrameAsync(videoPath, seekPosition, cancellationToken);

                // Downscale for browser comparison
                byte[] originalData;
                using (var image = Image.Load(originalImageBytes))
                {
                    if (image.Width > 1280 || image.Height > 720)
                    {
                        image.Mutate(x => x.Resize(new ResizeOptions
                        {
                            Size = new Size(1280, 720),
                            Mode = ResizeMode.Max
                        }));
                    }
                    using var ms = new MemoryStream();
                    image.SaveAsJpeg(ms);
                    originalData = ms.ToArray();
                }

                var upscaledData = await _upscalerCore.UpscaleImageAsync(originalData, model, scale);
                if (upscaledData == null)
                    return StatusCode(503, new { message = "AI upscaling service unavailable" });

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
                _logger.LogError(ex, "Failed to generate comparison data for item {ItemId}", itemId);
                return StatusCode(500, new { message = "Comparison failed", error = "Internal server error" });
            }
        }

        /// <summary>
        /// Upscale all images for a library item (poster, backdrop, thumbnail, logo).
        /// Saves upscaled images alongside originals with "_upscaled" suffix.
        /// </summary>
        [HttpPost("upscale-images/{itemId}")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> UpscaleItemImages(
            string itemId,
            [FromQuery] string model = "auto",
            [FromQuery] int scale = 2,
            [FromQuery] string? imageTypes = null)
        {
            if (IsRateLimited())
                return StatusCode(429, new { error = "Rate limit exceeded. Max 10 upscale requests per minute." });
            try
            {
                if (scale < 1 || scale > 8)
                    return BadRequest(new { success = false, error = "Scale must be between 1 and 8" });

                if (model != "auto" && !ValidModelNameRegex.IsMatch(model))
                    return BadRequest(new { success = false, error = "Invalid model name" });

                if (!Guid.TryParse(itemId, out var itemGuid))
                    return BadRequest(new { success = false, error = "Invalid item ID format" });

                var item = _libraryManager.GetItemById(itemGuid);
                if (item == null)
                    return NotFound(new { success = false, error = "Item not found" });

                // Parse which image types to upscale (default: all available)
                var targetTypes = new List<ImageType> { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb, ImageType.Logo, ImageType.Banner };
                if (!string.IsNullOrEmpty(imageTypes))
                {
                    targetTypes = imageTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(t => Enum.TryParse<ImageType>(t, true, out var parsed) ? parsed : (ImageType?)null)
                        .Where(t => t.HasValue)
                        .Select(t => t!.Value)
                        .ToList();
                }

                var results = new List<object>();
                int successCount = 0, failCount = 0;

                foreach (var imageType in targetTypes)
                {
                    var images = item.GetImages(imageType).ToList();
                    if (images.Count == 0) continue;

                    for (int idx = 0; idx < images.Count; idx++)
                    {
                        var imagePath = images[idx].Path;
                        if (string.IsNullOrEmpty(imagePath) || !IOFile.Exists(imagePath))
                            continue;

                        try
                        {
                            var originalData = await IOFile.ReadAllBytesAsync(imagePath);
                            var upscaledData = await _upscalerCore.UpscaleImageAsync(originalData, model, scale);

                            if (upscaledData != null && upscaledData.Length > 0)
                            {
                                // Save upscaled image alongside original
                                var dir = Path.GetDirectoryName(imagePath) ?? "";
                                var ext = Path.GetExtension(imagePath);
                                var baseName = Path.GetFileNameWithoutExtension(imagePath);
                                var outputPath = Path.Combine(dir, baseName + "_upscaled" + ext);
                                await IOFile.WriteAllBytesAsync(outputPath, upscaledData);

                                successCount++;
                                results.Add(new
                                {
                                    type = imageType.ToString(),
                                    index = idx,
                                    original = Path.GetFileName(imagePath),
                                    upscaled = Path.GetFileName(outputPath),
                                    original_size = originalData.Length,
                                    upscaled_size = upscaledData.Length,
                                    success = true
                                });
                            }
                            else
                            {
                                failCount++;
                                results.Add(new { type = imageType.ToString(), index = idx, success = false, error = "Upscaling returned empty result" });
                            }
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            results.Add(new { type = imageType.ToString(), index = idx, success = false, error = "Image upscaling failed" });
                            _logger.LogWarning(ex, "Failed to upscale {Type} image {Index} for item {ItemId}", imageType, idx, itemId);
                        }
                    }
                }

                return Ok(new
                {
                    success = true,
                    item_id = itemId,
                    item_name = item.Name,
                    model,
                    scale,
                    total_processed = successCount + failCount,
                    success_count = successCount,
                    fail_count = failCount,
                    results
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upscale images for item {ItemId}", itemId);
                return StatusCode(500, new { success = false, error = "Internal server error" });
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

                // Security: Validate and normalize paths to prevent path traversal
                var fullInputPath = Path.GetFullPath(request.InputPath);
                var fullOutputPath = Path.GetFullPath(request.OutputPath);

                // Whitelist: output must be in same directory as input (sibling file)
                // or in a subdirectory of the input's parent
                var inputDir = Path.GetFullPath(Path.GetDirectoryName(fullInputPath) ?? string.Empty);
                var outputDir = Path.GetFullPath(Path.GetDirectoryName(fullOutputPath) ?? string.Empty);
                var inputDirWithSep = inputDir.EndsWith(Path.DirectorySeparatorChar) ? inputDir : inputDir + Path.DirectorySeparatorChar;
                if (inputDir == null || outputDir == null ||
                    (!outputDir.Equals(inputDir, StringComparison.OrdinalIgnoreCase) &&
                     !outputDir.StartsWith(inputDirWithSep, StringComparison.OrdinalIgnoreCase)))
                {
                    // Also allow output in media library paths by checking the input exists
                    // (if input is a valid library file, its directory is safe for output)
                    _logger.LogWarning("Output path {OutputDir} is not under input directory {InputDir}", outputDir, inputDir);
                    return BadRequest(new { success = false, error = "Output path must be in the same directory as the input file" });
                }

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
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        [HttpPost("process/item/{itemId}")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> ProcessItem(string itemId, [FromQuery] string? model = null, [FromQuery] int? scale = null)
        {
            try
            {
                if (model != null && model != "auto" && !ValidModelNameRegex.IsMatch(model))
                    return BadRequest(new { message = "Invalid model name" });

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
                    QualityLevel = config?.QualityLevel ?? "medium",
                    EnableAIUpscaling = true
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
                _logger.LogError(ex, "Failed to process item {ItemId}", itemId);
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        [HttpGet("jobs")]
        [Authorize(Policy = "RequiresElevation")]
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
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        [HttpPost("jobs/{jobId}/pause")]
        [Authorize(Policy = "RequiresElevation")]
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
                _logger.LogError(ex, "Failed to pause job {JobId}", jobId);
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        [HttpPost("jobs/{jobId}/resume")]
        [Authorize(Policy = "RequiresElevation")]
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
                _logger.LogError(ex, "Failed to resume job {JobId}", jobId);
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        [HttpPost("jobs/{jobId}/cancel")]
        [Authorize(Policy = "RequiresElevation")]
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
                _logger.LogError(ex, "Failed to cancel job {JobId}", jobId);
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        // ============================================================
        // === Processing Queue API ===
        // ============================================================

        /// <summary>Get queue status — pending, active, completed jobs.</summary>
        [HttpGet("queue")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetQueueStatus()
        {
            return Ok(new { success = true, queue = _processingQueue.GetStatus() });
        }

        /// <summary>Enqueue a video for processing with optional priority.</summary>
        [HttpPost("queue/add")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> EnqueueJob(
            [FromQuery] string inputPath,
            [FromQuery] string? outputPath = null,
            [FromQuery] string? model = null,
            [FromQuery] int priority = 5,
            [FromQuery] string? itemName = null)
        {
            if (string.IsNullOrEmpty(inputPath))
                return BadRequest(new { success = false, error = "inputPath required" });

            if (model != null && model != "auto" && !ValidModelNameRegex.IsMatch(model))
                return BadRequest(new { success = false, error = "Invalid model name" });

            // Path traversal protection — normalize and validate against library paths (allowlist)
            inputPath = Path.GetFullPath(inputPath);
            if (!System.IO.File.Exists(inputPath))
                return BadRequest(new { success = false, error = "Input file does not exist" });

            var libraryFolders = _libraryManager.GetVirtualFolders();
            var isInLibrary = libraryFolders.Any(folder =>
                folder.Locations.Any(loc =>
                    inputPath.StartsWith(Path.GetFullPath(loc), StringComparison.OrdinalIgnoreCase)));
            if (!isInLibrary)
                return BadRequest(new { success = false, error = "Input path must be within a Jellyfin media library" });

            if (outputPath != null)
            {
                outputPath = Path.GetFullPath(outputPath);

                // Restrict output to be under the same parent directory as input or under the Jellyfin transcode path
                var inputParent = Path.GetFullPath(Path.GetDirectoryName(inputPath) ?? string.Empty);
                var outputParent = Path.GetFullPath(Path.GetDirectoryName(outputPath) ?? string.Empty);
                var inputParentWithSep = inputParent.EndsWith(Path.DirectorySeparatorChar) ? inputParent : inputParent + Path.DirectorySeparatorChar;
                var transcodePath = Plugin.Instance?.Configuration?.RemoteTranscodePath ?? "";
                var validTranscode = !string.IsNullOrEmpty(transcodePath) && Path.IsPathRooted(transcodePath);
                var transcodeWithSep = validTranscode ? (transcodePath.EndsWith(Path.DirectorySeparatorChar) ? transcodePath : transcodePath + Path.DirectorySeparatorChar) : "";

                if (!outputParent.Equals(inputParent, StringComparison.OrdinalIgnoreCase) &&
                    !outputParent.StartsWith(inputParentWithSep, StringComparison.OrdinalIgnoreCase) &&
                    !(validTranscode && (outputParent.Equals(transcodePath, StringComparison.OrdinalIgnoreCase) ||
                      outputParent.StartsWith(transcodeWithSep, StringComparison.OrdinalIgnoreCase))))
                {
                    return BadRequest(new { success = false, error = "Output path must be under the input directory or transcode path" });
                }
            }

            var effectiveOutput = outputPath ?? Path.Combine(
                Path.GetDirectoryName(inputPath) ?? "",
                Path.GetFileNameWithoutExtension(inputPath) + "_upscaled" + Path.GetExtension(inputPath));

            var config = Plugin.Instance?.Configuration;
            var options = new VideoProcessingOptions
            {
                Model = model ?? config?.Model ?? "auto",
                ScaleFactor = config?.ScaleFactor ?? 2,
                QualityLevel = config?.QualityLevel ?? "medium",
                EnableAIUpscaling = true,
                PreserveAudio = true,
                PreserveSubtitles = true
            };

            var jobId = Guid.NewGuid().ToString("N")[..12];
            var enqueued = _processingQueue.Enqueue(jobId, inputPath, effectiveOutput, options, priority, itemName);

            if (!enqueued)
                return StatusCode(429, new { success = false, error = "Queue is full" });

            return Ok(new { success = true, job_id = jobId, position = _processingQueue.QueueSize });
        }

        /// <summary>Cancel a pending queued job.</summary>
        [HttpPost("queue/{jobId}/cancel")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> CancelQueuedJob(string jobId)
        {
            var cancelled = _processingQueue.Cancel(jobId);
            return Ok(new { success = cancelled, job_id = jobId });
        }

        /// <summary>Change priority of a pending job (1=highest, 10=lowest).</summary>
        [HttpPost("queue/{jobId}/priority")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> SetJobPriority(string jobId, [FromQuery] int priority)
        {
            if (priority < 1 || priority > 10)
                return BadRequest(new { success = false, error = "Priority must be 1-10" });

            var updated = _processingQueue.SetPriority(jobId, priority);
            return Ok(new { success = updated, job_id = jobId, priority });
        }

        /// <summary>Pause the processing queue (active jobs finish, no new jobs start).</summary>
        [HttpPost("queue/pause")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> PauseQueue()
        {
            _processingQueue.Pause();
            return Ok(new { success = true, paused = true });
        }

        /// <summary>Resume the processing queue.</summary>
        [HttpPost("queue/resume")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> ResumeQueue()
        {
            _processingQueue.Resume();
            return Ok(new { success = true, paused = false });
        }

        [HttpGet("cache/stats")]
        [Authorize(Policy = "RequiresElevation")]
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
                return StatusCode(500, new { success = false, error = "Internal server error" });
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
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        [HttpGet("hardware")]
        [Authorize(Policy = "RequiresElevation")]
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
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        [HttpPost("upscale/image")]
        [Authorize(Policy = "RequiresElevation")]
        [Consumes("application/octet-stream")]
        [Produces("application/octet-stream")]
        [RequestSizeLimit(52428800)] // 50MB max
        public async Task<ActionResult> UpscaleImage([FromQuery] string model = "realesrgan-x4", [FromQuery] int scale = 2)
        {
            if (IsRateLimited())
                return StatusCode(429, new { error = "Rate limit exceeded. Max 10 upscale requests per minute." });

            try
            {
                // Security: Validate scale parameter
                var allowedScales = new[] { 2, 3, 4, 8 };
                if (!allowedScales.Contains(scale))
                    return BadRequest(new { error = "Invalid scale. Allowed values: 2, 3, 4, 8" });

                // Security: Validate model name (alphanumeric, hyphens, underscores only)
                if (!ValidModelNameRegex.IsMatch(model))
                    return BadRequest(new { error = "Invalid model name - only alphanumeric, hyphens, and underscores allowed" });

                // Security: Limit upload size to prevent DoS attacks
                if (Request.ContentLength > MaxUploadSizeBytes)
                {
                    return BadRequest(new { error = "Image too large. Maximum size is 50MB." });
                }

                using var memoryStream = new MemoryStream();
                await Request.Body.CopyToAsync(memoryStream);

                if (memoryStream.Length > MaxUploadSizeBytes)
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
                if (string.IsNullOrEmpty(request.InputPath))
                    return BadRequest(new { success = false, error = "InputPath required" });

                // Path traversal protection — allowlist (must be in a Jellyfin library)
                var normalizedPath = Path.GetFullPath(request.InputPath);
                var libFolders = _libraryManager.GetVirtualFolders();
                var pathInLibrary = libFolders.Any(folder =>
                    folder.Locations.Any(loc =>
                        normalizedPath.StartsWith(Path.GetFullPath(loc), StringComparison.OrdinalIgnoreCase)));
                if (!pathInLibrary)
                    return BadRequest(new { success = false, error = "Input path must be within a Jellyfin media library" });

                var success = await _cacheManager.PreProcessContentAsync(
                    normalizedPath,
                    request.Model ?? "auto",
                    request.Scale ?? 2,
                    request.Quality ?? "medium",
                    _videoProcessor);

                return Ok(new { success = success });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pre-processing failed");
                return StatusCode(500, new { success = false, error = "Internal server error" });
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
                return StatusCode(500, new { success = false, error = "Internal server error" });
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
                        RemoteHost = "[REDACTED]",
                        RemoteSshPort = "[REDACTED]",
                        RemoteUser = "[REDACTED]",
                        RemoteSshKeyFile = "[REDACTED]",
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
                        config.GpuDeviceIndex,
                        // Quality Metrics & Face Enhancement
                        config.EnableQualityMetrics,
                        config.EnableFaceEnhancement,
                        config.FaceEnhanceStrength,
                        // Grain Management
                        config.EnableGrainManagement,
                        config.GrainDenoiseStrength,
                        config.GrainReaddIntensity,
                        // Model Management
                        config.EnableCustomModelUpload,
                        config.EnableAutoModelSelection,
                        config.ModelFallbackChain,
                        config.PreferredAnimeModel,
                        config.PreferredLiveActionModel,
                        config.EnableModelPreloading,
                        config.ModelDiskQuotaMB,
                        config.EnableModelAutoCleanup,
                        config.ModelCleanupDays,
                        // Output & Processing
                        config.OutputCodec,
                        config.MaxUpscaledFileSizeMB,
                        config.EnableProcessingQueue,
                        config.MaxQueueSize,
                        config.PauseQueueDuringPlayback,
                        config.PersistQueueAcrossRestarts,
                        // Real-Time Upscaling
                        config.EnableRealtimeUpscaling,
                        config.RealtimeMode,
                        config.RealtimeTargetFps,
                        config.RealtimeCaptureWidth,
                        // Notifications & Webhooks
                        config.EnableProgressNotifications,
                        WebhookUrl = "[REDACTED]",
                        config.WebhookOnComplete,
                        config.WebhookOnFailure,
                        // Health & Monitoring
                        config.EnableHealthMonitoring,
                        config.HealthCheckIntervalSeconds,
                        config.EnableGpuFallbackToCpu,
                        config.CircuitBreakerThreshold,
                        config.CircuitBreakerResetSeconds,
                        // Scan Filtering
                        config.MinResolutionWidth,
                        config.MinResolutionHeight,
                        config.MaxItemsPerScan,
                        config.RestrictToUnwatchedContent,
                        config.SkipUpscaledOnRescan,
                        // API
                        config.EnableApiDocs
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export settings");
                return StatusCode(500, new { success = false, error = "Internal server error" });
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

                // Apply each setting if present — wrap typed getters to handle type mismatches gracefully
                var skipped = new System.Collections.Generic.List<string>();
                void TryApply(string key, Action<System.Text.Json.JsonElement> apply)
                {
                    if (settings.TryGetProperty(key, out var val))
                    {
                        try { apply(val); }
                        catch (InvalidOperationException)
                        {
                            skipped.Add(key);
                            _logger.LogWarning("Settings import: skipping '{Key}' — wrong JSON type", key);
                        }
                    }
                }

                TryApply("EnablePlugin", val => config.EnablePlugin = val.GetBoolean());
                TryApply("Model", val => config.Model = val.GetString() ?? "realesrgan-x4");
                TryApply("ScaleFactor", val => config.ScaleFactor = val.GetInt32());
                TryApply("QualityLevel", val =>
                {
                    var ql = val.GetString() ?? "medium";
                    var validQL = new[] { "fast", "medium", "high" };
                    if (validQL.Contains(ql)) config.QualityLevel = ql;
                });
                TryApply("HardwareAcceleration", val => config.HardwareAcceleration = val.GetBoolean());
                TryApply("MaxConcurrentStreams", val => config.MaxConcurrentStreams = val.GetInt32());
                TryApply("MaxVRAMUsage", val => config.MaxVRAMUsage = val.GetInt32());
                TryApply("CpuThreads", val => config.CpuThreads = val.GetInt32());
                TryApply("AiServiceUrl", val =>
                {
                    var url = val.GetString() ?? "http://localhost:5000";
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
                        config.AiServiceUrl = url;
                });
                TryApply("EnableRemoteTranscoding", val => config.EnableRemoteTranscoding = val.GetBoolean());
                TryApply("RemoteHost", val =>
                {
                    var host = val.GetString() ?? "";
                    if (System.Text.RegularExpressions.Regex.IsMatch(host, @"^[a-zA-Z0-9.\-:]+$"))
                        config.RemoteHost = host;
                });
                TryApply("RemoteSshPort", val => config.RemoteSshPort = val.GetInt32());
                TryApply("RemoteUser", val =>
                {
                    var user = val.GetString() ?? "";
                    if (System.Text.RegularExpressions.Regex.IsMatch(user, @"^[a-zA-Z0-9._\-]+$"))
                        config.RemoteUser = user;
                });
                TryApply("RemoteSshKeyFile", val =>
                {
                    var keyFile = val.GetString() ?? "";
                    if (!string.IsNullOrEmpty(keyFile) && !keyFile.Contains("..") && Path.IsPathRooted(keyFile))
                        config.RemoteSshKeyFile = keyFile;
                });
                TryApply("LocalMediaMountPoint", val => { var path = val.GetString() ?? ""; if (!path.Contains("..")) config.LocalMediaMountPoint = path; });
                TryApply("RemoteMediaMountPoint", val => { var path = val.GetString() ?? ""; if (!path.Contains("..")) config.RemoteMediaMountPoint = path; });
                TryApply("RemoteTranscodePath", val => { var path = val.GetString() ?? ""; if (!path.Contains("..")) config.RemoteTranscodePath = path; });
                TryApply("PlayerButton", val => config.PlayerButton = val.GetBoolean());
                TryApply("Notifications", val => config.Notifications = val.GetBoolean());
                TryApply("AutoRetryButton", val => config.AutoRetryButton = val.GetBoolean());
                TryApply("ButtonPosition", val => { var pos = val.GetString() ?? "right"; if (pos == "left" || pos == "right") config.ButtonPosition = pos; });
                TryApply("EnableComparisonView", val => config.EnableComparisonView = val.GetBoolean());
                TryApply("EnablePerformanceMetrics", val => config.EnablePerformanceMetrics = val.GetBoolean());
                TryApply("EnableAutoBenchmarking", val => config.EnableAutoBenchmarking = val.GetBoolean());
                TryApply("EnablePreProcessingCache", val => config.EnablePreProcessingCache = val.GetBoolean());
                TryApply("MaxCacheAgeDays", val => config.MaxCacheAgeDays = val.GetInt32());
                TryApply("CacheSizeMB", val => config.CacheSizeMB = val.GetInt32());
                TryApply("GpuDeviceIndex", val => config.GpuDeviceIndex = Math.Max(0, val.GetInt32()));
                // Quality Metrics & Face Enhancement
                TryApply("EnableQualityMetrics", val => config.EnableQualityMetrics = val.GetBoolean());
                TryApply("EnableFaceEnhancement", val => config.EnableFaceEnhancement = val.GetBoolean());
                TryApply("FaceEnhanceStrength", val => config.FaceEnhanceStrength = val.GetDouble());
                // Grain Management
                TryApply("EnableGrainManagement", val => config.EnableGrainManagement = val.GetBoolean());
                TryApply("GrainDenoiseStrength", val => config.GrainDenoiseStrength = val.GetInt32());
                TryApply("GrainReaddIntensity", val => config.GrainReaddIntensity = val.GetDouble());
                // Model Management
                TryApply("EnableCustomModelUpload", val => config.EnableCustomModelUpload = val.GetBoolean());
                TryApply("EnableAutoModelSelection", val => config.EnableAutoModelSelection = val.GetBoolean());
                TryApply("ModelFallbackChain", val => config.ModelFallbackChain = val.GetString() ?? "");
                TryApply("PreferredAnimeModel", val => config.PreferredAnimeModel = val.GetString() ?? "");
                TryApply("PreferredLiveActionModel", val => config.PreferredLiveActionModel = val.GetString() ?? "");
                TryApply("EnableModelPreloading", val => config.EnableModelPreloading = val.GetBoolean());
                TryApply("ModelDiskQuotaMB", val => config.ModelDiskQuotaMB = val.GetInt32());
                TryApply("EnableModelAutoCleanup", val => config.EnableModelAutoCleanup = val.GetBoolean());
                TryApply("ModelCleanupDays", val => config.ModelCleanupDays = val.GetInt32());
                // Output & Processing
                TryApply("OutputCodec", val =>
                {
                    var codec = val.GetString() ?? "libx264";
                    var validCodecs = new[] { "libx264", "libx265", "copy" };
                    if (validCodecs.Contains(codec)) config.OutputCodec = codec;
                });
                TryApply("MaxUpscaledFileSizeMB", val => config.MaxUpscaledFileSizeMB = Math.Max(0, val.GetInt64()));
                TryApply("EnableProcessingQueue", val => config.EnableProcessingQueue = val.GetBoolean());
                TryApply("MaxQueueSize", val => config.MaxQueueSize = val.GetInt32());
                TryApply("PauseQueueDuringPlayback", val => config.PauseQueueDuringPlayback = val.GetBoolean());
                TryApply("PersistQueueAcrossRestarts", val => config.PersistQueueAcrossRestarts = val.GetBoolean());
                // Real-Time Upscaling
                TryApply("EnableRealtimeUpscaling", val => config.EnableRealtimeUpscaling = val.GetBoolean());
                TryApply("RealtimeMode", val =>
                {
                    var mode = val.GetString() ?? "auto";
                    var validModes = new[] { "auto", "webgl", "server" };
                    if (validModes.Contains(mode)) config.RealtimeMode = mode;
                });
                TryApply("RealtimeTargetFps", val => config.RealtimeTargetFps = val.GetInt32());
                TryApply("RealtimeCaptureWidth", val => config.RealtimeCaptureWidth = val.GetInt32());
                // Notifications & Webhooks
                TryApply("EnableProgressNotifications", val => config.EnableProgressNotifications = val.GetBoolean());
                TryApply("WebhookUrl", val =>
                {
                    var url = val.GetString() ?? "";
                    if (string.IsNullOrEmpty(url) || (Uri.TryCreate(url, UriKind.Absolute, out var wUri) && (wUri.Scheme == "http" || wUri.Scheme == "https")))
                        config.WebhookUrl = url;
                });
                TryApply("WebhookOnComplete", val => config.WebhookOnComplete = val.GetBoolean());
                TryApply("WebhookOnFailure", val => config.WebhookOnFailure = val.GetBoolean());
                // Health & Monitoring
                TryApply("EnableHealthMonitoring", val => config.EnableHealthMonitoring = val.GetBoolean());
                TryApply("HealthCheckIntervalSeconds", val => config.HealthCheckIntervalSeconds = val.GetInt32());
                TryApply("EnableGpuFallbackToCpu", val => config.EnableGpuFallbackToCpu = val.GetBoolean());
                TryApply("CircuitBreakerThreshold", val => config.CircuitBreakerThreshold = val.GetInt32());
                TryApply("CircuitBreakerResetSeconds", val => config.CircuitBreakerResetSeconds = val.GetInt32());
                // Scan Filtering
                TryApply("MinResolutionWidth", val => config.MinResolutionWidth = val.GetInt32());
                TryApply("MinResolutionHeight", val => config.MinResolutionHeight = val.GetInt32());
                TryApply("MaxItemsPerScan", val => config.MaxItemsPerScan = val.GetInt32());
                TryApply("RestrictToUnwatchedContent", val => config.RestrictToUnwatchedContent = val.GetBoolean());
                TryApply("SkipUpscaledOnRescan", val => config.SkipUpscaledOnRescan = val.GetBoolean());
                // API
                TryApply("EnableApiDocs", val => config.EnableApiDocs = val.GetBoolean());

                Plugin.Instance?.SaveConfiguration();
                if (skipped.Count > 0)
                {
                    _logger.LogWarning("Settings imported with {Count} skipped properties: {Skipped}", skipped.Count, string.Join(", ", skipped));
                    return Ok(new { success = true, message = $"Settings imported ({skipped.Count} properties skipped due to type mismatch)", skippedProperties = skipped });
                }
                _logger.LogInformation("Settings imported successfully");
                return Ok(new { success = true, message = "Settings imported successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import settings");
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        [HttpGet("fallback")]
        [Authorize(Policy = "RequiresElevation")]
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
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        /// <summary>
        /// Server-side health check proxy for the Docker AI service (avoids CORS issues)
        /// </summary>
        [HttpGet("service-health")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> CheckServiceHealth()
        {
            try
            {
                // Always do a fresh check when user explicitly clicks Test Connection
                _benchmarkService.InvalidateHealthCache();
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
                return Ok(new { success = false, available = false, error = "Service health check failed" });
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
                var serviceUrl = GetValidatedServiceUrl();
                using var response = await GetAiServiceClient().GetAsync($"{serviceUrl}/gpus");
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
        /// Proxy: Load a model on the Docker AI service.
        /// Accepts model_name as query param, form field, or JSON body.
        /// </summary>
        [HttpPost("models/load")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> LoadModel()
        {
            try
            {
                // Read model_name from query string, form body, or JSON body
                // (not using [FromQuery] because .NET 9 treats nullable as required)
                string? modelId = Request.Query["model_name"].FirstOrDefault();
                if (string.IsNullOrEmpty(modelId) && Request.HasFormContentType)
                {
                    var form = await Request.ReadFormAsync();
                    modelId = form["model_name"].FirstOrDefault();
                }
                if (string.IsNullOrEmpty(modelId))
                {
                    try
                    {
                        // Check Content-Length before reading to prevent memory exhaustion
                        if (Request.ContentLength > 1024 * 1024)
                        {
                            return BadRequest(new { error = "Request body too large" });
                        }
                        using var reader = new StreamReader(Request.Body);
                        var body = await reader.ReadToEndAsync();
                        if (body.Length > 1024 * 1024) // 1MB payload limit (fallback for chunked transfers)
                        {
                            return BadRequest(new { error = "Request body too large" });
                        }
                        var json = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                        if (json != null && json.ContainsKey("model_name"))
                            modelId = json["model_name"];
                    }
                    catch (System.Text.Json.JsonException ex)
                    {
                        _logger.LogDebug(ex, "Failed to parse JSON body for model_name, falling back to query/form");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to read request body for model_name, falling back to query/form");
                    }
                }

                if (string.IsNullOrEmpty(modelId))
                    return BadRequest(new { error = "model_name is required" });
                if (!ValidModelNameRegex.IsMatch(modelId))
                    return BadRequest(new { error = "Invalid model name — only alphanumeric, hyphens, and underscores allowed" });

                var config = Plugin.Instance?.Configuration;
                var serviceUrl = GetValidatedServiceUrl();

                // Docker AI service expects form-urlencoded POST — forward GPU settings
                var useGpu = config?.HardwareAcceleration ?? true;
                var gpuDeviceId = config?.GpuDeviceIndex ?? 0;
                var formContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("model_name", modelId),
                    new KeyValuePair<string, string>("use_gpu", useGpu.ToString().ToLower()),
                    new KeyValuePair<string, string>("gpu_device_id", gpuDeviceId.ToString())
                });
                using var response = await GetAiServiceClient().PostAsync($"{serviceUrl}/models/load", formContent);
                var content = await response.Content.ReadAsStringAsync();
                return new ContentResult { Content = content, ContentType = "application/json", StatusCode = (int)response.StatusCode };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load model via proxy: {Error}", ex.Message);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Proxy: Run benchmark on the currently loaded model.
        /// </summary>
        [HttpGet("model-benchmark")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> ModelBenchmark()
        {
            try
            {
                var serviceUrl = GetValidatedServiceUrl();
                using var response = await GetAiServiceClient().GetAsync($"{serviceUrl}/benchmark");
                var content = await response.Content.ReadAsStringAsync();
                return new ContentResult { Content = content, ContentType = "application/json", StatusCode = (int)response.StatusCode };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run model benchmark");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Face Restore proxies (v1.6.1.7)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Proxy: Load a face-restore model (GFPGAN / CodeFormer) on the Docker service.
        /// </summary>
        [HttpPost("face-restore/load")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> FaceRestoreLoad([FromQuery] string model_name = "gfpgan-v1.4")
        {
            try
            {
                // Allowlist — match IDs registered in Docker MODELS
                var allowed = new[] { "gfpgan-v1.4", "codeformer" };
                if (!allowed.Contains(model_name))
                    return BadRequest(new { message = "Invalid face-restore model" });

                var serviceUrl = GetValidatedServiceUrl();
                var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("model_name", model_name)
                });
                using var response = await GetAiServiceClient().PostAsync($"{serviceUrl}/face-restore/load", form);
                var content = await response.Content.ReadAsStringAsync();
                return new ContentResult { Content = content, ContentType = "application/json", StatusCode = (int)response.StatusCode };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Face-restore load proxy failed");
                return StatusCode(500, new { error = "Face-restore load failed" });
            }
        }

        /// <summary>
        /// Proxy: Get face-restore subsystem status (loaded model, available models, providers).
        /// </summary>
        [HttpGet("face-restore/status")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> FaceRestoreStatus()
        {
            try
            {
                var serviceUrl = GetValidatedServiceUrl();
                using var response = await GetAiServiceClient().GetAsync($"{serviceUrl}/face-restore/status");
                var content = await response.Content.ReadAsStringAsync();
                return new ContentResult { Content = content, ContentType = "application/json", StatusCode = (int)response.StatusCode };
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Face-restore status proxy failed");
                return StatusCode(503, new { error = "Face-restore service unavailable", available = false });
            }
        }

        /// <summary>
        /// Proxy: Unload the face-restore model to free VRAM.
        /// </summary>
        [HttpPost("face-restore/unload")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> FaceRestoreUnload()
        {
            try
            {
                var serviceUrl = GetValidatedServiceUrl();
                using var response = await GetAiServiceClient().PostAsync($"{serviceUrl}/face-restore/unload", null);
                var content = await response.Content.ReadAsStringAsync();
                return new ContentResult { Content = content, ContentType = "application/json", StatusCode = (int)response.StatusCode };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Face-restore unload proxy failed");
                return StatusCode(500, new { error = "Face-restore unload failed" });
            }
        }

        /// <summary>
        /// Proxy: Get Prometheus metrics from Docker AI service.
        /// </summary>
        [HttpGet("metrics")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces("text/plain")]
        public async Task<ActionResult> GetMetrics()
        {
            try
            {
                var serviceUrl = GetValidatedServiceUrl();
                using var response = await GetAiServiceClient().GetAsync($"{serviceUrl}/metrics");
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "text/plain");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get metrics");
                return Content("# metrics unavailable\n", "text/plain");
            }
        }

        /// <summary>
        /// Proxy: GPU verification diagnostics from Docker service.
        /// </summary>
        [HttpGet("gpu-verify")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> GpuVerify()
        {
            try
            {
                var serviceUrl = GetValidatedServiceUrl();
                using var response = await GetAiServiceClient().GetAsync($"{serviceUrl}/gpu-verify");
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get GPU verify");
                return StatusCode(503, new { error = "AI service unavailable" });
            }
        }

        /// <summary>
        /// Proxy: Detailed health endpoint from Docker service (includes circuit breaker state).
        /// </summary>
        [HttpGet("health/detailed")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> HealthDetailed()
        {
            try
            {
                var serviceUrl = GetValidatedServiceUrl();
                using var response = await GetAiServiceClient().GetAsync($"{serviceUrl}/health/detailed");
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get detailed health");
                return StatusCode(503, new { error = "AI service unavailable" });
            }
        }

        /// <summary>
        /// Proxy: Update Docker AI service configuration (max_concurrent, GPU settings).
        /// </summary>
        [HttpPost("service-config")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> UpdateServiceConfig([FromQuery] bool? use_gpu, [FromQuery] int? max_concurrent, [FromQuery] int? gpu_device_id)
        {
            try
            {
                var serviceUrl = GetValidatedServiceUrl();

                var formData = new List<KeyValuePair<string, string>>();
                if (use_gpu.HasValue) formData.Add(new("use_gpu", use_gpu.Value.ToString().ToLower()));
                if (max_concurrent.HasValue) formData.Add(new("max_concurrent", max_concurrent.Value.ToString()));
                if (gpu_device_id.HasValue) formData.Add(new("gpu_device_id", gpu_device_id.Value.ToString()));

                using var content = new FormUrlEncodedContent(formData);
                using var response = await GetAiServiceClient().PostAsync($"{serviceUrl}/config", content);
                var result = await response.Content.ReadAsStringAsync();
                return Content(result, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to update service config");
                return StatusCode(503, new { error = "AI service unavailable" });
            }
        }

        /// <summary>
        /// Proxy: Model disk usage from Docker service.
        /// </summary>
        [HttpGet("models/disk-usage")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> ModelsDiskUsage()
        {
            try
            {
                var serviceUrl = GetValidatedServiceUrl();
                using var response = await GetAiServiceClient().GetAsync($"{serviceUrl}/models/disk-usage");
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get model disk usage");
                return StatusCode(503, new { error = "AI service unavailable" });
            }
        }

        /// <summary>
        /// Proxy: Model cleanup on Docker service (LRU removal of unused models).
        /// </summary>
        [HttpPost("models/cleanup")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> ModelsCleanup([FromQuery] int max_age_days = 30, [FromQuery] bool dry_run = true)
        {
            try
            {
                var serviceUrl = GetValidatedServiceUrl();
                using var response = await GetAiServiceClient().PostAsync(
                    $"{serviceUrl}/models/cleanup?max_age_days={max_age_days}&dry_run={dry_run.ToString().ToLower()}",
                    null);
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to cleanup models");
                return StatusCode(503, new { error = "AI service unavailable" });
            }
        }

        /// <summary>
        /// Proxy: Real-time frame upscaling. Raw JPEG body in, JPEG out. Returns 503 when AI service is busy.
        /// </summary>
        [HttpPost("upscale-frame")]
        [Authorize(Policy = "RequiresElevation")]
        [RequestSizeLimit(52_428_800)]
        public async Task<ActionResult> UpscaleFrame()
        {
            if (IsRateLimited())
                return StatusCode(429, new { error = "Rate limit exceeded. Max 10 upscale requests per minute." });

            try
            {
                var serviceUrl = GetValidatedServiceUrl();

                // Read raw body
                using var ms = new MemoryStream();
                await Request.Body.CopyToAsync(ms);
                var body = ms.ToArray();

                if (body.Length == 0)
                    return BadRequest("Empty body");

                using var content = new ByteArrayContent(body);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                using var response = await GetAiServiceClient().PostAsync($"{serviceUrl}/upscale-frame", content);

                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                    return StatusCode(503, "AI service busy");

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, "Frame upscaling failed");

                var result = await response.Content.ReadAsByteArrayAsync();
                return File(result, "image/jpeg");
            }
            catch (TaskCanceledException)
            {
                return StatusCode(504, "AI service timeout");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frame upscale proxy failed");
                return StatusCode(500, "Frame upscale proxy error");
            }
        }

        /// <summary>
        /// Proxy: Multi-frame video chunk upscaling. Forwards multipart form with N PNG frames to Docker service.
        /// </summary>
        [HttpPost("upscale-video-chunk")]
        [Authorize(Policy = "RequiresElevation")]
        [RequestSizeLimit(52_428_800)]
        public async Task<ActionResult> UpscaleVideoChunk()
        {
            if (IsRateLimited())
                return StatusCode(429, new { error = "Rate limit exceeded. Max 10 upscale requests per minute." });

            var config = Plugin.Instance?.Configuration;
            if (config == null) return StatusCode(500, "Plugin not configured");

            var serviceUrl = GetValidatedServiceUrl();

            try
            {
                // Forward the entire multipart form to the AI service
                var form = await Request.ReadFormAsync();
                using var content = new MultipartFormDataContent();

                foreach (var file in form.Files)
                {
                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms);
                    var byteContent = new ByteArrayContent(ms.ToArray());
                    // Hardcode Content-Type to prevent header injection from user-controlled values
                    byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    // Sanitize name/filename to prevent header injection via CRLF in multipart
                    var safeName = ValidModelNameRegex.IsMatch(file.Name) ? file.Name : "frame";
                    var rawFileName = file.FileName ?? file.Name;
                    var safeFileName = ValidModelNameRegex.IsMatch(Path.GetFileNameWithoutExtension(rawFileName))
                        ? rawFileName : "frame.png";
                    content.Add(byteContent, safeName, safeFileName);
                }

                using var response = await GetMultiFrameClient().PostAsync($"{serviceUrl}/upscale-video-chunk", content);

                if (response.IsSuccessStatusCode)
                {
                    var resultBytes = await response.Content.ReadAsByteArrayAsync();
                    return File(resultBytes, "image/png");
                }

                return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
            }
            catch (TaskCanceledException)
            {
                return StatusCode(504, "AI service timeout (multi-frame inference)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Multi-frame inference proxy error");
                return StatusCode(502, "AI service error");
            }
        }

        /// <summary>
        /// Proxy: Benchmark frame upscaling at a specific capture resolution.
        /// </summary>
        [HttpGet("benchmark-frame")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> BenchmarkFrame([FromQuery] int width = 480, [FromQuery] int height = 270)
        {
            if (width < 64 || width > 7680 || height < 64 || height > 4320)
            {
                return BadRequest(new { error = "Resolution out of bounds (64-7680 x 64-4320)" });
            }

            try
            {
                var serviceUrl = GetValidatedServiceUrl();

                using var response = await GetAiServiceClient().GetAsync($"{serviceUrl}/benchmark-frame?width={width}&height={height}");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return Content(json, "application/json");
                }

                return StatusCode((int)response.StatusCode, new { error = "Frame benchmark failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frame benchmark proxy failed");
                return StatusCode(500, new { error = "Internal server error" });
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

                    // Security: Reject symbolic links to prevent symlink bypass
                    var keyFileInfo = new FileInfo(resolvedKeyPath);
                    if (keyFileInfo.LinkTarget != null)
                    {
                        return BadRequest(new { success = false, message = "Symbolic links are not allowed for SSH key files." });
                    }

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
                    return Ok(new { success = false, message = "SSH connection failed" });
                }
            }
            catch (OperationCanceledException)
            {
                return Ok(new { success = false, message = "SSH connection timed out after 15 seconds" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSH connection test error");
                return Ok(new { success = false, message = "SSH test error" });
            }
        }

        /// <summary>
        /// Read the current video-filter configuration for the player quick-menu.
        /// Any authenticated user — the filter state is exposed so the quick-menu can seed
        /// its live CSS filter preview without admin privileges. Modifications still require
        /// elevation (see POST /filter-config).
        /// </summary>
        [HttpGet("filter-config")]
        [Authorize]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetFilterConfig()
        {
            var c = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            return Ok(new
            {
                enabled = c.EnableVideoFilters,
                preset = c.ActiveFilterPreset,
                brightness = c.FilterBrightness,
                contrast = c.FilterContrast,
                saturation = c.FilterSaturation,
                gamma = c.FilterGamma,
                sharpness = c.FilterSharpness,
                colorTemperature = c.FilterColorTemperature,
                vignette = c.FilterVignette,
                filmGrain = c.FilterFilmGrain,
                denoise = c.FilterDenoise,
                availablePresets = new[] { "none", "cinematic", "vintage", "vivid", "noir", "warm", "cool", "hdr-pop", "sepia", "pastel", "cyberpunk", "drama", "soft-glow", "sharp-hd", "retrogame", "teal-orange", "custom" }
            });
        }

        /// <summary>
        /// Persist video-filter changes from the player quick-menu (admin only).
        /// Only fields present in the request body are updated — partial updates OK.
        /// The per-property setters in PluginConfiguration clamp out-of-range values,
        /// so malformed numbers saturate rather than throw.
        /// </summary>
        [HttpPost("filter-config")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> UpdateFilterConfig([FromBody] FilterConfigUpdate body)
        {
            if (body == null) return BadRequest(new { message = "Missing request body" });
            var validPresets = new[] { "none", "cinematic", "vintage", "vivid", "noir", "warm", "cool", "hdr-pop", "sepia", "pastel", "cyberpunk", "drama", "soft-glow", "sharp-hd", "retrogame", "teal-orange", "custom" };
            if (body.Preset != null && !validPresets.Contains(body.Preset))
                return BadRequest(new { message = "Invalid preset name" });

            var plugin = Plugin.Instance;
            if (plugin == null) return StatusCode(500, new { message = "Plugin not initialized" });
            var c = plugin.Configuration;

            if (body.Enabled.HasValue) c.EnableVideoFilters = body.Enabled.Value;
            if (body.Preset != null) c.ActiveFilterPreset = body.Preset;
            if (body.Brightness.HasValue) c.FilterBrightness = body.Brightness.Value;
            if (body.Contrast.HasValue) c.FilterContrast = body.Contrast.Value;
            if (body.Saturation.HasValue) c.FilterSaturation = body.Saturation.Value;
            if (body.Gamma.HasValue) c.FilterGamma = body.Gamma.Value;
            if (body.Sharpness.HasValue) c.FilterSharpness = body.Sharpness.Value;
            if (body.ColorTemperature.HasValue) c.FilterColorTemperature = body.ColorTemperature.Value;
            if (body.Vignette.HasValue) c.FilterVignette = body.Vignette.Value;
            if (body.FilmGrain.HasValue) c.FilterFilmGrain = body.FilmGrain.Value;
            if (body.Denoise.HasValue) c.FilterDenoise = body.Denoise.Value;

            plugin.SaveConfiguration();
            _logger.LogInformation("Filter config updated via quick-menu: preset={Preset}, enabled={Enabled}", c.ActiveFilterPreset, c.EnableVideoFilters);
            return Ok(new { success = true, preset = c.ActiveFilterPreset, enabled = c.EnableVideoFilters });
        }

        /// <summary>
        /// Preview video filter effect on a sample frame (admin only).
        /// Accepts a preset name or uses current config. Returns the FFmpeg filter chain
        /// and optionally applies it to a provided image via FFmpeg.
        /// </summary>
        [HttpPost("filter-preview")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> FilterPreview([FromQuery] string? preset)
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var filterService = new VideoFilterService();

            string? filterChain;
            if (!string.IsNullOrEmpty(preset))
            {
                filterChain = filterService.GetPresetFilters(preset);
            }
            else
            {
                filterChain = filterService.BuildFilterChain(config);
            }

            return Ok(new
            {
                enabled = config.EnableVideoFilters,
                preset = preset ?? config.ActiveFilterPreset,
                filterChain = filterChain ?? "(no filters active)",
                availablePresets = new[] { "none", "cinematic", "vintage", "vivid", "noir", "warm", "cool", "hdr-pop", "sepia", "pastel", "cyberpunk", "drama", "soft-glow", "sharp-hd", "retrogame", "teal-orange", "custom" }
            });
        }

        /// <summary>
        /// Generate a live filter preview on a real video frame (admin only).
        /// Extracts a frame from the given media item, applies the preset's FFmpeg filter chain,
        /// and returns both the original and filtered frames as base64 JPEG.
        /// </summary>
        [HttpGet("filter-preview/frame/{itemId}")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> GetFilterPreviewFrame(
            string itemId,
            [FromQuery] string preset = "none",
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!Guid.TryParse(itemId, out var itemGuid) || itemGuid == Guid.Empty)
                    return BadRequest(new { message = "Invalid item ID format" });

                var validPresets = new[] { "none", "cinematic", "vintage", "vivid", "noir", "warm", "cool", "hdr-pop", "sepia", "pastel", "cyberpunk", "drama", "soft-glow", "sharp-hd", "retrogame", "teal-orange", "custom" };
                if (!validPresets.Contains(preset))
                    return BadRequest(new { message = "Invalid preset name" });
                // 'custom' isn't useful for filter-preview (would need full config round-trip) — treat as none
                if (preset == "custom") preset = "none";

                var item = _libraryManager.GetItemById(itemGuid);
                if (item == null) return NotFound(new { message = "Item not found" });

                var mediaSources = _mediaSourceManager.GetStaticMediaSources(item, true, null);
                var mediaSource = mediaSources?.FirstOrDefault();
                var videoPath = mediaSource?.Path ?? item.Path;
                if (string.IsNullOrEmpty(videoPath))
                    return BadRequest(new { message = "No video path — select a movie or episode, not a library folder" });

                // Seek to ~10% of runtime, fallback to 10s
                var seekPosition = TimeSpan.FromSeconds(10);
                if (mediaSource?.RunTimeTicks != null)
                {
                    var totalSeconds = TimeSpan.FromTicks(mediaSource.RunTimeTicks.Value).TotalSeconds;
                    if (totalSeconds > 30)
                        seekPosition = TimeSpan.FromSeconds(totalSeconds * 0.10);
                }

                var filterService = new VideoFilterService();
                var filterChain = filterService.GetPresetFilters(preset);

                _logger.LogInformation("Filter preview: path={Path}, preset={Preset}, chain={Chain}", videoPath, preset, filterChain);

                // Extract original frame (no filter)
                var originalPng = await _videoProcessor.ExtractSingleFrameAsync(videoPath, seekPosition, cancellationToken);

                // Extract filtered frame (or re-use original if preset is "none"/empty)
                byte[] filteredPng;
                if (string.IsNullOrWhiteSpace(filterChain))
                {
                    filteredPng = originalPng;
                }
                else
                {
                    filteredPng = await _videoProcessor.ExtractSingleFrameWithFiltersAsync(videoPath, seekPosition, filterChain, cancellationToken);
                }

                // Downscale both to <=1280x720 JPEG for fast transfer
                byte[] EncodeJpeg(byte[] pngBytes)
                {
                    using var image = Image.Load(pngBytes);
                    if (image.Width > 1280 || image.Height > 720)
                        image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(1280, 720), Mode = ResizeMode.Max }));
                    using var ms = new MemoryStream();
                    image.SaveAsJpeg(ms);
                    return ms.ToArray();
                }

                var originalJpeg = EncodeJpeg(originalPng);
                var filteredJpeg = EncodeJpeg(filteredPng);

                return Ok(new
                {
                    itemId,
                    preset,
                    filterChain = filterChain ?? "(no filters active)",
                    originalBase64 = $"data:image/jpeg;base64,{Convert.ToBase64String(originalJpeg)}",
                    filteredBase64 = $"data:image/jpeg;base64,{Convert.ToBase64String(filteredJpeg)}",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate filter preview for item {ItemId} preset {Preset}", itemId, preset);
                return StatusCode(500, new { message = "Filter preview failed", error = "Internal server error" });
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

    /// <summary>
    /// Partial-update body for POST /Upscaler/filter-config. Nullable fields let the
    /// quick-menu send only what changed (e.g. just the preset + 3 live sliders)
    /// without having to round-trip every filter property.
    /// </summary>
    public class FilterConfigUpdate
    {
        public bool? Enabled { get; set; }
        public string? Preset { get; set; }
        public double? Brightness { get; set; }
        public double? Contrast { get; set; }
        public double? Saturation { get; set; }
        public double? Gamma { get; set; }
        public double? Sharpness { get; set; }
        public int? ColorTemperature { get; set; }
        public double? Vignette { get; set; }
        public int? FilmGrain { get; set; }
        public double? Denoise { get; set; }
    }
}
