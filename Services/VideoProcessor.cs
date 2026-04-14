using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.MediaEncoding;
using FFMpegCore;
using JellyfinUpscalerPlugin.Models;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Video processing orchestrator — delegates analysis, strategy selection, frame processing,
    /// method execution, and job management to focused sub-services.
    /// </summary>
    public class VideoProcessor : IDisposable
    {
        private readonly ILogger<VideoProcessor> _logger;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly UpscalerCore _upscalerCore;
        private readonly HttpUpscalerService _httpUpscalerService;
        private readonly LibraryScanHelper _libraryScanHelper;
        private readonly UpscalerProgressHub _progressHub;

        // Sub-services
        private readonly VideoAnalyzer _videoAnalyzer;
        private readonly ProcessingStrategySelector _strategySelector;
        private readonly VideoFrameProcessor _frameProcessor;
        private readonly ProcessingMethodExecutor _methodExecutor;
        private readonly VideoJobManager _jobManager;

        // Shared state dictionaries
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ProcessingJob> _activeJobs = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _jobCancellationTokens = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _pausedJobs = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, VideoProcessingMetrics> _performanceHistory = new();

        // Processing queue for concurrent streams
        private readonly SemaphoreSlim _processingSemaphore;
        private readonly Timer? _statisticsTimer;

        private string _ffmpegPath = string.Empty;
        private string _ffprobePath = string.Empty;

        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        private static readonly TimeSpan SemaphoreTimeout = TimeSpan.FromMinutes(30);
        private const int StatisticsIntervalSeconds = 10;

        public VideoProcessor(
            ILogger<VideoProcessor> logger,
            IMediaEncoder mediaEncoder,
            UpscalerCore upscalerCore,
            HttpUpscalerService httpUpscalerService,
            UpscalerProgressHub progressHub,
            LibraryScanHelper libraryScanHelper,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _mediaEncoder = mediaEncoder;
            _upscalerCore = upscalerCore;
            _httpUpscalerService = httpUpscalerService;
            _libraryScanHelper = libraryScanHelper;
            _progressHub = progressHub;

            // Limit concurrent processing based on hardware
            _processingSemaphore = new SemaphoreSlim(Math.Max(1, Config.MaxConcurrentStreams));

            // Initialize FFmpeg
            InitializeFFmpeg();

            // Construct sub-services (shared state passed by reference)
            _videoAnalyzer = new VideoAnalyzer(_logger, _ffprobePath);

            _strategySelector = new ProcessingStrategySelector(_logger);

            _frameProcessor = new VideoFrameProcessor(
                _logger,
                _ffmpegPath,
                upscalerCore,
                progressHub,
                httpClientFactory,
                _pausedJobs);

            _methodExecutor = new ProcessingMethodExecutor(
                _logger,
                _ffmpegPath,
                progressHub,
                httpClientFactory,
                _frameProcessor,
                _pausedJobs);

            _jobManager = new VideoJobManager(
                _logger,
                _activeJobs,
                _jobCancellationTokens,
                _pausedJobs,
                _performanceHistory,
                _strategySelector);

            // Initialize statistics timer
            if (Config.EnablePerformanceMetrics)
            {
                _statisticsTimer = new Timer(_jobManager.UpdateStatistics, null,
                    TimeSpan.FromSeconds(StatisticsIntervalSeconds), TimeSpan.FromSeconds(StatisticsIntervalSeconds));
            }

            _logger.LogInformation("VideoProcessor initialized with FFmpeg integration");
        }

        /// <summary>
        /// Initialize FFmpeg configuration
        /// </summary>
        private void InitializeFFmpeg()
        {
            try
            {
                _ffmpegPath = _mediaEncoder.EncoderPath;
                _ffprobePath = _mediaEncoder.ProbePath;

                if (string.IsNullOrEmpty(_ffmpegPath))
                {
                    _logger.LogWarning("FFmpeg path not available from MediaEncoder");
                    return;
                }

                GlobalFFOptions.Configure(new FFOptions
                {
                    BinaryFolder = Path.GetDirectoryName(_ffmpegPath) ?? string.Empty,
                    TemporaryFilesFolder = Path.GetTempPath()
                });

                _logger.LogInformation("FFmpeg configured: {FfmpegPath}", _ffmpegPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize FFmpeg");
            }
        }

        /// <summary>
        /// Extract a single frame from a video at the given position, returned as PNG bytes.
        /// </summary>
        public Task<byte[]> ExtractSingleFrameAsync(string videoPath, TimeSpan position, CancellationToken cancellationToken = default)
        {
            // Re-resolve ffmpegPath in case it wasn't available at construction time
            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                _ffmpegPath = _mediaEncoder.EncoderPath;
                _logger.LogInformation("Late-resolved FFmpeg path: {Path}", _ffmpegPath);
            }
            return _frameProcessor.ExtractSingleFrameAsync(videoPath, position, cancellationToken, _ffmpegPath);
        }

        /// <summary>
        /// Extract a single frame and apply an FFmpeg filter chain in one pass.
        /// Used for live filter preview in the config UI.
        /// </summary>
        public Task<byte[]> ExtractSingleFrameWithFiltersAsync(string videoPath, TimeSpan position, string? filterChain, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                _ffmpegPath = _mediaEncoder.EncoderPath;
                _logger.LogInformation("Late-resolved FFmpeg path: {Path}", _ffmpegPath);
            }
            return _frameProcessor.ExtractSingleFrameWithFiltersAsync(videoPath, position, filterChain, cancellationToken, _ffmpegPath);
        }

        /// <summary>
        /// Process video with AI upscaling
        /// </summary>
        public async Task<VideoProcessingResult> ProcessVideoAsync(
            string inputPath,
            string outputPath,
            VideoProcessingOptions options,
            CancellationToken cancellationToken = default)
        {
            var jobId = Guid.NewGuid().ToString();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _jobCancellationTokens[jobId] = cts;
            _pausedJobs[jobId] = false;

            var job = new ProcessingJob
            {
                Id = jobId,
                InputPath = inputPath,
                OutputPath = outputPath,
                Options = options,
                StartTime = DateTime.UtcNow,
                Status = ProcessingStatus.Starting
            };

            _activeJobs[jobId] = job;

            var semaphoreAcquired = false;
            try
            {
                if (!await _processingSemaphore.WaitAsync(SemaphoreTimeout, cancellationToken))
                {
                    _logger.LogWarning("Video processing timed out waiting for semaphore: {FileName}", Path.GetFileName(inputPath));
                    return new VideoProcessingResult { Success = false, Error = "Processing queue timeout - too many concurrent jobs" };
                }
                semaphoreAcquired = true;
                _logger.LogInformation("Starting video processing: {FileName}", Path.GetFileName(inputPath));

                // 1. Analyze input video
                var inputInfo = await _videoAnalyzer.AnalyzeVideoAsync(inputPath);
                job.InputInfo = inputInfo;

                // 2. Detect hardware capabilities
                var hardwareProfile = await _upscalerCore.DetectHardwareAsync();
                job.HardwareProfile = hardwareProfile;

                // 3. Optimize processing options
                var optimizedOptions = _strategySelector.OptimizeProcessingOptions(options, inputInfo, hardwareProfile);
                job.OptimizedOptions = optimizedOptions;

                // 3b. Model fallback chain: try primary model, fall back to alternatives
                var modelChain = _strategySelector.BuildVideoModelChain(optimizedOptions.Model);
                bool modelLoaded = false;
                foreach (var candidateModel in modelChain)
                {
                    try
                    {
                        var success = await _httpUpscalerService.EnsureModelLoadedAsync(candidateModel, cancellationToken);
                        if (success)
                        {
                            modelLoaded = true;
                            if (candidateModel != optimizedOptions.Model)
                            {
                                _logger.LogInformation("Model fallback: {Original} -> {Fallback}", optimizedOptions.Model, candidateModel);
                                optimizedOptions.Model = candidateModel;
                            }
                            break;
                        }
                        _logger.LogWarning("Failed to load model {Model}, trying next in fallback chain", candidateModel);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Model {Model} failed to load, trying next", candidateModel);
                    }
                }

                if (!modelLoaded)
                {
                    _logger.LogWarning("No model in fallback chain could be loaded, proceeding with default");
                }

                // 4. Check multi-frame model support
                var serviceStatus = await _upscalerCore.GetServiceStatusAsync();
                int inputFrames = serviceStatus?.InputFrames ?? 1;

                // 5. Choose processing method
                var processingMethod = _strategySelector.DetermineProcessingMethod(inputInfo, hardwareProfile, optimizedOptions, inputFrames);
                job.ProcessingMethod = processingMethod;

                // 6. Execute processing
                job.Status = ProcessingStatus.Processing;
                var result = await _methodExecutor.ExecuteProcessingAsync(inputPath, outputPath, job, inputFrames, cancellationToken);

                job.Status = result.Success ? ProcessingStatus.Completed : ProcessingStatus.Failed;
                job.EndTime = DateTime.UtcNow;
                job.Result = result;

                // 7. Update performance history
                _jobManager.UpdatePerformanceHistory(job);

                await _progressHub.SendJobCompleted(job.Id, Path.GetFileName(inputPath), result.Success, result.Error);

                // 8. Fire webhook notification (fire-and-forget with error logging)
                if (result.Success)
                {
                    _ = _upscalerCore.SendWebhookAsync("complete", Path.GetFileName(inputPath), true)
                        .ContinueWith(t => _logger.LogWarning(t.Exception, "Webhook notification failed"), TaskContinuationOptions.OnlyOnFaulted);
                }
                else
                {
                    _ = _upscalerCore.SendWebhookAsync("failure", Path.GetFileName(inputPath), false, result.Error)
                        .ContinueWith(t => _logger.LogWarning(t.Exception, "Webhook notification failed"), TaskContinuationOptions.OnlyOnFaulted);
                }

                // 9. Trigger library scan for new upscaled file
                if (result.Success && !string.IsNullOrEmpty(outputPath))
                {
                    await _libraryScanHelper.ScanUpscaledFile(inputPath, outputPath);
                }

                _logger.LogInformation("Video processing completed: {Success}, Time: {Duration}s",
                    result.Success, job.ProcessingDuration.TotalSeconds.ToString("F1"));

                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Video processing cancelled: {JobId}", jobId);
                job.Status = ProcessingStatus.Cancelled;
                return new VideoProcessingResult { Success = false, Error = "Processing cancelled" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Video processing failed: {JobId}", jobId);
                job.Status = ProcessingStatus.Failed;
                job.Error = ex.Message;

                _ = _upscalerCore.SendWebhookAsync("failure", Path.GetFileName(inputPath), false, ex.Message)
                    .ContinueWith(t => _logger.LogWarning(t.Exception, "Webhook notification failed"), TaskContinuationOptions.OnlyOnFaulted);

                return new VideoProcessingResult { Success = false, Error = ex.Message };
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _processingSemaphore.Release();
                }
                _activeJobs.TryRemove(jobId, out _);
                if (_jobCancellationTokens.TryRemove(jobId, out var removedCts))
                {
                    removedCts.Dispose();
                }
                _pausedJobs.TryRemove(jobId, out _);
            }
        }

        /// <summary>
        /// Get all active processing jobs
        /// </summary>
        public List<object> GetActiveJobs() => _jobManager.GetActiveJobs();

        /// <summary>
        /// Pause a running job
        /// </summary>
        public bool PauseJob(string jobId) => _jobManager.PauseJob(jobId);

        /// <summary>
        /// Resume a paused job
        /// </summary>
        public bool ResumeJob(string jobId) => _jobManager.ResumeJob(jobId);

        /// <summary>
        /// Cancel a running job
        /// </summary>
        public bool CancelJob(string jobId) => _jobManager.CancelJob(jobId);

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            _statisticsTimer?.Dispose();

            foreach (var kvp in _jobCancellationTokens)
            {
                try { kvp.Value?.Cancel(); }
                catch (ObjectDisposedException) { /* Already disposed */ }
            }

            foreach (var kvp in _jobCancellationTokens)
            {
                try { kvp.Value?.Dispose(); }
                catch (ObjectDisposedException) { /* Already disposed */ }
            }
            _jobCancellationTokens.Clear();
            _processingSemaphore?.Dispose();
        }
    }
}
