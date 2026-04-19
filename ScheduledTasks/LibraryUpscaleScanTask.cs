using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using JellyfinUpscalerPlugin.Services;
using JellyfinUpscalerPlugin.Models;

namespace JellyfinUpscalerPlugin.ScheduledTasks
{
    /// <summary>
    /// Scheduled task that scans the library for low-resolution media
    /// and processes them through the AI upscaling pipeline.
    /// Appears in Jellyfin Dashboard → Scheduled Tasks → AI Upscaler category.
    /// </summary>
    public class LibraryUpscaleScanTask : IScheduledTask
    {
        private readonly ILogger<LibraryUpscaleScanTask> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly HttpUpscalerService _httpUpscalerService;
        private readonly VideoProcessor _videoProcessor;
        private readonly UpscalerCore _upscalerCore;

        public LibraryUpscaleScanTask(
            ILogger<LibraryUpscaleScanTask> logger,
            ILibraryManager libraryManager,
            HttpUpscalerService httpUpscalerService,
            VideoProcessor videoProcessor,
            UpscalerCore upscalerCore)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _httpUpscalerService = httpUpscalerService;
            _videoProcessor = videoProcessor;
            _upscalerCore = upscalerCore;
        }

        public string Name => "Scan & Upscale Library";
        public string Key => "AIUpscalerLibraryScan";
        public string Category => "AI Upscaler";
        public string Description =>
            "Scans media library for content below the configured resolution threshold " +
            "and upscales matching items using the Docker AI service. " +
            "Upscaled files are saved alongside originals with '_upscaled' suffix.";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.DailyTrigger,
                    TimeOfDayTicks = TimeSpan.FromHours(3).Ticks // 3 AM daily
                }
            };
        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.EnablePlugin)
            {
                _logger.LogInformation("AI Upscaler: Plugin disabled, skipping library scan");
                return;
            }

            _logger.LogInformation("AI Upscaler: Starting library scan for low-resolution media...");

            // Check Docker AI service connectivity first
            _logger.LogInformation("AI Upscaler: Checking Docker AI service at {Url}...", config.AiServiceUrl);
            var serviceAvailable = await _httpUpscalerService.IsServiceAvailableAsync(cancellationToken);
            if (!serviceAvailable)
            {
                _logger.LogWarning("AI Upscaler: Docker AI service not reachable at {Url}, aborting scan. " +
                    "Make sure the Docker container is running and the URL is correct in plugin settings.",
                    config.AiServiceUrl);
                return;
            }
            _logger.LogInformation("AI Upscaler: Docker AI service is online");

            // Parse library filter: empty = all libraries, else CSV of virtual-folder item IDs
            var enabledLibraryIds = (config.EnabledLibraryIds ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => Guid.TryParse(s, out _))
                .Select(Guid.Parse)
                .ToHashSet();

            // Resolve library IDs to library paths (for path-based filtering post-fetch;
            // InternalItemsQuery.ParentId only accepts a single GUID, and users may pick multiple).
            HashSet<string>? enabledLibraryPaths = null;
            if (enabledLibraryIds.Count > 0)
            {
                var virtualFolders = _libraryManager.GetVirtualFolders();
                enabledLibraryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var vf in virtualFolders)
                {
                    if (Guid.TryParse(vf.ItemId, out var vfId) && enabledLibraryIds.Contains(vfId))
                    {
                        foreach (var loc in vf.Locations ?? Array.Empty<string>())
                        {
                            if (!string.IsNullOrEmpty(loc))
                                enabledLibraryPaths.Add(Path.GetFullPath(loc));
                        }
                    }
                }
                _logger.LogInformation(
                    "AI Upscaler: Library filter active — {Count} selected, {Paths} resolved paths",
                    enabledLibraryIds.Count, enabledLibraryPaths.Count);
            }

            // Get all video items from the library
            var query = new InternalItemsQuery
            {
                MediaTypes = new[] { MediaType.Video },
                IsVirtualItem = false,
                Recursive = true
            };

            var items = _libraryManager.GetItemList(query);

            if (enabledLibraryPaths != null && enabledLibraryPaths.Count > 0)
            {
                items = items.Where(it =>
                {
                    var p = it.Path;
                    if (string.IsNullOrEmpty(p)) return false;
                    var full = Path.GetFullPath(p);
                    foreach (var lib in enabledLibraryPaths)
                    {
                        var libWithSep = lib.EndsWith(Path.DirectorySeparatorChar) ? lib : lib + Path.DirectorySeparatorChar;
                        if (full.StartsWith(libWithSep, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    return false;
                }).ToList();
            }

            var totalItems = items.Count;

            _logger.LogInformation("AI Upscaler: Found {Total} video items to analyze", totalItems);

            // Resolution threshold from config (default: 1080p)
            var minWidth = config.MinResolutionWidth > 0 ? config.MinResolutionWidth : 1920;
            var minHeight = config.MinResolutionHeight > 0 ? config.MinResolutionHeight : 1080;

            // Phase 1: Scan and collect low-res items
            var lowResVideos = new List<(Video video, int width, int height)>();
            var scanned = 0;
            var noResolutionCount = 0;
            var alreadyUpscaledCount = 0;
            var highResCount = 0;

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;

                // Use first 30% of progress for scanning
                progress.Report((double)scanned / totalItems * 30);

                if (item is not Video video || string.IsNullOrEmpty(video.Path))
                {
                    continue;
                }

                // Skip if already upscaled (file has _upscaled suffix)
                var fileName = Path.GetFileNameWithoutExtension(video.Path);
                if (fileName.EndsWith("_upscaled", StringComparison.OrdinalIgnoreCase))
                {
                    alreadyUpscaledCount++;
                    continue;
                }

                // Skip if an upscaled version already exists
                var dir = Path.GetDirectoryName(video.Path);
                var ext = Path.GetExtension(video.Path);
                var upscaledPath = Path.Combine(dir ?? "", fileName + "_upscaled" + ext);
                if (File.Exists(upscaledPath))
                {
                    alreadyUpscaledCount++;
                    continue;
                }

                // Check video resolution via media streams (multiple fallback methods)
                int? detectedWidth = null;
                int? detectedHeight = null;

                // Method 1: MediaStreams from DB
                var mediaStreams = video.GetMediaStreams();
                var videoStream = mediaStreams?.FirstOrDefault(s => s.Type == MediaStreamType.Video);
                if (videoStream?.Width > 0 && videoStream?.Height > 0)
                {
                    detectedWidth = videoStream.Width;
                    detectedHeight = videoStream.Height;
                }

                // Method 2: Direct properties on Video object
                if (detectedWidth == null || detectedHeight == null)
                {
                    if (video.Width > 0 && video.Height > 0)
                    {
                        detectedWidth = video.Width;
                        detectedHeight = video.Height;
                    }
                }

                if (detectedWidth == null || detectedHeight == null)
                {
                    noResolutionCount++;
                    _logger.LogDebug("AI Upscaler: Skipping {Name} — no resolution info available", video.Name);
                    continue;
                }

                if (detectedWidth < minWidth || detectedHeight < minHeight)
                {
                    lowResVideos.Add((video, detectedWidth.Value, detectedHeight.Value));
                    _logger.LogDebug("AI Upscaler: Candidate: {Name} ({W}x{H})", video.Name, detectedWidth, detectedHeight);
                }
                else
                {
                    highResCount++;
                }
            }

            _logger.LogInformation(
                "AI Upscaler: Scan complete. {Total} videos: {LowRes} below {Width}x{Height}, " +
                "{HighRes} already high-res, {Upscaled} already upscaled, {NoRes} no resolution info",
                totalItems, lowResVideos.Count, minWidth, minHeight,
                highResCount, alreadyUpscaledCount, noResolutionCount);

            if (lowResVideos.Count == 0)
            {
                progress.Report(100);
                return;
            }

            // Apply MaxItemsPerScan limit
            var maxItems = config.MaxItemsPerScan;
            if (maxItems > 0 && lowResVideos.Count > maxItems)
            {
                _logger.LogInformation("Limiting scan to {Max} items (of {Total} found)", maxItems, lowResVideos.Count);
                lowResVideos = lowResVideos.Take(maxItems).ToList();
            }

            // Phase 2: Process low-res videos through the AI upscaling pipeline
            // Get service status for multi-frame model detection
            var serviceStatus = await _httpUpscalerService.GetServiceStatusAsync();
            int serviceInputFrames = serviceStatus?.InputFrames ?? 1;

            var scaleFactor = config.ScaleFactor > 0 ? config.ScaleFactor : 2;
            var successCount = 0;
            var failCount = 0;

            _logger.LogInformation(
                "AI Upscaler: Starting upscaling of {Count} videos, scale={Scale}x, service input_frames={InputFrames}",
                lowResVideos.Count, scaleFactor, serviceInputFrames);

            for (int i = 0; i < lowResVideos.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (video, width, height) = lowResVideos[i];
                var inputPath = video.Path;
                var dir = Path.GetDirectoryName(inputPath) ?? "";
                var ext = Path.GetExtension(inputPath);
                var baseName = Path.GetFileNameWithoutExtension(inputPath);
                var outputPath = Path.Combine(dir, baseName + "_upscaled" + ext);

                // Auto-select best model for this specific video's content (or use configured model)
                string model;
                if (config.EnableAutoModelSelection && (string.IsNullOrEmpty(config.Model) || config.Model == "auto"))
                {
                    model = _upscalerCore.ResolveModelForVideo(
                        genres: video.Genres,
                        width: width,
                        height: height,
                        isBatch: true,
                        inputFrames: serviceInputFrames);
                }
                else
                {
                    model = config.Model ?? "realesrgan-x4";
                }

                // Progress: 30% scan + 70% processing
                var processingProgress = 30 + ((double)(i + 1) / lowResVideos.Count * 70);
                progress.Report(processingProgress);

                _logger.LogInformation(
                    "AI Upscaler: [{Index}/{Total}] Processing: {Name} ({Width}x{Height}) model={Model} -> {Output}",
                    i + 1, lowResVideos.Count, video.Name, width, height, model, Path.GetFileName(outputPath));

                try
                {
                    var options = new VideoProcessingOptions
                    {
                        Model = model,
                        ScaleFactor = scaleFactor,
                        QualityLevel = config.QualityLevel ?? "medium",
                        PreserveAudio = true,
                        PreserveSubtitles = true,
                        EnableAIUpscaling = true
                    };

                    var result = await _videoProcessor.ProcessVideoAsync(
                        inputPath, outputPath, options, cancellationToken);

                    if (result.Success)
                    {
                        successCount++;
                        _logger.LogInformation(
                            "AI Upscaler: Successfully upscaled: {Name} -> {Output}",
                            video.Name, Path.GetFileName(outputPath));

                        // Fire webhook notification (fire-and-forget)
                        _ = _upscalerCore.SendWebhookAsync("complete", video.Name, true);
                    }
                    else
                    {
                        failCount++;
                        _logger.LogWarning(
                            "AI Upscaler: Failed to upscale {Name}: {Error}",
                            video.Name, result.Error);

                        // Fire webhook notification (fire-and-forget)
                        _ = _upscalerCore.SendWebhookAsync("failure", video.Name, false, result.Error);

                        // Clean up partial output
                        if (File.Exists(outputPath))
                        {
                            try { File.Delete(outputPath); }
                            catch (Exception ex) { _logger.LogWarning(ex, "AI Upscaler: Failed to cleanup partial output: {Path}", outputPath); }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("AI Upscaler: Task cancelled during processing of {Name}", video.Name);
                    throw;
                }
                catch (Exception ex)
                {
                    failCount++;
                    _logger.LogError(ex, "AI Upscaler: Error processing {Name}", video.Name);

                    // Fire webhook notification (fire-and-forget)
                    _ = _upscalerCore.SendWebhookAsync("failure", video.Name, false, ex.Message);

                    // Clean up partial output
                    if (File.Exists(outputPath))
                    {
                        try { File.Delete(outputPath); }
                        catch (Exception cleanupEx) { _logger.LogWarning(cleanupEx, "AI Upscaler: Failed to cleanup partial output: {Path}", outputPath); }
                    }
                }
            }

            _logger.LogInformation(
                "AI Upscaler: Batch processing complete. Success: {Success}, Failed: {Failed}, Total: {Total}",
                successCount, failCount, lowResVideos.Count);

            progress.Report(100);
        }
    }
}
