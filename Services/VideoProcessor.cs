using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using CliWrap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
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
        private readonly PluginConfiguration _config;
        
        // Processing queue for concurrent streams
        private readonly SemaphoreSlim _processingSemaphore;
        private readonly Dictionary<string, ProcessingJob> _activeJobs = new();
        
        // Performance monitoring
        private readonly Dictionary<string, VideoProcessingMetrics> _performanceHistory = new();
        private readonly Timer _statisticsTimer;
        
        // FFmpeg configuration
        private string _ffmpegPath;
        private string _ffprobePath;
        
        public VideoProcessor(
            ILogger<VideoProcessor> logger,
            IMediaEncoder mediaEncoder,
            UpscalerCore upscalerCore,
            PluginConfiguration config)
        {
            _logger = logger;
            _mediaEncoder = mediaEncoder;
            _upscalerCore = upscalerCore;
            _config = config;
            
            // Limit concurrent processing based on hardware
            _processingSemaphore = new SemaphoreSlim(_config.MaxConcurrentStreams);
            
            // Initialize FFmpeg
            InitializeFFmpeg();
            
            // Initialize statistics timer
            if (_config.EnablePerformanceMetrics)
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
                    BinaryFolder = Path.GetDirectoryName(_ffmpegPath),
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
            
            try
            {
                await _processingSemaphore.WaitAsync(cancellationToken);
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
                
                // 4. Choose processing method
                var processingMethod = DetermineProcessingMethod(inputInfo, hardwareProfile, optimizedOptions);
                job.ProcessingMethod = processingMethod;
                
                // 5. Execute processing
                var result = await ExecuteProcessingAsync(inputPath, outputPath, job, cancellationToken);
                
                job.Status = result.Success ? ProcessingStatus.Completed : ProcessingStatus.Failed;
                job.EndTime = DateTime.Now;
                job.Result = result;
                
                // 6. Update performance history
                UpdatePerformanceHistory(job);
                
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
                return new VideoProcessingResult { Success = false, Error = ex.Message };
            }
            finally
            {
                _processingSemaphore.Release();
                _activeJobs.Remove(jobId);
            }
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
                    ColorRange = videoStream.PixelFormat ?? "unknown",
                    FileSize = new FileInfo(inputPath).Length,
                    HasAudio = mediaInfo.AudioStreams.Any(),
                    HasSubtitles = mediaInfo.SubtitleStreams.Any()
                };
                
                // Enhanced analysis
                info.EstimatedQuality = EstimateVideoQuality(info);
                info.IsHDR = IsHDRVideo(videoStream);
                info.AspectRatio = (double)info.Width / info.Height;
                
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
            if (optimized.Scale == 0)
            {
                optimized.Scale = inputInfo.Width <= 720 ? 3 : 2;
            }
            
            // Adjust quality based on hardware capabilities
            if (hardwareProfile.SupportsCUDA && hardwareProfile.VramMB > 8192)
            {
                optimized.Quality = "high";
            }
            else if (hardwareProfile.SupportsDirectML)
            {
                optimized.Quality = "medium";
            }
            else
            {
                optimized.Quality = "fast";
            }
            
            // Enable hardware acceleration if available
            if (hardwareProfile.SupportsCUDA)
            {
                optimized.HardwareAcceleration = "cuda";
            }
            else if (hardwareProfile.SupportsDirectML)
            {
                optimized.HardwareAcceleration = "directml";
            }
            else if (hardwareProfile.AvailableHwAccels.Contains("qsv"))
            {
                optimized.HardwareAcceleration = "qsv";
            }
            
            _logger.LogInformation($"🎯 Optimized options: {optimized.Model} @ {optimized.Scale}x, {optimized.Quality} quality, {optimized.HardwareAcceleration} accel");
            
            return optimized;
        }

        /// <summary>
        /// Determine the best processing method
        /// </summary>
        private ProcessingMethod DetermineProcessingMethod(
            VideoInfo inputInfo,
            HardwareProfile hardwareProfile,
            VideoProcessingOptions options)
        {
            // Real-time processing for short videos or live streams
            if (inputInfo.Duration.TotalMinutes < 5 || options.EnableRealTimeProcessing)
            {
                return ProcessingMethod.RealTime;
            }
            
            // Frame-by-frame for high quality
            if (options.Quality == "high" && hardwareProfile.SupportsCUDA)
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
            CancellationToken cancellationToken)
        {
            return job.ProcessingMethod switch
            {
                ProcessingMethod.RealTime => await ProcessRealTimeAsync(inputPath, outputPath, job, cancellationToken),
                ProcessingMethod.FrameByFrame => await ProcessFrameByFrameAsync(inputPath, outputPath, job, cancellationToken),
                ProcessingMethod.Batch => await ProcessBatchAsync(inputPath, outputPath, job, cancellationToken),
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
                
                return new VideoProcessingResult
                {
                    Success = success,
                    OutputPath = outputPath,
                    ProcessingTime = DateTime.Now - job.StartTime,
                    Method = ProcessingMethod.RealTime,
                    Error = success ? null : $"FFmpeg exited with code {result.ExitCode}"
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
                
                try
                {
                    // 1. Extract frames
                    var framesDir = Path.Combine(tempDir, "frames");
                    Directory.CreateDirectory(framesDir);
                    
                    await ExtractFramesAsync(inputPath, framesDir, cancellationToken);
                    
                    // 2. Process frames with AI
                    var processedDir = Path.Combine(tempDir, "processed");
                    Directory.CreateDirectory(processedDir);
                    
                    await ProcessFramesAsync(framesDir, processedDir, job.OptimizedOptions, cancellationToken);
                    
                    // 3. Reconstruct video
                    await ReconstructVideoAsync(processedDir, inputPath, outputPath, job.OptimizedOptions, cancellationToken);
                    
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
                _logger.LogInformation("📦 Starting batch processing");
                
                // Use FFmpeg with scale filter for now
                // TODO: Implement batch AI processing
                var args = BuildFFmpegCommand(inputPath, outputPath, job.OptimizedOptions, job.HardwareProfile);
                
                var result = await Cli.Wrap(_ffmpegPath)
                    .WithArguments(args)
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync(cancellationToken);
                
                var success = result.ExitCode == 0;
                
                return new VideoProcessingResult
                {
                    Success = success,
                    OutputPath = outputPath,
                    ProcessingTime = DateTime.Now - job.StartTime,
                    Method = ProcessingMethod.Batch,
                    Error = success ? null : $"FFmpeg exited with code {result.ExitCode}"
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
        private async Task ExtractFramesAsync(string inputPath, string framesDir, CancellationToken cancellationToken)
        {
            var args = $"-i \"{inputPath}\" -vf fps=30 \"{framesDir}/frame_%06d.png\"";
            
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
            CancellationToken cancellationToken)
        {
            var frameFiles = Directory.GetFiles(framesDir, "*.png").OrderBy(f => f).ToArray();
            
            var processingTasks = frameFiles.Select(async (frameFile, index) =>
            {
                try
                {
                    var frameData = await File.ReadAllBytesAsync(frameFile, cancellationToken);
                    var upscaledData = await _upscalerCore.UpscaleImageAsync(frameData, options.Model, options.Scale);
                    
                    var outputFile = Path.Combine(processedDir, Path.GetFileName(frameFile));
                    await File.WriteAllBytesAsync(outputFile, upscaledData, cancellationToken);
                    
                    if (index % 100 == 0)
                    {
                        _logger.LogInformation($"📸 Processed {index}/{frameFiles.Length} frames");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"⚠️ Failed to process frame {frameFile}");
                }
            });
            
            await Task.WhenAll(processingTasks);
        }

        /// <summary>
        /// Reconstruct video from processed frames
        /// </summary>
        private async Task ReconstructVideoAsync(
            string processedDir,
            string originalPath,
            string outputPath,
            VideoProcessingOptions options,
            CancellationToken cancellationToken)
        {
            // Get audio from original video
            var audioArgs = $"-i \"{originalPath}\" -vn -acodec copy -y \"{Path.Combine(Path.GetTempPath(), "temp_audio.aac")}\"";
            await Cli.Wrap(_ffmpegPath).WithArguments(audioArgs).ExecuteAsync(cancellationToken);
            
            // Reconstruct video with audio
            var reconstructArgs = $"-framerate 30 -i \"{processedDir}/frame_%06d.png\" -i \"{Path.Combine(Path.GetTempPath(), "temp_audio.aac")}\" -c:v libx264 -c:a copy -pix_fmt yuv420p -y \"{outputPath}\"";
            
            var result = await Cli.Wrap(_ffmpegPath)
                .WithArguments(reconstructArgs)
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync(cancellationToken);
            
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
            var args = new List<string>();
            
            // Hardware acceleration
            if (options.HardwareAcceleration == "cuda" && hardwareProfile.SupportsCUDA)
            {
                args.Add("-hwaccel cuda");
                args.Add("-hwaccel_output_format cuda");
            }
            else if (options.HardwareAcceleration == "qsv" && hardwareProfile.AvailableHwAccels.Contains("qsv"))
            {
                args.Add("-hwaccel qsv");
            }
            
            // Input
            args.Add($"-i \"{inputPath}\"");
            
            // Video filters
            var filters = new List<string>();
            
            // Scaling filter
            if (options.Scale > 1)
            {
                if (options.HardwareAcceleration == "cuda")
                {
                    filters.Add($"scale_cuda={options.Scale}*iw:{options.Scale}*ih");
                }
                else
                {
                    filters.Add($"scale={options.Scale}*iw:{options.Scale}*ih:lanczos");
                }
            }
            
            // Quality filters
            if (options.Quality == "high")
            {
                filters.Add("unsharp=5:5:1.0:5:5:0.0");
            }
            
            if (filters.Count > 0)
            {
                args.Add($"-vf \"{string.Join(",", filters)}\"");
            }
            
            // Output encoding
            if (options.HardwareAcceleration == "cuda")
            {
                args.Add("-c:v h264_nvenc");
            }
            else if (options.HardwareAcceleration == "qsv")
            {
                args.Add("-c:v h264_qsv");
            }
            else
            {
                args.Add("-c:v libx264");
            }
            
            // Audio
            args.Add("-c:a copy");
            
            // Output
            args.Add($"-y \"{outputPath}\"");
            
            return string.Join(" ", args);
        }

        /// <summary>
        /// Estimate video quality
        /// </summary>
        private VideoQuality EstimateVideoQuality(VideoInfo info)
        {
            var bitRatePerPixel = info.BitRate / (info.Width * info.Height * info.FrameRate);
            
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
            var metrics = new VideoProcessingMetrics
            {
                JobId = job.Id,
                ProcessingTime = job.ProcessingDuration,
                InputResolution = $"{job.InputInfo.Width}x{job.InputInfo.Height}",
                OutputResolution = $"{job.InputInfo.Width * job.OptimizedOptions.Scale}x{job.InputInfo.Height * job.OptimizedOptions.Scale}",
                Model = job.OptimizedOptions.Model,
                Scale = job.OptimizedOptions.Scale,
                Method = job.ProcessingMethod,
                Success = job.Result?.Success ?? false,
                Timestamp = DateTime.Now
            };
            
            _performanceHistory[job.Id] = metrics;
            
            // Keep only last 100 entries
            if (_performanceHistory.Count > 100)
            {
                var oldestKey = _performanceHistory.Keys.OrderBy(k => _performanceHistory[k].Timestamp).First();
                _performanceHistory.Remove(oldestKey);
            }
        }

        /// <summary>
        /// Update statistics timer callback
        /// </summary>
        private void UpdateStatistics(object state)
        {
            try
            {
                var activeJobs = _activeJobs.Count;
                var completedJobs = _performanceHistory.Count(m => m.Value.Success);
                var failedJobs = _performanceHistory.Count(m => !m.Value.Success);
                
                _logger.LogInformation($"📊 Stats: {activeJobs} active, {completedJobs} completed, {failedJobs} failed");
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
            _processingSemaphore?.Dispose();
            _statisticsTimer?.Dispose();
        }
    }

    /// <summary>
    /// Video processing options
    /// </summary>
    public class VideoProcessingOptions
    {
        public string Model { get; set; } = "auto";
        public int Scale { get; set; } = 2;
        public string Quality { get; set; } = "medium";
        public string HardwareAcceleration { get; set; } = "auto";
        public bool EnableRealTimeProcessing { get; set; } = false;
        public bool PreserveAudio { get; set; } = true;
        public bool PreserveSubtitles { get; set; } = true;
        
        public VideoProcessingOptions() { }
        
        public VideoProcessingOptions(VideoProcessingOptions other)
        {
            Model = other.Model;
            Scale = other.Scale;
            Quality = other.Quality;
            HardwareAcceleration = other.HardwareAcceleration;
            EnableRealTimeProcessing = other.EnableRealTimeProcessing;
            PreserveAudio = other.PreserveAudio;
            PreserveSubtitles = other.PreserveSubtitles;
        }
    }

    /// <summary>
    /// Video information
    /// </summary>
    public class VideoInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public double FrameRate { get; set; }
        public TimeSpan Duration { get; set; }
        public string Codec { get; set; } = "";
        public long BitRate { get; set; }
        public string PixelFormat { get; set; } = "";
        public string ColorSpace { get; set; } = "";
        public string ColorRange { get; set; } = "";
        public long FileSize { get; set; }
        public bool HasAudio { get; set; }
        public bool HasSubtitles { get; set; }
        public VideoQuality EstimatedQuality { get; set; }
        public bool IsHDR { get; set; }
        public double AspectRatio { get; set; }
    }

    /// <summary>
    /// Processing job information
    /// </summary>
    public class ProcessingJob
    {
        public string Id { get; set; } = "";
        public string InputPath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public VideoProcessingOptions Options { get; set; } = new();
        public VideoProcessingOptions OptimizedOptions { get; set; } = new();
        public VideoInfo InputInfo { get; set; } = new();
        public HardwareProfile HardwareProfile { get; set; } = new();
        public ProcessingMethod ProcessingMethod { get; set; }
        public ProcessingStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public VideoProcessingResult Result { get; set; } = new();
        public string Error { get; set; } = "";
        
        public TimeSpan ProcessingDuration => EndTime - StartTime;
    }

    /// <summary>
    /// Video processing result
    /// </summary>
    public class VideoProcessingResult
    {
        public bool Success { get; set; }
        public string OutputPath { get; set; } = "";
        public TimeSpan ProcessingTime { get; set; }
        public ProcessingMethod Method { get; set; }
        public string Error { get; set; } = "";
        public Dictionary<string, object> Metrics { get; set; } = new();
    }

    /// <summary>
    /// Video processing metrics
    /// </summary>
    public class VideoProcessingMetrics
    {
        public string JobId { get; set; } = "";
        public TimeSpan ProcessingTime { get; set; }
        public string InputResolution { get; set; } = "";
        public string OutputResolution { get; set; } = "";
        public string Model { get; set; } = "";
        public int Scale { get; set; }
        public ProcessingMethod Method { get; set; }
        public bool Success { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Processing method enumeration
    /// </summary>
    public enum ProcessingMethod
    {
        RealTime,
        FrameByFrame,
        Batch
    }

    /// <summary>
    /// Processing status enumeration
    /// </summary>
    public enum ProcessingStatus
    {
        Starting,
        Analyzing,
        Processing,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Video quality enumeration
    /// </summary>
    public enum VideoQuality
    {
        VeryLow,
        Low,
        Medium,
        High,
        VeryHigh
    }
}