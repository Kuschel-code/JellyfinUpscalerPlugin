using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using JellyfinUpscalerPlugin.Models;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Manages active processing jobs: listing, pausing, resuming, cancelling, and tracking performance history.
    /// </summary>
    public class VideoJobManager
    {
        private readonly ILogger _logger;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ProcessingJob> _activeJobs;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _jobCancellationTokens;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _pausedJobs;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, VideoProcessingMetrics> _performanceHistory;
        private readonly ProcessingStrategySelector _strategySelector;

        public VideoJobManager(
            ILogger logger,
            System.Collections.Concurrent.ConcurrentDictionary<string, ProcessingJob> activeJobs,
            System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> jobCancellationTokens,
            System.Collections.Concurrent.ConcurrentDictionary<string, bool> pausedJobs,
            System.Collections.Concurrent.ConcurrentDictionary<string, VideoProcessingMetrics> performanceHistory,
            ProcessingStrategySelector strategySelector)
        {
            _logger = logger;
            _activeJobs = activeJobs;
            _jobCancellationTokens = jobCancellationTokens;
            _pausedJobs = pausedJobs;
            _performanceHistory = performanceHistory;
            _strategySelector = strategySelector;
        }

        /// <summary>
        /// Get all active processing jobs
        /// </summary>
        public List<object> GetActiveJobs()
        {
            return _activeJobs.Values.Select(job => new
            {
                jobId = job.Id,
                inputPath = Path.GetFileName(job.InputPath),
                outputPath = Path.GetFileName(job.OutputPath),
                status = job.Status.ToString(),
                progress = _strategySelector.CalculateJobProgress(job),
                startTime = job.StartTime,
                duration = job.ProcessingDuration.TotalSeconds,
                method = job.ProcessingMethod.ToString(),
                isPaused = _pausedJobs.GetValueOrDefault(job.Id, false)
            }).Cast<object>().ToList();
        }

        /// <summary>
        /// Pause a running job
        /// </summary>
        public bool PauseJob(string jobId)
        {
            if (!_activeJobs.ContainsKey(jobId))
            {
                return false;
            }

            _pausedJobs[jobId] = true;
            _logger.LogInformation("Job {JobId} paused", jobId);
            return true;
        }

        /// <summary>
        /// Resume a paused job
        /// </summary>
        public bool ResumeJob(string jobId)
        {
            if (!_activeJobs.ContainsKey(jobId))
            {
                return false;
            }

            _pausedJobs[jobId] = false;
            _logger.LogInformation("Job {JobId} resumed", jobId);
            return true;
        }

        /// <summary>
        /// Cancel a running job
        /// </summary>
        public bool CancelJob(string jobId)
        {
            if (!_jobCancellationTokens.TryGetValue(jobId, out var cts))
            {
                return false;
            }

            cts.Cancel();
            _logger.LogInformation("Job {JobId} cancelled", jobId);
            return true;
        }

        /// <summary>
        /// Update performance history after a job completes
        /// </summary>
        public void UpdatePerformanceHistory(ProcessingJob job)
        {
            var inputW = job.InputInfo?.Width ?? 0;
            var inputH = job.InputInfo?.Height ?? 0;
            var scale = job.OptimizedOptions?.ScaleFactor ?? 1;

            var metrics = new VideoProcessingMetrics
            {
                JobId = job.Id,
                ProcessingTime = job.ProcessingDuration,
                InputResolution = $"{inputW}x{inputH}",
                OutputResolution = $"{inputW * scale}x{inputH * scale}",
                Model = job.OptimizedOptions?.Model ?? "unknown",
                Scale = job.OptimizedOptions?.ScaleFactor ?? 1,
                Method = job.ProcessingMethod,
                Success = job.Result?.Success ?? false,
                Timestamp = DateTime.UtcNow
            };

            _performanceHistory[job.Id] = metrics;

            // Keep only last 100 entries
            if (_performanceHistory.Count > 100)
            {
                var oldestKey = _performanceHistory.Keys.OrderBy(k => _performanceHistory[k].Timestamp).First();
                _performanceHistory.TryRemove(oldestKey, out _);
            }
        }

        /// <summary>
        /// Update statistics (for timer callback)
        /// </summary>
        public void UpdateStatistics(object? state)
        {
            try
            {
                var activeJobs = _activeJobs.Count;
                var completedJobs = _performanceHistory.Count(m => m.Value.Success);
                var failedJobs = _performanceHistory.Count(m => !m.Value.Success);

                _logger.LogDebug("Stats: {ActiveJobs} active, {CompletedJobs} completed, {FailedJobs} failed",
                    activeJobs, completedJobs, failedJobs);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Statistics update failed");
            }
        }
    }
}
