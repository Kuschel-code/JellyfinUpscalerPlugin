using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CliWrap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using JellyfinUpscalerPlugin.Models;
using Image = SixLabors.ImageSharp.Image;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Handles frame-level operations: extraction, AI upscaling, HDR frame processing, and video reconstruction.
    /// </summary>
    public class VideoFrameProcessor
    {
        private readonly ILogger _logger;
        private readonly string _ffmpegPath;
        private readonly UpscalerCore _upscalerCore;
        private readonly UpscalerProgressHub _progressHub;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _pausedJobs;

        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public VideoFrameProcessor(
            ILogger logger,
            string ffmpegPath,
            UpscalerCore upscalerCore,
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
            bool isHDR = false)
        {
            var effectiveFps = frameRate > 0 ? frameRate : 30;

            var vfFilters = new List<string>();
            if (isInterlaced)
            {
                vfFilters.Add("bwdif=mode=send_frame:parity=auto:deint=all");
                _logger.LogInformation("Applying bwdif deinterlacing filter during frame extraction for {File}", Path.GetFileName(inputPath));
            }
            vfFilters.Add($"fps={effectiveFps}");

            var vfArg = string.Join(",", vfFilters);

            if (isHDR)
            {
                _logger.LogInformation("Extracting frames as 16-bit PNG for HDR content: {File}", Path.GetFileName(inputPath));
            }

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

        /// <summary>
        /// Process frames with AI upscaling
        /// </summary>
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
            int failedFrames = 0;

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

                                _logger.LogInformation("Processed {ProcessedFrames}/{TotalFrames} frames ({Fps} FPS)",
                                    processedFrames, totalFrames, fps.ToString("F1"));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failedFrames);
                        _logger.LogWarning(ex, "Failed to upscale frame {Frame}, using original", frameFile);
                        var outputFile = Path.Combine(processedDir, Path.GetFileName(frameFile));
                        File.Copy(frameFile, outputFile, true);
                        // Fail the entire job if >50% of frames fail (service likely down)
                        if (failedFrames > totalFrames / 2)
                        {
                            _logger.LogError("More than 50% of frames failed ({Failed}/{Total}), aborting job", failedFrames, totalFrames);
                            throw new InvalidOperationException($"Too many frame failures: {failedFrames}/{totalFrames}");
                        }
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
        public async Task<byte[]?> UpscaleHDRFrameAsync(byte[] frameData, int scale, CancellationToken cancellationToken)
        {
            if (scale < 1 || scale > 8)
            {
                _logger.LogWarning("Invalid scale factor {Scale} for HDR upscaling, using default 2", scale);
                scale = 2;
            }

            var config = Plugin.Instance?.Configuration;
            var baseUrl = config?.AiServiceUrl ?? "http://localhost:5000";
            var client = _httpClientFactory.CreateClient("UpscalerHDR");

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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract audio, continuing without audio");
                hasAudio = false;
            }

            var outputCodec = Config.OutputCodec ?? "libx264";
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
                codecArgs = $"-c:v {outputCodec} -pix_fmt yuv420p10le -colorspace bt2020nc -color_primaries bt2020 -color_trc smpte2084";
                _logger.LogInformation("Using HDR output settings: 10-bit yuv420p10le with BT.2020/PQ metadata");
            }
            else
            {
                codecArgs = $"-c:v {outputCodec} -pix_fmt yuv420p";
            }

            var result = await Cli.Wrap(_ffmpegPath)
                .WithArguments(args => {
                    args.Add("-framerate").Add(effectiveFps.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Add("-i").Add(Path.Combine(processedDir, "frame_%06d.png"));
                    if (hasAudio && File.Exists(tempAudioPath))
                        args.Add("-i").Add(tempAudioPath);
                    foreach (var part in codecArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        args.Add(part);
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
    }
}
