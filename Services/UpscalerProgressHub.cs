using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Session;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// SignalR-compatible progress hub for real-time upscaling status updates
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

                // Broadcast to all active sessions using GeneralCommand
                await _sessionManager.SendMessageToAdminSessions(
                    MediaBrowser.Model.Session.SessionMessageType.UserDataChanged,
                    messageData,
                    System.Threading.CancellationToken.None
                );

                _logger.LogDebug($"📊 Progress update sent: {message.FileName} - {message.Progress:F1}%");
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
            var progress = totalFrames > 0 ? (currentFrame * 100.0 / totalFrames) : 0;
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
