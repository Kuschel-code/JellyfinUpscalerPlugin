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

        public LibraryUpscaleScanTask(
            ILogger<LibraryUpscaleScanTask> logger,
            ILibraryManager libraryManager,
            HttpUpscalerService httpUpscalerService,
            VideoProcessor videoProcessor)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _httpUpscalerService = httpUpscalerService;
            _videoProcessor = videoProcessor;
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
            var serviceAvailable = await _httpUpscalerService.IsServiceAvailableAsync(cancellationToken);
            if (!serviceAvailable)
            {
                _logger.LogWarning("AI Upscaler: Docker AI service not reachable at {Url}, aborting scan",
                    config.AiServiceUrl);
                return;
            }

            // Get all video items from the library
            var query = new InternalItemsQuery
            {
                MediaTypes = new[] { MediaType.Video },
                IsVirtualItem = false,
                Recursive = true
            };

            var items = _libraryManager.GetItemList(query);
            var totalItems = items.Count;

            _logger.LogInformation("AI Upscaler: Found {Total} video items to analyze", totalItems);

            // Resolution threshold from config (default: 1080p)
            var minWidth = config.MinResolutionWidth > 0 ? config.MinResolutionWidth : 1920;
            var minHeight = config.MinResolutionHeight > 0 ? config.MinResolutionHeight : 1080;

            // Phase 1: Scan and collect low-res items
            var lowResVideos = new List<(Video video, int width, int height)>();
            var scanned = 0;

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
                    continue;
                }

                // Skip if an upscaled version already exists
                var dir = Path.GetDirectoryName(video.Path);
                var ext = Path.GetExtension(video.Path);
                var upscaledPath = Path.Combine(dir ?? "", fileName + "_upscaled" + ext);
                if (File.Exists(upscaledPath))
                {
                    continue;
                }

                // Check video resolution via media streams
                var mediaStreams = video.GetMediaStreams();
                var videoStream = mediaStreams?.FirstOrDefault(s => s.Type == MediaStreamType.Video);

                if (videoStream == null || videoStream.Width == null || videoStream.Height == null)
                {
                    continue;
                }

                if (videoStream.Width < minWidth || videoStream.Height < minHeight)
                {
                    lowResVideos.Add((video, videoStream.Width ?? 0, videoStream.Height ?? 0));
                }
            }

            _logger.LogInformation(
                "AI Upscaler: Scan complete. Found {LowRes}/{Total} items below {Width}x{Height}",
                lowResVideos.Count, totalItems, minWidth, minHeight);

            if (lowResVideos.Count == 0)
            {
                progress.Report(100);
                return;
            }

            // Phase 2: Process low-res videos through the AI upscaling pipeline
            var model = config.Model ?? "realesrgan-x4";
            var scaleFactor = config.ScaleFactor > 0 ? config.ScaleFactor : 2;
            var successCount = 0;
            var failCount = 0;

            _logger.LogInformation(
                "AI Upscaler: Starting upscaling of {Count} videos with model={Model}, scale={Scale}x",
                lowResVideos.Count, model, scaleFactor);

            for (int i = 0; i < lowResVideos.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (video, width, height) = lowResVideos[i];
                var inputPath = video.Path;
                var dir = Path.GetDirectoryName(inputPath) ?? "";
                var ext = Path.GetExtension(inputPath);
                var baseName = Path.GetFileNameWithoutExtension(inputPath);
                var outputPath = Path.Combine(dir, baseName + "_upscaled" + ext);

                // Progress: 30% scan + 70% processing
                var processingProgress = 30 + ((double)(i + 1) / lowResVideos.Count * 70);
                progress.Report(processingProgress);

                _logger.LogInformation(
                    "AI Upscaler: [{Index}/{Total}] Processing: {Name} ({Width}x{Height}) -> {Output}",
                    i + 1, lowResVideos.Count, video.Name, width, height, Path.GetFileName(outputPath));

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
                    }
                    else
                    {
                        failCount++;
                        _logger.LogWarning(
                            "AI Upscaler: Failed to upscale {Name}: {Error}",
                            video.Name, result.Error);

                        // Clean up partial output
                        if (File.Exists(outputPath))
                        {
                            try { File.Delete(outputPath); } catch { }
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

                    // Clean up partial output
                    if (File.Exists(outputPath))
                    {
                        try { File.Delete(outputPath); } catch { }
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
