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
        private readonly PluginConfiguration _config;
        
        // Processing queue for concurrent streams
        private readonly SemaphoreSlim _processingSemaphore;
        private readonly Dictionary<string, ProcessingJob> _activeJobs = new();
        
        // Performance monitoring
        private readonly Dictionary<string, VideoProcessingMetrics> _performanceHistory = new();
        private readonly Timer? _statisticsTimer;
        
        // FFmpeg configuration
        private string _ffmpegPath = string.Empty;
        private string _ffprobePath = string.Empty;
        
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
            if (optimized.ScaleFactor == 0)
            {
                optimized.ScaleFactor = inputInfo.Width <= 720 ? 3 : 2;
            }
            
            // Adjust quality based on hardware capabilities
            if (hardwareProfile.SupportsCUDA && hardwareProfile.VramMB > 8192)
            {
                optimized.QualityLevel = "high";
            }
            else if (hardwareProfile.SupportsDirectML)
            {
                optimized.QualityLevel = "medium";
            }
            else
            {
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
                _logger.LogInformation("📦 Starting batch AI processing (redirecting to frame-by-frame pipeline)");
                
                // For v1.4.0, we use the stable frame-by-frame pipeline for batch processing
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

            for (int i = 0; i < totalFrames; i++)
            {
                int index = i;
                string frameFile = frameFiles[i];

                await semaphore.WaitAsync(cancellationToken);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var frameData = await File.ReadAllBytesAsync(frameFile, cancellationToken);
                        var upscaledData = await _upscalerCore.UpscaleImageAsync(frameData, options.Model, options.ScaleFactor);
                        
                        var outputFile = Path.Combine(processedDir, Path.GetFileName(frameFile));
                        await File.WriteAllBytesAsync(outputFile, upscaledData, cancellationToken);
                        
                        if (index % 100 == 0 || index == totalFrames - 1)
                        {
                            _logger.LogInformation($"📸 Processed {index + 1}/{totalFrames} frames");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"⚠️ Failed to process frame {frameFile}");
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
            
            // Scaling filter
            if (options.ScaleFactor > 1)
            {
                if (options.HardwareAcceleration == "cuda")
                {
                    filters.Add($"scale_cuda={options.ScaleFactor}*iw:{options.ScaleFactor}*ih");
                }
                else if (options.HardwareAcceleration == "vaapi")
                {
                    filters.Add($"scale_vaapi={options.ScaleFactor}*iw:{options.ScaleFactor}*ih");
                }
                else if (options.HardwareAcceleration == "qsv")
                {
                    filters.Add($"scale_qsv={options.ScaleFactor}*iw:{options.ScaleFactor}*ih");
                }
                else
                {
                    filters.Add($"scale={options.ScaleFactor}*iw:{options.ScaleFactor}*ih:flags=lanczos");
                }
            }
            
            // Quality filters
            if (options.QualityLevel == "high")
            {
                if (options.HardwareAcceleration == "vaapi")
                {
                    filters.Add("sharpen_vaapi");
                }
                else
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
                args.Add("-c:v libx264 -preset medium -crf 23");
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
                OutputResolution = $"{job.InputInfo.Width * job.OptimizedOptions.ScaleFactor}x{job.InputInfo.Height * job.OptimizedOptions.ScaleFactor}",
                Model = job.OptimizedOptions.Model,
                Scale = job.OptimizedOptions.ScaleFactor,
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
        private void UpdateStatistics(object? state)
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
}