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
    /// Core upscaling engine - Docker-based implementation
    /// Delegates AI processing to the external Docker AI service via HTTP.
    /// </summary>
    /// <summary>
    /// v1.8.3.13 - the result of an automatic model decision INCLUDING its reasoning.
    /// Before this, the reasoning existed only as LogDebug output (off by default in
    /// Jellyfin) and was discarded, so users saw a model they never picked with no
    /// explanation - especially when a multi-frame model was silently substituted.
    /// </summary>
    /// <param name="Model">The model that will actually be used.</param>
    /// <param name="Reason">One human-readable sentence: why this model.</param>
    /// <param name="Signals">The facts the heuristic reacted to (content, resolution, job type).</param>
    /// <param name="SubstitutedFrom">Set when the preferred model was unavailable and a stand-in was used.</param>
    /// <param name="SubstitutionReason">Why the substitution happened (null when none).</param>
    /// <param name="Scale">
    /// v1.8.3.14 - the factor the output will REALLY grow by. The AI service ignores the
    /// requested scale and uses the loaded model's native one, so reporting the configured
    /// value made a 1080p job claim "2x" while producing 8K frames. 0 means the model id
    /// does not encode a scale; callers then keep their configured value.
    /// </param>
    public record AutoPick(
        string Model,
        string Reason,
        string[] Signals,
        string? SubstitutedFrom,
        string? SubstitutionReason,
        int Scale = 0);

    public class UpscalerCore : IUpscalerCore, IDisposable
    {
        private readonly ILogger<UpscalerCore> _logger;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly IFileSystem _fileSystem;
        private readonly IApplicationPaths _appPaths;
        private readonly HttpUpscalerService _httpUpscaler;

        /// <summary>
        /// Check if an IP address is private, loopback, link-local, or otherwise reserved.
        /// Handles IPv4-mapped IPv6 addresses (e.g. ::ffff:192.168.1.1) correctly.
        /// </summary>
        private static bool IsPrivateOrReservedIp(System.Net.IPAddress ip)
        {
            if (System.Net.IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return true;

            // Normalize IPv4-mapped IPv6 to IPv4 for range checks
            var checkIp = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
            var bytes = checkIp.GetAddressBytes();

            if (bytes.Length == 4)
            {
                return bytes[0] == 0 ||                                              // 0.x.x.x
                    bytes[0] == 127 ||                                               // 127.x.x.x
                    bytes[0] == 10 ||                                                // 10.x.x.x
                    (bytes[0] == 169 && bytes[1] == 254) ||                          // 169.254.x.x link-local
                    (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||        // 100.64-127.x.x CGNAT
                    (bytes[0] == 192 && bytes[1] == 168) ||                          // 192.168.x.x
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31);           // 172.16-31.x.x
            }

            return false;
        }

        // Shared HttpClient for webhook delivery — reused to avoid socket exhaustion
        private static readonly System.Net.Http.HttpClient _webhookClient = new(new System.Net.Http.SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        })
        { Timeout = TimeSpan.FromSeconds(10) };
        
        // Hardware detection cache (avoid repeated HTTP calls)
        private static HardwareProfile? _cachedHardwareProfile;
        private static DateTime _lastHardwareCheck = DateTime.MinValue;
        private static readonly object _hwCacheLock = new();
        
        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();
        private volatile bool _disposed;
        
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
            
            var version = typeof(Plugin).Assembly.GetName().Version?.ToString(4) ?? "unknown";
            _logger.LogInformation("UpscalerCore v{Version} initialized - Docker-based AI processing", version);
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
                // Resolve "auto" to the best model for the content
                var effectiveModel = model == "auto" ? ResolveAutoModel() : model;

                // Build model chain: primary model + fallback chain from config
                var modelChain = BuildModelChain(effectiveModel);

                foreach (var candidateModel in modelChain)
                {
                    try
                    {
                        _logger.LogDebug("Trying model {Model} for image upscale ({Size} bytes, scale={Scale})",
                            candidateModel, imageData.Length, scale);

                        var modelLoaded = await _httpUpscaler.EnsureModelLoadedAsync(candidateModel, cancellationToken);
                        if (!modelLoaded)
                        {
                            _logger.LogWarning("Could not load model {Model}, trying next in chain", candidateModel);
                            continue;
                        }

                        var result = await _httpUpscaler.UpscaleImageAsync(imageData, scale, cancellationToken);

                        if (result != null && result.Length > 0)
                        {
                            stopwatch.Stop();
                            _logger.LogInformation("Image upscaled with {Model}: {InputSize} -> {OutputSize} bytes in {Time}ms",
                                candidateModel, imageData.Length, result.Length, stopwatch.ElapsedMilliseconds);
                            return result;
                        }

                        _logger.LogWarning("Model {Model} returned empty result, trying next", candidateModel);
                    }
                    catch (Exception ex) when (modelChain.IndexOf(candidateModel) < modelChain.Count - 1)
                    {
                        _logger.LogWarning(ex, "Model {Model} failed, trying next in fallback chain", candidateModel);
                    }
                }

                _logger.LogError("All models in chain failed, using fallback resize");
                return await FallbackResizeAsync(imageData, scale);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI upscaling failed, using fallback resize");
                return await FallbackResizeAsync(imageData, scale);
            }
        }

        /// <summary>
        /// Build a model fallback chain: primary model + configured fallback models.
        /// </summary>
        private List<string> BuildModelChain(string primaryModel)
        {
            var chain = new List<string> { primaryModel };
            var fallbackConfig = Config.ModelFallbackChain;
            if (!string.IsNullOrWhiteSpace(fallbackConfig))
            {
                var fallbacks = fallbackConfig.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var fb in fallbacks)
                {
                    if (!chain.Contains(fb, StringComparer.OrdinalIgnoreCase))
                        chain.Add(fb);
                }
            }
            return chain;
        }

        /// <summary>
        /// Send a webhook notification for job events.
        /// Fire-and-forget — never blocks or throws.
        /// </summary>
        public async Task SendWebhookAsync(string eventType, string itemName, bool success, string? error = null)
        {
            var webhookUrl = Config.WebhookUrl;
            if (string.IsNullOrWhiteSpace(webhookUrl))
                return;

            // SSRF prevention: only allow http/https webhook URLs, block private IPs
            if (webhookUrl.Length > 2048)
            {
                _logger.LogWarning("Webhook URL rejected (exceeds 2048 chars)");
                return;
            }

            if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var webhookUri) ||
                (webhookUri.Scheme != "http" && webhookUri.Scheme != "https"))
            {
                _logger.LogWarning("Webhook URL rejected (invalid scheme): {Url}", webhookUrl);
                return;
            }

            // Block localhost, zero address, and private IP ranges
            var host = webhookUri.Host;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase) ||
                host == "::1" ||
                host == "0.0.0.0" ||
                host.StartsWith("[::ffff:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Webhook URL rejected (localhost/blocked host): {Url}", webhookUrl);
                return;
            }

            if (System.Net.IPAddress.TryParse(host, out var ip))
            {
                if (IsPrivateOrReservedIp(ip))
                {
                    _logger.LogWarning("Webhook URL rejected (private IP): {Url}", webhookUrl);
                    return;
                }
            }

            if (eventType == "complete" && !Config.WebhookOnComplete) return;
            if (eventType == "failure" && !Config.WebhookOnFailure) return;

            try
            {
                // DNS rebinding protection: resolve hostname and re-check resolved IPs
                if (!System.Net.IPAddress.TryParse(host, out _))
                {
                    var addresses = await System.Net.Dns.GetHostAddressesAsync(host);
                    foreach (var addr in addresses)
                    {
                        if (IsPrivateOrReservedIp(addr))
                        {
                            _logger.LogWarning("Webhook URL rejected (DNS resolves to private IP {Ip}): {Url}", addr, webhookUrl);
                            return;
                        }
                    }
                }

                var payload = new Dictionary<string, object>
                {
                    ["event"] = eventType,
                    ["item"] = itemName,
                    ["success"] = success,
                    ["timestamp"] = DateTime.UtcNow.ToString("o"),
                    ["plugin_version"] = Config.PluginVersion
                };
                if (error != null) payload["error"] = error;

                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                await _webhookClient.PostAsync(webhookUrl, content);
                _logger.LogDebug("Webhook sent: {Event} for {Item}", eventType, itemName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Webhook delivery failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Resolve "auto" model selection to the best default model.
        /// Uses the configured model if set, otherwise picks realesrgan-x4.
        /// </summary>
        private string ResolveAutoModel()
        {
            var configured = Config.Model;
            if (!string.IsNullOrEmpty(configured) && configured != "auto")
                return configured;
            return "realesrgan-x4";
        }

        /// <summary>
        /// Pick the first available model from a preferred → fallback chain. Logs the fallback
        /// decision (the underlying <see cref="ModelAvailability.PickAvailable"/> is pure; this
        /// wrapper adds telemetry).
        /// </summary>
        /// <remarks>
        /// v1.6.1.19 - the underlying <c>KnownUnavailable</c> set + pure picker live in
        /// <see cref="ModelAvailability"/> so <see cref="HardwareBenchmarkService"/> and any
        /// future resolver can use the same source of truth. Before v1.6.1.19 this was a private
        /// HashSet inside UpscalerCore which led to v1.6.1.18's audit catching the same bug
        /// pattern duplicated in HardwareBenchmarkService.
        /// </remarks>
        private string PickAvailable(string preferred, params string[] fallbacks)
        {
            var picked = ModelAvailability.PickAvailable(preferred, fallbacks);
            if (picked == preferred)
            {
                return picked;
            }

            // Did we land on the ultimate fallback (realesrgan-x4) only because every explicit
            // candidate was unavailable? That deserves a Warning. Otherwise this is a normal
            // one-step fallback and Information is enough.
            bool everyCandidateUnavailable = ModelAvailability.IsKnownUnavailable(preferred);
            foreach (var fb in fallbacks)
            {
                if (string.IsNullOrWhiteSpace(fb)) continue;
                if (!ModelAvailability.IsKnownUnavailable(fb)) { everyCandidateUnavailable = false; break; }
            }

            if (everyCandidateUnavailable && picked == "realesrgan-x4")
            {
                _logger.LogWarning("Auto-model: {Preferred} and all fallbacks are unavailable, defaulting to realesrgan-x4", preferred);
            }
            else
            {
                _logger.LogInformation("Auto-model: {Preferred} is unavailable (self-host required), falling back to {Fallback}", preferred, picked);
            }
            return picked;
        }

        /// <summary>
        /// Resolve the best model for video content based on metadata.
        /// Considers: anime vs live-action, resolution, batch vs real-time.
        /// </summary>
        /// <param name="genres">Content genre tags (e.g. "Animation", "Anime")</param>
        /// <param name="width">Source video width</param>
        /// <param name="height">Source video height</param>
        /// <param name="isBatch">True for scheduled batch processing, false for real-time</param>
        /// <param name="inputFrames">Available multi-frame model frame count (from service status)</param>
        /// <returns>Best model name for the content</returns>
        public string ResolveModelForVideo(
            IEnumerable<string>? genres = null,
            int width = 0,
            int height = 0,
            bool isBatch = true,
            int inputFrames = 1,
            bool forceAuto = false)
            => ResolveModelForVideoDetailed(genres, width, height, isBatch, inputFrames, forceAuto).Model;

        /// <summary>
        /// v1.8.3.14 - hardware tier from the service's /recommend ("strong-gpu", "weak-cpu", ...),
        /// cached because it effectively never changes for a given box. The resolver is
        /// synchronous and must never perform a network call, so this is refreshed by callers
        /// that are async anyway. Null means "unknown" and disables the cap entirely.
        /// </summary>
        private static volatile string? _hardwareTier;

        /// <summary>Last known hardware tier, or null when the service was never reached.</summary>
        public static string? HardwareTier => _hardwareTier;

        /// <summary>
        /// Store the tier reported by the AI service. A null/blank value clears the cap
        /// rather than pinning a stale one.
        /// </summary>
        public static void UpdateHardwareTier(string? tier)
        {
            _hardwareTier = string.IsNullOrWhiteSpace(tier) ? null : tier.Trim();
        }

        /// <summary>
        /// v1.8.3.13 - same heuristic as <see cref="ResolveModelForVideo"/>, but it KEEPS the
        /// reasoning instead of writing it to a debug log nobody has enabled. The UI shows
        /// Reason/Signals next to the picked model, and a substitution (multi-frame model has no
        /// public ONNX -> single-frame stand-in) is surfaced instead of silently swapping the
        /// user's expectation. Same "never fail silently" rule the favorites flow follows.
        /// </summary>
        public AutoPick ResolveModelForVideoDetailed(
            IEnumerable<string>? genres = null,
            int width = 0,
            int height = 0,
            bool isBatch = true,
            int inputFrames = 1,
            bool forceAuto = false)
        {
            var configured = Config.Model;
            if (!forceAuto && !string.IsNullOrEmpty(configured) && configured != "auto")
            {
                return new AutoPick(configured, "Custom mode: your configured model is used as-is.",
                    new[] { "Mode: Custom" }, null, null, ModelScale.NativeScaleOf(configured));
            }

            var genreList = genres?.Select(g => g.ToLowerInvariant()).ToList() ?? new List<string>();
            bool isAnime = genreList.Any(g => g.Contains("anime") || g.Contains("animation") || g.Contains("cartoon"));
            bool isLowRes = width > 0 && height > 0 && (width < 720 || height < 480);
            bool isVeryLowRes = width > 0 && height > 0 && (width < 480 || height < 360);

            // The signals the heuristic actually reacted to - shown verbatim in the UI.
            var signals = new List<string>();
            signals.Add(isAnime ? "Content: anime/animation (from genres)" : "Content: live action");
            if (width > 0 && height > 0)
            {
                var resLabel = isVeryLowRes ? " (very low)" : isLowRes ? " (low)" : "";
                signals.Add($"Resolution: {width}x{height}{resLabel}");
            }
            else
            {
                signals.Add("Resolution: unknown");
            }
            signals.Add(isBatch ? "Job: batch (quality first)" : "Job: real-time (speed first)");
            if (inputFrames > 1) signals.Add($"Multi-frame available: {inputFrames} frames");

            // v1.8.3.14 - hardware budget. The content heuristic knows what SUITS the
            // material; the service's /recommend knows what the MACHINE can do. Until now
            // nothing joined them, so auto could hand a Celeron a full restoration net.
            // The tier is cached (never fetched inside this synchronous resolver) and an
            // unknown tier means "no cap" - auto must never block on a missing service.
            var tier = HardwareTier;
            if (!string.IsNullOrEmpty(tier))
            {
                signals.Add($"Hardware: {HardwareBudget.DescribeTier(tier)}");
            }
            else
            {
                signals.Add("Hardware: unknown (service unreachable - no cap applied)");
            }

            // v1.8.3.14 - an explicit user override is never capped: the user asked for
            // this model, so we run it and merely note that it is heavy for this box.
            AutoPick PickOverride(string reason, string preferred, params string[] fallbacks)
            {
                var picked = PickAvailable(preferred, fallbacks);
                var localSignals = new List<string>(signals);
                if (!HardwareBudget.FitsTier(picked, tier))
                {
                    localSignals.Add($"Note: {picked} is heavy for this {HardwareBudget.DescribeTier(tier)} - kept because you selected it.");
                }
                if (string.Equals(picked, preferred, StringComparison.OrdinalIgnoreCase))
                {
                    return new AutoPick(picked, reason, localSignals.ToArray(), null, null, ModelScale.NativeScaleOf(picked));
                }
                return new AutoPick(picked, reason, localSignals.ToArray(), preferred,
                    $"{preferred} is not available on this service, so the closest available model was used instead.",
                    ModelScale.NativeScaleOf(picked));
            }

            AutoPick Pick(string reason, string preferred, params string[] fallbacks)
            {
                var picked = PickAvailable(preferred, fallbacks);

                // Step 2: walk the same ordered candidate list again, this time skipping
                // anything too heavy for the detected hardware.
                if (!HardwareBudget.FitsTier(picked, tier))
                {
                    var overBudget = picked;
                    string? affordable = null;
                    foreach (var candidate in fallbacks)
                    {
                        if (string.IsNullOrWhiteSpace(candidate)) continue;
                        if (ModelAvailability.IsKnownUnavailable(candidate)) continue;
                        if (!HardwareBudget.FitsTier(candidate, tier)) continue;
                        affordable = candidate;
                        break;
                    }
                    // The branch fallbacks carry no Light option, so on a weak CPU every
                    // job used to collapse to the crudest model in the catalog. Walk a
                    // quality-ordered ladder within the affordable weight class first
                    // (found by live-testing v1.8.3.14 on a CPU-only NAS).
                    if (affordable == null)
                    {
                        var max = HardwareBudget.MaxWeightFor(tier);
                        if (max != null)
                        {
                            foreach (var candidate in HardwareBudget.AffordableLadder(max.Value, isAnime))
                            {
                                if (ModelAvailability.IsKnownUnavailable(candidate)) continue;
                                if (!HardwareBudget.FitsTier(candidate, tier)) continue;
                                affordable = candidate;
                                break;
                            }
                        }
                    }
                    // Last resort: the lightest model in the catalog always runs.
                    affordable ??= "fsrcnn-x2";
                    return new AutoPick(affordable, reason, signals.ToArray(), overBudget,
                        $"{overBudget} suits the material but is too heavy for this {HardwareBudget.DescribeTier(tier)} - {affordable} was used instead so the job actually finishes.",
                        ModelScale.NativeScaleOf(affordable));
                }

                if (picked == preferred)
                {
                    return new AutoPick(picked, reason, signals.ToArray(), null, null, ModelScale.NativeScaleOf(picked));
                }
                var why = ModelAvailability.IsKnownUnavailable(preferred)
                    ? $"{preferred} has no public ONNX build (self-host required), so the closest available model was used instead."
                    : $"{preferred} is not available on this service, so the closest available model was used instead.";
                return new AutoPick(picked, reason, signals.ToArray(), preferred, why, ModelScale.NativeScaleOf(picked));
            }

            if (isBatch && inputFrames > 1)
            {
                if (isAnime)
                {
                    return Pick("Anime content in a batch job with multi-frame support -> AnimeSR v2 (temporal consistency).",
                        "animesr-v2-x4", "realesrgan-animevideo-x4", "anime-compact-x4");
                }
                if (isVeryLowRes)
                {
                    return Pick($"Very low resolution ({width}x{height}) in a batch job with multi-frame support -> RealBasicVSR handles heavy degradation best.",
                        "realbasicvsr-x4", "ultrasharp-v2-x4", "realesrgan-x4");
                }
                return Pick("Batch job with multi-frame support -> EDVR-M for temporal consistency.",
                    "edvr-m-x4", "ultrasharp-v2-x4", "nomos2-realplksr-x4", "realesrgan-x4");
            }

            if (isAnime)
            {
                var animeOverride = Config.PreferredAnimeModel;
                if (!string.IsNullOrWhiteSpace(animeOverride))
                {
                    signals.Add("Override: Preferred Anime Model is set");
                    return PickOverride($"Anime content and your Preferred Anime Model ({animeOverride}) applies.",
                        animeOverride, "realesrgan-animevideo-x4", "anime-compact-x4");
                }
                if (isBatch)
                {
                    // Live-test finding: this branch still handed 1080p a 4x model, i.e.
                    // 7680x4320. The live-action side already refuses that; anime is not
                    // a special case just because the source is drawn.
                    if (ModelScale.TargetScaleFor(width, height) >= 4)
                    {
                        return Pick("Anime content in a batch job - quality first.",
                            "realesrgan-animevideo-x4", "anime-compact-x4", "realesrgan-x4");
                    }
                    return Pick($"Anime batch job on {(width > 0 ? $"{width}x{height}" : "large")} material - 2x; a 4x pass would target 8K for no visible gain.",
                        "apisr-anime-x2", "span-x2", "realesrgan-animevideo-x4");
                }
                return Pick("Anime content in real time -> lightweight anime compact model (speed first).",
                    "anime-compact-x4", "realesrgan-animevideo-x4", "realesrgan-x4");
            }

            var liveActionOverride = Config.PreferredLiveActionModel;
            if (!string.IsNullOrWhiteSpace(liveActionOverride))
            {
                signals.Add("Override: Preferred Live-Action Model is set");
                return PickOverride($"Live action and your Preferred Live-Action Model ({liveActionOverride}) applies.",
                    liveActionOverride, "ultrasharp-v2-x4", "nomos2-realplksr-x4", "realesrgan-x4");
            }

            if (!isBatch)
            {
                if (isLowRes)
                {
                    return Pick($"Low resolution ({width}x{height}) in real time -> SPAN 2x stays fast and keeps the output size manageable.",
                        "span-x2", "nomosuni-compact-x2", "realesrgan-x4");
                }
                return Pick("HD content in real time -> ultra-fast 2x model for mild enhancement.",
                    "nomosuni-compact-x2", "span-x2", "realesrgan-x4");
            }

            if (isVeryLowRes)
            {
                return Pick($"Very low resolution ({width}x{height}) in a batch job - restore quality comes first.",
                    "ultrasharp-v2-x4", "nomos2-realplksr-x4", "realesrgan-x4");
            }
            if (isLowRes)
            {
                return Pick($"Low resolution ({width}x{height}) in a batch job - a full 4x restore is worth the time.", "realesrgan-x4");
            }
            // v1.8.3.14 - 4x on 1080p is 8K: four times the compute of a 2x pass for an
            // output no client can display. The 4x default only earns its cost on small
            // sources, which the two low-res branches above already cover.
            if (ModelScale.TargetScaleFor(width, height) >= 4)
            {
                return Pick("General batch job - balanced 4x default.", "realesrgan-x4");
            }
            return Pick($"Batch job on {(width > 0 ? $"{width}x{height}" : "large")} material - 2x; a 4x pass would target 8K for no visible gain.",
                "realesrgan-x2-plus", "span-x2", "realesrgan-x4");
        }

        /// <summary>
        /// Returns the preset key ("none", "vivid", "sharp-hd", ...) best suited for
        /// the given content. Mapped against VideoFilterService.GetPresetFilters keys.
        /// Conservative by default: returns "none" when we lack signal to choose.
        /// </summary>
        public string ResolveFilterForVideo(
            IEnumerable<string>? genres = null,
            int width = 0,
            int height = 0)
        {
            var genreList = genres?.Select(g => g.ToLowerInvariant()).ToList() ?? new List<string>();

            bool isAnime = genreList.Any(g => g.Contains("anime") || g.Contains("animation") || g.Contains("cartoon"));
            if (isAnime)
            {
                _logger.LogDebug("Auto-filter: anime content → vivid");
                return "vivid";
            }

            bool isHorror = genreList.Any(g => g.Contains("horror") || g.Contains("thriller"));
            if (isHorror)
            {
                _logger.LogDebug("Auto-filter: horror/thriller → drama");
                return "drama";
            }

            bool isSciFi = genreList.Any(g => g.Contains("sci-fi") || g.Contains("science fiction") || g.Contains("cyberpunk"));
            if (isSciFi)
            {
                _logger.LogDebug("Auto-filter: sci-fi → cyberpunk");
                return "cyberpunk";
            }

            bool isDoc = genreList.Any(g => g.Contains("documentary") || g.Contains("news"));
            if (isDoc)
            {
                _logger.LogDebug("Auto-filter: documentary → sharp-hd");
                return "sharp-hd";
            }

            // Low-res/SD source gets mild sharpening to recover detail lost to upscaling.
            bool isLowRes = width > 0 && height > 0 && (width < 1280 || height < 720);
            if (isLowRes)
            {
                _logger.LogDebug("Auto-filter: low-res ({W}x{H}) → sharp-hd", width, height);
                return "sharp-hd";
            }

            // HD content where we don't have genre signal — no filter beats a wrong filter.
            _logger.LogDebug("Auto-filter: no strong signal → none");
            return "none";
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
                // Known silent fallback: returning original bytes unmodified when all resize paths fail
                _logger.LogError(ex, "Fallback resize also failed, returning original image unmodified");
                return imageData; // Return original as last resort
            }
        }

        /// <summary>
        /// Detect available hardware capabilities.
        /// </summary>
        public async Task<HardwareProfile> DetectHardwareAsync()
        {
            // Return cached profile if fresh (cache for 60 seconds)
            lock (_hwCacheLock)
            {
                if (_cachedHardwareProfile != null && (DateTime.UtcNow - _lastHardwareCheck).TotalSeconds < 60)
                {
                    return _cachedHardwareProfile;
                }
            }

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

                    profile.SupportsCUDA = profile.CudaAvailable;
                    profile.SupportsDirectML = profile.DirectMlAvailable;
                    profile.ServiceAvailable = true;
                    profile.AvailableProviders = new List<string>(status.AvailableProviders);
                    profile.MaxConcurrentStreams = status.MaxConcurrent;
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

            // Cache the result
            lock (_hwCacheLock)
            {
                _cachedHardwareProfile = profile;
                _lastHardwareCheck = DateTime.UtcNow;
            }

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
                ["hardware_cached"] = _cachedHardwareProfile != null
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Do NOT dispose _httpUpscaler - it is a singleton managed by the DI container
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }

    // PerformanceMetrics class moved to Models/UpscalerModels.cs to avoid duplication
}