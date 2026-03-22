using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using JellyfinUpscalerPlugin.Services;
using JellyfinUpscalerPlugin.Models;

namespace JellyfinUpscalerPlugin.ScheduledTasks
{
    /// <summary>
    /// Scheduled task that scans the library for low-resolution images
    /// (posters, backdrops, thumbnails, logos) and upscales them using AI.
    /// Runs separately from video upscaling to keep tasks focused.
    /// </summary>
    public class ImageUpscaleScanTask : IScheduledTask
    {
        private readonly ILogger<ImageUpscaleScanTask> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly HttpUpscalerService _httpUpscalerService;
        private readonly UpscalerCore _upscalerCore;

        /// <summary>
        /// Image types to process, in priority order.
        /// </summary>
        private static readonly ImageType[] TargetImageTypes = new[]
        {
            ImageType.Primary,    // Poster/cover
            ImageType.Backdrop,   // Background/fanart
            ImageType.Thumb,      // Thumbnail
            ImageType.Logo,       // Logo overlay
            ImageType.Banner      // Banner image
        };

        /// <summary>
        /// Minimum dimensions below which an image is considered low-res and worth upscaling.
        /// </summary>
        private const int MinImageWidth = 600;
        private const int MinImageHeight = 900;
        private const int MinBackdropWidth = 1280;
        private const int MinBackdropHeight = 720;

        public ImageUpscaleScanTask(
            ILogger<ImageUpscaleScanTask> logger,
            ILibraryManager libraryManager,
            HttpUpscalerService httpUpscalerService,
            UpscalerCore upscalerCore)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _httpUpscalerService = httpUpscalerService;
            _upscalerCore = upscalerCore;
        }

