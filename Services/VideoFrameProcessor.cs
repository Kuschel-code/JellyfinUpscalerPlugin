using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CliWrap;
using SixLabors.ImageSharp;
using JellyfinUpscalerPlugin.Models;
using Image = SixLabors.ImageSharp.Image;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Signals that the Docker AI service returned a local resize/original fallback.
    /// Video jobs must not turn this into a successful-looking library result.
    /// </summary>
    public sealed class AiUpscalingUnavailableException(string message) : InvalidOperationException(message)
    {
    }

    /// <summary>
    /// Handles frame-level operations: extraction, AI upscaling, HDR frame processing, and video reconstruction.
    /// </summary>
    public class VideoFrameProcessor
    {
        private readonly ILogger _logger;
        private string _ffmpegPath;

        public void UpdateFFmpegPath(string newPath)
        {
            if (!string.IsNullOrEmpty(newPath))
            {
                _ffmpegPath = newPath;
            }
        }

        // v1.7.3.1 - depends on IUpscalerCore interface (test-seam) instead of concrete.
        private readonly IUpscalerCore _upscalerCore;
        private readonly UpscalerProgressHub _progressHub;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _pausedJobs;

        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public VideoFrameProcessor(
            ILogger logger,
            string ffmpegPath,
            IUpscalerCore upscalerCore,
            UpscalerProgressHub progressHub,
            IHttpClientFactory httpClientFactory,
            System.Collections.Concurrent.ConcurrentDictionary<string, bool> pausedJobs)
        {
            _logger = logger;
            _ffmpegPath = ffmpegPath;
            _upscalerCore = upscalerCore;
            _progressHub = progressHub;
            _httpClientFactory = httpClientFactory;
            _pausedJobs = pausedJobs;
        }

        /// <summary>
        /// Extract frames from video
        /// </summary>
        public async Task ExtractFramesAsync(
            string inputPath,
            string framesDir,
            double frameRate,
            CancellationToken cancellationToken,
            bool isInterlaced = false,
            bool isHDR = false,
            string jobId = "",
            int estimatedTotalFrames = 0)
        {
            var effectiveFps = frameRate > 0 ? frameRate : 30;

            var vfFilters = new List<string>();
            if (isInterlaced)
            {
                vfFilters.Add("bwdif=mode=send_frame:parity=auto:deint=all");
                _logger.LogInformation("Applying bwdif deinterlacing filter during frame extraction for {File}", Path.GetFileName(inputPath));
            }

            // v1.8.2 - denoise-before-upscale prefilter (Netflix lesson): clean compression
            // noise on the source frames BEFORE they hit the SR model — better reconstruction
            // and a smaller re-encode. Runs after deinterlace, before fps/creative filters.
            var denoisePrefilter = new VideoFilterService().BuildDenoisePrefilter(Config);
            if (denoisePrefilter != null)
            {
                vfFilters.Add(denoisePrefilter);
                _logger.LogInformation("Applying denoise prefilter '{Filter}' before upscale for {File}", denoisePrefilter, Path.GetFileName(inputPath));
            }

            // v1.8.3.22 - a comma is the FILTER SEPARATOR in an ffmpeg filtergraph. On any
            // comma-decimal locale (de-DE, fr-FR) 23.976 rendered as "fps=23,976" and ffmpeg
            // died with "No such filter: '976'" - i.e. every frame-by-frame job on those
            // servers. Lines 484/490 of this same file already did this correctly.
            vfFilters.Add($"fps={effectiveFps.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

            // Camera-style video filters (applied during frame extraction)
            var videoFilterChain = new VideoFilterService().BuildFilterChain(Config);
            if (videoFilterChain != null)
            {
                vfFilters.Add(videoFilterChain);
            }

            if (isHDR)
            {
                // The color matrix must drive pixel conversion, not only output tags.
                if (denoisePrefilter != null || videoFilterChain != null)
                    throw new NotSupportedException("HDR denoise and creative filters have not been validated for RGB16. Disable them before processing PQ.");
                vfFilters.Add("scale=in_color_matrix=bt2020:out_range=pc,format=rgb48be");
            }
            var vfArg = string.Join(",", vfFilters);

            if (isHDR)
            {
                _logger.LogInformation("Extracting frames as 16-bit PNG for HDR content: {File}", Path.GetFileName(inputPath));
            }

            // v1.7.11 - extraction reports no progress on its own (one blocking ffmpeg call), so a
            // background poller counts the written frame_*.png every ~2s and feeds the extraction band
            // (Gap 1). File-count is deliberate (robust across ffmpeg builds vs parsing stderr 'frame=').
            // The CTS is linked to cancellationToken; the finally cancels + awaits the poller so it can
            // never outlive the job (no orphan task, no write-after-clear).
            using var pollerCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var reportExtraction = !string.IsNullOrEmpty(jobId) && estimatedTotalFrames > 0;
            var pollerTask = reportExtraction
                ? Task.Run(async () =>
                {
                    try
                    {
                        while (!pollerCts.Token.IsCancellationRequested)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2), pollerCts.Token);
                            var n = Directory.Exists(framesDir)
                                ? Directory.GetFiles(framesDir, "frame_*.png").Length : 0;
                            await _progressHub.SendExtractionProgress(jobId, n, estimatedTotalFrames);
                        }
                    }
                    catch (OperationCanceledException) { /* normal: extraction finished or job cancelled */ }
                }, pollerCts.Token)
                : Task.CompletedTask;

            try
            {
                var result = await Cli.Wrap(_ffmpegPath)
                    .WithArguments(args => {
                        args.Add("-i").Add(inputPath)
                            .Add("-vf").Add(vfArg);
                        if (isHDR) args.Add("-pix_fmt").Add("rgb48be");
                        args.Add(Path.Combine(framesDir, "frame_%06d.png"));
                    })
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync(cancellationToken);

                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Frame extraction failed with exit code {result.ExitCode}");
                }
            }
            finally
            {
                // Stop the poller and wait for it (no orphan). Deliberately does NOT clear the
                // extraction cache - that happens only on terminal paths so the bar stays monotonic.
                pollerCts.Cancel();
                try { await pollerTask; } catch { /* poller is best-effort */ }
            }
        }

        /// <summary>
        /// Extract a single frame from a video at the given position and return it as PNG bytes.
        /// Uses input seeking (-ss before -i) for fast keyframe-based seeking.
        /// </summary>
        public async Task<byte[]> ExtractSingleFrameAsync(string videoPath, TimeSpan position, CancellationToken cancellationToken = default, string? ffmpegOverride = null)
        {
            var ffmpeg = !string.IsNullOrEmpty(ffmpegOverride) ? ffmpegOverride : _ffmpegPath;
            if (string.IsNullOrEmpty(ffmpeg))
                throw new InvalidOperationException("FFmpeg path is not configured — cannot extract frames");

            using var ms = new MemoryStream();
            var stderr = new StringBuilder();

            var result = await Cli.Wrap(ffmpeg)
                .WithArguments(args =>
                {
                    args.Add("-ss").Add(position.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                        .Add("-i").Add(videoPath)
                        .Add("-frames:v").Add("1")
                        .Add("-f").Add("image2pipe")
                        .Add("-vcodec").Add("png")
                        .Add("pipe:1");
                })
                .WithStandardOutputPipe(PipeTarget.ToStream(ms))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr))
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync(cancellationToken);

            if (result.ExitCode != 0 || ms.Length == 0)
            {
                var stderrText = stderr.ToString();
                // Log the last 1000 chars of stderr (the error is typically at the end, after the banner)
                var logPart = stderrText.Length > 1000 ? stderrText[^1000..] : stderrText;
                _logger.LogError("FFmpeg frame extraction failed: exit={Code}, stderr(tail)={Stderr}", result.ExitCode, logPart);
                throw new InvalidOperationException($"Single frame extraction failed (exit code {result.ExitCode}, bytes {ms.Length})");
            }

            return ms.ToArray();
        }

        /// <summary>
        /// Extract a single frame and apply an FFmpeg filter chain in one pass.
        /// Returns the filtered frame as PNG bytes. Used for live filter preview in the config UI.
        /// </summary>
        public async Task<byte[]> ExtractSingleFrameWithFiltersAsync(
            string videoPath,
            TimeSpan position,
            string? filterChain,
            CancellationToken cancellationToken = default,
            string? ffmpegOverride = null)
        {
            var ffmpeg = !string.IsNullOrEmpty(ffmpegOverride) ? ffmpegOverride : _ffmpegPath;
            if (string.IsNullOrEmpty(ffmpeg))
                throw new InvalidOperationException("FFmpeg path is not configured — cannot extract frames");

            using var ms = new MemoryStream();
            var stderr = new StringBuilder();

            var result = await Cli.Wrap(ffmpeg)
                .WithArguments(args =>
                {
                    args.Add("-ss").Add(position.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                        .Add("-i").Add(videoPath)
                        .Add("-frames:v").Add("1");
                    if (!string.IsNullOrWhiteSpace(filterChain))
                        args.Add("-vf").Add(filterChain);
                    args.Add("-f").Add("image2pipe")
                        .Add("-vcodec").Add("png")
                        .Add("pipe:1");
                })
                .WithStandardOutputPipe(PipeTarget.ToStream(ms))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr))
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync(cancellationToken);

            if (result.ExitCode != 0 || ms.Length == 0)
            {
                var stderrText = stderr.ToString();
                var logPart = stderrText.Length > 1000 ? stderrText[^1000..] : stderrText;
                _logger.LogError("FFmpeg filtered frame extraction failed: exit={Code}, filter={Filter}, stderr(tail)={Stderr}",
                    result.ExitCode, filterChain, logPart);
                throw new InvalidOperationException($"Filtered frame extraction failed (exit code {result.ExitCode}, bytes {ms.Length})");
            }

            return ms.ToArray();
        }

        /// <summary>
        /// Process frames with AI upscaling
        /// </summary>
        /// <summary>
        /// v1.8.3 — upscale ONE frame: read -> AI upscale -> write to processedDir.
        /// Library/batch processing must fail closed when the Docker service falls back to a local
        /// resize; silently copying or storing that CPU result makes the host look busy while the
        /// container remains idle (Discussion #80).
        /// </summary>
        public async Task<bool> UpscaleSingleFrameAsync(
            string frameFile,
            string processedDir,
            VideoProcessingOptions options,
            bool isHDR,
            CancellationToken cancellationToken)
        {
            var frameData = await File.ReadAllBytesAsync(frameFile, cancellationToken);
            byte[]? upscaledData;
            if (isHDR)
            {
                upscaledData = await UpscaleHDRFrameAsync(frameData, options.ScaleFactor, cancellationToken);
            }
            else
            {
                var upscale = await _upscalerCore.UpscaleImageDetailedAsync(
                    frameData, options.Model, options.ScaleFactor, cancellationToken, allowLocalFallback: false);
                if (!upscale.UsedAi)
                {
                    throw new AiUpscalingUnavailableException(
                        $"Docker AI service did not produce an upscaled frame: {upscale.FallbackReason ?? "unknown reason"}");
                }
                upscaledData = upscale.Data;
            }

            var outputFile = Path.Combine(processedDir, Path.GetFileName(frameFile));
            if (upscaledData != null && upscaledData.Length > 0)
            {
                if (!isHDR)
                {
                    var source = Image.Identify(frameData);
                    ValidateNativeAiOutput(upscaledData, source.Width, source.Height);
                }
                await File.WriteAllBytesAsync(outputFile, upscaledData, cancellationToken);
                return true;
            }

            throw new InvalidDataException("Upscaling returned no frame; original-frame fallback is forbidden.");
        }

        public async Task ProcessFramesAsync(
            string framesDir,
            string processedDir,
            VideoProcessingOptions options,
            string processingJobId,
            CancellationToken cancellationToken,
            bool isHDR = false)
        {
            var frameFiles = Directory.GetFiles(framesDir, "*.png").OrderBy(f => f).ToArray();
            int totalFrames = frameFiles.Length;
            if (totalFrames == 0)
                throw new InvalidDataException("No frames were extracted for AI processing.");
            cancellationToken.ThrowIfCancellationRequested();

            int maxConcurrency = 1;
            try
            {
                var profile = await _upscalerCore.DetectHardwareAsync();
                maxConcurrency = Math.Max(1, profile.MaxConcurrentStreams);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogDebug(ex, "Hardware detection failed, using default concurrency"); }

            _logger.LogInformation("Processing {TotalFrames} frames with max concurrency: {MaxConcurrency}", totalFrames, maxConcurrency);

            var processedFrames = 0;
            var startTime = DateTime.UtcNow;
            long lastProgressTicks = DateTime.UtcNow.Ticks;

            // Parallel.ForEachAsync stops scheduling on the first failure, cancels its
            // in-flight workers and joins them before returning. A task list behind a
            // semaphore continued the whole movie after Docker had already failed.
            await Parallel.ForEachAsync(frameFiles, new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = cancellationToken
            }, async (frameFile, frameToken) =>
            {
                // Check for pause before processing.
                while (_pausedJobs.GetValueOrDefault(processingJobId, false))
                {
                    await Task.Delay(500, frameToken);
                }

                // Any failed/empty frame aborts the video. Copying the original would
                // create a partly upscaled output and report an incomplete job as success.
                await UpscaleSingleFrameAsync(frameFile, processedDir, options, isHDR, frameToken);
                var completed = Interlocked.Increment(ref processedFrames);
                var nowTicks = DateTime.UtcNow.Ticks;
                var prevTicks = Interlocked.Read(ref lastProgressTicks);
                if ((nowTicks - prevTicks) >= TimeSpan.TicksPerSecond * 2 || completed == totalFrames)
                {
                    if (Interlocked.CompareExchange(ref lastProgressTicks, nowTicks, prevTicks) == prevTicks)
                    {
                        var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                        var fps = elapsed > 0 ? completed / elapsed : 0;
                        await _progressHub.SendFrameProgress(processingJobId, Path.GetFileName(frameFile), completed, totalFrames, fps);
                        _logger.LogInformation("Processed {ProcessedFrames}/{TotalFrames} frames ({Fps} FPS)",
                            completed, totalFrames, fps.ToString("F1"));
                    }
                }
            });
        }

        /// <summary>
        /// Upscale a single HDR frame via the /upscale-hdr endpoint on the AI service
        /// </summary>
        public async Task<byte[]?> UpscaleHDRFrameAsync(byte[] frameData, int scale, CancellationToken cancellationToken)
        {
            if (scale < 1 || scale > 8)
            {
                _logger.LogWarning("Invalid scale factor {Scale} for HDR upscaling, using default 2", scale);
                scale = 2;
            }

            var config = Plugin.Instance?.Configuration;
            // v1.8.3.22 - TrimEnd. A configured URL with a trailing slash produced
            // "http://host:5000//upscale-hdr", which FastAPI answers with 404 - and the
            // caller silently copied every original frame through, so the whole HDR job
            // re-encoded unchanged and reported success. HttpUpscalerService.GetServiceUrl
            // has trimmed for releases; this copy never did.
            var baseUrl = (config?.AiServiceUrl ?? "http://localhost:5000").TrimEnd('/');
            HdrFrameContract.ValidateRgb16Png(frameData);
            var client = _httpClientFactory.CreateClient("UpscalerHDR");

            using var content = new MultipartFormDataContent();
            using var imageContent = new ByteArrayContent(frameData);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(imageContent, "file", "frame.png");
            content.Add(new StringContent(scale.ToString()), "scale");
            content.Add(new StringContent("smpte2084"), "transfer");
            content.Add(new StringContent("bt2020"), "primaries");

            _logger.LogDebug("Sending HDR frame ({Size} bytes) to AI service for {Scale}x upscaling", frameData.Length, scale);

            using var response = await client.PostAsync($"{baseUrl}/upscale-hdr", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var output = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var nativeScale = HdrFrameContract.ValidateOutput(frameData, output);
                _logger.LogDebug("HDR frame native output scale: {Scale}x", nativeScale);
                return output;
            }

            _logger.LogWarning("HDR upscale failed with status {Status}", response.StatusCode);
            throw new InvalidDataException($"HDR service rejected frame ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        /// <summary>
        /// Reconstruct video from processed frames
        /// </summary>
        public async Task ReconstructVideoAsync(
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
            HdrFrameContract.Validate(inputInfo, ProcessingMethod.FrameByFrame, Config.OutputCodec);
            ValidateFrameSequence(processedDir);

            try
            {
                var audioResult = await Cli.Wrap(_ffmpegPath)
                    .WithArguments(args => args
                        .Add("-i").Add(originalPath)
                        .Add("-vn").Add("-acodec").Add("copy")
                        .Add("-y").Add(tempAudioPath))
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync(cancellationToken);

                hasAudio = audioResult.ExitCode == 0 && File.Exists(tempAudioPath) && new FileInfo(tempAudioPath).Length > 0;

                if (!hasAudio)
                {
                    _logger.LogInformation("No audio track found in source video");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to extract audio, continuing without audio");
                hasAudio = false;
            }

            // v1.6.1.23 - allowlist sourced from CodecRegistry (was inline 7-entry list missing
            // libsvtav1, libaom-av1, libvpx-vp9, av1_nvenc, av1_qsv).
            var outputCodec = Config.OutputCodec ?? "libx264";
            if (!CodecRegistry.OutputCodecs.Contains(outputCodec))
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
                codecArgs = $"-c:v {outputCodec} -pix_fmt yuv420p10le -colorspace bt2020nc -color_primaries bt2020 -color_trc smpte2084";
                _logger.LogInformation("Using HDR output settings: 10-bit yuv420p10le with BT.2020/PQ metadata");
            }
            else
            {
                codecArgs = $"-c:v {outputCodec} -pix_fmt yuv420p";
            }

            var result = await Cli.Wrap(_ffmpegPath)
                .WithArguments(args => {
                    args.Add("-xerror")
                        .Add("-framerate").Add(effectiveFps.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Add("-i").Add(Path.Combine(processedDir, "frame_%06d.png"));
                    if (hasAudio && File.Exists(tempAudioPath))
                        args.Add("-i").Add(tempAudioPath);
                    foreach (var part in codecArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        args.Add(part);
                    if (isHDR)
                    {
                        args.Add("-vf").Add("scale=out_color_matrix=bt2020:in_range=pc:out_range=tv");
                        args.Add("-color_range").Add("tv");
                        args.Add("-x265-params").Add(HdrFrameContract.X265Parameters(inputInfo!));
                    }
                    args.Add("-r").Add(effectiveFps.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (hasAudio && File.Exists(tempAudioPath))
                        args.Add("-c:a").Add("copy");
                    args.Add("-y").Add(outputPath);
                })
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync(cancellationToken);

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
        /// Encode a raw RGB24 frame buffer to JPEG bytes for transport to AI service.
        /// </summary>
        public static byte[] EncodeRawFrameToJpeg(byte[] rawRgb, int width, int height)
        {
            using var image = Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Rgb24>(rawRgb, width, height);
            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 90 });
            return ms.ToArray();
        }

        /// <summary>
        /// Decode a service response and derive its native scale from real pixels.
        /// Never resize an unexpected image to make it fit the configured scale.
        /// </summary>
        private static int ValidateNativeScale(int width, int height, int frameCount, int sourceWidth, int sourceHeight)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0 || frameCount != 1)
                throw new InvalidDataException("AI output requires a single image and known source dimensions.");
            var scale = width / sourceWidth;
            if (scale < 1 || scale > 8 || width != sourceWidth * scale || height != sourceHeight * scale)
                throw new InvalidDataException("AI output dimensions do not match a supported native model scale.");
            return scale;
        }

        internal static (int Width, int Height, int Scale) ValidateNativeAiOutput(
            byte[] imageBytes, int sourceWidth, int sourceHeight)
        {
            using var image = Image.Load(imageBytes);
            var scale = ValidateNativeScale(image.Width, image.Height, image.Frames.Count, sourceWidth, sourceHeight);
            return (image.Width, image.Height, scale);
        }

        internal static void ValidateFrameSequence(string processedDir)
        {
            var frames = Directory.GetFiles(processedDir, "frame_*.png").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            if (frames.Length == 0) throw new InvalidDataException("No processed frames are available for encoding.");
            var first = Image.Identify(frames[0]);
            for (var index = 0; index < frames.Length; index++)
            {
                if (Path.GetFileName(frames[index]) != $"frame_{index + 1:D6}.png")
                    throw new InvalidDataException("Processed frame sequence has a missing or out-of-order frame.");
                var info = Image.Identify(frames[index]);
                if (info.Width != first.Width || info.Height != first.Height)
                    throw new InvalidDataException("AI output dimensions changed within the video; mixed model scales are not supported.");
            }
        }

        internal static (byte[] Data, int Width, int Height, int Scale) DecodeNativeAiFrame(
            byte[] imageBytes, int sourceWidth, int sourceHeight)
        {
            using var image = Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(imageBytes);
            var scale = ValidateNativeScale(image.Width, image.Height, image.Frames.Count, sourceWidth, sourceHeight);
            var rawBytes = new byte[checked(image.Width * image.Height * 3)];
            image.CopyPixelDataTo(rawBytes);
            return (rawBytes, image.Width, image.Height, scale);
        }

        /// <summary>
        /// Decode JPEG bytes from AI service back to raw RGB24 frame buffer.
        /// Returns null if decoding fails or dimensions do not match.
        /// </summary>
        public static byte[]? DecodeJpegToRawFrame(byte[] jpegBytes, int expectedWidth, int expectedHeight)
        {
            try
            {
                using var image = Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(jpegBytes);
                if (image.Width != expectedWidth || image.Height != expectedHeight)
                {
                    return null;
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
    }
}
