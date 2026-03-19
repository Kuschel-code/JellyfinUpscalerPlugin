using System;
using System.Collections.Generic;
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

namespace JellyfinUpscalerPlugin.ScheduledTasks
{
    /// <summary>
    /// Scheduled task that scans the library for low-resolution media
    /// and queues items for AI upscaling via the Docker service.
    /// Appears in Jellyfin Dashboard → Scheduled Tasks → AI Upscaler category.
    /// </summary>
    public class LibraryUpscaleScanTask : IScheduledTask
    {
        private readonly ILogger<LibraryUpscaleScanTask> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly HttpUpscalerService _httpUpscalerService;

        public LibraryUpscaleScanTask(
            ILogger<LibraryUpscaleScanTask> logger,
            ILibraryManager libraryManager,
            HttpUpscalerService httpUpscalerService)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _httpUpscalerService = httpUpscalerService;
        }

        public string Name => "Scan Library for Upscaling";
        public string Key => "AIUpscalerLibraryScan";
        public string Category => "AI Upscaler";
        public string Description =>
            "Scans media library for content below the configured resolution threshold " +
            "and queues matching items for AI upscaling. Requires a running Docker AI service.";

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
                _logger.LogWarning("AI Upscaler: Docker AI service not reachable, aborting scan");
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
            var lowResItems = 0;
            var processed = 0;

            _logger.LogInformation("AI Upscaler: Found {Total} video items to analyze", totalItems);

            // Resolution threshold from config (default: 1080p)
            var minWidth = config.MinResolutionWidth > 0 ? config.MinResolutionWidth : 1920;
            var minHeight = config.MinResolutionHeight > 0 ? config.MinResolutionHeight : 1080;

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                processed++;
                progress.Report((double)processed / totalItems * 100);

                if (item is not Video video)
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
                    lowResItems++;
                    _logger.LogDebug(
                        "AI Upscaler: Low-res item found: {Name} ({Width}x{Height})",
                        video.Name, videoStream.Width, videoStream.Height);
                }
            }

            _logger.LogInformation(
                "AI Upscaler: Library scan complete. Found {LowRes}/{Total} items below {Width}x{Height}",
                lowResItems, totalItems, minWidth, minHeight);

            progress.Report(100);
        }
    }
}
