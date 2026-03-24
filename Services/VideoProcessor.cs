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
        
        public VideoProcessor(
            ILogger<VideoProcessor> logger,
            IMediaEncoder mediaEncoder,
            UpscalerCore upscalerCore,
            HttpUpscalerService httpUpscalerService,
            UpscalerProgressHub progressHub,
            LibraryScanHelper libraryScanHelper)
        {
            _logger = logger;
            _mediaEncoder = mediaEncoder;
            _upscalerCore = upscalerCore;
            _httpUpscalerService = httpUpscalerService;
            _progressHub = progressHub;
            _libraryScanHelper = libraryScanHelper;
            
            // Limit concurrent processing based on hardware
            _processingSemaphore = new SemaphoreSlim(Config.MaxConcurrentStreams);
            
            // Initialize FFmpeg
            InitializeFFmpeg();
            
            // Initialize statistics timer
            if (Config.EnablePerformanceMetrics)
            {
                _statisticsTimer = new Timer(UpdateStatistics, null, 
                    TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
            }
            
            _logger.LogInformation("🎬 VideoProcessor initialized with FFmpeg integration");
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
                    _logger.LogWarning("⚠️ FFmpeg path not available from MediaEncoder");
                    return;
                }
                
                // Configure FFMpegCore
                GlobalFFOptions.Configure(new FFOptions
                {
                    BinaryFolder = Path.GetDirectoryName(_ffmpegPath) ?? string.Empty,
                    TemporaryFilesFolder = Path.GetTempPath()
                });
                
                _logger.LogInformation($"✅ FFmpeg configured: {_ffmpegPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize FFmpeg");
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
                StartTime = DateTime.Now,
                Status = ProcessingStatus.Starting
            };
            
            _activeJobs[jobId] = job;
            
            var semaphoreAcquired = false;
            try
            {
                await _processingSemaphore.WaitAsync(cancellationToken);
                semaphoreAcquired = true;
                _logger.LogInformation($"🚀 Starting video processing: {Path.GetFileName(inputPath)}");

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
                job.EndTime = DateTime.Now;
                job.Result = result;
                
                // 6. Update performance history
                UpdatePerformanceHistory(job);
                
                await _progressHub.SendJobCompleted(job.Id, Path.GetFileName(inputPath), result.Success, result.Error);

                // 7. Fire webhook notification (fire-and-forget)
                if (result.Success)
                {
                    _ = _upscalerCore.SendWebhookAsync("complete", Path.GetFileName(inputPath), true);
                }
                else
                {
                    _ = _upscalerCore.SendWebhookAsync("failure", Path.GetFileName(inputPath), false, result.Error);
                }

                // 8. Trigger library scan for new upscaled file
                if (result.Success && !string.IsNullOrEmpty(outputPath))
                {
                    await _libraryScanHelper.ScanUpscaledFile(inputPath, outputPath);
                }

                _logger.LogInformation($"✅ Video processing completed: {result.Success}, Time: {job.ProcessingDuration.TotalSeconds:F1}s");

                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation($"ℹ️ Video processing cancelled: {jobId}");
                job.Status = ProcessingStatus.Cancelled;
                return new VideoProcessingResult { Success = false, Error = "Processing cancelled" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Video processing failed: {jobId}");
                job.Status = ProcessingStatus.Failed;
                job.Error = ex.Message;

                // Fire webhook notification (fire-and-forget)
                _ = _upscalerCore.SendWebhookAsync("failure", Path.GetFileName(inputPath), false, ex.Message);

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
            _logger.LogInformation($"⏸️ Job {jobId} paused");
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
            _logger.LogInformation($"▶️ Job {jobId} resumed");
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
            _logger.LogInformation($"🛑 Job {jobId} cancelled");
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
                return 5.0;

            if (job.Status == ProcessingStatus.Processing && job.InputInfo != null && job.InputInfo.Duration.TotalSeconds > 0)
            {
                var elapsed = (DateTime.Now - job.StartTime).TotalSeconds;
                var estimatedTotal = job.InputInfo.Duration.TotalSeconds * 0.5; // rough: processing ~2x realtime
                if (estimatedTotal <= 0) estimatedTotal = 60;
                return Math.Min(95, (elapsed / estimatedTotal) * 100);
            }

            return 10;
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
                info.IsHDR = IsHDRVideo(videoStream);
                info.AspectRatio = info.Height > 0 ? (double)info.Width / info.Height : 0;
                
                _logger.LogInformation($"📊 Video analysis: {info.Width}x{info.Height} @ {info.FrameRate:F1}fps, {info.Codec}, {info.BitRate}kbps");
                
                return info;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Video analysis failed");
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
            
            _logger.LogInformation($"🎯 Optimized options: {optimized.Model} @ {optimized.ScaleFactor}x, {optimized.QualityLevel} quality, {optimized.HardwareAcceleration} accel");
            
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
                _logger.LogInformation("⚡ Starting real-time processing");
                
                var args = BuildFFmpegCommand(inputPath, outputPath, job.OptimizedOptions, job.HardwareProfile);
                
                var result = await Cli.Wrap(_ffmpegPath)
                    .WithArguments(args)
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync(cancellationToken);
                
                var success = result.ExitCode == 0;

                if (!success)
                {
                    _logger.LogError("❌ Real-time FFmpeg processing failed with exit code {ExitCode} for {InputPath}", result.ExitCode, inputPath);
                }

                return new VideoProcessingResult
                {
                    Success = success,
                    OutputPath = outputPath,
                    ProcessingTime = DateTime.Now - job.StartTime,
                    Method = ProcessingMethod.RealTime,
                    Error = success ? string.Empty : $"FFmpeg exited with code {result.ExitCode}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Real-time processing failed");
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
                _logger.LogInformation("🎬 Starting frame-by-frame processing");
                
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
                    await ExtractFramesAsync(inputPath, framesDir, framesFps, cancellationToken);
                    
                    // 2. Process frames with AI
                    var processedDir = Path.Combine(tempDir, "processed");
                    Directory.CreateDirectory(processedDir);
                    
                    await ProcessFramesAsync(framesDir, processedDir, job.OptimizedOptions, job.Id, cancellationToken);
                    
                    // 3. Reconstruct video
                    await ReconstructVideoAsync(processedDir, inputPath, outputPath, job.OptimizedOptions, framesFps, cancellationToken);
                    
                    return new VideoProcessingResult
                    {
                        Success = true,
                        OutputPath = outputPath,
                        ProcessingTime = DateTime.Now - job.StartTime,
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
                        _logger.LogWarning(ex, "⚠️ Failed to cleanup temp directory");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Frame-by-frame processing failed");
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
                var framesDir = Path.Combine(tempDir, "frames");
                var processedDir = Path.Combine(tempDir, "processed");
                Directory.CreateDirectory(framesDir);
                Directory.CreateDirectory(processedDir);

                // Extract frames (same as frame-by-frame)
                var effectiveFps = job.InputInfo?.FrameRate ?? 24;
                var extractArgs = $"-i \"{inputPath}\" -vf fps={effectiveFps} \"{framesDir}/frame_%06d.png\"";
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

                // Reuse a single HttpClient for the entire processing loop to avoid socket exhaustion
                using var multiFrameClient = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };

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

                        var response = await multiFrameClient.PostAsync($"{serviceUrl}/upscale-video-chunk", content, cancellationToken);

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
                await ReconstructVideoAsync(processedDir, inputPath, outputPath, job.OptimizedOptions, effectiveFps, cancellationToken);

                return new VideoProcessingResult
                {
                    Success = true,
                    OutputPath = outputPath,
                    ProcessingTime = DateTime.Now - job.StartTime,
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
                _logger.LogInformation("📦 Starting batch AI processing (redirecting to frame-by-frame pipeline)");
                
                // For v1.4.1, we use the stable frame-by-frame pipeline for batch processing
                // as it provides the most consistent AI quality and progress tracking.
                var result = await ProcessFrameByFrameAsync(inputPath, outputPath, job, cancellationToken);
                
                return new VideoProcessingResult
                {
                    Success = result.Success,
                    OutputPath = result.OutputPath,
                    ProcessingTime = DateTime.Now - job.StartTime,
                    Method = ProcessingMethod.Batch,
                    Error = result.Error
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Batch processing failed");
                return new VideoProcessingResult
                {
                    Success = false,
                    Error = ex.Message,
                    Method = ProcessingMethod.Batch
                };
            }
        }

        /// <summary>
        /// Extract frames from video
        /// </summary>
        private async Task ExtractFramesAsync(string inputPath, string framesDir, double frameRate, CancellationToken cancellationToken)
        {
            // Use provided frame rate or default to 30 if invalid
            var effectiveFps = frameRate > 0 ? frameRate : 30;
            var args = $"-i \"{inputPath}\" -vf fps={effectiveFps} \"{framesDir}/frame_%06d.png\"";
            
            var result = await Cli.Wrap(_ffmpegPath)
                .WithArguments(args)
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync(cancellationToken);
            
            if (result.ExitCode != 0)
            {
                throw new Exception($"Frame extraction failed with exit code {result.ExitCode}");
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
            CancellationToken cancellationToken)
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
            catch { }

            using var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = new List<Task>();

            _logger.LogInformation($"🚀 Processing {totalFrames} frames with max concurrency: {maxConcurrency}");

            var processedFrames = 0;
            var startTime = DateTime.Now;
            long lastProgressTicks = DateTime.Now.Ticks;

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
                        var upscaledData = await _upscalerCore.UpscaleImageAsync(frameData, options.Model, options.ScaleFactor);
                        
                        var outputFile = Path.Combine(processedDir, Path.GetFileName(frameFile));
                        await File.WriteAllBytesAsync(outputFile, upscaledData, cancellationToken);
                        
                        Interlocked.Increment(ref processedFrames);
                        
                        var nowTicks = DateTime.Now.Ticks;
                        var prevTicks = Interlocked.Read(ref lastProgressTicks);
                        if ((nowTicks - prevTicks) >= TimeSpan.TicksPerSecond * 2 || index == totalFrames - 1)
                        {
                            if (Interlocked.CompareExchange(ref lastProgressTicks, nowTicks, prevTicks) == prevTicks)
                            {
                                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                                var fps = elapsed > 0 ? processedFrames / elapsed : 0;

                                await _progressHub.SendFrameProgress(
                                    processingJobId,
                                    Path.GetFileName(frameFile),
                                    processedFrames,
                                    totalFrames,
                                    fps
                                );

                                _logger.LogInformation($"📸 Processed {processedFrames}/{totalFrames} frames ({fps:F1} FPS)");
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
        /// Reconstruct video from processed frames
        /// </summary>
        private async Task ReconstructVideoAsync(
            string processedDir,
            string originalPath,
            string outputPath,
            VideoProcessingOptions options,
            double frameRate,
            CancellationToken cancellationToken)
        {
            var tempAudioPath = Path.Combine(Path.GetTempPath(), $"temp_audio_{Guid.NewGuid()}.mka");
            var hasAudio = false;
            var effectiveFps = frameRate > 0 ? frameRate : 30.0;

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
                    _logger.LogInformation("ℹ️ No audio track found in source video");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to extract audio, continuing without audio");
                hasAudio = false;
            }
            
            // Reconstruct video with or without audio
            var outputCodec = Config.OutputCodec ?? "libx264";
            var codecArgs = outputCodec == "copy" ? "-c:v copy" : $"-c:v {outputCodec} -pix_fmt yuv420p";

            string reconstructArgs;
            if (hasAudio && File.Exists(tempAudioPath))
            {
                reconstructArgs = $"-framerate {effectiveFps} -i \"{processedDir}/frame_%06d.png\" -i \"{tempAudioPath}\" {codecArgs} -r {effectiveFps} -c:a aac -b:a 192k -y \"{outputPath}\"";
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
                throw new Exception($"Video reconstruction failed with exit code {result.ExitCode}");
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
            _logger.LogInformation("🛠️ Building FFmpeg command for hardware acceleration...");
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
                        _logger.LogInformation("🎯 Using NVIDIA VSR (Video Super Resolution)");
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
                        _logger.LogInformation("🎯 Using AMD FSR-style upscaling");
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
                        _logger.LogInformation("🎯 Using Anime4K-style shader upscaling");
                        filters.Add($"libplacebo=w={options.ScaleFactor}*iw:h={options.ScaleFactor}*ih:upscaler=ewa_lanczos:downscaler=ewa_lanczos");
                    }
                    else if (useAdvancedUpscaling)
                    {
                        _logger.LogInformation("🎯 Using FSR (FidelityFX Super Resolution)");
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
            _logger.LogDebug($"💻 Generated FFmpeg Command: ffmpeg {fullCommand}");
            
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
        /// Check if video is HDR
        /// </summary>
        private bool IsHDRVideo(FFMpegCore.VideoStream videoStream)
        {
            return videoStream.PixelFormat?.Contains("bt2020") == true ||
                   videoStream.PixelFormat?.Contains("smpte2084") == true ||
                   videoStream.PixelFormat?.Contains("p010") == true;
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
                Timestamp = DateTime.Now
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
                
                _logger.LogDebug($"📊 Stats: {activeJobs} active, {completedJobs} completed, {failedJobs} failed");
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
                catch { /* Ignore cancellation errors */ }
            }

            // Brief wait for tasks to observe cancellation
            Thread.Sleep(500);

            foreach (var kvp in _jobCancellationTokens)
            {
                try { kvp.Value?.Dispose(); }
                catch { /* Ignore disposal errors */ }
            }
            _jobCancellationTokens.Clear();
            _processingSemaphore?.Dispose();
        }
    }
}