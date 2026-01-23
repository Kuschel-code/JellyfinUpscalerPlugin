using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Jellyfin.Data.Enums;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// AI Upscaler Background Service
    /// </summary>
    public class UpscalerService : IHostedService
    {
        private readonly ILogger<UpscalerService> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly ISessionManager _sessionManager;
        private Timer? _timer;

        /// <summary>
        /// Initializes a new instance of the UpscalerService class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="libraryManager">Library manager instance.</param>
        /// <param name="sessionManager">Session manager instance.</param>
        public UpscalerService(
            ILogger<UpscalerService> logger,
            ILibraryManager libraryManager,
            ISessionManager sessionManager)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _sessionManager = sessionManager;
        }

        /// <summary>
        /// Start the service
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AI Upscaler Service: Starting background service");

            // Start timer for periodic tasks (every 30 seconds)
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));

            return Task.CompletedTask;
        }

        /// <summary>
        /// Stop the service
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AI Upscaler Service: Stopping background service");

            _timer?.Change(Timeout.Infinite, 0);
            _timer?.Dispose();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Background work method
        /// </summary>
        /// <param name="state">Timer state</param>
        private void DoWork(object? state)
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config == null || !config.EnablePlugin)
                {
                    return;
                }

                // Monitor active sessions for status reporting
                var sessions = _sessionManager.Sessions;
                var activeVideoSessions = 0;

                foreach (var session in sessions)
                {
                    if (session.PlayState?.PlayMethod != null && 
                        session.NowPlayingItem != null &&
                        (session.NowPlayingItem.Type == BaseItemKind.Video || session.NowPlayingItem.Type == BaseItemKind.Movie || session.NowPlayingItem.Type == BaseItemKind.Episode))
                    {
                        activeVideoSessions++;
                    }
                }

                if (activeVideoSessions > 0)
                {
                    _logger.LogDebug("AI Upscaler: {Count} active video sessions being monitored. Model: {Model}", 
                        activeVideoSessions, config.Model);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Upscaler Service: Error in background work");
            }
        }

        /// <summary>
        /// Dispose the service
        /// </summary>
        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}