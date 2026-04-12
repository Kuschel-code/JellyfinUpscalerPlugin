using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CliWrap;
using JellyfinUpscalerPlugin.Models;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Executes video processing using the selected method: RealTime, FrameByFrame, Batch, MultiFrame, or RealTimeAI.
    /// </summary>
    public class ProcessingMethodExecutor
    {
        private readonly ILogger _logger;
        private readonly string _ffmpegPath;
        private readonly UpscalerProgressHub _progressHub;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly VideoFrameProcessor _frameProcessor;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _pausedJobs;

        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public ProcessingMethodExecutor(
            ILogger logger,
            string ffmpegPath,
            UpscalerProgressHub progressHub,
            IHttpClientFactory httpClientFactory,
            VideoFrameProcessor frameProcessor,
            System.Collections.Concurrent.ConcurrentDictionary<string, bool> pausedJobs)
        {
            _logger = logger;
            _ffmpegPath = ffmpegPath;
            _progressHub = progressHub;
            _httpClientFactory = httpClientFactory;
            _frameProcessor = frameProcessor;
            _pausedJobs = pausedJobs;
        }

        /// <summary>
        /// Execute video processing based on method
        /// </summary>
        public async Task<VideoProcessingResult> ExecuteProcessingAsync(
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
                    .WithArguments(a => {
                        foreach (var part in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            a.Add(part);
                    })
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
                var estimatedSpaceNeeded = (long)((job.InputInfo?.Duration.TotalSeconds ?? 300) * 25 * 500_000);
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
                    await _frameProcessor.ExtractFramesAsync(inputPath, framesDir, framesFps, cancellationToken, isInterlaced, isHDR);

                    // 2. Process frames with AI
                    var processedDir = Path.Combine(tempDir, "processed");
                    Directory.CreateDirectory(processedDir);

                    await _frameProcessor.ProcessFramesAsync(framesDir, processedDir, job.OptimizedOptions, job.Id, cancellationToken, isHDR);

                    // 3. Reconstruct video
                    await _frameProcessor.ReconstructVideoAsync(processedDir, inputPath, outputPath, job.OptimizedOptions, framesFps, cancellationToken, job.InputInfo);

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
                const long minFreeSpace = 2L * 1024 * 1024 * 1024;
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
                _logger.LogInformation("Extracting frames for multi-frame processing with filter: {Filter}", mfVfArg);

                await Cli.Wrap(_ffmpegPath)
                    .WithArguments(args => args
                        .Add("-i").Add(inputPath)
                        .Add("-vf").Add(mfVfArg)
                        .Add(Path.Combine(framesDir, "frame_%06d.png")))
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

                int halfWindow = (inputFrames - 1) / 2;
                int totalFrames = frameFiles.Count;
                int processedCount = 0;
                var serviceUrl = Config.AiServiceUrl?.TrimEnd('/') ?? "http://localhost:5000";

                var multiFrameClient = _httpClientFactory.CreateClient("AiUpscalerLongTimeout");

                // SEQUENTIAL sliding window -- do NOT parallelize
                for (int i = 0; i < totalFrames; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var windowPaths = new List<string>();
                        int startIdx = i - halfWindow;
                        for (int j = 0; j < inputFrames; j++)
                        {
                            int idx = Math.Clamp(startIdx + j, 0, totalFrames - 1);
                            windowPaths.Add(frameFiles[idx]);
                        }

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

                _logger.LogInformation("Reconstructing video from {Count} processed frames with audio", totalFrames);
                await _frameProcessor.ReconstructVideoAsync(processedDir, inputPath, outputPath, job.OptimizedOptions, effectiveFps, cancellationToken, job.InputInfo);

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
        /// Real-time AI processing using FFmpeg pipe decode -> AI upscale -> FFmpeg pipe encode.
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
            var frameByteSize = inputWidth * inputHeight * 3;
            var upscaledFrameByteSize = outputWidth * outputHeight * 3;
            var serviceUrl = Config.AiServiceUrl?.TrimEnd('/') ?? "http://localhost:5000";

            _logger.LogInformation(
                "Starting RealTimeAI processing: {InputW}x{InputH} -> {OutputW}x{OutputH} @ {Fps}fps, model={Model}",
                inputWidth, inputHeight, outputWidth, outputHeight, effectiveFps, job.OptimizedOptions?.Model);

            Process? decoderProcess = null;
            Process? encoderProcess = null;
            var tempAudioPath = Path.Combine(Path.GetTempPath(), $"rtai_audio_{Guid.NewGuid()}.mka");

            try
            {
                var hasAudio = false;
                try
                {
                    var audioResult = await Cli.Wrap(_ffmpegPath)
                        .WithArguments(args => args
                            .Add("-i").Add(inputPath)
                            .Add("-vn").Add("-acodec").Add("copy")
                            .Add("-y").Add(tempAudioPath))
                        .WithValidation(CommandResultValidation.None)
                        .ExecuteAsync(cancellationToken);
                    hasAudio = audioResult.ExitCode == 0 && File.Exists(tempAudioPath) && new FileInfo(tempAudioPath).Length > 0;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract audio for RealTimeAI, continuing without");
                }

                decoderProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _ffmpegPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = false,
                        CreateNoWindow = true
                    }
                };
                // Use ArgumentList to prevent path injection (no shell interpolation)
                decoderProcess.StartInfo.ArgumentList.Add("-i");
                decoderProcess.StartInfo.ArgumentList.Add(inputPath);
                decoderProcess.StartInfo.ArgumentList.Add("-f");
                decoderProcess.StartInfo.ArgumentList.Add("rawvideo");
                decoderProcess.StartInfo.ArgumentList.Add("-pix_fmt");
                decoderProcess.StartInfo.ArgumentList.Add("rgb24");
                decoderProcess.StartInfo.ArgumentList.Add("-v");
                decoderProcess.StartInfo.ArgumentList.Add("quiet");
                decoderProcess.StartInfo.ArgumentList.Add("-");

                var outputCodec = Config.OutputCodec ?? "libx264";
                var allowedCodecs = new HashSet<string> { "libx264", "libx265", "hevc_nvenc", "h264_nvenc", "h264_qsv", "hevc_qsv" };
                if (!allowedCodecs.Contains(outputCodec))
                {
                    outputCodec = "libx264";
                }

                encoderProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _ffmpegPath,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardError = false,
                        CreateNoWindow = true
                    }
                };
                // Use ArgumentList to prevent path injection (no shell interpolation)
                var fpsStr = effectiveFps.ToString(System.Globalization.CultureInfo.InvariantCulture);
                encoderProcess.StartInfo.ArgumentList.Add("-f");
                encoderProcess.StartInfo.ArgumentList.Add("rawvideo");
                encoderProcess.StartInfo.ArgumentList.Add("-pix_fmt");
                encoderProcess.StartInfo.ArgumentList.Add("rgb24");
                encoderProcess.StartInfo.ArgumentList.Add("-s");
                encoderProcess.StartInfo.ArgumentList.Add($"{outputWidth}x{outputHeight}");
                encoderProcess.StartInfo.ArgumentList.Add("-r");
                encoderProcess.StartInfo.ArgumentList.Add(fpsStr);
                encoderProcess.StartInfo.ArgumentList.Add("-i");
                encoderProcess.StartInfo.ArgumentList.Add("-");
                if (hasAudio && File.Exists(tempAudioPath))
                {
                    encoderProcess.StartInfo.ArgumentList.Add("-i");
                    encoderProcess.StartInfo.ArgumentList.Add(tempAudioPath);
                    encoderProcess.StartInfo.ArgumentList.Add("-c:a");
                    encoderProcess.StartInfo.ArgumentList.Add("copy");
                }
                encoderProcess.StartInfo.ArgumentList.Add("-c:v");
                encoderProcess.StartInfo.ArgumentList.Add(outputCodec);
                encoderProcess.StartInfo.ArgumentList.Add("-pix_fmt");
                encoderProcess.StartInfo.ArgumentList.Add("yuv420p");
                encoderProcess.StartInfo.ArgumentList.Add("-y");
                encoderProcess.StartInfo.ArgumentList.Add(outputPath);

                decoderProcess.Start();
                encoderProcess.Start();

                var decoderStream = decoderProcess.StandardOutput.BaseStream;
                var encoderStream = encoderProcess.StandardInput.BaseStream;

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

                    var totalRead = 0;
                    while (totalRead < frameByteSize)
                    {
                        var bytesRead = await decoderStream.ReadAsync(
                            frameBuffer, totalRead, frameByteSize - totalRead, cancellationToken);

                        if (bytesRead == 0)
                        {
                            break;
                        }
                        totalRead += bytesRead;
                    }

                    if (totalRead < frameByteSize)
                    {
                        break;
                    }

                    try
                    {
                        var jpegBytes = VideoFrameProcessor.EncodeRawFrameToJpeg(frameBuffer, inputWidth, inputHeight);
                        using var jpegContent = new ByteArrayContent(jpegBytes);
                        jpegContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

                        using var response = await httpClient.PostAsync($"{serviceUrl}/upscale-frame", jpegContent, cancellationToken);

                        if (response.IsSuccessStatusCode)
                        {
                            var upscaledJpeg = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                            var upscaledRaw = VideoFrameProcessor.DecodeJpegToRawFrame(upscaledJpeg, outputWidth, outputHeight);

                            if (upscaledRaw != null && upscaledRaw.Length == upscaledFrameByteSize)
                            {
                                await encoderStream.WriteAsync(upscaledRaw, 0, upscaledRaw.Length, cancellationToken);
                                framesProcessed++;
                            }
                            else
                            {
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

                    if (framesProcessed % 60 == 0 || framesProcessed == 1)
                    {
                        var elapsed = (DateTime.UtcNow - processingStartTime).TotalSeconds;
                        var currentFps = elapsed > 0 ? framesProcessed / elapsed : 0;
                        var realTimeRatio = effectiveFps > 0 ? currentFps / effectiveFps : 0;

                        await _progressHub.SendFrameProgress(
                            job.Id,
                            Path.GetFileName(inputPath),
                            framesProcessed,
                            -1,
                            currentFps
                        );

                        _logger.LogInformation(
                            "RealTimeAI: {Processed} frames ({Dropped} dropped), {Fps:F1} FPS, {Ratio:F2}x real-time",
                            framesProcessed, framesDropped, currentFps, realTimeRatio);
                    }
                }

                encoderStream.Close();

                await Task.WhenAll(
                    Task.Run(() => decoderProcess.WaitForExit(30000), cancellationToken),
                    Task.Run(() => encoderProcess.WaitForExit(60000), cancellationToken));

                var totalElapsed = DateTime.UtcNow - processingStartTime;
                var avgFps = totalElapsed.TotalSeconds > 0 ? framesProcessed / totalElapsed.TotalSeconds : 0;

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
                // Clean up temp audio file to prevent leaks on cancellation/failure
                try { if (File.Exists(tempAudioPath)) File.Delete(tempAudioPath); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete temp audio file"); }
            }
        }

        /// <summary>
        /// Build FFmpeg command for processing
        /// </summary>
        public string BuildFFmpegCommand(
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

            if (options.ScaleFactor > 1)
            {
                var useAdvancedUpscaling = options.QualityLevel == "high" || options.EnableAIUpscaling;

                if (options.HardwareAcceleration == "cuda")
                {
                    if (useAdvancedUpscaling && hardwareProfile.GpuName?.Contains("RTX") == true)
                    {
                        _logger.LogInformation("Using NVIDIA VSR (Video Super Resolution)");
                        filters.Add($"hwupload_cuda");
                        filters.Add($"scale_cuda={options.ScaleFactor}*iw:{options.ScaleFactor}*ih:interp_algo=lanczos");
                        filters.Add($"unsharp_cuda=luma_amount=1.5:chroma_amount=0.5");
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
                    if (useAdvancedUpscaling)
                    {
                        _logger.LogInformation("Using AMD FSR-style upscaling");
                        filters.Add($"hwupload");
                        filters.Add($"scale_vaapi=w={options.ScaleFactor}*iw:h={options.ScaleFactor}*ih");
                        filters.Add($"sharpen_vaapi");
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
                else if (options.HardwareAcceleration != "cuda")
                {
                    filters.Add("unsharp=5:5:1.0:5:5:0.0");
                }
            }

            // Camera-style video filters (post-processing)
            var videoFilterChain = new VideoFilterService().BuildFilterChain(Config);
            if (videoFilterChain != null)
            {
                filters.Add(videoFilterChain);
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
    }
}
