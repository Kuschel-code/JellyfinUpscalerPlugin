using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using JellyfinUpscalerPlugin.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JellyfinUpscalerPlugin.Services;

/// <summary>The v1.8.3.31 HDR boundary: PQ/BT.2020, RGB16 PNG frames, libx265 10-bit output.</summary>
internal static class HdrFrameContract
{
    // SDR transfer characteristics, named as ffprobe reports them.
    private static readonly string[] SdrTransfers =
    {
        "bt709", "smpte170m", "bt470m", "bt470bg", "gamma22", "gamma28", "smpte240m",
        "iec61966-2-1", "iec61966-2-4", "bt1361e", "bt2020-10", "bt2020-12"
    };

    private static bool IsUntagged(string transfer) => string.IsNullOrEmpty(transfer) ||
        transfer.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
        transfer.Equals("unspecified", StringComparison.OrdinalIgnoreCase);

    private static bool IsSdrTransfer(string transfer) =>
        Array.Exists(SdrTransfers, t => t.Equals(transfer, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// HDR needs positive evidence: dynamic metadata, a tagged transfer that is not SDR
    /// (PQ, HLG, or one this pipeline does not know - refused later as unknown), or
    /// BT.2020 primaries without an SDR transfer. Bit depth alone is not evidence:
    /// 1.8.3.31/32 treated every untagged 10-bit file as HDR and refused ordinary SDR
    /// encodes (common for anime) with "HDR requires PQ/ST.2084".
    /// </summary>
    public static bool IsHdr(VideoInfo info) => info.IsHDR || info.HasDynamicHDR ||
        (!IsUntagged(info.ColorTransfer) && !IsSdrTransfer(info.ColorTransfer)) ||
        (info.ColorPrimaries.Equals("bt2020", StringComparison.OrdinalIgnoreCase) && !IsSdrTransfer(info.ColorTransfer));

    public static void ValidateInput(VideoInfo info)
    {
        if (!IsHdr(info)) return;
        if (info.HasDynamicHDR) throw new NotSupportedException("Dynamic HDR (Dolby Vision/HDR10+) is not supported.");
        if (!info.ColorTransfer.Equals("smpte2084", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("HDR requires PQ/ST.2084 (smpte2084). HLG and unknown transfer functions are not supported.");
        if (!info.ColorPrimaries.Equals("bt2020", StringComparison.OrdinalIgnoreCase) || info.BitDepth < 10)
            throw new NotSupportedException("HDR requires BT.2020 primaries and at least 10-bit input.");
    }

    public static void Validate(VideoInfo? info, ProcessingMethod method, string codec, int inputFrames = 1)
    {
        if (info == null || !IsHdr(info)) return;
        ValidateInput(info);
        if (inputFrames > 1 || method is not (ProcessingMethod.FrameByFrame or ProcessingMethod.Batch))
            throw new NotSupportedException("HDR realtime and multi-frame processing are not supported. Use individual RGB16 PNG frames.");
        if (codec != "libx265") throw new NotSupportedException("HDR output requires libx265 with yuv420p10le. Select libx265 before starting the job.");
    }

    public static (int Width, int Height) ValidateRgb16Png(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(signature) || bytes[24] != 16 || bytes[25] != 2)
            throw new InvalidDataException("HDR transport requires a 16-bit RGB PNG; JPEG, 8-bit, grayscale and alpha are not supported.");
        using var image = Image.Load<Rgb48>(bytes);
        return (image.Width, image.Height);
    }

    public static int ValidateOutput(byte[] input, byte[] output)
    {
        var source = ValidateRgb16Png(input);
        var target = ValidateRgb16Png(output);
        var scale = target.Width / source.Width;
        if (scale < 1 || scale > 8 || target.Width != source.Width * scale || target.Height != source.Height * scale)
            throw new InvalidDataException("HDR output dimensions do not match a supported native model scale.");
        return scale;
    }

    private static string Number(JsonElement side, string key, decimal factor)
    {
        var text = side.GetProperty(key).ToString();
        var parts = text.Split('/');
        var value = decimal.Parse(parts[0], CultureInfo.InvariantCulture);
        if (parts.Length == 2) value /= decimal.Parse(parts[1], CultureInfo.InvariantCulture);
        if (parts.Length > 2 || value < 0) throw new InvalidDataException("Invalid HDR metadata: " + key);
        return decimal.Round(value * factor, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Read stream and frame side data; changing static metadata is rejected as dynamic.</summary>
    public static void ReadMetadata(JsonElement element, VideoInfo info)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) ReadMetadata(item, info);
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("side_data_type", out var type))
            {
                var name = type.GetString() ?? "";
                if (name.Contains("DOVI", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Dolby", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("HDR10+", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("2094", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Dynamic HDR", StringComparison.OrdinalIgnoreCase)) info.HasDynamicHDR = true;
                if (name == "Mastering display metadata")
                {
                    string C(string prefix) => Number(element, prefix + "_x", 50000) + "," + Number(element, prefix + "_y", 50000);
                    var value = $"G({C("green")})B({C("blue")})R({C("red")})WP({C("white_point")})L({Number(element, "max_luminance", 10000)},{Number(element, "min_luminance", 10000)})";
                    if (info.MasteringDisplayMetadata != null && info.MasteringDisplayMetadata != value) info.HasDynamicHDR = true;
                    info.MasteringDisplayMetadata = value;
                }
                if (name == "Content light level metadata")
                {
                    var value = Number(element, "max_content", 1) + "," + Number(element, "max_average", 1);
                    if (info.ContentLightMetadata != null && info.ContentLightMetadata != value) info.HasDynamicHDR = true;
                    info.ContentLightMetadata = value;
                }
            }
            foreach (var property in element.EnumerateObject()) ReadMetadata(property.Value, info);
        }
    }

    public static string X265Parameters(VideoInfo info) => "hdr10=1:repeat-headers=1" +
        (info.MasteringDisplayMetadata == null ? "" : ":master-display=" + info.MasteringDisplayMetadata) +
        (info.ContentLightMetadata == null ? "" : ":max-cll=" + info.ContentLightMetadata);
}
