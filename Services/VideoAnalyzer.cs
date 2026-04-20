using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using FFMpegCore;
using CliWrap;
using JellyfinUpscalerPlugin.Models;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Analyzes video files using FFprobe to extract metadata, HDR properties, and interlace detection.
    /// </summary>
    public class VideoAnalyzer
    {
        private readonly ILogger _logger;
        private string _ffprobePath;

        public VideoAnalyzer(ILogger logger, string ffprobePath)
        {
            _logger = logger;
            _ffprobePath = ffprobePath;
        }

        /// <summary>
        /// Update the ffprobe path after construction — required when Jellyfin's MediaEncoder
        /// resolves its ProbePath late (after plugin singletons are already built).
        /// </summary>
        public void UpdateFFprobePath(string newPath)
        {
            if (!string.IsNullOrEmpty(newPath))
            {
                _ffprobePath = newPath;
            }
        }

        /// <summary>
        /// Analyze video properties using FFprobe
        /// </summary>
        public async Task<VideoInfo> AnalyzeVideoAsync(string inputPath)
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

                _logger.LogInformation("Video analysis: {Width}x{Height} @ {Fps}fps, {Codec}, {BitRate}kbps, HDR: {IsHDR}, BitDepth: {BitDepth}, Interlaced: {Interlaced}",
                    info.Width, info.Height, info.FrameRate.ToString("F1"), info.Codec, info.BitRate, info.IsHDR, info.BitDepth, info.IsInterlaced);

                return info;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Video analysis failed");
                throw;
            }
        }

        /// <summary>
        /// Estimate video quality based on bit rate per pixel
        /// </summary>
        public VideoQuality EstimateVideoQuality(VideoInfo info)
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
        public bool IsHDRVideo(FFMpegCore.VideoStream? videoStream, VideoInfo info)
        {
            if (videoStream == null) return false;

            bool pixelFormatHDR = videoStream.PixelFormat?.Contains("bt2020") == true ||
                                  videoStream.PixelFormat?.Contains("smpte2084") == true ||
                                  videoStream.PixelFormat?.Contains("p010") == true;

            bool transferHDR = string.Equals(info.ColorTransfer, "smpte2084", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(info.ColorTransfer, "arib-std-b67", StringComparison.OrdinalIgnoreCase);

            bool primariesBT2020 = string.Equals(info.ColorPrimaries, "bt2020", StringComparison.OrdinalIgnoreCase);

            bool highBitDepth = info.BitDepth > 8;

            return pixelFormatHDR || transferHDR || (primariesBT2020 && highBitDepth);
        }

        /// <summary>
        /// Detect HDR properties (color_transfer, color_primaries, bit_depth) via raw FFprobe JSON output
        /// </summary>
        public async Task DetectHDRPropertiesAsync(string inputPath, VideoInfo info)
        {
            try
            {
                var stdoutBuffer = new StringBuilder();
                var result = await Cli.Wrap(_ffprobePath)
                    .WithArguments(args => args
                        .Add("-v").Add("quiet")
                        .Add("-select_streams").Add("v:0")
                        .Add("-show_streams")
                        .Add("-print_format").Add("json")
                        .Add(inputPath))
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
        public async Task<bool> DetectInterlacedAsync(string inputPath)
        {
            try
            {
                var stdOutBuffer = new StringBuilder();
                var result = await Cli.Wrap(_ffprobePath)
                    .WithArguments(args => args
                        .Add("-v").Add("quiet")
                        .Add("-select_streams").Add("v:0")
                        .Add("-show_entries").Add("stream=field_order")
                        .Add("-of").Add("json")
                        .Add(inputPath))
                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOutBuffer))
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync();

                if (result.ExitCode == 0)
                {
                    var output = stdOutBuffer.ToString();

                    if (output.Contains("\"tt\"") || output.Contains("\"bb\"") ||
                        output.Contains("\"tb\"") || output.Contains("\"bt\""))
                    {
                        _logger.LogInformation("Interlaced content detected via field_order for {File}", Path.GetFileName(inputPath));
                        return true;
                    }

                    if (output.Contains("\"progressive\""))
                    {
                        return false;
                    }
                }

                // Fallback: check full stream info for interlaced indicators
                var stdOutBuffer2 = new StringBuilder();
                var result2 = await Cli.Wrap(_ffprobePath)
                    .WithArguments(args => args
                        .Add("-v").Add("quiet")
                        .Add("-select_streams").Add("v:0")
                        .Add("-show_streams")
                        .Add("-of").Add("json")
                        .Add(inputPath))
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
    }
}
