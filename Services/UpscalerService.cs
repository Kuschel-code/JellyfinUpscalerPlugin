using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Jellyfin.Data.Enums;
using JellyfinUpscalerPlugin.Models;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// AI Upscaler Background Service — monitors sessions and processes queued jobs.
    /// </summary>
    public class UpscalerService : IHostedService, IDisposable
    {
        private readonly ILogger<UpscalerService> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly ISessionManager _sessionManager;
        private readonly ProcessingQueue _queue;
        private readonly VideoProcessor _videoProcessor;
        private readonly HttpUpscalerService _httpUpscaler;
        private Timer? _monitorTimer;
        private CancellationTokenSource? _queueCts;
        private Task? _queueWorkerTask;

        public UpscalerService(
            ILogger<UpscalerService> logger,
            ILibraryManager libraryManager,
            ISessionManager sessionManager,
            ProcessingQueue queue,
            VideoProcessor videoProcessor,
            HttpUpscalerService httpUpscaler)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _sessionManager = sessionManager;
            _queue = queue;
            _videoProcessor = videoProcessor;
            _httpUpscaler = httpUpscaler;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var version = typeof(Plugin).Assembly.GetName().Version?.ToString(4) ?? "unknown";
            _logger.LogInformation("AI Upscaler Service v{Version}: Starting background service", version);

            // Initialize queue with plugin data path
            var dataPath = Plugin.Instance?.DataFolderPath;
            _queue.Initialize(dataPath);

            // Start session monitor timer (every 30s)
            _monitorTimer = new Timer(MonitorSessions, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));

            // Start queue worker loop
            _queueCts = new CancellationTokenSource();
            _queueWorkerTask = Task.Run(() => QueueWorkerLoopAsync(_queueCts.Token), _queueCts.Token);

            _logger.LogInformation("AI Upscaler Service: Queue worker started (queue size: {Size})", _queue.QueueSize);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AI Upscaler Service: Stopping background service");

            _monitorTimer?.Change(Timeout.Infinite, 0);

            // Cancel queue worker
            if (_queueCts != null)
            {
                _queueCts.Cancel();
                try
                {
                    if (_queueWorkerTask != null)
                    {
                        await Task.WhenAny(_queueWorkerTask, Task.Delay(5000, CancellationToken.None));
                    }
                }
                catch (OperationCanceledException) { }
            }
        }

        /// <summary>
        /// Queue worker loop — dequeues jobs and processes them via VideoProcessor.
        /// </summary>
        private async Task QueueWorkerLoopAsync(CancellationToken ct)
        {
            _logger.LogInformation("Queue worker loop started");

            while (!ct.IsCancellationRequested)
            {
                QueuedJob? job = null;
                try
                {
                    // Block until a job is available
                    job = await _queue.DequeueAsync(ct);
                    if (job == null || ct.IsCancellationRequested) break;

                    var config = Plugin.Instance?.Configuration;
                    if (config == null || !config.EnablePlugin)
                    {
                        _queue.Complete(job.JobId, false, "Plugin disabled");
                        continue;
                    }

                    // Pause during playback if configured
                    if (config.PauseQueueDuringPlayback)
                    {
                        while (HasActiveVideoSessions() && !ct.IsCancellationRequested)
                        {
                            _logger.LogDebug("Queue paused during playback, waiting...");
                            await Task.Delay(10000, ct);
                        }
                    }

                    // Check Docker service is available
                    if (!await _httpUpscaler.IsServiceAvailableAsync())
                    {
                        _queue.Complete(job.JobId, false, "AI service unavailable");
                        _logger.LogWarning("Queue job {JobId} failed: AI service unavailable", job.JobId);
                        continue;
                    }

                    _logger.LogInformation("Processing queued job {JobId}: {Name} (model={Model}, scale={Scale}x)",
                        job.JobId, job.ItemName, job.Options?.Model ?? "auto", job.Options?.ScaleFactor ?? 2);

                    // Process the video
                    var result = await _videoProcessor.ProcessVideoAsync(
                        job.InputPath,
                        job.OutputPath,
                        job.Options ?? new VideoProcessingOptions(),
                        ct);

                    _queue.Complete(job.JobId, result.Success, result.Success ? null : result.Error);

                    if (result.Success)
                    {
                        _logger.LogInformation("Queue job {JobId} completed successfully: {Output}", job.JobId, job.OutputPath);
                    }
                    else
                    {
                        _logger.LogWarning("Queue job {JobId} failed: {Error}", job.JobId, result.Error);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Queue worker error processing job {JobId}", job?.JobId ?? "unknown");
                    if (job != null)
                    {
                        _queue.Complete(job.JobId, false, ex.Message);
                    }
                    // Brief delay before retrying to avoid tight error loops
                    await Task.Delay(2000, ct);
                }
            }

            _logger.LogInformation("Queue worker loop stopped");
        }

        /// <summary>
        /// Monitor active video sessions for status reporting.
        /// </summary>
        private void MonitorSessions(object? state)
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config == null || !config.EnablePlugin) return;

                var count = 0;
                foreach (var session in _sessionManager.Sessions)
                {
                    if (session.PlayState?.PlayMethod != null &&
                        session.NowPlayingItem != null &&
                        (session.NowPlayingItem.Type == BaseItemKind.Video ||
                         session.NowPlayingItem.Type == BaseItemKind.Movie ||
                         session.NowPlayingItem.Type == BaseItemKind.Episode))
                    {
                        count++;
                    }
                }

                if (count > 0)
                {
                    _logger.LogDebug("AI Upscaler: {Count} active video sessions, queue size: {Queue}",
                        count, _queue.QueueSize);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Upscaler Service: Error in session monitor");
            }
        }

        private bool HasActiveVideoSessions()
        {
            foreach (var session in _sessionManager.Sessions)
            {
                if (session.PlayState?.PlayMethod != null &&
                    session.NowPlayingItem != null &&
                    (session.NowPlayingItem.Type == BaseItemKind.Video ||
                     session.NowPlayingItem.Type == BaseItemKind.Movie ||
                     session.NowPlayingItem.Type == BaseItemKind.Episode))
                {
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            _monitorTimer?.Dispose();
            _queueCts?.Cancel();
            _queueCts?.Dispose();
        }
    }
}
