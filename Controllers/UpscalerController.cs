using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
        private readonly Services.ImportCatalogService _importCatalog;

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
            IHttpClientFactory httpClientFactory,
            Services.ImportCatalogService importCatalog)
        {
            _importCatalog = importCatalog;
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

        /// <summary>
        /// v1.8.3.22 — is this path inside a Jellyfin media library?
        ///
        /// Every endpoint that takes a filesystem path from the request body needs this.
        /// They each had their own copy, and ProcessVideo had none at all: the whole class
        /// carries only [Authorize], so ANY authenticated non-admin user could hand it any
        /// path on the server. Combined with ffmpeg's -y that was an arbitrary-overwrite
        /// primitive, and the "input file not found" reply worked as a file-existence
        /// oracle for paths the user has no business probing.
        ///
        /// The comparison appends a directory separator on purpose. A bare StartsWith lets
        /// "/media/mov-private" pass an allowlist that only contains "/media/mov".
        /// </summary>
        private bool IsInsideMediaLibrary(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return false;
            foreach (var folder in _libraryManager.GetVirtualFolders())
            {
                foreach (var loc in folder.Locations)
                {
                    if (IsPathUnderRoot(fullPath, loc)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The actual containment rule, pulled out of <see cref="IsInsideMediaLibrary"/> so it
        /// can be tested for what it does rather than for how it is written.
        ///
        /// It used to live inline, guarded only by a test asserting that the source contained
        /// the string "rootWithSep". Mutation testing showed that guard was hollow: putting the
        /// bypass back — comparing against <c>root</c> instead of <c>rootWithSep</c> — left the
        /// declaration, and therefore the asserted string, untouched, and the suite stayed
        /// green. A test that pins an identifier does not pin behaviour.
        ///
        /// The separator is the whole point: a bare StartsWith lets "/media/mov-private" pass
        /// an allowlist that only contains "/media/mov".
        /// </summary>
        internal static bool IsPathUnderRoot(string fullPath, string? root)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrWhiteSpace(root)) return false;

            var normalisedRoot = Path.GetFullPath(root);
            if (fullPath.Equals(normalisedRoot, StringComparison.OrdinalIgnoreCase)) return true;

            var rootWithSep = normalisedRoot.EndsWith(Path.DirectorySeparatorChar)
                ? normalisedRoot
                : normalisedRoot + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// v1.8.3.22 — refuse to write over a file that already exists.
        ///
        /// The pipeline calls ffmpeg with -y, so naming an existing file as the output
        /// destroys it without a word. The output allowlist only constrains the DIRECTORY,
        /// which means "the other film in the same folder" was always a legal target.
        /// </summary>
        /// <summary>
        /// v1.8.3.27 — extract the AI service's own error text from a failed proxy response.
        ///
        /// FastAPI answers {"detail": "..."}; the proxies used to drop it and substitute a
        /// generic message, so "No model loaded" — which names the fix — reached the user as
        /// "Frame upscaling failed". Returns null when there is nothing useful to add, so the
        /// caller's own message stands rather than being padded with noise.
        /// </summary>
        private static async Task<string?> ReadServiceDetailAsync(HttpResponseMessage response)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body)) return null;
                if (body.Length > 500) body = body.Substring(0, 500);

                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("detail", out var d))
                    {
                        return d.ValueKind == System.Text.Json.JsonValueKind.String ? d.GetString() : d.ToString();
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // Not JSON - an HTML error page from a reverse proxy, say. The raw text is
                    // still more use than nothing.
                }
                return body;
            }
            catch
            {
                return null;
            }
        }

        private static bool WouldOverwriteExistingFile(string fullOutputPath)
            => !string.IsNullOrEmpty(fullOutputPath) && IOFile.Exists(fullOutputPath);
        // v1.7.12 - longer-timeout clients for the two slow operation classes that hit the 120s wall:
        // first-load auto-downloads (~380MB) and CPU benchmarks on weak hardware (#72-class boxes).
        private HttpClient GetDownloadClient() => _httpClientFactory.CreateClient("AiUpscalerDownload");   // 570s (< 600s UI)
        private HttpClient GetBenchmarkClient() => _httpClientFactory.CreateClient("AiUpscalerLongTimeout"); // 300s
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

            // Fallback: load the embedded models-fallback.json resource (auto-generated from
            // docker-ai-service/app/main.py via Scripts/sync-fallback-models.ps1).
            // v1.6.1.17 — replaces a hardcoded 12-model list that drifted from the registry by 24 models.
            return GetEmbeddedFallbackModels();
        }

        /// <summary>
        /// Embedded copy of the model registry, loaded once and cached for the lifetime of the
        /// process. Source: Resources/models-fallback.json (auto-generated from
        /// docker-ai-service/app/main.py:AVAILABLE_MODELS).
        /// </summary>
        private static readonly Lazy<string> _fallbackModelsJson = new(() =>
        {
            var asm = typeof(UpscalerController).Assembly;
            const string resourceName = "JellyfinUpscalerPlugin.Resources.models-fallback.json";
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null) return "{\"models\":[],\"total\":0}";
            using var reader = new System.IO.StreamReader(stream);
            return reader.ReadToEnd();
        });

        /// <summary>
        /// Return the embedded fallback model list (used when the Docker AI service is unreachable).
        /// </summary>
        private ActionResult GetEmbeddedFallbackModels()
        {
            try
            {
                _logger.LogDebug("Returning embedded fallback model list (Docker service unreachable)");
                return Content(_fallbackModelsJson.Value, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load embedded models-fallback.json");
                return Ok(new { models = Array.Empty<object>(), total = 0 });
            }
        }

        /// <summary>
        /// Set of model IDs registered with category="face_restore" in the embedded fallback
        /// registry. Derived once from <see cref="_fallbackModelsJson"/> and used by
        /// <see cref="FaceRestoreLoad"/> to validate the model_name parameter.
        /// </summary>
        /// <remarks>
        /// v1.6.1.21 (P1b) - the FaceRestore backend allowlist used to be hardcoded as
        /// <c>{ "gfpgan-v1.4", "codeformer" }</c>. v1.6.1.19 made the FRONTEND dropdown
        /// auto-populated from the registry, but the backend kept its hardcoded list — meaning
        /// any future face-restore model (e.g. RestoreFormer++) added to AVAILABLE_MODELS
        /// would appear in the UI but get rejected with HTTP 400 from the backend. Asymmetric
        /// drift. This Lazy parses the same embedded JSON the frontend uses, so both sides
        /// stay in sync automatically.
        ///
        /// On parse failure: falls back to the hardcoded {gfpgan-v1.4, codeformer} pair so a
        /// corrupted/missing JSON resource doesn't break face-restore entirely.
        /// </remarks>
        private static readonly Lazy<HashSet<string>> _faceRestoreModelIds = new(() =>
        {
            var fallback = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gfpgan-v1.4", "codeformer" };
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(_fallbackModelsJson.Value);
                if (!doc.RootElement.TryGetProperty("models", out var modelsArray) ||
                    modelsArray.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    return fallback;
                }
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in modelsArray.EnumerateArray())
                {
                    if (m.TryGetProperty("category", out var cat) &&
                        cat.ValueKind == System.Text.Json.JsonValueKind.String &&
                        string.Equals(cat.GetString(), "face_restore", StringComparison.OrdinalIgnoreCase) &&
                        m.TryGetProperty("id", out var id) &&
                        id.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var idStr = id.GetString();
                        if (!string.IsNullOrWhiteSpace(idStr)) ids.Add(idStr);
                    }
                }
                return ids.Count > 0 ? ids : fallback;
            }
            catch
            {
                return fallback;
            }
        });

        // v1.7.3.1 - Hotfix: removed dead endpoint `GET /Upscaler/js/{name}`. The v1.7.3
        // release notes announced this delete but a batch-edit interrupt left the code
        // intact (caught by external audit). 0 callers across the codebase; all JS-files
        // load via Jellyfin's /web/configurationpage?name=UPSCALERXyz mechanism instead.

        /// <summary>
        /// Lists Jellyfin media libraries (virtual folders) so the config UI can render
        /// a library picker for the scheduled-scan scope filter (issue #64).
        /// </summary>
        [HttpGet("libraries")]
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
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetStatus()
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null) return BadRequest();
            // Return only non-sensitive operational state (not the full config).
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

        // ── Managed API tokens ───────────────────────────────────────────
        // Jellyfin-admin-only management of the AI service's hashed token list.
        // These transparently proxy /auth/tokens on the service; the shared
        // AiServiceAuthHandler attaches X-Api-Token (the configured token) so
        // the plugin bootstraps token management with its existing credential.
        [HttpGet("tokens")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public Task<ActionResult> ListApiTokens()
            => ProxyTokenRequest(HttpMethod.Get, "/auth/tokens", null);

        [HttpPost("tokens")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public Task<ActionResult> CreateApiToken([FromQuery] string name, [FromQuery] int? expiresDays = null)
        {
            var form = new List<KeyValuePair<string, string>> { new("name", name ?? string.Empty) };
            if (expiresDays.HasValue)
                form.Add(new("expires_days", expiresDays.Value.ToString()));
            return ProxyTokenRequest(HttpMethod.Post, "/auth/tokens", new FormUrlEncodedContent(form));
        }

        [HttpDelete("tokens/{id}")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public Task<ActionResult> RevokeApiToken(string id)
            => ProxyTokenRequest(HttpMethod.Delete, $"/auth/tokens/{Uri.EscapeDataString(id ?? string.Empty)}", null);

        /// <summary>
        /// Transparent proxy to the AI service's /auth/tokens API. Forwards the
        /// service's status code + JSON body verbatim (403 = plugin token rejected,
        /// 404 = unknown id, 400 = invalid name).
        /// </summary>
        private async Task<ActionResult> ProxyTokenRequest(HttpMethod method, string path, HttpContent? content)
        {
            var baseUrl = GetValidatedServiceUrl();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var req = new HttpRequestMessage(method, $"{baseUrl}{path}") { Content = content };
                using var response = await GetAiServiceClient().SendAsync(req, cts.Token);
                var json = await response.Content.ReadAsStringAsync();
                return new ContentResult
                {
                    StatusCode = (int)response.StatusCode,
                    Content = string.IsNullOrEmpty(json) ? "{}" : json,
                    ContentType = "application/json"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Upscaler: token proxy {Method} {Path} failed", method, path);
                return StatusCode(502, new { error = "AI service unreachable" });
            }
        }

        /// <summary>
        /// Hardware-aware model recommendation — proxies the AI service's /recommend,
        /// which picks a model + scale the detected hardware can actually run.
        /// </summary>
        [HttpGet("recommend")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> RecommendForHardware()
        {
            var baseUrl = GetValidatedServiceUrl();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var response = await GetAiServiceClient().GetAsync($"{baseUrl}/recommend", cts.Token);
                var json = await response.Content.ReadAsStringAsync();
                // v1.8.3.14 - remember the hardware tier so the (synchronous) content
                // resolver can cap its picks without ever making a network call.
                CacheHardwareTier(json);
                return new ContentResult
                {
                    StatusCode = (int)response.StatusCode,
                    Content = string.IsNullOrEmpty(json) ? "{}" : json,
                    ContentType = "application/json"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Upscaler: recommend proxy failed");
                return StatusCode(502, new { error = "AI service unreachable" });
            }
        }

        /// <summary>
        /// v1.8.3.6 — the direct-ONNX entries of the OpenModelDB import catalog
        /// (site/models-import.json), annotated with whether the PLUGIN can import
        /// them one-click (https + allowlisted host + plain .onnx + sha256 pin).
        /// </summary>
        [HttpGet("models/importable")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> GetImportableModels()
        {
            // v1.8.3.8: the AI service owns the catalog view now (zip support,
            // convertible list, converter availability). Proxy it; fall back to
            // the local v1.8.3.6 logic for older service images.
            try
            {
                var svcUrl = GetValidatedServiceUrl();
                using var resp = await GetDownloadClient().GetAsync($"{svcUrl}/models/import-catalog", HttpContext.RequestAborted);
                if (resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    return new ContentResult { Content = body, ContentType = "application/json", StatusCode = 200 };
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug("import-catalog proxy unavailable, using local fallback: {Message}", ex.Message);
            }

            var catalog = await _importCatalog.GetCatalogAsync(HttpContext.RequestAborted);
            if (catalog == null)
                return StatusCode(502, new { error = "Import catalog unavailable (could not fetch models-import.json)" });
            return Ok(new
            {
                generated = (string?)null,
                converter_available = false,
                direct = catalog.Select(m =>
                {
                    var eligible = Services.ImportCatalogService.IsDirectlyImportable(m.DownloadUrl)
                                   && !string.IsNullOrEmpty(m.Sha256)
                                   && m.SizeBytes <= Services.ImportCatalogService.MaxImportBytes;
                    return new
                    {
                        id = m.Id,
                        name = m.Name,
                        scale = (object)m.ScaleInt,
                        architecture = m.Architecture,
                        license = m.License,
                        non_commercial = Services.ImportCatalogService.IsNonCommercial(m.License),
                        size_bytes = m.SizeBytes,
                        omdb_url = m.OmdbUrl,
                        kind = "direct",
                        eligible,
                        reason = eligible ? null : "not one-click importable (update the AI service image for zip support / see the OMDB page)",
                        model_name = Services.ImportCatalogService.ToModelName(m.Id)
                    };
                }),
                convertible = Array.Empty<object>()
            });
        }

        /// <summary>Request body for <see cref="ImportModel"/>.</summary>
        public class ImportModelRequest
        {
            public string? Id { get; set; }
        }

        /// <summary>
        /// v1.8.3.6 — one-click community-model import. Admin-only. The flow:
        /// resolve the catalog id (NO free-form URLs), download the pinned ONNX from
        /// an allowlisted host, verify its sha256 against the catalog pin, then hand
        /// it to the AI service's existing /models/upload (which shape-validates the
        /// model and registers it in the live model list as omdb-*).
        /// </summary>
        [HttpPost("models/import")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> ImportModel([FromBody] ImportModelRequest body)
        {
            if (string.IsNullOrWhiteSpace(body?.Id))
                return BadRequest(new { error = "id is required" });

            // v1.8.3.8: prefer the service-side importer (zip support, download runs
            // in the container instead of the Jellyfin process). 404 = older image
            // without the endpoint -> local v1.8.3.6 path below.
            try
            {
                var svcUrl = GetValidatedServiceUrl();
                using var payload = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new { id = body.Id }),
                    System.Text.Encoding.UTF8, "application/json");
                using var svcResp = await GetDownloadClient().PostAsync($"{svcUrl}/models/import-from-catalog", payload, HttpContext.RequestAborted);
                if (svcResp.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    var svcBody = await svcResp.Content.ReadAsStringAsync();
                    return new ContentResult { Content = svcBody, ContentType = "application/json", StatusCode = (int)svcResp.StatusCode };
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug("service-side import unavailable, using local path: {Message}", ex.Message);
            }

            var entry = await _importCatalog.ResolveAsync(body.Id, HttpContext.RequestAborted);
            if (entry == null)
                return NotFound(new { error = $"'{body.Id}' is not in the import catalog" });
            if (!Services.ImportCatalogService.IsDirectlyImportable(entry.DownloadUrl))
                return BadRequest(new { error = "This entry has no direct .onnx download the plugin can fetch (zip bundle or interactive host) - download it manually from its OpenModelDB page" });
            if (string.IsNullOrEmpty(entry.Sha256))
                return BadRequest(new { error = "This entry has no sha256 pin - refusing to import unverifiable data" });
            if (entry.SizeBytes > Services.ImportCatalogService.MaxImportBytes)
                return BadRequest(new { error = "Model exceeds the 500 MB import limit" });

            var serviceUrl = GetValidatedServiceUrl();
            var modelName = Services.ImportCatalogService.ToModelName(entry.Id);
            try
            {
                // 1) download from the pinned, allowlisted URL (NO service token on this client).
                //    Redirects are followed (GitHub releases redirect to objects.githubusercontent.com);
                //    only the START url is allowlist-checked - acceptable because the bytes must
                //    still match the catalog's sha256 pin below, and this client carries no secret.
                var external = _httpClientFactory.CreateClient("ExternalModelDownload");
                byte[] data;
                using (var dl = await external.GetAsync(entry.DownloadUrl, HttpContext.RequestAborted))
                {
                    if (!dl.IsSuccessStatusCode)
                        return StatusCode(502, new { error = $"Download failed (HTTP {(int)dl.StatusCode} from source)" });
                    if (dl.Content.Headers.ContentLength is > Services.ImportCatalogService.MaxImportBytes)
                        return StatusCode(502, new { error = "Source reports a file above the 500 MB import limit" });
                    data = await dl.Content.ReadAsByteArrayAsync(HttpContext.RequestAborted);
                }
                if (data.LongLength > Services.ImportCatalogService.MaxImportBytes)
                    return StatusCode(502, new { error = "Downloaded file exceeds the 500 MB import limit" });

                // 2) supply-chain gate: the bytes must match the catalog pin exactly
                var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();
                if (!sha.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("AI Upscaler: import of {Id} rejected - sha256 mismatch (expected {Expected}, got {Actual})",
                        entry.Id, entry.Sha256, sha);
                    return StatusCode(502, new { error = "sha256 mismatch - the upstream file changed since the catalog was generated. Import refused; the weekly catalog refresh will re-pin it if the change is legitimate." });
                }

                // 3) hand off to the service's validating upload (registers the model live)
                using var form = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(data);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                form.Add(fileContent, "file", modelName + ".onnx");
                form.Add(new StringContent(modelName), "model_name");
                form.Add(new StringContent(entry.ScaleInt.ToString(System.Globalization.CultureInfo.InvariantCulture)), "scale");
                form.Add(new StringContent($"{entry.Name} (OpenModelDB import, license: {entry.License ?? "unclear"})"), "description");

                using var response = await GetDownloadClient().PostAsync($"{serviceUrl}/models/upload", form, HttpContext.RequestAborted);
                var serviceBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    return new ContentResult { Content = serviceBody, ContentType = "application/json", StatusCode = (int)response.StatusCode };
                }
                _logger.LogInformation("AI Upscaler: imported {Id} as {ModelName} ({Bytes} bytes, sha256 verified)", entry.Id, modelName, data.LongLength);
                return Ok(new
                {
                    success = true,
                    imported_as = modelName,
                    scale = entry.ScaleInt,
                    license = entry.License,
                    non_commercial = Services.ImportCatalogService.IsNonCommercial(entry.License),
                    size_bytes = data.LongLength
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Upscaler: model import of {Id} failed", body.Id);
                return StatusCode(502, new { error = "Import failed: " + ex.Message });
            }
        }

        /// <summary>
        /// v1.8.3.8 - convert a pth catalog model to ONNX on the AI service and
        /// register it. Requires the docker7-converter image (501 otherwise).
        /// </summary>
        [HttpPost("models/convert")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> ConvertModel([FromBody] ImportModelRequest body)
        {
            if (string.IsNullOrWhiteSpace(body?.Id))
                return BadRequest(new { error = "id is required" });
            try
            {
                var svcUrl = GetValidatedServiceUrl();
                using var payload = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new { id = body.Id }),
                    System.Text.Encoding.UTF8, "application/json");
                using var resp = await GetDownloadClient().PostAsync($"{svcUrl}/models/convert-from-catalog", payload, HttpContext.RequestAborted);
                var respBody = await resp.Content.ReadAsStringAsync();
                // Review fix: a 404 is ambiguous - the ROUTE missing on a pre-1.8.3.8
                // image vs the service's own "id not in catalog". FastAPI's route-404
                // body is exactly {"detail":"Not Found"}; the catalog error carries a
                // descriptive detail. Only claim "image too old" for the former.
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound
                    && (!respBody.Contains("\"detail\"", StringComparison.Ordinal)
                        || respBody.Contains("\"detail\":\"Not Found\"", StringComparison.Ordinal)))
                    return StatusCode(501, new { error = "The AI service image is too old for conversion - update to v1.8.3.8+ (docker7-converter)" });
                return new ContentResult { Content = respBody, ContentType = "application/json", StatusCode = (int)resp.StatusCode };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Upscaler: model conversion of {Id} failed", body.Id);
                return StatusCode(502, new { error = "Conversion failed: " + ex.Message });
            }
        }

        /// <summary>
        /// v1.8.3.8 - direct file install: forwards an .onnx to the service's
        /// validated /models/upload, or a .pth/.safetensors to /models/convert-upload.
        /// Closes the gap for models hosted on Google Drive / Mega etc.: download in
        /// the browser, hand the file to this endpoint via the config page.
        /// </summary>
        [HttpPost("models/upload-file")]
        [Authorize(Policy = "RequiresElevation")]
        [RequestSizeLimit(600_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 600_000_000)]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> UploadModelFile([FromForm] IFormFile file, [FromForm] string modelName, [FromForm] int scale = 2)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "file is required" });
            if (string.IsNullOrWhiteSpace(modelName))
                return BadRequest(new { error = "modelName is required" });

            var ext = System.IO.Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
            var isOnnx = ext == ".onnx";
            var isPth = ext is ".pth" or ".pt" or ".safetensors";
            if (!isOnnx && !isPth)
                return BadRequest(new { error = "Unsupported file type - expected .onnx (direct install) or .pth/.pt/.safetensors (converted install)" });

            try
            {
                var svcUrl = GetValidatedServiceUrl();
                using var form = new MultipartFormDataContent();
                var stream = new StreamContent(file.OpenReadStream());
                stream.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                form.Add(stream, "file", file.FileName ?? ("model" + ext));
                form.Add(new StringContent(modelName), "model_name");
                if (isOnnx)
                    form.Add(new StringContent(scale.ToString(System.Globalization.CultureInfo.InvariantCulture)), "scale");
                form.Add(new StringContent($"Uploaded via Jellyfin config page ({file.FileName})"), "description");

                var target = isOnnx ? "/models/upload" : "/models/convert-upload";
                using var resp = await GetDownloadClient().PostAsync($"{svcUrl}{target}", form, HttpContext.RequestAborted);
                var respBody = await resp.Content.ReadAsStringAsync();
                if (!isOnnx && resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return StatusCode(501, new { error = "The AI service image is too old for conversion - update to v1.8.3.8+ (docker7-converter)" });
                return new ContentResult { Content = respBody, ContentType = "application/json", StatusCode = (int)resp.StatusCode };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Upscaler: model file upload failed");
                return StatusCode(502, new { error = "Upload failed: " + ex.Message });
            }
        }

        /// <summary>
        /// v1.8.3.11 - generic proxy helper: forward a request to the AI service and
        /// hand the JSON body/status straight back to the browser.
        /// </summary>
        private async Task<ActionResult> ProxyServiceAsync(HttpMethod method, string path, object? jsonBody = null)
        {
            try
            {
                var svcUrl = GetValidatedServiceUrl();
                using var req = new HttpRequestMessage(method, $"{svcUrl}{path}");
                if (jsonBody != null)
                {
                    req.Content = new StringContent(
                        System.Text.Json.JsonSerializer.Serialize(jsonBody),
                        System.Text.Encoding.UTF8, "application/json");
                }
                using var resp = await GetDownloadClient().SendAsync(req, HttpContext.RequestAborted);
                var body = await resp.Content.ReadAsStringAsync();
                return new ContentResult { Content = body, ContentType = "application/json", StatusCode = (int)resp.StatusCode };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Upscaler: proxy {Path} failed", path);
                return StatusCode(502, new { error = "AI service unreachable: " + ex.Message });
            }
        }

        /// <summary>v1.8.3.11 - start an async catalog import/convert job on the service.</summary>
        [HttpPost("models/import-async")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public Task<ActionResult> ImportModelAsync([FromBody] ImportModelRequest body)
            => ProxyServiceAsync(HttpMethod.Post, "/models/import-async", new { id = body?.Id });

        /// <summary>v1.8.3.11 - poll an async import job.</summary>
        [HttpGet("models/import-status/{jobId}")]
        [Produces(MediaTypeNames.Application.Json)]
        public Task<ActionResult> ImportModelStatus([FromRoute] string jobId)
            => ProxyServiceAsync(HttpMethod.Get, $"/models/import-status/{Uri.EscapeDataString(jobId)}");

        /// <summary>v1.8.3.11 - start a background catalog-model download (big face-restore models).</summary>
        [HttpPost("models/download-async")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> DownloadModelAsync([FromForm] string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName) || !Regex.IsMatch(modelName, "^[a-zA-Z0-9._-]{1,64}$"))
                return BadRequest(new { error = "invalid modelName" });
            try
            {
                var svcUrl = GetValidatedServiceUrl();
                using var form = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("model_name", modelName) });
                using var resp = await GetDownloadClient().PostAsync($"{svcUrl}/models/download-async", form, HttpContext.RequestAborted);
                var body = await resp.Content.ReadAsStringAsync();
                return new ContentResult { Content = body, ContentType = "application/json", StatusCode = (int)resp.StatusCode };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Upscaler: download-async proxy failed");
                return StatusCode(502, new { error = "AI service unreachable: " + ex.Message });
            }
        }

        /// <summary>v1.8.3.11 - poll a background download job.</summary>
        [HttpGet("models/download-status/{jobId}")]
        [Produces(MediaTypeNames.Application.Json)]
        public Task<ActionResult> DownloadModelStatus([FromRoute] string jobId)
            => ProxyServiceAsync(HttpMethod.Get, $"/models/download-status/{Uri.EscapeDataString(jobId)}");

        /// <summary>v1.8.3.11 - delete a custom/imported model (service refuses built-ins).</summary>
        [HttpDelete("models/import/{modelName}")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public Task<ActionResult> DeleteImportedModel([FromRoute] string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName) || !Regex.IsMatch(modelName, "^[a-zA-Z0-9._-]{1,64}$"))
                return Task.FromResult<ActionResult>(BadRequest(new { error = "invalid model name" }));
            return ProxyServiceAsync(HttpMethod.Delete, $"/models/upload/{Uri.EscapeDataString(modelName)}");
        }

        /// <summary>
        /// v1.8.3.14 - extract "tier" from a /recommend payload and hand it to
        /// <see cref="Services.UpscalerCore.UpdateHardwareTier"/>. Failures are swallowed on
        /// purpose: an unreadable payload must leave auto-mode uncapped, not broken.
        /// </summary>
        private void CacheHardwareTier(string? recommendJson)
        {
            if (string.IsNullOrWhiteSpace(recommendJson)) return;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(recommendJson);
                if (doc.RootElement.TryGetProperty("tier", out var tierEl) &&
                    tierEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    Services.UpscalerCore.UpdateHardwareTier(tierEl.GetString());
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogDebug("Could not read hardware tier from /recommend: {Message}", ex.Message);
            }
        }

        // The hardware tier changes when the box changes, not when the video does.
        // Refreshing it per request would put a second HTTP round-trip (and, on a dead
        // service, a second timeout) in front of every auto decision.
        private static DateTime _tierCheckedUtc = DateTime.MinValue;
        private static readonly TimeSpan TierTtl = TimeSpan.FromMinutes(10);

        /// <summary>
        /// v1.8.3.14 - refresh the cached hardware tier if it is stale. Short timeout and
        /// fully non-fatal: if the service is slow or down, the resolver simply runs
        /// uncapped (its "Hardware: unknown" signal says so).
        /// </summary>
        private async Task RefreshHardwareTierAsync()
        {
            if (DateTime.UtcNow - _tierCheckedUtc < TierTtl) return;
            _tierCheckedUtc = DateTime.UtcNow;   // set first: a failing service must not retry per request
            try
            {
                var baseUrl = GetValidatedServiceUrl();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var resp = await GetAiServiceClient().GetAsync($"{baseUrl}/recommend", cts.Token);
                if (resp.IsSuccessStatusCode)
                {
                    CacheHardwareTier(await resp.Content.ReadAsStringAsync());
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Hardware tier refresh skipped: {Message}", ex.Message);
            }
        }

        /// <summary>Request body for <see cref="ComputeVmaf"/>.</summary>
        public class VmafRequest
        {
            public string? Reference { get; set; }
            public string? Distorted { get; set; }
        }

        /// <summary>
        /// v1.8.2 — objective VMAF quality score (0–100) of an upscaled/distorted file
        /// against a reference, via ffmpeg+libvmaf. Returns 501 if this ffmpeg build
        /// lacks libvmaf. Admin-only — it reads arbitrary server file paths.
        /// </summary>
        [HttpPost("vmaf")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> ComputeVmaf([FromBody] VmafRequest body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Reference) || string.IsNullOrWhiteSpace(body.Distorted))
                return BadRequest(new { error = "reference and distorted paths are required" });
            if (body.Reference.Contains("..") || body.Distorted.Contains(".."))
                return BadRequest(new { error = "invalid path" });
            if (!System.IO.File.Exists(body.Reference) || !System.IO.File.Exists(body.Distorted))
                return NotFound(new { error = "reference or distorted file not found" });

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                var res = await new Services.VmafService()
                    .ComputeVmafAsync(_mediaEncoder.EncoderPath, body.Distorted, body.Reference, cts.Token);
                return Ok(new { vmaf = res.Mean, min = res.Min, max = res.Max, harmonic_mean = res.Harmonic });
            }
            catch (Services.VmafUnavailableException ex)
            {
                return StatusCode(501, new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VMAF computation failed");
                return StatusCode(500, new { error = "VMAF computation failed" });
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
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> GetHardwareInfo()
        {
            try
            {
                // v1.8.3.27 - all three of these used to be assertions, not observations.
                // GpuAvailable returned the HardwareAcceleration CONFIG TOGGLE (default on),
                // so a CPU-only server was told it had a GPU; FFmpegAvailable and OnnxRuntime
                // were literal true / "Available" and had never checked anything. Caught on a
                // live box whose /gpu-verify said gpu_list: [], nvidia-smi missing, /dev/dri
                // absent - while this endpoint cheerfully reported a GPU.
                var config = Plugin.Instance?.Configuration;
                var status = await _benchmarkService.GetServiceStatusAsync();

                // null is not false. "The service has not answered yet" and "there is no GPU"
                // are different facts, and reporting the second for the first is how the old
                // version came to claim a GPU on a box that has none.
                bool? gpuAvailable = status == null ? null : status.UsingGpu;

                var ffmpegPath = _mediaEncoder?.EncoderPath;

                return Ok(new
                {
                    GpuAvailable = gpuAvailable,
                    // What the user asked for, kept separate from what actually exists. The
                    // old field conflated the two and only ever reported this one.
                    GpuAccelerationRequested = config?.HardwareAcceleration ?? false,
                    FFmpegAvailable = !string.IsNullOrEmpty(ffmpegPath) && IOFile.Exists(ffmpegPath),
                    FFmpegPath = ffmpegPath,
                    OnnxRuntime = status == null
                        ? "unknown (service not reached)"
                        : (status.AvailableProviders.Length > 0
                            ? string.Join(", ", status.AvailableProviders)
                            : "unavailable"),
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

        /// <summary>
        /// Benchmark-derived settings recommendations from the LOCAL hardware benchmark
        /// service. Distinct from <c>/recommend</c> (proxies the AI service's own
        /// hardware-aware model pick) and <c>/recommend-model</c> (content-based pick
        /// for a specific video). All three are consumed by different UI surfaces —
        /// they look alike but are not aliases of each other.
        /// </summary>
        [HttpGet("hardware-benchmark")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> GetHardwareBenchmark()
        {
            try
            {
                return Ok(await _benchmarkService.GetRecommendationsAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get hardware benchmark");
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        // v1.8.3.20 - one of the three "recommend" names is not a recommendation at all.
        //
        // /recommend proxies the service's hardware model pick, /recommend-model picks a
        // model for one video - and this one runs the LOCAL benchmark and returns
        // hardware.cpuCores, hardware.cudaAvailable and system.platform. Three names that
        // read as variants of each other, one of which answers a different question.
        //
        // The honest fix is a correct name, not a shared schema: folding this onto the
        // /recommend shape would delete the hardware.* and system.* fields that the
        // sidebar, the quick menu and the System tab render. So the payload is untouched
        // and only the route is renamed; this alias keeps every external caller working.
        private static int _deprecatedRecommendationsHits;

        /// <summary>
        /// Deprecated alias of <see cref="GetHardwareBenchmark"/>. Identical payload.
        /// Use <c>GET /Upscaler/hardware-benchmark</c>.
        /// </summary>
        [HttpGet("recommendations")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> GetHardwareRecommendations()
        {
            // Warn once per server lifetime: this is polled by the sidebar, so logging per
            // request would bury the log it is trying to be visible in.
            if (System.Threading.Interlocked.Exchange(ref _deprecatedRecommendationsHits, 1) == 0)
            {
                _logger.LogWarning(
                    "GET /Upscaler/recommendations is deprecated and will be removed in a future release - " +
                    "it returns a hardware benchmark, not a model recommendation. Use /Upscaler/hardware-benchmark " +
                    "(identical payload).");
            }
            return await GetHardwareBenchmark();
        }

        /// <summary>
        /// Get the recommended AI model for specific content parameters (genres,
        /// resolution). Used by the in-player panel when Auto-Mode is enabled.
        /// Distinct from <c>/recommend</c> (service hardware pick) and
        /// <c>/recommendations</c> (local benchmark results) — see the note there.
        /// </summary>
        [HttpGet("recommend-model")]
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
                // v1.8.3.14 - make sure the hardware tier is current before deciding.
                await RefreshHardwareTierAsync();

                // v1.8.3.13 - keep the reasoning: the detailed resolver returns why this
                // model was chosen and whether a substitution happened, so the UI can show it.
                var pick = _upscalerCore.ResolveModelForVideoDetailed(
                    genres: genreList,
                    width: width,
                    height: height,
                    isBatch: isBatch,
                    inputFrames: inputFrames,
                    forceAuto: true);
                var recommendedModel = pick.Model;

                var filterPick = _upscalerCore.ResolveFilterForVideoDetailed(
                    genres: genreList,
                    width: width,
                    height: height);

                var config = Plugin.Instance?.Configuration;
                return Ok(new
                {
                    success = true,
                    recommended_model = recommendedModel,
                    // v1.8.3.13 - schema now matches the Python service's /recommend,
                    // which has always returned a human-readable reason.
                    reason = pick.Reason,
                    signals = pick.Signals,
                    substituted_from = pick.SubstitutedFrom,
                    substitution_reason = pick.SubstitutionReason,
                    // v1.8.3.14 - the scale the output will REALLY grow by (the service uses
                    // the model's native factor, not the configured one). 0 = model id does
                    // not encode a scale, so the UI keeps showing the configured value.
                    recommended_scale = pick.Scale,
                    output_size = Services.ModelScale.DescribeOutput(width, height, pick.Scale),
                    recommended_filter = filterPick.Preset,
                    // v1.8.3.20 - the filter is a SUGGESTION now, never applied for the
                    // user, so it has to say what it is based on or it cannot be judged.
                    filter_reason = filterPick.Reason,
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

                var upscaledData = await _upscalerCore.UpscaleImageAsync(originalData, model, scale, HttpContext.RequestAborted);
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
                            var originalData = await IOFile.ReadAllBytesAsync(imagePath, HttpContext.RequestAborted);
                            var upscaledData = await _upscalerCore.UpscaleImageAsync(originalData, model, scale, HttpContext.RequestAborted);

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

                // v1.8.3.22 - the check this endpoint never had. Its siblings EnqueueJob
                // and PreProcessVideo have gated on the library allowlist since v1.7.5;
                // this one only checked that the file exists, so any authenticated user
                // could name any path on the server.
                if (!IsInsideMediaLibrary(fullInputPath))
                {
                    _logger.LogWarning("ProcessVideo rejected: input path is outside every media library");
                    return BadRequest(new { success = false, error = "Input path must be within a Jellyfin media library" });
                }

                // ffmpeg runs with -y. Without this, "output" could name the film next to
                // the input and silently replace it.
                if (WouldOverwriteExistingFile(fullOutputPath))
                {
                    return BadRequest(new { success = false, error = "Output file already exists - refusing to overwrite it" });
                }

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
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetActiveJobs()
        {
            try
            {
                var jobs = _videoProcessor.GetActiveJobs();
                // v1.8.3.20 - folded into the SAME response rather than a new endpoint: the
                // dashboard needs both to render one status line, and two polls for one line
                // would double the request rate for no benefit.
                return Ok(new { success = true, jobs = jobs, history = _videoProcessor.GetCompletionSummary() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve active jobs");
                return StatusCode(500, new { success = false, error = "Internal server error" });
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
                _logger.LogError(ex, "Failed to pause job {JobId}", jobId);
                return StatusCode(500, new { success = false, error = "Internal server error" });
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
                _logger.LogError(ex, "Failed to resume job {JobId}", jobId);
                return StatusCode(500, new { success = false, error = "Internal server error" });
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
                _logger.LogError(ex, "Failed to cancel job {JobId}", jobId);
                return StatusCode(500, new { success = false, error = "Internal server error" });
            }
        }

        // ============================================================
        // === Processing Queue API ===
        // ============================================================

        /// <summary>Get queue status — pending, active, completed jobs.</summary>
        [HttpGet("queue")]
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> GetQueueStatus()
        {
            return Ok(new { success = true, queue = _processingQueue.GetStatus() });
        }

        /// <summary>Enqueue a video for processing with optional priority.</summary>
        [HttpPost("queue/add")]
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

            // v1.8.3.22 - shared helper. The inline version compared without a trailing
            // separator, so "/media/mov-private" passed an allowlist holding "/media/mov".
            if (!IsInsideMediaLibrary(inputPath))
                return BadRequest(new { success = false, error = "Input path must be within a Jellyfin media library" });

            if (outputPath != null)
            {
                outputPath = Path.GetFullPath(outputPath);

                // v1.8.3.22 - the directory allowlist below constrains WHERE the output may
                // go, never WHAT it may replace. ffmpeg runs with -y, so "the other film in
                // this folder" was a legal target and would be destroyed without a word.
                if (WouldOverwriteExistingFile(outputPath))
                    return BadRequest(new { success = false, error = "Output file already exists - refusing to overwrite it" });

                // Restrict output to be under the same parent directory as input or under the Jellyfin transcode path
                var inputParent = Path.GetFullPath(Path.GetDirectoryName(inputPath) ?? string.Empty);
                var outputParent = Path.GetFullPath(Path.GetDirectoryName(outputPath) ?? string.Empty);
                var inputParentWithSep = inputParent.EndsWith(Path.DirectorySeparatorChar) ? inputParent : inputParent + Path.DirectorySeparatorChar;

                if (!outputParent.Equals(inputParent, StringComparison.OrdinalIgnoreCase) &&
                    !outputParent.StartsWith(inputParentWithSep, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { success = false, error = "Output path must be under the input directory" });
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
        [Produces(MediaTypeNames.Application.Json)]
        public ActionResult<object> CancelQueuedJob(string jobId)
        {
            var cancelled = _processingQueue.Cancel(jobId);
            return Ok(new { success = cancelled, job_id = jobId });
        }

        /// <summary>Change priority of a pending job (1=highest, 10=lowest).</summary>
        [HttpPost("queue/{jobId}/priority")]
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
                await Request.Body.CopyToAsync(memoryStream, HttpContext.RequestAborted);

                if (memoryStream.Length > MaxUploadSizeBytes)
                {
                    return BadRequest(new { error = "Image too large. Maximum size is 50MB." });
                }

                var inputImage = memoryStream.ToArray();
                var upscaledImage = await _upscalerCore.UpscaleImageAsync(inputImage, model, scale, HttpContext.RequestAborted);
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
                // v1.8.3.22 - shared helper (separator-safe prefix; see IsInsideMediaLibrary).
                if (!IsInsideMediaLibrary(normalizedPath))
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

        // v1.7.3 - removed dead endpoint `POST /Upscaler/cache/config`. It wrote
        // `EnablePreProcessingCache` which is a Ghost-Property (removed from UI in v22,
        // 0 service-consumers). The endpoint persisted a value that nothing read.

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
                // v1.7.0 - QualityLevelRegistry single source of truth (UI-match: low/medium/high).
                // Was {fast, medium, high} pre-v1.7.0 -- "low" was silently dropped on import.
                TryApply("QualityLevel", val =>
                {
                    var ql = val.GetString() ?? "medium";
                    if (QualityLevelRegistry.Levels.Contains(ql)) config.QualityLevel = ql;
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
                TryApply("PlayerButton", val => config.PlayerButton = val.GetBoolean());
                TryApply("Notifications", val => config.Notifications = val.GetBoolean());
                TryApply("AutoRetryButton", val => config.AutoRetryButton = val.GetBoolean());
                // v1.7.0 - ButtonPositionRegistry single source of truth (UI-match: left/right/center).
                // Was {left, right} pre-v1.7.0 -- "center" silently dropped despite full CSS+keyframes.
                TryApply("ButtonPosition", val =>
                {
                    var pos = val.GetString() ?? "right";
                    if (ButtonPositionRegistry.Positions.Contains(pos)) config.ButtonPosition = pos;
                });
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
                // v1.6.1.23 (P0) - was a 3-entry inline list silently rejecting 9 of 12 UI options.
                // CodecRegistry.OutputCodecs is the single source of truth, kept in lockstep with
                // the #OutputCodec dropdown in configurationpage.html via CodecRegistryTests.
                TryApply("OutputCodec", val =>
                {
                    var codec = val.GetString() ?? "libx264";
                    if (CodecRegistry.OutputCodecs.Contains(codec)) config.OutputCodec = codec;
                });
                TryApply("MaxUpscaledFileSizeMB", val => config.MaxUpscaledFileSizeMB = Math.Max(0, val.GetInt64()));
                TryApply("EnableProcessingQueue", val => config.EnableProcessingQueue = val.GetBoolean());
                TryApply("MaxQueueSize", val => config.MaxQueueSize = val.GetInt32());
                TryApply("PauseQueueDuringPlayback", val => config.PauseQueueDuringPlayback = val.GetBoolean());
                TryApply("PersistQueueAcrossRestarts", val => config.PersistQueueAcrossRestarts = val.GetBoolean());
                // Real-Time Upscaling
                TryApply("EnableRealtimeUpscaling", val => config.EnableRealtimeUpscaling = val.GetBoolean());
                // v1.7.1 - RealtimeModeRegistry single source of truth.
                // UI exposes {auto, lanczos, anime4k, ai-webgpu, server}. Import additionally
                // accepts {webgl} as backwards-compat alias for v1.6.x saved configs;
                // player-integration.js re-maps webgl -> lanczos at runtime.
                TryApply("RealtimeMode", val =>
                {
                    var mode = val.GetString() ?? "auto";
                    if (RealtimeModeRegistry.AcceptedAtImport.Contains(mode)) config.RealtimeMode = mode;
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

                // v1.7.0 - 18 properties that were silently lost on Settings-Import. Each
                // had UI surface (or property in PluginConfiguration) but no TryApply, so a
                // backup-restore reset them to defaults. Now imported.
                TryApply("AiServiceApiToken", val => config.AiServiceApiToken = (val.GetString() ?? "").Trim());
                TryApply("EnabledLibraryIds", val =>
                {
                    var ids = val.GetString() ?? "";
                    if (System.Text.RegularExpressions.Regex.IsMatch(ids, @"^[a-fA-F0-9,\-]*$"))
                        config.EnabledLibraryIds = ids;
                });
                TryApply("EnableFaceRestore", val => config.EnableFaceRestore = val.GetBoolean());
                TryApply("FaceRestoreModel", val => config.FaceRestoreModel = val.GetString() ?? "gfpgan-v1.4");
                TryApply("FaceRestoreMaxPerFrame", val => config.FaceRestoreMaxPerFrame = val.GetInt32());
                TryApply("FaceRestoreMaxWidth", val => config.FaceRestoreMaxWidth = val.GetInt32());
                TryApply("EnableVideoFilters", val => config.EnableVideoFilters = val.GetBoolean());
                TryApply("ActiveFilterPreset", val =>
                {
                    var preset = val.GetString() ?? "none";
                    if (VideoFilterService.SupportedPresets.Contains(preset)) config.ActiveFilterPreset = preset;
                });
                TryApply("FilterLutPath", val => { var p = val.GetString() ?? ""; if (!p.Contains("..")) config.FilterLutPath = p; });
                TryApply("FilterBrightness", val => config.FilterBrightness = val.GetDouble());
                TryApply("FilterContrast", val => config.FilterContrast = val.GetDouble());
                TryApply("FilterSaturation", val => config.FilterSaturation = val.GetDouble());
                TryApply("FilterGamma", val => config.FilterGamma = val.GetDouble());
                TryApply("FilterSharpness", val => config.FilterSharpness = val.GetDouble());
                TryApply("FilterVignette", val => config.FilterVignette = val.GetDouble());
                TryApply("FilterDenoise", val => config.FilterDenoise = val.GetDouble());
                TryApply("FilterColorTemperature", val => config.FilterColorTemperature = val.GetInt32());
                TryApply("FilterFilmGrain", val => config.FilterFilmGrain = val.GetInt32());
                // v1.8.2 - denoise-before-encode prefilter
                TryApply("EnableDenoisePrefilter", val => config.EnableDenoisePrefilter = val.GetBoolean());
                TryApply("DenoisePrefilterMethod", val =>
                {
                    var m = val.GetString() ?? "hqdn3d";
                    if (VideoFilterService.SupportedDenoiseMethods.Contains(m)) config.DenoisePrefilterMethod = m.ToLowerInvariant();
                });
                TryApply("DenoisePrefilterStrength", val => config.DenoisePrefilterStrength = val.GetDouble());

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
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult<object>> GetGpuList()
        {
            try
            {
                var serviceUrl = GetValidatedServiceUrl();
                using var response = await GetAiServiceClient().GetAsync($"{serviceUrl}/gpus", HttpContext.RequestAborted);
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
                using var response = await GetDownloadClient().PostAsync($"{serviceUrl}/models/load", formContent, HttpContext.RequestAborted);
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
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> ModelBenchmark()
        {
            try
            {
                var serviceUrl = GetValidatedServiceUrl();
                using var response = await GetBenchmarkClient().GetAsync($"{serviceUrl}/benchmark", HttpContext.RequestAborted);
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
        /// <summary>
        /// v1.8.3.22 - the proxy route the config page has been calling since the feature
        /// shipped. Only /face-restore/load, /status and /unload were ever defined here, so
        /// "Preview on Selected Media" ended in HTTP 404 every single time - the feature was
        /// entirely dead. The endpoint exists on the AI service; only the plugin hop was
        /// missing.
        ///
        /// Raw image bytes in, processed image out, with the service's X-Face-Count header
        /// forwarded because the UI reads it to report how many faces were found.
        /// </summary>
        /// <summary>
        /// v1.8.3.24 — cover detected objects in one frame, for the real-time player loop.
        ///
        /// This is what discussion #11 actually needed and what v1.8.3.23 still lacked: the
        /// masking existed as a service endpoint nothing called. The player captures frames
        /// already; it just had nowhere to send them.
        ///
        /// Masking parameters come from the plugin config rather than the query string. The
        /// caller is the player running in a browser, so anything it could pass a user could
        /// pass, and every value here is forwarded to the AI service.
        /// </summary>
        [HttpPost("detect-mask")]
        public async Task<ActionResult> DetectMaskFrame()
        {
            try
            {
                const int MaxFrameBytes = 32 * 1024 * 1024;
                if (Request.ContentLength > MaxFrameBytes)
                    return StatusCode(413, new { message = "Frame too large" });

                using var ms = new MemoryStream();
                await Request.Body.CopyToAsync(ms, HttpContext.RequestAborted);
                if (ms.Length > MaxFrameBytes)
                    return StatusCode(413, new { message = "Frame too large" });

                var bytes = ms.ToArray();
                if (bytes.Length == 0)
                    return BadRequest(new { message = "Empty body" });

                var config = Plugin.Instance?.Configuration;
                if (config?.EnableObjectMasking != true)
                    return BadRequest(new { message = "Object masking is disabled in the plugin settings" });

                var serviceUrl = GetValidatedServiceUrl();
                var query = BuildObjectMaskQuery(config);

                using var content = new ByteArrayContent(bytes);
                content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue(
                        Request.ContentType?.StartsWith("image/") == true ? Request.ContentType : "image/jpeg");

                using var response = await GetBenchmarkClient()
                    .PostAsync($"{serviceUrl}/detect-mask{query}", content, HttpContext.RequestAborted);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    return new ContentResult { Content = err, ContentType = "application/json", StatusCode = (int)response.StatusCode };
                }

                // The player shows this so the user can tell "nothing was found" from
                // "the feature is not running".
                if (response.Headers.TryGetValues("X-Detections", out var detections))
                {
                    Response.Headers["X-Detections"] = detections.FirstOrDefault();
                }
                var image = await response.Content.ReadAsByteArrayAsync();
                return File(image, response.Content.Headers.ContentType?.MediaType ?? "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Object-mask frame proxy failed");
                return StatusCode(500, new { error = "Object masking failed" });
            }
        }

        /// <summary>
        /// Builds the query string for /detect-mask from configuration.
        /// Split out so the encoding can be tested without a controller: a class list like
        /// "dog,cat" must survive as two classes, and a stray "&amp;" must not turn into an
        /// extra parameter.
        /// </summary>
        internal static string BuildObjectMaskQuery(PluginConfiguration config)
        {
            var classes = string.IsNullOrWhiteSpace(config.ObjectMaskClasses) ? "animals" : config.ObjectMaskClasses;
            var mode = config.ObjectMaskMode == "blur" ? "blur" : "box";
            return "?classes=" + Uri.EscapeDataString(classes.Trim()) +
                   "&mode=" + mode +
                   "&confidence=" + config.ObjectMaskConfidence.ToString("0.##", CultureInfo.InvariantCulture) +
                   "&pad=" + config.ObjectMaskPadding.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// v1.8.3.24 — load the detection model into the AI service. Admin only: it reads a
        /// file from the service's model directory and holds it in memory.
        /// </summary>
        [HttpPost("object-mask/load-model")]
        [Authorize(Policy = "RequiresElevation")]
        public async Task<ActionResult> LoadObjectMaskModel([FromQuery] string? modelName = null)
        {
            try
            {
                // v1.8.3.28, reported in discussion #11: this read ONLY the saved config, so
                // typing a new model id and pressing Load loaded the PREVIOUS one and the
                // status line showed that previous name - "sticks on a previous model name
                // different than the one just entered". I even wrote a comment in the UI
                // describing this trap instead of closing it. The caller now says which model
                // it means; the saved value remains the fallback for scripts and other clients.
                var config = Plugin.Instance?.Configuration;
                var model = !string.IsNullOrWhiteSpace(modelName) ? modelName.Trim() : config?.ObjectMaskModel;
                if (string.IsNullOrWhiteSpace(model))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "No detection model configured. Import one first - none ships with the plugin, " +
                                "because every catalog entry carries a verified sha256 pin.",
                    });
                }

                var serviceUrl = GetValidatedServiceUrl();
                using var form = new MultipartFormDataContent { { new StringContent(model), "model_name" } };
                using var response = await GetBenchmarkClient()
                    .PostAsync($"{serviceUrl}/models/load-detector", form, HttpContext.RequestAborted);

                var body = await response.Content.ReadAsStringAsync();
                return new ContentResult
                {
                    Content = body,
                    ContentType = "application/json",
                    StatusCode = (int)response.StatusCode,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loading the detection model failed");
                return StatusCode(500, new { error = "Could not load the detection model" });
            }
        }

        [HttpPost("face-restore/frame")]
        public async Task<ActionResult> FaceRestoreFrame()
        {
            try
            {
                // A preview frame is a single JPEG. Reading an unbounded request body into
                // the Jellyfin heap is the same pattern the review flagged on the import
                // path, so cap it here rather than discovering the limit under memory
                // pressure. 32 MB is far above any single frame and far below trouble.
                const int MaxPreviewFrameBytes = 32 * 1024 * 1024;
                if (Request.ContentLength > MaxPreviewFrameBytes)
                    return StatusCode(413, new { message = "Frame too large" });

                using var ms = new MemoryStream();
                await Request.Body.CopyToAsync(ms, HttpContext.RequestAborted);
                if (ms.Length > MaxPreviewFrameBytes)
                    return StatusCode(413, new { message = "Frame too large" });

                var bytes = ms.ToArray();
                if (bytes.Length == 0)
                    return BadRequest(new { message = "Empty body" });

                var serviceUrl = GetValidatedServiceUrl();
                using var content = new ByteArrayContent(bytes);
                content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue(
                        Request.ContentType?.StartsWith("image/") == true ? Request.ContentType : "image/jpeg");

                using var response = await GetBenchmarkClient()
                    .PostAsync($"{serviceUrl}/face-restore/frame", content, HttpContext.RequestAborted);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    return new ContentResult { Content = err, ContentType = "application/json", StatusCode = (int)response.StatusCode };
                }

                if (response.Headers.TryGetValues("X-Face-Count", out var faceCount))
                {
                    Response.Headers["X-Face-Count"] = faceCount.FirstOrDefault();
                }
                var image = await response.Content.ReadAsByteArrayAsync();
                return File(image, response.Content.Headers.ContentType?.MediaType ?? "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Face-restore frame proxy failed");
                return StatusCode(500, new { error = "Face-restore preview failed" });
            }
        }

        [HttpPost("face-restore/load")]
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> FaceRestoreLoad([FromQuery] string model_name = "gfpgan-v1.4")
        {
            try
            {
                // v1.6.1.21 (P1b) - allowlist now derived from the embedded registry (category="face_restore"),
                // symmetric to the frontend dropdown that v1.6.1.19 auto-populated. Was hardcoded
                // {gfpgan-v1.4, codeformer} — caused FrontendBackend asymmetric drift any time a new
                // face-restore model was added (UI showed it, backend 400ed). See _faceRestoreModelIds.
                if (!_faceRestoreModelIds.Value.Contains(model_name))
                    return BadRequest(new { message = "Invalid face-restore model" });

                var serviceUrl = GetValidatedServiceUrl();
                var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("model_name", model_name)
                });
                using var response = await GetDownloadClient().PostAsync($"{serviceUrl}/face-restore/load", form, HttpContext.RequestAborted);
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
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> FaceRestoreStatus()
        {
            try
            {
                var serviceUrl = GetValidatedServiceUrl();
                using var response = await GetAiServiceClient().GetAsync($"{serviceUrl}/face-restore/status", HttpContext.RequestAborted);
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
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> FaceRestoreUnload()
        {
            try
            {
                var serviceUrl = GetValidatedServiceUrl();
                using var response = await GetAiServiceClient().PostAsync($"{serviceUrl}/face-restore/unload", null, HttpContext.RequestAborted);
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
                using var response = await GetAiServiceClient().GetAsync($"{serviceUrl}/metrics", HttpContext.RequestAborted);
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
        [Produces(MediaTypeNames.Application.Json)]
        public async Task<ActionResult> GpuVerify()
        {
            try
            {
                var serviceUrl = GetValidatedServiceUrl();
                using var response = await GetAiServiceClient().GetAsync($"{serviceUrl}/gpu-verify", HttpContext.RequestAborted);
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
                using var response = await GetAiServiceClient().GetAsync($"{serviceUrl}/health/detailed", HttpContext.RequestAborted);
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
                using var response = await GetAiServiceClient().PostAsync($"{serviceUrl}/config", content, HttpContext.RequestAborted);
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
                using var response = await GetAiServiceClient().GetAsync($"{serviceUrl}/models/disk-usage", HttpContext.RequestAborted);
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
                    null,
                    HttpContext.RequestAborted);
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

                using var response = await GetAiServiceClient().PostAsync($"{serviceUrl}/upscale-frame", content, HttpContext.RequestAborted);

                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                    return StatusCode(503, "AI service busy");

                if (!response.IsSuccessStatusCode)
                {
                    // v1.8.3.27 - pass the service's own reason through. On a freshly started
                    // container the service answers {"detail":"No model loaded"} - one line
                    // that tells the user exactly what to do - and this used to replace it
                    // with "Frame upscaling failed", which tells them nothing. Found by
                    // calling the endpoint on a live server after a container restart.
                    var detail = await ReadServiceDetailAsync(response);
                    return StatusCode((int)response.StatusCode, new { error = "Frame upscaling failed", detail });
                }

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

                using var response = await GetMultiFrameClient().PostAsync($"{serviceUrl}/upscale-video-chunk", content, HttpContext.RequestAborted);

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

                using var response = await GetBenchmarkClient().GetAsync($"{serviceUrl}/benchmark-frame?width={width}&height={height}", HttpContext.RequestAborted);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return Content(json, "application/json");
                }

                return StatusCode((int)response.StatusCode,
                    new { error = "Frame benchmark failed", detail = await ReadServiceDetailAsync(response) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frame benchmark proxy failed");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        // v1.7.1 - The local _validFilterPresets array previously here moved to
        // VideoFilterService.SupportedPresets (single source of truth, semantically lives where
        // the preset implementations live). 5 inline references in this file now go through there.

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
                availablePresets = VideoFilterService.SupportedPresets
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
            if (body.Preset != null && !VideoFilterService.SupportedPresets.Contains(body.Preset))
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
                availablePresets = VideoFilterService.SupportedPresets
            });
        }

        /// <summary>
        /// Generate a live filter preview on a real video frame (admin only).
        /// Extracts a frame from the given media item, applies the preset's FFmpeg filter chain,
        /// and returns both the original and filtered frames as base64 JPEG.
        /// </summary>
        [HttpGet("filter-preview/frame/{itemId}")]
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

                if (!VideoFilterService.SupportedPresets.Contains(preset))
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
