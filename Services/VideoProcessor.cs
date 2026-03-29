using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CliWrap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using JellyfinUpscalerPlugin.Models;
using Image = SixLabors.ImageSharp.Image;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Video processing engine with FFmpeg integration - Phase 2 Implementation
    /// </summary>
    public class VideoProcessor : IDisposable
    {
        private readonly ILogger<VideoProcessor> _logger;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly UpscalerCore _upscalerCore;
        private readonly HttpUpscalerService _httpUpscalerService;
        private readonly UpscalerProgressHub _progressHub;
        private readonly LibraryScanHelper _libraryScanHelper;
        private readonly IHttpClientFactory _httpClientFactory;

        // Processing queue for concurrent streams
        private readonly SemaphoreSlim _processingSemaphore;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ProcessingJob> _activeJobs = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _jobCancellationTokens = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _pausedJobs = new();
        
        // Performance monitoring
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, VideoProcessingMetrics> _performanceHistory = new();
        private readonly Timer? _statisticsTimer;
        
        // FFmpeg configuration
        private string _ffmpegPath = string.Empty;
        private string _ffprobePath = string.Empty;
        
        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        // Processing constants
        private const double ProgressStartingPercent = 5.0;
        private const double ProgressMaxPercent = 95.0;
        private const double ProgressDefaultPercent = 10.0;
        private const double EstimatedProcessingSpeedRatio = 0.5;
        private const double EstimatedTotalFallbackSeconds = 60.0;
        private const int StatisticsIntervalSeconds = 10;
        private static readonly TimeSpan SemaphoreTimeout = TimeSpan.FromMinutes(30);

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
            _progressHub = progressHub;
            _libraryScanHelper = libraryScanHelper;
            _httpClientFactory = httpClientFactory;
            
            // Limit concurrent processing based on hardware
            _processingSemaphore = new SemaphoreSlim(Math.Max(1, Config.MaxConcurrentStreams));
            
            // Initialize FFmpeg
            InitializeFFmpeg();
            
            // Initialize statistics timer
            if (Config.EnablePerformanceMetrics)
            {
                _statisticsTimer = new Timer(UpdateStatistics, null, 
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
                
                // Configure FFMpegCore
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
                var inputInfo = await AnalyzeVideoAsync(inputPath);
                job.InputInfo = inputInfo;
                
                // 2. Detect hardware capabilities
                var hardwareProfile = await _upscalerCore.DetectHardwareAsync();
                job.HardwareProfile = hardwareProfile;
                
                // 3. Optimize processing options
                var optimizedOptions = OptimizeProcessingOptions(options, inputInfo, hardwareProfile);
                job.OptimizedOptions = optimizedOptions;

                // 3b. Model fallback chain: try primary model, fall back to alternatives
                var modelChain = BuildVideoModelChain(optimizedOptions.Model);
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
                var processingMethod = DetermineProcessingMethod(inputInfo, hardwareProfile, optimizedOptions, inputFrames);
                job.ProcessingMethod = processingMethod;

                // 6. Execute processing
                job.Status = ProcessingStatus.Processing;
                var result = await ExecuteProcessingAsync(inputPath, outputPath, job, inputFrames, cancellationToken);
                
                job.Status = result.Success ? ProcessingStatus.Completed : ProcessingStatus.Failed;
                job.EndTime = DateTime.UtcNow;
                job.Result = result;
                
                // 6. Update performance history
                UpdatePerformanceHistory(job);
                
                await _progressHub.SendJobCompleted(job.Id, Path.GetFileName(inputPath), result.Success, result.Error);

                // 7. Fire webhook notification (fire-and-forget with error logging)
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

                // 8. Trigger library scan for new upscaled file
                if (result.Success && !string.IsNullOrEmpty(outputPath))
                {
                    await _libraryScanHelper.ScanUpscaledFile(inputPath, outputPath);
                }

                _logger.LogInformation("Video processing completed: {Success}, Time: {Duration}s", result.Success, job.ProcessingDuration.TotalSeconds.ToString("F1"));

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

                // Fire webhook notification (fire-and-forget with error logging)
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
        public List<object> GetActiveJobs()
        {
            return _activeJobs.Values.Select(job => new
            {
                jobId = job.Id,
                inputPath = Path.GetFileName(job.InputPath),
                outputPath = Path.GetFileName(job.OutputPath),
                status = job.Status.ToString(),
                progress = CalculateJobProgress(job),
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
        /// Calculate job progress percentage
        /// </summary>
        private double CalculateJobProgress(ProcessingJob job)
        {
            if (job.Status == ProcessingStatus.Completed)
                return 100.0;
            if (job.Status == ProcessingStatus.Cancelled || job.Status == ProcessingStatus.Failed)
                return 0;
            if (job.Status == ProcessingStatus.Starting || job.Status == ProcessingStatus.Analyzing)
                return ProgressStartingPercent;

            if (job.Status == ProcessingStatus.Processing && job.InputInfo != null && job.InputInfo.Duration.TotalSeconds > 0)
            {
                var elapsed = (DateTime.UtcNow - job.StartTime).TotalSeconds;
                var estimatedTotal = job.InputInfo.Duration.TotalSeconds * EstimatedProcessingSpeedRatio; // rough: processing ~2x realtime
                if (estimatedTotal <= 0) estimatedTotal = EstimatedTotalFallbackSeconds;
                return Math.Min(ProgressMaxPercent, (elapsed / estimatedTotal) * 100);
            }

            return ProgressDefaultPercent;
        }

        /// <summary>
        /// Analyze video properties using FFprobe
        /// </summary>
        private async Task<VideoInfo> AnalyzeVideoAsync(string inputPath)
        {
            try
            {
                var mediaInfo = await FFProbe.AnalyseAsync(inputPath);
                var videoStream = mediaInfo.VideoStreams.FirstOrDefault();
                
                if (videoStream == null)
                {
                    throw new InvalidOperationException("No video stream found");
                }
                
                var info = new VideoInfo
                {
                    Width = videoStream.Width,
                    Height = videoStream.Height,
                    FrameRate = videoStream.FrameRate,
                    Duration = mediaInfo.Duration,
                    Codec = videoStream.CodecName,
                    BitRate = videoStream.BitRate,
                    PixelFormat = videoStream.PixelFormat,
                    ColorSpace = videoStream.PixelFormat ?? "unknown",
                    ColorRange = "unknown",
                    FileSize = new FileInfo(inputPath).Length,
                    HasAudio = mediaInfo.AudioStreams.Any(),
                    HasSubtitles = mediaInfo.SubtitleStreams.Any()
                };
                
                // Enhanced analysis
                info.EstimatedQuality = EstimateVideoQuality(info);

                // Detect HDR properties via raw FFprobe for color_transfer/color_primaries/bit_depth
                await DetectHDRPropertiesAsync(inputPath, info);

                info.IsHDR = IsHDRVideo(videoStream, info);
                info.IsInterlaced = await DetectInterlacedAsync(inputPath);
                info.AspectRatio = info.Height > 0 ? (double)info.Width / info.Height : 0;

                _logger.LogInformation("Video analysis: {Width}x{Height} @ {Fps}fps, {Codec}, {BitRate}kbps, HDR: {IsHDR}, BitDepth: {BitDepth}, Interlaced: {Interlaced}", info.Width, info.Height, info.FrameRate.ToString("F1"), info.Codec, info.BitRate, info.IsHDR, info.BitDepth, info.IsInterlaced);
                
                return info;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Video analysis failed");
                throw;
            }
        }

        /// <summary>
        /// Optimize processing options based on hardware and input
        /// </summary>
        private VideoProcessingOptions OptimizeProcessingOptions(
            VideoProcessingOptions options,
            VideoInfo inputInfo,
            HardwareProfile hardwareProfile)
        {
            var optimized = new VideoProcessingOptions(options);
            
            // Auto-select model based on hardware
            if (string.IsNullOrEmpty(optimized.Model) || optimized.Model == "auto")
            {
                optimized.Model = hardwareProfile.RecommendedModel;
            }
            
            // Auto-select scale based on input resolution
            if (optimized.ScaleFactor == 0)
            {
                optimized.ScaleFactor = inputInfo.Width <= 720 ? 3 : 2;
            }
            
            // Normalize "low" quality to "fast" (UI offers "low" but backend uses "fast")
            if (string.Equals(optimized.QualityLevel, "low", StringComparison.OrdinalIgnoreCase))
            {
                optimized.QualityLevel = "fast";
            }

            // Only auto-select quality if not explicitly configured by user
            if (string.IsNullOrEmpty(optimized.QualityLevel) || optimized.QualityLevel == "auto")
            {
                if (hardwareProfile.SupportsCUDA && hardwareProfile.VramMB > 8192)
                    optimized.QualityLevel = "high";
                else if (hardwareProfile.SupportsDirectML)
                    optimized.QualityLevel = "medium";
                else
                    optimized.QualityLevel = "fast";
            }
            
            // Enable hardware acceleration if available
            if (hardwareProfile.SupportsCUDA)
            {
                optimized.HardwareAcceleration = "cuda";
            }
            else if (hardwareProfile.AvailableHwAccels.Contains("vaapi"))
            {
                optimized.HardwareAcceleration = "vaapi";
            }
            else if (hardwareProfile.AvailableHwAccels.Contains("qsv"))
            {
                optimized.HardwareAcceleration = "qsv";
            }
            else if (hardwareProfile.SupportsDirectML)
            {
                optimized.HardwareAcceleration = "directml";
            }
            
            _logger.LogInformation("Optimized options: {Model} @ {Scale}x, {Quality} quality, {Accel} accel", optimized.Model, optimized.ScaleFactor, optimized.QualityLevel, optimized.HardwareAcceleration);
            
            return optimized;
        }

        /// <summary>
        /// Build a model fallback chain for video processing.
        /// Primary model first, then configured fallbacks from ModelFallbackChain config.
        /// </summary>
        private List<string> BuildVideoModelChain(string primaryModel)
        {
            var chain = new List<string> { primaryModel };
            var fallbackConfig = Config.ModelFallbackChain;
            if (!string.IsNullOrWhiteSpace(fallbackConfig))
            {
                var fallbacks = fallbackConfig.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var fb in fallbacks)
                {
                    if (!chain.Contains(fb, StringComparer.OrdinalIgnoreCase))
                        chain.Add(fb);
                }
            }
            return chain;
        }

        /// <summary>
        /// Determine the best processing method
        /// </summary>
        private ProcessingMethod DetermineProcessingMethod(
            VideoInfo inputInfo,
            HardwareProfile hardwareProfile,
            VideoProcessingOptions options,
            int inputFrames = 1)
        {
            // Multi-frame VSR takes priority when model supports it
            if (options.EnableAIUpscaling && inputFrames > 1)
            {
                _logger.LogInformation("Multi-frame model detected (input_frames={Frames}), using MultiFrame processing", inputFrames);
                return ProcessingMethod.MultiFrame;
            }

            // Real-time AI upscaling: pipe-based decode -> upscale -> encode
            // Only use when conditions are favorable for real-time performance
            if (options.EnableAIUpscaling && IsRealTimeAIFeasible(inputInfo, hardwareProfile, options))
            {
                _logger.LogInformation("Real-time AI upscaling selected for {Width}x{Height} @ {Fps}fps with model {Model}",
                    inputInfo.Width, inputInfo.Height, inputInfo.FrameRate, options.Model);
                return ProcessingMethod.RealTimeAI;
            }

            // Real-time processing for short videos or live streams
            // BUT: if AI upscaling is explicitly requested, use frame-by-frame instead
            if (!options.EnableAIUpscaling && (inputInfo.Duration.TotalMinutes < 5 || options.EnableRealTimeProcessing))
            {
                return ProcessingMethod.RealTime;
            }

            // Frame-by-frame for high quality
            if (options.QualityLevel == "high" && hardwareProfile.SupportsCUDA)
            {
                return ProcessingMethod.FrameByFrame;
            }

            // Batch processing for efficiency
            return ProcessingMethod.Batch;
        }

        /// <summary>
        /// Check if real-time AI upscaling is feasible for the given input.
        /// Requires: fast model, reasonable input resolution, and sufficient benchmark FPS.
        /// </summary>
        private bool IsRealTimeAIFeasible(VideoInfo inputInfo, HardwareProfile hardwareProfile, VideoProcessingOptions options)
        {
            // Only enable for explicit real-time request
            if (!options.EnableRealTimeProcessing)
            {
                return false;
            }

            // Input resolution must be <= 1080p
            if (inputInfo.Width > 1920 || inputInfo.Height > 1080)
            {
                _logger.LogDebug("RealTimeAI skipped: input resolution {Width}x{Height} exceeds 1080p", inputInfo.Width, inputInfo.Height);
                return false;
            }

            // Only fast models are suitable for real-time
            var fastModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "span-x2", "span-x4",
                "clearreality-x4",
                "fsrcnn-x2", "fsrcnn-x3", "fsrcnn-x4",
                "espcn-x2", "espcn-x3", "espcn-x4"
            };

            var modelName = options.Model?.ToLowerInvariant() ?? "";
            if (!fastModels.Contains(modelName) && !modelName.Contains("fsrcnn") && !modelName.Contains("espcn") && !modelName.Contains("span"))
            {
                _logger.LogDebug("RealTimeAI skipped: model '{Model}' is not in the fast model list", options.Model);
                return false;
            }

            // Check benchmark FPS if available (must be >= 80% of target FPS)
            var targetFps = inputInfo.FrameRate > 0 ? inputInfo.FrameRate : 30.0;
            var benchmarkFps = hardwareProfile.BenchmarkFps;
            if (benchmarkFps > 0 && benchmarkFps < targetFps * 0.8)
            {
                _logger.LogDebug("RealTimeAI skipped: benchmark FPS {BenchFps:F1} < {Threshold:F1} (80% of target {TargetFps:F1})",
                    benchmarkFps, targetFps * 0.8, targetFps);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Execute video processing based on method
        /// </summary>
        private async Task<VideoProcessingResult> ExecuteProcessingAsync(
            string inputPath,
            string outputPath,
            ProcessingJob job,
            int inputFrames,
            CancellationToken cancellationToken)
        {
            return job.ProcessingMethod switch
            {
                ProcessingMethod.RealTime => await ProcessRealTimeAsync(inputPath, outputPath, job, cancellationToken),
                ProcessingMethod.FrameByFrame => await ProcessFrameByFrameAsync(inputPath, outputPath, job, cancellationToken),
                ProcessingMethod.Batch => await ProcessBatchAsync(inputPath, outputPath, job, cancellationToken),
                ProcessingMethod.MultiFrame => await ProcessMultiFrameAsync(inputPath, outputPath, job, inputFrames, cancellationToken),
                ProcessingMethod.RealTimeAI => await ProcessRealTimeAIAsync(inputPath, outputPath, job, cancellationToken),
                _ => throw new NotSupportedException($"Processing method {job.ProcessingMethod} not supported")
            };
        }

        /// <summary>
        /// Real-time processing using FFmpeg filters
        /// </summary>
        private async Task<VideoProcessingResult> ProcessRealTimeAsync(
            string inputPath,
            string outputPath,
            ProcessingJob job,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting real-time processing");
                
                var args = BuildFFmpegCommand(inputPath, outputPath, job.OptimizedOptions, job.HardwareProfile);
                
                var result = await Cli.Wrap(_ffmpegPath)
                    .WithArguments(args)
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync(cancellationToken);
                
                var success = result.ExitCode == 0;

                if (!success)
                {
                    _logger.LogError("Real-time FFmpeg processing failed with exit code {ExitCode} for {InputPath}", result.ExitCode, inputPath);
                }

                return new VideoProcessingResult
                {
                    Success = success,
                    OutputPath = outputPath,
                    ProcessingTime = DateTime.UtcNow - job.StartTime,
                    Method = ProcessingMethod.RealTime,
                    Error = success ? string.Empty : $"FFmpeg exited with code {result.ExitCode}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Real-time processing failed");
                return new VideoProcessingResult
                {
                    Success = false,
                    Error = ex.Message,
                    Method = ProcessingMethod.RealTime
                };
            }
        }

        /// <summary>
        /// Frame-by-frame processing with AI upscaling
        /// </summary>
        private async Task<VideoProcessingResult> ProcessFrameByFrameAsync(
            string inputPath,
            string outputPath,
            ProcessingJob job,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting frame-by-frame processing");
                
                var tempDir = Path.Combine(Path.GetTempPath(), "JellyfinUpscaler", job.Id);
                Directory.CreateDirectory(tempDir);

                // Disk space check before frame extraction
                var driveInfo = new DriveInfo(Path.GetPathRoot(tempDir) ?? "/");
                var estimatedSpaceNeeded = (long)((job.InputInfo?.Duration.TotalSeconds ?? 300) * 25 * 500_000); // ~500KB per frame estimate
                if (driveInfo.AvailableFreeSpace < estimatedSpaceNeeded)
                {
                    _logger.LogError("Insufficient disk space. Need ~{Need}GB, have {Have}GB",
                        estimatedSpaceNeeded / 1_000_000_000.0, driveInfo.AvailableFreeSpace / 1_000_000_000.0);
                    throw new InvalidOperationException($"Insufficient disk space for frame extraction");
                }

                try
                {
                    // 1. Extract frames
                    var framesDir = Path.Combine(tempDir, "frames");
                    Directory.CreateDirectory(framesDir);
                    
                    var framesFps = job.InputInfo?.FrameRate ?? 30.0;
                    var isInterlaced = job.InputInfo?.IsInterlaced ?? false;
                    var isHDR = job.InputInfo?.IsHDR ?? false;
                    await ExtractFramesAsync(inputPath, framesDir, framesFps, cancellationToken, isInterlaced, isHDR);

                    // 2. Process frames with AI
                    var processedDir = Path.Combine(tempDir, "processed");
                    Directory.CreateDirectory(processedDir);
                    
                    await ProcessFramesAsync(framesDir, processedDir, job.OptimizedOptions, job.Id, cancellationToken, isHDR);

                    // 3. Reconstruct video
                    await ReconstructVideoAsync(processedDir, inputPath, outputPath, job.OptimizedOptions, framesFps, cancellationToken, job.InputInfo);
                    
                    return new VideoProcessingResult
                    {
                        Success = true,
                        OutputPath = outputPath,
                        ProcessingTime = DateTime.UtcNow - job.StartTime,
                        Method = ProcessingMethod.FrameByFrame
                    };
                }
                finally
                {
                    // Cleanup temp directory
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to cleanup temp directory");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frame-by-frame processing failed");
                return new VideoProcessingResult
                {
                    Success = false,
                    Error = ex.Message,
                    Method = ProcessingMethod.FrameByFrame
                };
            }
        }

        /// <summary>
        /// Multi-frame processing with sliding window for VSR models
        /// </summary>
        private async Task<VideoProcessingResult> ProcessMultiFrameAsync(
            string inputPath,
            string outputPath,
            ProcessingJob job,
            int inputFrames,
            CancellationToken cancellationToken)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"upscaler_mf_{Guid.NewGuid():N}");

            try
            {
                // Disk space check before frame extraction (minimum 2GB required)
                var driveInfo = new DriveInfo(Path.GetPathRoot(tempDir) ?? "/");
                const long minFreeSpace = 2L * 1024 * 1024 * 1024; // 2 GB
                if (driveInfo.AvailableFreeSpace < minFreeSpace)
                {
                    _logger.LogWarning("Insufficient disk space for multi-frame processing. Need at least 2GB, have {Have:F1}GB",
                        driveInfo.AvailableFreeSpace / 1_000_000_000.0);
                    throw new InvalidOperationException("Insufficient disk space for multi-frame frame extraction (need at least 2GB free)");
                }

                var framesDir = Path.Combine(tempDir, "frames");
                var processedDir = Path.Combine(tempDir, "processed");
                Directory.CreateDirectory(framesDir);
                Directory.CreateDirectory(processedDir);

                // Extract frames (same as frame-by-frame, with deinterlacing if needed)
                var effectiveFps = job.InputInfo?.FrameRate ?? 24;
                var multiFrameIsInterlaced = job.InputInfo?.IsInterlaced ?? false;

                var mfVfFilters = new List<string>();
                if (multiFrameIsInterlaced)
                {
                    mfVfFilters.Add("bwdif=mode=send_frame:parity=auto:deint=all");
                    _logger.LogInformation("Applying bwdif deinterlacing filter for multi-frame extraction of {File}", Path.GetFileName(inputPath));
                }
                mfVfFilters.Add($"fps={effectiveFps.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

                var mfVfArg = string.Join(",", mfVfFilters);
                var extractArgs = $"-i \"{inputPath}\" -vf \"{mfVfArg}\" \"{framesDir}/frame_%06d.png\"";
                _logger.LogInformation("Extracting frames for multi-frame processing: {Args}", extractArgs);

                await Cli.Wrap(_ffmpegPath)
                    .WithArguments(extractArgs)
                    .WithValidation(CommandResultValidation.ZeroExitCode)
                    .ExecuteAsync(cancellationToken);

                var frameFiles = Directory.GetFiles(framesDir, "*.png")
                    .OrderBy(f => f)
                    .ToList();

                if (frameFiles.Count == 0)
                {
                    throw new InvalidOperationException("No frames extracted");
                }

                _logger.LogInformation("Extracted {Count} frames. Processing with {InputFrames}-frame sliding window (SEQUENTIAL)",
                    frameFiles.Count, inputFrames);

                // Use (inputFrames - 1) / 2 to correctly handle both odd and even frame counts
                int halfWindow = (inputFrames - 1) / 2;
                int totalFrames = frameFiles.Count;
                int processedCount = 0;
                var serviceUrl = Config.AiServiceUrl?.TrimEnd('/') ?? "http://localhost:5000";

                // Use IHttpClientFactory named client for proper DNS refresh and connection pooling
                var multiFrameClient = _httpClientFactory.CreateClient("AiUpscalerLongTimeout");

                // SEQUENTIAL sliding window -- do NOT parallelize
                for (int i = 0; i < totalFrames; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // Build window with boundary padding — count-based to produce exactly inputFrames entries
                        var windowPaths = new List<string>();
                        int startIdx = i - halfWindow;
                        for (int j = 0; j < inputFrames; j++)
                        {
                            int idx = Math.Clamp(startIdx + j, 0, totalFrames - 1);
                            windowPaths.Add(frameFiles[idx]);
                        }

                        // Send window to AI service
                        using var content = new MultipartFormDataContent();
                        for (int k = 0; k < windowPaths.Count; k++)
                        {
                            var frameBytes = await File.ReadAllBytesAsync(windowPaths[k], cancellationToken);
                            var byteContent = new ByteArrayContent(frameBytes);
                            byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                            content.Add(byteContent, $"frame_{k}", $"frame_{k}.png");
                        }

                        using var response = await multiFrameClient.PostAsync($"{serviceUrl}/upscale-video-chunk", content, cancellationToken);

                        if (response.IsSuccessStatusCode)
                        {
                            var resultBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                            var outputFile = Path.Combine(processedDir, Path.GetFileName(frameFiles[i]));
                            await File.WriteAllBytesAsync(outputFile, resultBytes, cancellationToken);
                        }
                        else
                        {
                            _logger.LogWarning("Multi-frame upscale failed for frame {Frame} (HTTP {Code}), using original",
                                i, (int)response.StatusCode);
                            var outputFile = Path.Combine(processedDir, Path.GetFileName(frameFiles[i]));
                            File.Copy(frameFiles[i], outputFile, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Multi-frame upscale error for frame {Frame}, using original", i);
                        var outputFile = Path.Combine(processedDir, Path.GetFileName(frameFiles[i]));
                        File.Copy(frameFiles[i], outputFile, true);
                    }

                    processedCount++;
                    var progress = (double)processedCount / totalFrames * 100;
                    _logger.LogDebug("Multi-frame progress: {Progress:F1}% ({Processed}/{Total})",
                        progress, processedCount, totalFrames);
                }

                // Reconstruct video with audio (uses same method as frame-by-frame)
                _logger.LogInformation("Reconstructing video from {Count} processed frames with audio", totalFrames);
                await ReconstructVideoAsync(processedDir, inputPath, outputPath, job.OptimizedOptions, effectiveFps, cancellationToken, job.InputInfo);

                return new VideoProcessingResult
                {
                    Success = true,
                    OutputPath = outputPath,
                    ProcessingTime = DateTime.UtcNow - job.StartTime,
                    Method = ProcessingMethod.MultiFrame
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Multi-frame processing failed");
                return new VideoProcessingResult
                {
                    Success = false,
                    Error = ex.Message,
                    Method = ProcessingMethod.MultiFrame
                };
            }
            finally
            {
                // Cleanup temp directory
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup temp directory: {Dir}", tempDir);
                }
            }
        }

        /// <summary>
        /// Batch processing for efficiency
        /// </summary>
        private async Task<VideoProcessingResult> ProcessBatchAsync(
            string inputPath,
            string outputPath,
            ProcessingJob job,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting batch AI processing (redirecting to frame-by-frame pipeline)");
                
                // For v1.4.1, we use the stable frame-by-frame pipeline for batch processing
                // as it provides the most consistent AI quality and progress tracking.
                var result = await ProcessFrameByFrameAsync(inputPath, outputPath, job, cancellationToken);
                
                return new VideoProcessingResult
                {
                    Success = result.Success,
                    OutputPath = result.OutputPath,
                    ProcessingTime = DateTime.UtcNow - job.StartTime,
                    Method = ProcessingMethod.Batch,
                    Error = result.Error
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch processing failed");
                return new VideoProcessingResult
                {
                    Success = false,
                    Error = ex.Message,
                    Method = ProcessingMethod.Batch
                };
            }
        }

        /// <summary>
        /// Real-time AI processing using FFmpeg pipe decode -> AI upscale -> FFmpeg pipe encode.
        /// Two FFmpeg processes connected through the AI service:
        /// FFmpeg(decode) -> stdout -> [C# reads frames] -> HTTP POST /upscale-frame -> [C# writes frames] -> stdin -> FFmpeg(encode)
        /// </summary>
        private async Task<VideoProcessingResult> ProcessRealTimeAIAsync(
            string inputPath,
            string outputPath,
            ProcessingJob job,
            CancellationToken cancellationToken)
        {
            var effectiveFps = job.InputInfo?.FrameRate > 0 ? job.InputInfo.FrameRate : 30.0;
            var inputWidth = job.InputInfo?.Width ?? 1920;
            var inputHeight = job.InputInfo?.Height ?? 1080;
            var scale = job.OptimizedOptions?.ScaleFactor ?? 2;
            var outputWidth = inputWidth * scale;
            var outputHeight = inputHeight * scale;
            var frameByteSize = inputWidth * inputHeight * 3; // rawvideo rgb24
            var upscaledFrameByteSize = outputWidth * outputHeight * 3;
            var serviceUrl = Config.AiServiceUrl?.TrimEnd('/') ?? "http://localhost:5000";

            _logger.LogInformation(
                "Starting RealTimeAI processing: {InputW}x{InputH} -> {OutputW}x{OutputH} @ {Fps}fps, model={Model}",
                inputWidth, inputHeight, outputWidth, outputHeight, effectiveFps, job.OptimizedOptions?.Model);

            Process? decoderProcess = null;
            Process? encoderProcess = null;

            try
            {
                // Audio extraction (same pattern as ReconstructVideoAsync)
                var tempAudioPath = Path.Combine(Path.GetTempPath(), $"rtai_audio_{Guid.NewGuid()}.mka");
                var hasAudio = false;
                try
                {
                    var audioArgs = $"-i \"{inputPath}\" -vn -acodec copy -y \"{tempAudioPath}\"";
                    var audioResult = await Cli.Wrap(_ffmpegPath)
                        .WithArguments(audioArgs)
                        .WithValidation(CommandResultValidation.None)
                        .ExecuteAsync(cancellationToken);
                    hasAudio = audioResult.ExitCode == 0 && File.Exists(tempAudioPath) && new FileInfo(tempAudioPath).Length > 0;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract audio for RealTimeAI, continuing without");
                }

                // Decoder FFmpeg: input -> raw frames on stdout
                var decoderArgs = $"-i \"{inputPath}\" -f rawvideo -pix_fmt rgb24 -v quiet -";
                decoderProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _ffmpegPath,
                        Arguments = decoderArgs,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = false,
                        CreateNoWindow = true
                    }
                };

                // Encoder FFmpeg: raw frames on stdin -> encoded output
                var outputCodec = Config.OutputCodec ?? "libx264";
                var allowedCodecs = new HashSet<string> { "libx264", "libx265", "hevc_nvenc", "h264_nvenc", "h264_qsv", "hevc_qsv" };
                if (!allowedCodecs.Contains(outputCodec))
                {
                    outputCodec = "libx264";
                }

                var encoderInputArgs = hasAudio && File.Exists(tempAudioPath)
                    ? $"-f rawvideo -pix_fmt rgb24 -s {outputWidth}x{outputHeight} -r {effectiveFps.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i - -i \"{tempAudioPath}\" -c:v {outputCodec} -pix_fmt yuv420p -c:a copy -y \"{outputPath}\""
                    : $"-f rawvideo -pix_fmt rgb24 -s {outputWidth}x{outputHeight} -r {effectiveFps.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i - -c:v {outputCodec} -pix_fmt yuv420p -y \"{outputPath}\"";

                encoderProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _ffmpegPath,
                        Arguments = encoderInputArgs,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardError = false,
                        CreateNoWindow = true
                    }
                };

                decoderProcess.Start();
                encoderProcess.Start();

                var decoderStream = decoderProcess.StandardOutput.BaseStream;
                var encoderStream = encoderProcess.StandardInput.BaseStream;

                // Use IHttpClientFactory for AI service calls
                var httpClient = _httpClientFactory.CreateClient("AiUpscalerLongTimeout");

                var framesProcessed = 0;
                var framesDropped = 0;
                var processingStartTime = DateTime.UtcNow;
                var frameBuffer = new byte[frameByteSize];

                while (!cancellationToken.IsCancellationRequested)
                {
                    // Check for pause
                    while (_pausedJobs.GetValueOrDefault(job.Id, false))
                    {
                        await Task.Delay(500, cancellationToken);
                    }

                    // Read one full frame from decoder stdout
                    var totalRead = 0;
                    while (totalRead < frameByteSize)
                    {
                        var bytesRead = await decoderStream.ReadAsync(
                            frameBuffer, totalRead, frameByteSize - totalRead, cancellationToken);

                        if (bytesRead == 0)
                        {
                            // End of stream
                            break;
                        }
                        totalRead += bytesRead;
                    }

                    if (totalRead < frameByteSize)
                    {
                        // End of video or incomplete frame
                        break;
                    }

                    try
                    {
                        // Send frame to AI service for upscaling via /upscale-frame (JPEG encode/decode)
                        using var frameContent = new ByteArrayContent(frameBuffer);
                        frameContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                        // Use raw frame endpoint: encode as JPEG for transport
                        var jpegBytes = EncodeRawFrameToJpeg(frameBuffer, inputWidth, inputHeight);
                        using var jpegContent = new ByteArrayContent(jpegBytes);
                        jpegContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

                        using var response = await httpClient.PostAsync($"{serviceUrl}/upscale-frame", jpegContent, cancellationToken);

                        if (response.IsSuccessStatusCode)
                        {
                            var upscaledJpeg = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                            var upscaledRaw = DecodeJpegToRawFrame(upscaledJpeg, outputWidth, outputHeight);

                            if (upscaledRaw != null && upscaledRaw.Length == upscaledFrameByteSize)
                            {
                                await encoderStream.WriteAsync(upscaledRaw, 0, upscaledRaw.Length, cancellationToken);
                                framesProcessed++;
                            }
                            else
                            {
                                // Write original frame scaled up as fallback
                                _logger.LogDebug("Upscaled frame size mismatch, skipping frame {Frame}", framesProcessed);
                                framesDropped++;
                            }
                        }
                        else
                        {
                            _logger.LogWarning("AI service returned {StatusCode} for frame {Frame}", (int)response.StatusCode, framesProcessed);
                            framesDropped++;
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogWarning(ex, "AI service request failed for frame {Frame}", framesProcessed);
                        framesDropped++;
                    }

                    // Progress reporting (every 2 seconds or every 60 frames)
                    if (framesProcessed % 60 == 0 || framesProcessed == 1)
                    {
                        var elapsed = (DateTime.UtcNow - processingStartTime).TotalSeconds;
                        var currentFps = elapsed > 0 ? framesProcessed / elapsed : 0;
                        var realTimeRatio = effectiveFps > 0 ? currentFps / effectiveFps : 0;

                        await _progressHub.SendFrameProgress(
                            job.Id,
                            Path.GetFileName(inputPath),
                            framesProcessed,
                            -1,  // Total unknown in streaming mode
                            currentFps
                        );

                        _logger.LogInformation(
                            "RealTimeAI: {Processed} frames ({Dropped} dropped), {Fps:F1} FPS, {Ratio:F2}x real-time",
                            framesProcessed, framesDropped, currentFps, realTimeRatio);
                    }
                }

                // Close encoder stdin to signal end of input
                encoderStream.Close();

                // Wait for both processes to exit
                await Task.WhenAll(
                    Task.Run(() => decoderProcess.WaitForExit(30000), cancellationToken),
                    Task.Run(() => encoderProcess.WaitForExit(60000), cancellationToken));

                var totalElapsed = DateTime.UtcNow - processingStartTime;
                var avgFps = totalElapsed.TotalSeconds > 0 ? framesProcessed / totalElapsed.TotalSeconds : 0;

                // Cleanup temp audio
                try { if (File.Exists(tempAudioPath)) File.Delete(tempAudioPath); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to cleanup temp audio"); }

                var success = encoderProcess.ExitCode == 0 && framesProcessed > 0;

                _logger.LogInformation(
                    "RealTimeAI completed: {Frames} frames, {Dropped} dropped, {Fps:F1} avg FPS, encoder exit={ExitCode}",
                    framesProcessed, framesDropped, avgFps, encoderProcess.ExitCode);

                return new VideoProcessingResult
                {
                    Success = success,
                    OutputPath = outputPath,
                    ProcessingTime = totalElapsed,
                    Method = ProcessingMethod.RealTimeAI,
                    Error = success ? string.Empty : $"Encoder exit code: {encoderProcess.ExitCode}, frames: {framesProcessed}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RealTimeAI processing failed");
                return new VideoProcessingResult
                {
                    Success = false,
                    Error = ex.Message,
                    Method = ProcessingMethod.RealTimeAI
                };
            }
            finally
            {
                try { decoderProcess?.StandardOutput?.BaseStream?.Dispose(); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to dispose decoder stream"); }
                try { encoderProcess?.StandardInput?.BaseStream?.Dispose(); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to dispose encoder stream"); }
                try { decoderProcess?.Kill(true); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to kill decoder process"); }
                try { encoderProcess?.Kill(true); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to kill encoder process"); }
                decoderProcess?.Dispose();
                encoderProcess?.Dispose();
            }
        }

        /// <summary>
        /// Encode a raw RGB24 frame buffer to JPEG bytes for transport to AI service.
        /// </summary>
        private static byte[] EncodeRawFrameToJpeg(byte[] rawRgb, int width, int height)
        {
            using var image = Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Rgb24>(rawRgb, width, height);
            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 90 });
            return ms.ToArray();
        }

        /// <summary>
        /// Decode JPEG bytes from AI service back to raw RGB24 frame buffer.
        /// Returns null if decoding fails or dimensions do not match.
        /// </summary>
        private static byte[]? DecodeJpegToRawFrame(byte[] jpegBytes, int expectedWidth, int expectedHeight)
        {
            try
            {
                using var image = Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(jpegBytes);
                if (image.Width != expectedWidth || image.Height != expectedHeight)
                {
                    // Resize to expected dimensions if they don't match
                    image.Mutate(x => x.Resize(expectedWidth, expectedHeight));
                }

                var rawBytes = new byte[expectedWidth * expectedHeight * 3];
                image.CopyPixelDataTo(rawBytes);
                return rawBytes;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DecodeJpegToRawFrame failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extract frames from video
        /// </summary>
        private async Task ExtractFramesAsync(string inputPath, string framesDir, double frameRate, CancellationToken cancellationToken, bool isInterlaced = false, bool isHDR = false)
        {
            // Use provided frame rate or default to 30 if invalid
            var effectiveFps = frameRate > 0 ? frameRate : 30;

            // Build video filter chain: deinterlace first (if needed), then fps extraction
            var vfFilters = new List<string>();
            if (isInterlaced)
            {
                // bwdif provides better quality than yadif for deinterlacing
                // send_frame: output one frame per field pair (preserves frame count)
                // parity=auto: auto-detect field parity
                // deint=all: deinterlace all frames regardless of flagging
                vfFilters.Add("bwdif=mode=send_frame:parity=auto:deint=all");
                _logger.LogInformation("Applying bwdif deinterlacing filter during frame extraction for {File}", Path.GetFileName(inputPath));
            }
            vfFilters.Add($"fps={effectiveFps}");

            var vfArg = string.Join(",", vfFilters);

            // For HDR content, extract as 16-bit PNG to preserve dynamic range
            var pixFmtArg = isHDR ? " -pix_fmt rgb48be" : "";
            var args = $"-i \"{inputPath}\" -vf \"{vfArg}\"{pixFmtArg} \"{framesDir}/frame_%06d.png\"";

            if (isHDR)
            {
                _logger.LogInformation("Extracting frames as 16-bit PNG for HDR content: {File}", Path.GetFileName(inputPath));
            }

            var result = await Cli.Wrap(_ffmpegPath)
                .WithArguments(args)
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync(cancellationToken);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"Frame extraction failed with exit code {result.ExitCode}");
            }
        }

        /// <summary>
        /// Process frames with AI upscaling
        /// </summary>
        private async Task ProcessFramesAsync(
            string framesDir,
            string processedDir,
            VideoProcessingOptions options,
            string processingJobId,
            CancellationToken cancellationToken,
            bool isHDR = false)
        {
            var frameFiles = Directory.GetFiles(framesDir, "*.png").OrderBy(f => f).ToArray();
            int totalFrames = frameFiles.Length;
            
            // Use a semaphore to limit concurrency based on hardware capabilities
            // Default to 1 for safety if not specified
            int maxConcurrency = 1;
            try 
            {
                var profile = await _upscalerCore.DetectHardwareAsync();
                maxConcurrency = Math.Max(1, profile.MaxConcurrentStreams);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Hardware detection failed, using default concurrency"); }

            using var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = new List<Task>();

            _logger.LogInformation("Processing {TotalFrames} frames with max concurrency: {MaxConcurrency}", totalFrames, maxConcurrency);

            var processedFrames = 0;
            var startTime = DateTime.UtcNow;
            long lastProgressTicks = DateTime.UtcNow.Ticks;

            for (int i = 0; i < totalFrames; i++)
            {
                int index = i;
                string frameFile = frameFiles[i];

                await semaphore.WaitAsync(cancellationToken);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        // Check for pause before processing
                        while (_pausedJobs.GetValueOrDefault(processingJobId, false))
                        {
                            await Task.Delay(500, cancellationToken);
                        }
                        
                        var frameData = await File.ReadAllBytesAsync(frameFile, cancellationToken);
                        byte[]? upscaledData;
                        if (isHDR)
                        {
                            upscaledData = await UpscaleHDRFrameAsync(frameData, options.ScaleFactor, cancellationToken);
                        }
                        else
                        {
                            upscaledData = await _upscalerCore.UpscaleImageAsync(frameData, options.Model, options.ScaleFactor);
                        }

                        var outputFile = Path.Combine(processedDir, Path.GetFileName(frameFile));
                        if (upscaledData != null && upscaledData.Length > 0)
                        {
                            await File.WriteAllBytesAsync(outputFile, upscaledData, cancellationToken);
                        }
                        else
                        {
                            _logger.LogWarning("AI service returned null for frame {Frame}, using original", Path.GetFileName(frameFile));
                            File.Copy(frameFile, outputFile, true);
                        }
                        
                        Interlocked.Increment(ref processedFrames);
                        
                        var nowTicks = DateTime.UtcNow.Ticks;
                        var prevTicks = Interlocked.Read(ref lastProgressTicks);
                        if ((nowTicks - prevTicks) >= TimeSpan.TicksPerSecond * 2 || index == totalFrames - 1)
                        {
                            if (Interlocked.CompareExchange(ref lastProgressTicks, nowTicks, prevTicks) == prevTicks)
                            {
                                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                                var fps = elapsed > 0 ? processedFrames / elapsed : 0;

                                await _progressHub.SendFrameProgress(
                                    processingJobId,
                                    Path.GetFileName(frameFile),
                                    processedFrames,
                                    totalFrames,
                                    fps
                                );

                                _logger.LogInformation("Processed {ProcessedFrames}/{TotalFrames} frames ({Fps} FPS)", processedFrames, totalFrames, fps.ToString("F1"));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to upscale frame {Frame}, using original", frameFile);
                        var outputFile = Path.Combine(processedDir, Path.GetFileName(frameFile));
                        File.Copy(frameFile, outputFile, true);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Upscale a single HDR frame via the /upscale-hdr endpoint on the AI service
        /// </summary>
        private async Task<byte[]?> UpscaleHDRFrameAsync(byte[] frameData, int scale, CancellationToken cancellationToken)
        {
            if (scale < 1 || scale > 8)
            {
                _logger.LogWarning("Invalid scale factor {Scale} for HDR upscaling, using default 2", scale);
                scale = 2;
            }

            var config = Plugin.Instance?.Configuration;
            var baseUrl = config?.AiServiceUrl ?? "http://localhost:5000";
            var client = _httpClientFactory.CreateClient("UpscalerHDR");
            client.Timeout = TimeSpan.FromMinutes(5);

            using var content = new MultipartFormDataContent();
            using var imageContent = new ByteArrayContent(frameData);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(imageContent, "file", "frame.png");
            content.Add(new StringContent(scale.ToString()), "scale");

            _logger.LogDebug("Sending HDR frame ({Size} bytes) to AI service for {Scale}x upscaling", frameData.Length, scale);

            using var response = await client.PostAsync($"{baseUrl}/upscale-hdr", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }

            _logger.LogWarning("HDR upscale failed with status {Status}", response.StatusCode);
            return null;
        }

        /// <summary>
        /// Reconstruct video from processed frames
        /// </summary>
        private async Task ReconstructVideoAsync(
            string processedDir,
            string originalPath,
            string outputPath,
            VideoProcessingOptions options,
            double frameRate,
            CancellationToken cancellationToken,
            VideoInfo? inputInfo = null)
        {
            var tempAudioPath = Path.Combine(Path.GetTempPath(), $"temp_audio_{Guid.NewGuid()}.mka");
            var hasAudio = false;
            var effectiveFps = frameRate > 0 ? frameRate : 30.0;
            var isHDR = inputInfo?.IsHDR ?? false;

            try
            {
                // Try to extract audio from original video (use .mka container to support any codec)
                var audioArgs = $"-i \"{originalPath}\" -vn -acodec copy -y \"{tempAudioPath}\"";
                var audioResult = await Cli.Wrap(_ffmpegPath)
                    .WithArguments(audioArgs)
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync(cancellationToken);

                hasAudio = audioResult.ExitCode == 0 && File.Exists(tempAudioPath) && new FileInfo(tempAudioPath).Length > 0;

                if (!hasAudio)
                {
                    _logger.LogInformation("No audio track found in source video");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract audio, continuing without audio");
                hasAudio = false;
            }

            // Reconstruct video with or without audio
            var outputCodec = Config.OutputCodec ?? "libx264";
            // Allowlist codecs to prevent FFmpeg argument injection
            var allowedCodecs = new HashSet<string> { "libx264", "libx265", "hevc_nvenc", "h264_nvenc", "h264_qsv", "hevc_qsv", "copy" };
            if (!allowedCodecs.Contains(outputCodec))
            {
                _logger.LogWarning("Invalid output codec '{Codec}' in ReconstructVideoAsync, falling back to libx264", outputCodec);
                outputCodec = "libx264";
            }

            string codecArgs;
            if (outputCodec == "copy")
            {
                codecArgs = "-c:v copy";
            }
            else if (isHDR)
            {
                // HDR: use 10-bit pixel format and preserve BT.2020/PQ color metadata
                codecArgs = $"-c:v {outputCodec} -pix_fmt yuv420p10le -colorspace bt2020nc -color_primaries bt2020 -color_trc smpte2084";
                _logger.LogInformation("Using HDR output settings: 10-bit yuv420p10le with BT.2020/PQ metadata");
            }
            else
            {
                codecArgs = $"-c:v {outputCodec} -pix_fmt yuv420p";
            }

            string reconstructArgs;
            if (hasAudio && File.Exists(tempAudioPath))
            {
                reconstructArgs = $"-framerate {effectiveFps} -i \"{processedDir}/frame_%06d.png\" -i \"{tempAudioPath}\" {codecArgs} -r {effectiveFps} -c:a copy -y \"{outputPath}\"";
            }
            else
            {
                reconstructArgs = $"-framerate {effectiveFps} -i \"{processedDir}/frame_%06d.png\" {codecArgs} -r {effectiveFps} -y \"{outputPath}\"";
            }
            
            var result = await Cli.Wrap(_ffmpegPath)
                .WithArguments(reconstructArgs)
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync(cancellationToken);
            
            // Cleanup temp audio file
            try
            {
                if (File.Exists(tempAudioPath))
                {
                    File.Delete(tempAudioPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to cleanup temp audio file");
            }
            
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"Video reconstruction failed with exit code {result.ExitCode}");
            }
        }

        /// <summary>
        /// Build FFmpeg command for processing
        /// </summary>
        private string BuildFFmpegCommand(
            string inputPath,
            string outputPath,
            VideoProcessingOptions options,
            HardwareProfile hardwareProfile)
        {
            _logger.LogInformation("Building FFmpeg command for hardware acceleration...");
            var args = new List<string>();
            
            // Hardware acceleration
            if (options.HardwareAcceleration == "cuda" && hardwareProfile.SupportsCUDA)
            {
                args.Add("-hwaccel cuda");
                args.Add("-hwaccel_output_format cuda");
            }
            else if (options.HardwareAcceleration == "vaapi" && hardwareProfile.AvailableHwAccels.Contains("vaapi"))
            {
                args.Add("-hwaccel vaapi");
                args.Add("-hwaccel_output_format vaapi");
                args.Add("-vaapi_device /dev/dri/renderD128");
            }
            else if (options.HardwareAcceleration == "qsv" && hardwareProfile.AvailableHwAccels.Contains("qsv"))
            {
                args.Add("-hwaccel qsv");
                args.Add("-hwaccel_output_format qsv");
            }
            
            // Input
            args.Add($"-i \"{inputPath}\"");
            
            // Video filters
            var filters = new List<string>();
            
            // Advanced AI Upscaling Filter Selection (v1.4.2)
            if (options.ScaleFactor > 1)
            {
                var useAdvancedUpscaling = options.QualityLevel == "high" || options.EnableAIUpscaling;
                
                if (options.HardwareAcceleration == "cuda")
                {
                    // NVIDIA RTX: Use Video Super Resolution (VSR) or CUDA scaling
                    if (useAdvancedUpscaling && hardwareProfile.GpuName?.Contains("RTX") == true)
                    {
                        _logger.LogInformation("Using NVIDIA VSR (Video Super Resolution)");
                        filters.Add($"hwupload_cuda");
                        filters.Add($"scale_cuda={options.ScaleFactor}*iw:{options.ScaleFactor}*ih:interp_algo=lanczos");
                        filters.Add($"unsharp_cuda=luma_amount=1.5:chroma_amount=0.5"); // AI-enhanced sharpening
                        filters.Add($"hwdownload");
                        filters.Add($"format=nv12");
                    }
                    else
                    {
                        filters.Add($"scale_cuda={options.ScaleFactor}*iw:{options.ScaleFactor}*ih");
                    }
                }
                else if (options.HardwareAcceleration == "vaapi")
                {
                    // AMD/Intel: Use FSR-like upscaling with VAAPI
                    if (useAdvancedUpscaling)
                    {
                        _logger.LogInformation("Using AMD FSR-style upscaling");
                        filters.Add($"hwupload");
                        filters.Add($"scale_vaapi=w={options.ScaleFactor}*iw:h={options.ScaleFactor}*ih");
                        filters.Add($"sharpen_vaapi"); // FSR-like sharpening
                        filters.Add($"hwdownload");
                        filters.Add($"format=nv12");
                    }
                    else
                    {
                        filters.Add($"scale_vaapi={options.ScaleFactor}*iw:{options.ScaleFactor}*ih");
                    }
                }
                else if (options.HardwareAcceleration == "qsv")
                {
                    filters.Add($"scale_qsv={options.ScaleFactor}*iw:{options.ScaleFactor}*ih");
                }
                else
                {
                    // Software: Use Anime4K or FSR via libplacebo if available
                    if (useAdvancedUpscaling && options.Model?.Contains("anime") == true)
                    {
                        _logger.LogInformation("Using Anime4K-style shader upscaling");
                        filters.Add($"libplacebo=w={options.ScaleFactor}*iw:h={options.ScaleFactor}*ih:upscaler=ewa_lanczos:downscaler=ewa_lanczos");
                    }
                    else if (useAdvancedUpscaling)
                    {
                        _logger.LogInformation("Using FSR (FidelityFX Super Resolution)");
                        filters.Add($"libplacebo=w={options.ScaleFactor}*iw:h={options.ScaleFactor}*ih:upscaler=ewa_lanczos");
                    }
                    else
                    {
                        filters.Add($"scale={options.ScaleFactor}*iw:{options.ScaleFactor}*ih:flags=lanczos");
                    }
                }
            }
            
            // Quality enhancement filters
            if (options.QualityLevel == "high")
            {
                if (options.HardwareAcceleration == "vaapi")
                {
                    filters.Add("sharpen_vaapi");
                }
                else if (options.HardwareAcceleration != "cuda") // CUDA already has unsharp_cuda above
                {
                    filters.Add("unsharp=5:5:1.0:5:5:0.0");
                }
            }
            
            if (filters.Count > 0)
            {
                args.Add($"-vf \"{string.Join(",", filters)}\"");
            }
            
            // Output encoding
            if (options.HardwareAcceleration == "cuda")
            {
                args.Add("-c:v h264_nvenc -preset p4 -tune hq -b:v 5M");
            }
            else if (options.HardwareAcceleration == "vaapi")
            {
                args.Add("-c:v h264_vaapi -qp 20");
            }
            else if (options.HardwareAcceleration == "qsv")
            {
                args.Add("-c:v h264_qsv -preset slow -b:v 5M");
            }
            else
            {
                var outputCodec = Config.OutputCodec ?? "libx264";
                // Allowlist codecs to prevent FFmpeg argument injection
                var allowedCodecs = new HashSet<string> { "libx264", "libx265", "hevc_nvenc", "h264_nvenc", "h264_qsv", "hevc_qsv", "copy" };
                if (!allowedCodecs.Contains(outputCodec))
                {
                    _logger.LogWarning("Invalid output codec '{Codec}', falling back to libx264", outputCodec);
                    outputCodec = "libx264";
                }
                if (outputCodec == "copy")
                {
                    args.Add("-c:v copy");
                }
                else
                {
                    args.Add($"-c:v {outputCodec} -preset medium -crf 23");
                }
            }

            // Audio
            args.Add("-c:a copy");
            
            // Output
            args.Add($"-y \"{outputPath}\"");
            
            var fullCommand = string.Join(" ", args);
            _logger.LogDebug("Generated FFmpeg Command: ffmpeg {Command}", fullCommand);
            
            return fullCommand;
        }

        /// <summary>
        /// Estimate video quality
        /// </summary>
        private VideoQuality EstimateVideoQuality(VideoInfo info)
        {
            var pixelRate = (double)info.Width * info.Height * info.FrameRate;
            if (pixelRate <= 0) return VideoQuality.VeryLow;
            var bitRatePerPixel = info.BitRate / pixelRate;

            return bitRatePerPixel switch
            {
                > 0.1 => VideoQuality.High,
                > 0.05 => VideoQuality.Medium,
                > 0.02 => VideoQuality.Low,
                _ => VideoQuality.VeryLow
            };
        }

        /// <summary>
        /// Check if video is HDR using pixel format, color transfer, color primaries, and bit depth
        /// </summary>
        private bool IsHDRVideo(FFMpegCore.VideoStream? videoStream, VideoInfo info)
        {
            if (videoStream == null) return false;

            // Check pixel format for HDR indicators
            bool pixelFormatHDR = videoStream.PixelFormat?.Contains("bt2020") == true ||
                                  videoStream.PixelFormat?.Contains("smpte2084") == true ||
                                  videoStream.PixelFormat?.Contains("p010") == true;

            // Check color transfer for PQ (HDR10/HDR10+/Dolby Vision) or HLG
            bool transferHDR = string.Equals(info.ColorTransfer, "smpte2084", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(info.ColorTransfer, "arib-std-b67", StringComparison.OrdinalIgnoreCase);

            // Check color primaries for BT.2020 wide color gamut
            bool primariesBT2020 = string.Equals(info.ColorPrimaries, "bt2020", StringComparison.OrdinalIgnoreCase);

            // Check bit depth > 8
            bool highBitDepth = info.BitDepth > 8;

            return pixelFormatHDR || transferHDR || (primariesBT2020 && highBitDepth);
        }

        /// <summary>
        /// Detect HDR properties (color_transfer, color_primaries, bit_depth) via raw FFprobe JSON output
        /// </summary>
        private async Task DetectHDRPropertiesAsync(string inputPath, VideoInfo info)
        {
            try
            {
                var stdoutBuffer = new System.Text.StringBuilder();
                var result = await Cli.Wrap(_ffprobePath)
                    .WithArguments($"-v quiet -select_streams v:0 -show_streams -print_format json \"{inputPath}\"")
                    .WithValidation(CommandResultValidation.None)
                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdoutBuffer))
                    .ExecuteAsync();

                if (result.ExitCode != 0)
                {
                    _logger.LogDebug("FFprobe HDR detection returned non-zero exit code for {File}", Path.GetFileName(inputPath));
                    return;
                }

                var json = stdoutBuffer.ToString();
                if (string.IsNullOrWhiteSpace(json)) return;

                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("streams", out var streams)) return;
                if (streams.GetArrayLength() == 0) return;

                var stream = streams[0];

                if (stream.TryGetProperty("color_transfer", out var ct))
                {
                    info.ColorTransfer = ct.GetString() ?? "";
                }

                if (stream.TryGetProperty("color_primaries", out var cp))
                {
                    info.ColorPrimaries = cp.GetString() ?? "";
                }

                if (stream.TryGetProperty("bits_per_raw_sample", out var bprs))
                {
                    var bprsStr = bprs.GetString() ?? "";
                    if (int.TryParse(bprsStr, out var bitDepth) && bitDepth > 0)
                    {
                        info.BitDepth = bitDepth;
                    }
                }

                // Fallback: infer bit depth from pixel format name
                if (info.BitDepth == 8 && !string.IsNullOrEmpty(info.PixelFormat))
                {
                    var pf = info.PixelFormat.ToLowerInvariant();
                    if (pf.Contains("p010") || pf.Contains("10le") || pf.Contains("10be"))
                        info.BitDepth = 10;
                    else if (pf.Contains("p012") || pf.Contains("12le") || pf.Contains("12be"))
                        info.BitDepth = 12;
                }

                _logger.LogDebug("HDR properties: ColorTransfer={Transfer}, ColorPrimaries={Primaries}, BitDepth={BitDepth}",
                    info.ColorTransfer, info.ColorPrimaries, info.BitDepth);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to detect HDR properties for {File}", Path.GetFileName(inputPath));
            }
        }

        /// <summary>
        /// Detect whether the video is interlaced using FFprobe field_order and stream info
        /// </summary>
        private async Task<bool> DetectInterlacedAsync(string inputPath)
        {
            try
            {
                var stdOutBuffer = new StringBuilder();
                var result = await Cli.Wrap(_ffprobePath)
                    .WithArguments($"-v quiet -select_streams v:0 -show_entries stream=field_order -of json \"{inputPath}\"")
                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOutBuffer))
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync();

                if (result.ExitCode == 0)
                {
                    var output = stdOutBuffer.ToString();

                    // Check field_order values that indicate interlaced content
                    // tt = top field first, bb = bottom field first, tb/bt = mixed field order
                    if (output.Contains("\"tt\"") || output.Contains("\"bb\"") ||
                        output.Contains("\"tb\"") || output.Contains("\"bt\""))
                    {
                        _logger.LogInformation("Interlaced content detected via field_order for {File}", Path.GetFileName(inputPath));
                        return true;
                    }

                    // "progressive" or "unknown" means not interlaced
                    if (output.Contains("\"progressive\""))
                    {
                        return false;
                    }
                }

                // Fallback: check full stream info for interlaced indicators
                var stdOutBuffer2 = new StringBuilder();
                var result2 = await Cli.Wrap(_ffprobePath)
                    .WithArguments($"-v quiet -select_streams v:0 -show_streams -of json \"{inputPath}\"")
                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOutBuffer2))
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync();

                if (result2.ExitCode == 0)
                {
                    var streamJson = stdOutBuffer2.ToString();
                    using var doc = JsonDocument.Parse(streamJson);
                    if (doc.RootElement.TryGetProperty("streams", out var streams) && streams.GetArrayLength() > 0)
                    {
                        var stream = streams[0];
                        // Check field_order value specifically (not substring match on entire JSON)
                        if (stream.TryGetProperty("field_order", out var fieldOrder))
                        {
                            var fo = fieldOrder.GetString()?.ToLowerInvariant() ?? "";
                            if (fo is "tt" or "bb" or "tb" or "bt")
                            {
                                _logger.LogInformation("Interlaced content detected via stream field_order for {File}", Path.GetFileName(inputPath));
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Interlace detection failed for {File}, assuming progressive", Path.GetFileName(inputPath));
                return false;
            }
        }

        /// <summary>
        /// Update performance history
        /// </summary>
        private void UpdatePerformanceHistory(ProcessingJob job)
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
        /// Update statistics timer callback
        /// </summary>
        private void UpdateStatistics(object? state)
        {
            try
            {
                var activeJobs = _activeJobs.Count;
                var completedJobs = _performanceHistory.Count(m => m.Value.Success);
                var failedJobs = _performanceHistory.Count(m => !m.Value.Success);
                
                _logger.LogDebug("Stats: {ActiveJobs} active, {CompletedJobs} completed, {FailedJobs} failed", activeJobs, completedJobs, failedJobs);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Statistics update failed");
            }
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            _statisticsTimer?.Dispose();

            // Cancel all active jobs and give them time to wind down
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