        public string Name => "Scan & Upscale Library Images";
        public string Key => "AIUpscalerImageScan";
        public string Category => "AI Upscaler";
        public string Description =>
            "Scans media library for low-resolution posters, backdrops, thumbnails, and logos, " +
            "then upscales them using the Docker AI service. " +
            "Upscaled images are saved alongside originals with '_upscaled' suffix.";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.WeeklyTrigger,
                    DayOfWeek = DayOfWeek.Sunday,
                    TimeOfDayTicks = TimeSpan.FromHours(4).Ticks // Sunday 4 AM
                }
            };
        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.EnablePlugin)
            {
                _logger.LogInformation("AI Upscaler Images: Plugin disabled, skipping scan");
                return;
            }

            _logger.LogInformation("AI Upscaler Images: Starting library scan for low-resolution images...");

            // Check Docker AI service connectivity
            var serviceAvailable = await _httpUpscalerService.IsServiceAvailableAsync(cancellationToken);
            if (!serviceAvailable)
            {
                _logger.LogWarning("AI Upscaler Images: Docker AI service not reachable, aborting scan");
                return;
            }

            // Get all items that could have images (movies, series, episodes, etc.)
            var query = new InternalItemsQuery
            {
                IsVirtualItem = false,
                Recursive = true
            };

            var items = _libraryManager.GetItemList(query);
            _logger.LogInformation("AI Upscaler Images: Found {Total} library items to check", items.Count);

            // Phase 1: Scan for low-res images
            var imagesToUpscale = new List<(BaseItem item, ImageType imageType, int index, string path, int width, int height)>();
            int scannedItems = 0;
            int alreadyUpscaledCount = 0;
            int highResCount = 0;

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scannedItems++;
                progress.Report((double)scannedItems / items.Count * 20); // 20% for scanning

                foreach (var imageType in TargetImageTypes)
                {
                    var images = item.GetImages(imageType).ToList();
                    for (int idx = 0; idx < images.Count; idx++)
                    {
                        var imagePath = images[idx].Path;
                        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                            continue;

                        // Skip if already upscaled
                        var fileName = Path.GetFileNameWithoutExtension(imagePath);
                        if (fileName.EndsWith("_upscaled", StringComparison.OrdinalIgnoreCase))
                        {
                            alreadyUpscaledCount++;
                            continue;
                        }

                        // Skip if upscaled version exists
                        var dir = Path.GetDirectoryName(imagePath) ?? "";
                        var ext = Path.GetExtension(imagePath);
                        var upscaledPath = Path.Combine(dir, fileName + "_upscaled" + ext);
                        if (File.Exists(upscaledPath))
                        {
                            alreadyUpscaledCount++;
                            continue;
                        }

                        // Check image dimensions
                        try
                        {
                            var imageInfo = await SixLabors.ImageSharp.Image.IdentifyAsync(imagePath, cancellationToken);
                            if (imageInfo == null) continue;

                            int imgWidth = imageInfo.Width;
                            int imgHeight = imageInfo.Height;

                            // Use different thresholds for backdrops vs posters
                            bool isLowRes;
                            if (imageType == ImageType.Backdrop || imageType == ImageType.Banner)
                            {
                                isLowRes = imgWidth < MinBackdropWidth || imgHeight < MinBackdropHeight;
                            }
                            else
                            {
                                isLowRes = imgWidth < MinImageWidth || imgHeight < MinImageHeight;
                            }

                            if (isLowRes)
                            {
                                imagesToUpscale.Add((item, imageType, idx, imagePath, imgWidth, imgHeight));
                            }
                            else
                            {
                                highResCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Could not identify image {Path}", imagePath);
                        }
                    }
                }
            }

            _logger.LogInformation(
                "AI Upscaler Images: Scan complete. {LowRes} low-res images found, " +
                "{HighRes} already high-res, {AlreadyUpscaled} already upscaled",
                imagesToUpscale.Count, highResCount, alreadyUpscaledCount);

            if (imagesToUpscale.Count == 0)
            {
                progress.Report(100);
                return;
            }

            // Apply limit
            var maxItems = config.MaxItemsPerScan;
            if (maxItems > 0 && imagesToUpscale.Count > maxItems)
            {
                _logger.LogInformation("Limiting to {Max} images (of {Total} found)", maxItems, imagesToUpscale.Count);
                imagesToUpscale = imagesToUpscale.Take(maxItems).ToList();
            }

            // Phase 2: Upscale images
            int successCount = 0, failCount = 0;

            for (int i = 0; i < imagesToUpscale.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (item, imageType, idx, imagePath, width, height) = imagesToUpscale[i];

                // Progress: 20% scan + 80% processing
                var processingProgress = 20 + ((double)(i + 1) / imagesToUpscale.Count * 80);
                progress.Report(processingProgress);

                _logger.LogInformation(
                    "AI Upscaler Images: [{Index}/{Total}] {Type}: {Name} ({Width}x{Height})",
                    i + 1, imagesToUpscale.Count, imageType, item.Name, width, height);

                try
                {
                    var originalData = await File.ReadAllBytesAsync(imagePath, cancellationToken);

                    // Pick appropriate scale: 2x for slightly low-res, 4x for very low-res
                    int scale = (width < 300 || height < 400) ? 4 : 2;

                    var upscaledData = await _upscalerCore.UpscaleImageAsync(originalData, "auto", scale, cancellationToken);

                    if (upscaledData != null && upscaledData.Length > 0)
                    {
                        var dir = Path.GetDirectoryName(imagePath) ?? "";
                        var ext = Path.GetExtension(imagePath);
                        var baseName = Path.GetFileNameWithoutExtension(imagePath);
                        var outputPath = Path.Combine(dir, baseName + "_upscaled" + ext);
                        await File.WriteAllBytesAsync(outputPath, upscaledData, cancellationToken);

                        successCount++;
                        _logger.LogInformation("AI Upscaler Images: Upscaled {Type} for {Name}: {Input} -> {Output}",
                            imageType, item.Name, $"{width}x{height}", outputPath);
                    }
                    else
                    {
                        failCount++;
                        _logger.LogWarning("AI Upscaler Images: Empty result for {Type} of {Name}", imageType, item.Name);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("AI Upscaler Images: Task cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    failCount++;
                    _logger.LogError(ex, "AI Upscaler Images: Error upscaling {Type} for {Name}", imageType, item.Name);
                }
            }

            _logger.LogInformation(
                "AI Upscaler Images: Complete. Success: {Success}, Failed: {Failed}, Total: {Total}",
                successCount, failCount, imagesToUpscale.Count);

            progress.Report(100);
        }
    }
}
