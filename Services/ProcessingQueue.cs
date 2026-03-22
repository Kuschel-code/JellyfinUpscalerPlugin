using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using JellyfinUpscalerPlugin.Models;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Priority-based processing queue for upscaling jobs.
    /// Supports pause/resume, priority adjustment, and optional persistence.
    /// </summary>
    public class ProcessingQueue
    {
        private readonly ILogger<ProcessingQueue> _logger;
        private readonly SortedSet<QueuedJob> _queue;
        private readonly ConcurrentDictionary<string, QueuedJob> _jobLookup = new();
        private readonly ConcurrentDictionary<string, QueuedJob> _activeJobs = new();
        private readonly ConcurrentDictionary<string, QueuedJob> _completedJobs = new();
        private readonly object _queueLock = new();
        private readonly SemaphoreSlim _signal = new(0);
        private bool _paused;
        private int _maxQueueSize = 100;
        private string? _persistPath;

        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public ProcessingQueue(ILogger<ProcessingQueue> logger)
        {
            _logger = logger;
            _queue = new SortedSet<QueuedJob>(new JobPriorityComparer());
        }

        /// <summary>
        /// Initialize queue settings from config.
        /// </summary>
        public void Initialize(string? dataPath = null)
        {
            _maxQueueSize = Config.MaxQueueSize > 0 ? Config.MaxQueueSize : 100;

            if (Config.PersistQueueAcrossRestarts && !string.IsNullOrEmpty(dataPath))
            {
                _persistPath = Path.Combine(dataPath, "upscaler_queue.json");
                LoadPersistedQueue();
            }

            _logger.LogInformation("ProcessingQueue initialized: maxSize={Max}, persist={Persist}, paused={Paused}",
                _maxQueueSize, _persistPath != null, _paused);
        }

        /// <summary>
        /// Enqueue a new processing job.
        /// </summary>
        public bool Enqueue(string jobId, string inputPath, string outputPath,
            VideoProcessingOptions options, int priority = 5, string? itemName = null)
        {
            lock (_queueLock)
            {
                if (_queue.Count >= _maxQueueSize)
                {
                    _logger.LogWarning("Queue full ({Count}/{Max}), rejecting job {JobId}", _queue.Count, _maxQueueSize, jobId);
                    return false;
                }

                var job = new QueuedJob
                {
                    JobId = jobId,
                    InputPath = inputPath,
                    OutputPath = outputPath,
                    Options = options,
                    Priority = priority,
                    ItemName = itemName ?? Path.GetFileNameWithoutExtension(inputPath),
                    EnqueuedAt = DateTime.UtcNow,
                    Status = QueueJobStatus.Pending
                };

                _queue.Add(job);
                _jobLookup[jobId] = job;
            }

            _signal.Release();
            PersistQueue();
            _logger.LogInformation("Job {JobId} enqueued: {Name} (priority={Priority}, queue size={Size})",
                jobId, itemName, priority, QueueSize);
            return true;
        }

        /// <summary>
        /// Dequeue the highest-priority job. Blocks until a job is available or cancelled.
        /// </summary>
        public async Task<QueuedJob?> DequeueAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await _signal.WaitAsync(ct);

                if (_paused)
                {
                    // Re-signal so next WaitAsync picks it up after unpause
                    _signal.Release();
                    await Task.Delay(500, ct);
                    continue;
                }

                lock (_queueLock)
                {
                    if (_queue.Count > 0)
                    {
                        var job = _queue.Min!;
                        _queue.Remove(job);
                        job.Status = QueueJobStatus.Processing;
                        job.StartedAt = DateTime.UtcNow;
                        _activeJobs[job.JobId] = job;
                        _jobLookup.TryRemove(job.JobId, out _);
                        PersistQueue();
                        return job;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Mark a job as completed.
        /// </summary>
        public void Complete(string jobId, bool success, string? error = null)
        {
            if (_activeJobs.TryRemove(jobId, out var job))
            {
                job.Status = success ? QueueJobStatus.Completed : QueueJobStatus.Failed;
                job.CompletedAt = DateTime.UtcNow;
                job.Error = error;
                _completedJobs[jobId] = job;

                // Keep only last 100 completed jobs
                if (_completedJobs.Count > 100)
                {
                    var oldest = _completedJobs.Values.OrderBy(j => j.CompletedAt).First();
                    _completedJobs.TryRemove(oldest.JobId, out _);
                }

                PersistQueue();
                _logger.LogInformation("Job {JobId} completed: success={Success}", jobId, success);
            }
        }

        /// <summary>
        /// Cancel a pending job.
        /// </summary>
        public bool Cancel(string jobId)
        {
            lock (_queueLock)
            {
                if (_jobLookup.TryRemove(jobId, out var job))
                {
                    _queue.Remove(job);
                    job.Status = QueueJobStatus.Cancelled;
                    _completedJobs[jobId] = job;
                    PersistQueue();
                    _logger.LogInformation("Job {JobId} cancelled", jobId);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Change priority of a pending job.
        /// </summary>
        public bool SetPriority(string jobId, int newPriority)
        {
            lock (_queueLock)
            {
                if (_jobLookup.TryGetValue(jobId, out var job))
                {
                    _queue.Remove(job);
                    job.Priority = Math.Clamp(newPriority, 1, 10);
                    _queue.Add(job);
                    PersistQueue();
                    _logger.LogDebug("Job {JobId} priority changed to {Priority}", jobId, newPriority);
                    return true;
                }
            }
            return false;
        }

        public void Pause()
        {
            _paused = true;
            _logger.LogInformation("Processing queue paused");
        }

        public void Resume()
        {
            _paused = false;
            _logger.LogInformation("Processing queue resumed");
            // Wake up any waiting dequeue calls
            lock (_queueLock)
            {
                for (int i = 0; i < _queue.Count; i++)
                    _signal.Release();
            }
        }

        public bool IsPaused => _paused;
        public int QueueSize { get { lock (_queueLock) { return _queue.Count; } } }
        public int ActiveCount => _activeJobs.Count;

        /// <summary>
        /// Get current queue status for API response.
        /// </summary>
        public object GetStatus()
        {
            List<object> pendingList;
            lock (_queueLock)
            {
                pendingList = _queue.Select(j => new
                {
                    j.JobId, j.ItemName, j.Priority, j.EnqueuedAt, status = j.Status.ToString()
                }).Cast<object>().ToList();
            }

            return new
            {
                paused = _paused,
                pending_count = QueueSize,
                active_count = ActiveCount,
                completed_count = _completedJobs.Count,
                max_queue_size = _maxQueueSize,
                pending = pendingList,
                active = _activeJobs.Values.Select(j => new
                {
                    j.JobId, j.ItemName, j.Priority, j.StartedAt,
                    duration_seconds = j.StartedAt.HasValue ? (DateTime.UtcNow - j.StartedAt.Value).TotalSeconds : 0
                }),
                recent_completed = _completedJobs.Values
                    .OrderByDescending(j => j.CompletedAt)
                    .Take(20)
                    .Select(j => new
                    {
                        j.JobId, j.ItemName, status = j.Status.ToString(),
                        j.CompletedAt, j.Error
                    })
            };
        }

        private void PersistQueue()
        {
            if (_persistPath == null) return;
            try
            {
                List<QueuedJob> snapshot;
                lock (_queueLock) { snapshot = _queue.ToList(); }

                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_persistPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to persist queue (non-critical)");
            }
        }

        private void LoadPersistedQueue()
        {
            if (_persistPath == null || !File.Exists(_persistPath)) return;
            try
            {
                var json = File.ReadAllText(_persistPath);
                var jobs = JsonSerializer.Deserialize<List<QueuedJob>>(json);
                if (jobs != null)
                {
                    lock (_queueLock)
                    {
                        foreach (var job in jobs.Where(j => j.Status == QueueJobStatus.Pending))
                        {
                            _queue.Add(job);
                            _jobLookup[job.JobId] = job;
                        }
                    }
                    // Signal for each restored job
                    for (int i = 0; i < jobs.Count(j => j.Status == QueueJobStatus.Pending); i++)
                        _signal.Release();

                    _logger.LogInformation("Restored {Count} queued jobs from disk", _queue.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load persisted queue, starting fresh");
            }
        }
    }

    /// <summary>
    /// A job in the processing queue.
    /// </summary>
    public class QueuedJob
    {
        public string JobId { get; set; } = "";
        public string InputPath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public VideoProcessingOptions Options { get; set; } = new();
        public int Priority { get; set; } = 5; // 1 = highest, 10 = lowest
        public string ItemName { get; set; } = "";
        public DateTime EnqueuedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public QueueJobStatus Status { get; set; }
        public string? Error { get; set; }
    }

    public enum QueueJobStatus
    {
        Pending,
        Processing,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Compares jobs by priority (lower number = higher priority), then by enqueue time.
    /// </summary>
    internal class JobPriorityComparer : IComparer<QueuedJob>
    {
        public int Compare(QueuedJob? x, QueuedJob? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int priorityCompare = x.Priority.CompareTo(y.Priority);
            if (priorityCompare != 0) return priorityCompare;

            int timeCompare = x.EnqueuedAt.CompareTo(y.EnqueuedAt);
            if (timeCompare != 0) return timeCompare;

            return string.Compare(x.JobId, y.JobId, StringComparison.Ordinal);
        }
    }
}
