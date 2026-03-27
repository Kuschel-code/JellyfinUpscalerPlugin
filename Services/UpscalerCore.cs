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
    public class UpscalerCore : IDisposable
    {
        private readonly ILogger<UpscalerCore> _logger;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly IFileSystem _fileSystem;
        private readonly IApplicationPaths _appPaths;
        private readonly HttpUpscalerService _httpUpscaler;

        // Shared HttpClient for webhook delivery — reused to avoid socket exhaustion
        private static readonly System.Net.Http.HttpClient _webhookClient = new() { Timeout = TimeSpan.FromSeconds(10) };
        
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

                _logger.LogWarning("All models in chain failed, using fallback resize");
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

            // SSRF prevention: only allow http/https webhook URLs
            if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var webhookUri) ||
                (webhookUri.Scheme != "http" && webhookUri.Scheme != "https"))
            {
                _logger.LogWarning("Webhook URL rejected (invalid scheme): {Url}", webhookUrl);
                return;
            }

            if (eventType == "complete" && !Config.WebhookOnComplete) return;
            if (eventType == "failure" && !Config.WebhookOnFailure) return;

            try
            {
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
                _logger.LogDebug(ex, "Webhook delivery failed (non-critical)");
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
            int inputFrames = 1)
        {
            // Check if user has explicitly configured a non-auto model
            var configured = Config.Model;
            if (!string.IsNullOrEmpty(configured) && configured != "auto")
                return configured;

            var genreList = genres?.Select(g => g.ToLowerInvariant()).ToList() ?? new List<string>();
            bool isAnime = genreList.Any(g => g.Contains("anime") || g.Contains("animation") || g.Contains("cartoon"));
            bool isLowRes = width > 0 && height > 0 && (width < 720 || height < 480);
            bool isVeryLowRes = width > 0 && height > 0 && (width < 480 || height < 360);

            // Multi-frame VSR: best quality for batch processing
            if (isBatch && inputFrames > 1)
            {
                if (isAnime)
                {
                    _logger.LogDebug("Auto-model: anime content + multi-frame batch → animesr-v2-x4");
                    return "animesr-v2-x4";
                }
                if (isVeryLowRes)
                {
                    // Very low res (VHS/DVD quality) → RealBasicVSR handles degradation best
                    _logger.LogDebug("Auto-model: very low-res ({W}x{H}) + multi-frame batch → realbasicvsr-x4", width, height);
                    return "realbasicvsr-x4";
                }
                // General multi-frame: EDVR-M is the safe default
                _logger.LogDebug("Auto-model: multi-frame batch → edvr-m-x4");
                return "edvr-m-x4";
            }

            // Single-frame models
            if (isAnime)
            {
                if (isBatch)
                {
                    _logger.LogDebug("Auto-model: anime content + batch → realesrgan-animevideo-x4");
                    return "realesrgan-animevideo-x4";
                }
                // Real-time anime: use the lightweight compact model
                _logger.LogDebug("Auto-model: anime content + real-time → anime-compact-x4");
                return "anime-compact-x4";
            }

            if (!isBatch)
            {
                // Real-time: prioritize speed. Use 2x models for lower VRAM/compute cost.
                // Low-res (480p) with 4x model would produce 1920p — too expensive for real-time.
                if (isLowRes)
                {
                    _logger.LogDebug("Auto-model: low-res real-time → span-x2 (fast 2x, manageable output)");
                    return "span-x2";
                }
                // HD content: already high-res, use lightweight 2x for mild enhancement
                _logger.LogDebug("Auto-model: HD real-time → nomosuni-compact-x2 (ultra-fast 2x)");
                return "nomosuni-compact-x2";
            }

            // Batch single-frame: prioritize quality
            if (isVeryLowRes)
            {
                _logger.LogDebug("Auto-model: very low-res batch → ultrasharp-v2-x4 (best quality)");
                return "ultrasharp-v2-x4";
            }
            if (isLowRes)
            {
                _logger.LogDebug("Auto-model: low-res batch → realesrgan-x4");
                return "realesrgan-x4";
            }

            // Default batch: good balance
            _logger.LogDebug("Auto-model: general batch → realesrgan-x4");
            return "realesrgan-x4";
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