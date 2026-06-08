using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Session;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Progress notification service using Jellyfin SessionManager
    /// Broadcasts progress to Jellyfin Dashboard via SessionManager
    /// </summary>
    public class UpscalerProgressHub
    {
        private readonly ILogger<UpscalerProgressHub> _logger;
        private readonly ISessionManager _sessionManager;

        public UpscalerProgressHub(
            ILogger<UpscalerProgressHub> logger,
            ISessionManager sessionManager)
        {
            _logger = logger;
            _sessionManager = sessionManager;
        }

        // v1.7.10 - cache the REAL frame-based progress per job at the single SendFrameProgress
        // chokepoint (both batch loops go through it), so the polled status
        // (ProcessingStrategySelector.CalculateJobProgress) can use it instead of a time estimate
        // that sticks at 95% on slow hardware (#70/#72).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> _latestFrameProgress = new();

        /// <summary>Latest real (frame-based) progress 0-100 for a job, or null if none reported yet.</summary>
        public static double? GetFrameProgress(string jobId)
            => _latestFrameProgress.TryGetValue(jobId, out var p) ? p : (double?)null;

        /// <summary>Idempotently drop a job's cached frame progress. Call on every terminal path
        /// (completed/cancelled/failed) so the static cache can't leak over server uptime.</summary>
        public static void ClearFrameProgress(string jobId) => _latestFrameProgress.TryRemove(jobId, out _);

        /// <summary>
        /// Send progress update to all connected clients
        /// </summary>
        public async Task SendProgressUpdate(UpscalerProgressMessage message)
        {
            try
            {
                var messageData = new
                {
                    MessageType = "UpscalerProgress",
                    Data = new
                    {
                        JobId = message.JobId,
                        FileName = message.FileName,
                        Progress = message.Progress,
                        CurrentFrame = message.CurrentFrame,
                        TotalFrames = message.TotalFrames,
                        Fps = message.Fps,
                        Status = message.Status,
                        EstimatedTimeRemaining = message.EstimatedTimeRemaining,
                        Timestamp = DateTime.UtcNow
                    }
                };

                // Broadcast to all active admin sessions using SessionMessageType.UserDataChanged
                await _sessionManager.SendMessageToAdminSessions(
                    MediaBrowser.Model.Session.SessionMessageType.UserDataChanged,
                    messageData,
                    System.Threading.CancellationToken.None
                );

                _logger.LogDebug("Progress update sent: {FileName} - {Progress:F1}%", message.FileName, message.Progress);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send progress update");
            }
        }

        /// <summary>
        /// Send job started notification
        /// </summary>
        public async Task SendJobStarted(string jobId, string fileName, int totalFrames)
        {
            await SendProgressUpdate(new UpscalerProgressMessage
            {
                JobId = jobId,
                FileName = fileName,
                Progress = 0,
                CurrentFrame = 0,
                TotalFrames = totalFrames,
                Status = "Starting",
                Fps = 0
            });
        }

        /// <summary>
        /// Send job completed notification
        /// </summary>
        public async Task SendJobCompleted(string jobId, string fileName, bool success, string? error = null)
        {
            ClearFrameProgress(jobId);
            await SendProgressUpdate(new UpscalerProgressMessage
            {
                JobId = jobId,
                FileName = fileName,
                Progress = 100,
                Status = success ? "Completed" : "Failed",
                Error = error
            });
        }

        /// <summary>
        /// Send frame processing update
        /// </summary>
        public async Task SendFrameProgress(string jobId, string fileName, int currentFrame, int totalFrames, double fps)
        {
            // v1.7.11 - when the total frame count is unknown (totalFrames <= 0, e.g. the
            // pipe-encode path before v1.7.11 passed -1) cache a -1 SENTINEL, not 0. A cached 0
            // was indistinguishable from "genuinely 0% done", so CalculateJobProgress's `> 0`
            // check fell back to the 95%-capped time estimate even while frames were flowing
            // (Gap 2). The SignalR message still reports 0 (not -1) so the UI never shows a
            // negative bar.
            var hasTotal = totalFrames > 0;
            var progress = hasTotal ? (currentFrame * 100.0 / totalFrames) : 0;
            _latestFrameProgress[jobId] = hasTotal ? progress : -1;
            var framesRemaining = totalFrames - currentFrame;
            var secondsRemaining = fps > 0 ? framesRemaining / fps : 0;

            await SendProgressUpdate(new UpscalerProgressMessage
            {
                JobId = jobId,
                FileName = fileName,
                Progress = progress,
                CurrentFrame = currentFrame,
                TotalFrames = totalFrames,
                Fps = fps,
                Status = "Processing",
                EstimatedTimeRemaining = TimeSpan.FromSeconds(secondsRemaining)
            });
        }
    }

    /// <summary>
    /// Progress message data structure
    /// </summary>
    public class UpscalerProgressMessage
    {
        public string JobId { get; set; } = "";
        public string FileName { get; set; } = "";
        public double Progress { get; set; }
        public int CurrentFrame { get; set; }
        public int TotalFrames { get; set; }
        public double Fps { get; set; }
        public string Status { get; set; } = "";
        public TimeSpan? EstimatedTimeRemaining { get; set; }
        public string? Error { get; set; }
    }
}
