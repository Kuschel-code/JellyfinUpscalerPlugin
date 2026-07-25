using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// v1.8.3.14 — how much bigger the output actually gets.
    ///
    /// The <c>scale</c> value the plugin sends with an upscale request is NOT what
    /// decides the output size: the AI service validates it against the loaded model
    /// and then uses the model's own scale anyway (docker-ai-service/app/main.py,
    /// <c>upscale_endpoint</c>: "Requested scale=N differs from loaded model scale=M.
    /// Using model's native scale=M."). Only the ffmpeg hardware-filter path honours
    /// the configured factor.
    ///
    /// That mismatch was a real reporting bug: the library scan logged "scale=2x" from
    /// the config while auto-mode handed the service a 4x model, so a 1080p batch job
    /// silently produced 8K frames. This class is the single place that answers both
    /// "what will this model really do?" and "what should we aim for?".
    /// </summary>
    internal static class ModelScale
    {
        // Catalog ids encode their native factor as a "-x<N>" suffix (realesrgan-x4,
        // span-x2, lapsrn-x8). Anchored at the end so "realesrgan-x4-256" — a tiled
        // 4x variant — still reads as 4x rather than 256.
        private static readonly Regex ScaleSuffix = new(@"-x(\d+)(?:-\w+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Native scale factor encoded in a model id, or 0 when the id does not say.
        /// Callers must treat 0 as "unknown" and fall back to the configured value —
        /// guessing a number here would recreate the very lie this class exists to fix.
        /// </summary>
        public static int NativeScaleOf(string? modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return 0;

            // "realesrgan-x2-plus" carries the factor in the middle, not at the end.
            var match = ScaleSuffix.Match(modelId);
            if (!match.Success)
            {
                match = Regex.Match(modelId, @"-x(\d+)-", RegexOptions.IgnoreCase);
                if (!match.Success) return 0;
            }

            return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale)
                   && scale >= 1 && scale <= 8
                ? scale
                : 0;
        }

        /// <summary>
        /// The factor that makes sense for a source of this size. Deliberately conservative
        /// at the top end: 4x on 1080p is 8K, which no client can display, takes four times
        /// the compute of a 2x pass and produces files nobody can stream.
        ///
        /// Note this differs from the "&gt;=1080p -&gt; 1x" figure in the original plan: 1x means
        /// "do not upscale at all", which would leave 1080p sources untouched on 4K displays —
        /// exactly the case the plugin exists for. 1x is reserved for sources that are already
        /// 4K or larger.
        /// </summary>
        public static int TargetScaleFor(int width, int height)
        {
            if (width <= 0 || height <= 0) return 2;   // unknown source -> the safe middle
            if (width >= 3000 || height >= 1700) return 1;   // already 4K: nothing to gain
            if (width <= 720 || height <= 480) return 4;     // SD -> a real restore is worth it
            return 2;                                        // 720p/1080p -> 1440p/4K
        }

        /// <summary>Human-readable "1920x1080 -&gt; 3840x2160" for the UI and logs.</summary>
        public static string DescribeOutput(int width, int height, int scale)
        {
            if (width <= 0 || height <= 0 || scale <= 0) return "unknown output size";
            return $"{width}x{height} -> {width * scale}x{height * scale}";
        }
    }
}
