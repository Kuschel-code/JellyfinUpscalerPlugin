using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using JellyfinUpscalerPlugin.Services;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Tasks
{
    /// <summary>
    /// Scheduled task for automated library upscaling - v1.4.1 NEW
    /// </summary>
    public class UpscaleLibraryTask : IScheduledTask
    {
        private readonly ILogger<UpscaleLibraryTask> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly VideoProcessor _videoProcessor;
        private readonly CacheManager _cacheManager;

        public UpscaleLibraryTask(
            ILogger<UpscaleLibraryTask> logger,
            ILibraryManager libraryManager,
            VideoProcessor videoProcessor,
            CacheManager cacheManager)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _videoProcessor = videoProcessor;
            _cacheManager = cacheManager;
        }

        public string Name => "Automated AI Upscaling";
        public string Key => "UpscaleLibraryTask";
        public string Description => "Automatically upscales library content based on AI models and performance profiles.";
        public string Category => "AI Upscaler";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Default: Run daily at 3 AM
            return new[]
            {
                new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerDaily, TimeOfDayTicks = TimeSpan.FromHours(3).Ticks }
            };
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.EnablePlugin || !config.EnableScheduledUpscaling)
            {
                _logger.LogInformation("AI Upscaler: Task skipped (plugin or scheduled upscaling disabled)");
                return;
            }

            _logger.LogInformation("🚀 AI Upscaler: Starting automated library scan");

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Movie", "Episode" },
                Recursive = true,
                IsFolder = false,
                DtoOptions = new MediaBrowser.Model.Dto.DtoOptions(true)
            };

            var items = _libraryManager.GetItemList(query)
                .Where(i => !string.IsNullOrEmpty(i.Path) && i.LocationType == LocationType.FileSystem)
                .ToList();

            int total = items.Count;
            int current = 0;
            int upscaledCount = 0;

            _logger.LogInformation($"🔍 AI Upscaler: Found {total} potential items for upscaling");

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (upscaledCount >= config.MaxItemsPerTask)
                {
                    _logger.LogInformation($"✋ AI Upscaler: Reached maximum items per task ({config.MaxItemsPerTask})");
                    break;
                }

                current++;
                progress.Report((double)current / total * 100);

                // Skip if already upscaled (check tags)
                if (item.Tags.Contains("AI-Upscaled"))
                {
                    continue;
                }

                // Check resolution - only upscale content below threshold
                var videoStream = item.GetMediaSources(false).FirstOrDefault()?.VideoStream;
                if (videoStream == null) continue;

                bool shouldUpscale = videoStream.Width > 0 && videoStream.Width < config.UpscaleResolutionThreshold;

                if (shouldUpscale)
                {
                    _logger.LogInformation($"✨ AI Upscaler: Automatically upscaling {item.Name} ({videoStream.Width}p -> {videoStream.Width * config.ScaleFactor}p)");

                    try
                    {
                        var options = new VideoProcessingOptions
                        {
                            Model = config.Model,
                            ScaleFactor = config.ScaleFactor,
                            QualityLevel = config.QualityLevel,
                            HardwareAcceleration = config.HardwareAcceleration ? "auto" : "none"
                        };

                        var outputPath = Path.Combine(
                            Path.GetDirectoryName(item.Path) ?? "",
                            Path.GetFileNameWithoutExtension(item.Path) + "_upscaled" + Path.GetExtension(item.Path)
                        );

                        var result = await _videoProcessor.ProcessVideoAsync(item.Path, outputPath, options, cancellationToken);

                        if (result.Success)
                        {
                            _logger.LogInformation($"✅ AI Upscaler: Successfully upscaled {item.Name}");
                            
                            // Add tag to original item to mark as processed
                            var tags = item.Tags.ToList();
                            tags.Add("AI-Upscaled");
                            item.Tags = tags.ToArray();
                            
                            _libraryManager.UpdateItem(item, item, ItemUpdateType.MetadataEdit, CancellationToken.None);
                            upscaledCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"❌ AI Upscaler: Failed to upscale {item.Name}");
                    }
                }
            }

            _logger.LogInformation($"🏁 AI Upscaler: Task completed. Upscaled {upscaledCount} items.");
        }
    }
}
