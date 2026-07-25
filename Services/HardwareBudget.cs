using System;
using System.Collections.Generic;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// v1.8.3.14 — the missing link between the two recommendation systems this plugin
    /// already had: content-based model selection (<see cref="UpscalerCore"/>, "what suits
    /// this material?") and the hardware-aware recommender in the Python service
    /// (<c>/recommend</c>, "what can this machine do?"). They were deliberately separate
    /// and never spoke to each other, so auto-mode could pick a heavy restoration model
    /// on a Celeron that the hardware advisor would have refused outright.
    ///
    /// This class does NOT add a third recommender. It classifies models into three coarse
    /// weight bands and answers one question: <i>may this hardware tier run that band?</i>
    /// The existing candidate chain in <c>UpscalerCore.Pick</c> then simply skips candidates
    /// that fail the check, and the existing v1.8.3.7 substitution reporting explains why.
    /// </summary>
    internal static class HardwareBudget
    {
        /// <summary>Coarse cost band of a model. Precision is not the point — order of magnitude is.</summary>
        public enum Weight
        {
            /// <summary>Classic OpenCV / tiny compact nets. Runs on anything, including a NAS CPU.</summary>
            Light,
            /// <summary>Compact ONNX super-resolution (SPAN, RealPLKSR, compact ESRGAN). Fine on a strong CPU or any GPU.</summary>
            Medium,
            /// <summary>Full ESRGAN/DAT/SwinIR-class restoration and multi-frame VSR. GPU territory.</summary>
            Heavy,
        }

        /// <summary>
        /// Hardware tiers as reported by the service's <c>/recommend</c> endpoint
        /// (docker-ai-service/app/main.py: strong-gpu | mid-gpu | igpu | strong-cpu | weak-cpu).
        /// Unknown or missing input means "no cap" — auto must never block on this.
        /// </summary>
        public static Weight? MaxWeightFor(string? tier)
        {
            if (string.IsNullOrWhiteSpace(tier)) return null;
            switch (tier.Trim().ToLowerInvariant())
            {
                case "strong-gpu":
                case "mid-gpu":
                    return Weight.Heavy;
                case "igpu":
                case "strong-cpu":
                    return Weight.Medium;
                case "weak-cpu":
                    return Weight.Light;
                default:
                    return null;   // unknown tier string -> behave exactly as before
            }
        }

        // Explicitly classified models. Everything not listed falls back to the prefix rules
        // below, so a newly added catalog entry degrades to a sensible guess instead of
        // silently being treated as free.
        private static readonly Dictionary<string, Weight> Explicit = new(StringComparer.OrdinalIgnoreCase)
        {
            // Light — OpenCV .pb and tiny compacts; these are the "runs anywhere" tier
            ["fsrcnn-x2"] = Weight.Light,
            ["fsrcnn-x3"] = Weight.Light,
            ["fsrcnn-x4"] = Weight.Light,
            ["espcn-x2"] = Weight.Light,
            ["espcn-x3"] = Weight.Light,
            ["espcn-x4"] = Weight.Light,
            ["lapsrn-x2"] = Weight.Light,
            ["lapsrn-x4"] = Weight.Light,
            ["lapsrn-x8"] = Weight.Light,
            ["nomosuni-compact-x2"] = Weight.Light,
            ["anime-compact-x4"] = Weight.Light,
            // clearreality-x4 measured 32x faster than realesrgan-x4 per CPU tile (v1.8.3.4 eval)
            ["clearreality-x4"] = Weight.Light,
            ["fallin-soft-x2"] = Weight.Light,

            // Medium — compact ONNX SR, usable on a strong CPU
            ["span-x2"] = Weight.Medium,
            ["span-x4"] = Weight.Medium,
            ["edsr-x2"] = Weight.Medium,
            ["edsr-x3"] = Weight.Medium,
            ["edsr-x4"] = Weight.Medium,
            ["lsdir-compact-x4"] = Weight.Medium,
            ["nomos2-realplksr-x4"] = Weight.Medium,
            ["bhi-realplksr-x4"] = Weight.Medium,
            ["purephoto-realplksr-x4"] = Weight.Medium,
            ["realesrgan-animevideo-x4"] = Weight.Medium,
            ["realesrgan-x4-256"] = Weight.Medium,
            ["apisr-anime-x2"] = Weight.Medium,

            // Heavy — full restoration nets and multi-frame VSR
            ["realesrgan-x4"] = Weight.Heavy,
            ["realesrgan-x2-plus"] = Weight.Heavy,
            ["ultrasharp-v2-x4"] = Weight.Heavy,
            ["nomos2-dat2-x4"] = Weight.Heavy,
            ["nomos8kdat-x4"] = Weight.Heavy,
            ["swinir-x4"] = Weight.Heavy,
            ["swinir-small-x2"] = Weight.Medium,
            ["swinir-small-x4"] = Weight.Medium,
            ["drct-l-x4"] = Weight.Heavy,
            ["fsdedither-x4"] = Weight.Heavy,
            ["edvr-m-x4"] = Weight.Heavy,
            ["realbasicvsr-x4"] = Weight.Heavy,
            ["animesr-v2-x4"] = Weight.Heavy,
        };

        /// <summary>
        /// Weight of a model id. Unlisted ids are classified by architecture prefix so the
        /// catalog can grow without this table being the bottleneck; imported community
        /// models (<c>omdb-*</c>) are treated as Medium because their size is unknown.
        /// </summary>
        public static Weight WeightOf(string? modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return Weight.Medium;
            if (Explicit.TryGetValue(modelId, out var w)) return w;

            var id = modelId.ToLowerInvariant();
            if (id.StartsWith("fsrcnn", StringComparison.Ordinal) ||
                id.StartsWith("espcn", StringComparison.Ordinal) ||
                id.StartsWith("lapsrn", StringComparison.Ordinal) ||
                id.Contains("compact", StringComparison.Ordinal))
                return Weight.Light;

            if (id.Contains("dat", StringComparison.Ordinal) ||
                id.Contains("swinir", StringComparison.Ordinal) ||
                id.Contains("hat", StringComparison.Ordinal) ||
                id.Contains("drct", StringComparison.Ordinal) ||
                id.Contains("vsr", StringComparison.Ordinal) ||
                id.Contains("edvr", StringComparison.Ordinal))
                return Weight.Heavy;

            return Weight.Medium;
        }

        /// <summary>
        /// True when <paramref name="modelId"/> is within budget for <paramref name="tier"/>.
        /// An unknown tier (service unreachable, new tier string) always returns true — the
        /// cap is an improvement, never a gate.
        /// </summary>
        public static bool FitsTier(string? modelId, string? tier)
        {
            var max = MaxWeightFor(tier);
            if (max == null) return true;
            return WeightOf(modelId) <= max.Value;
        }

        /// <summary>
        /// v1.8.3.14 (live-test fix) - ordered stand-ins that fit <paramref name="max"/>.
        ///
        /// The first cut only walked a branch's own declared fallbacks, and those chains
        /// contain no Light model: on a weak CPU every live-action job collapsed to the
        /// last-resort fsrcnn-x2, the crudest thing in the catalog, even though
        /// clearreality-x4 is Light, 4x, and measured ~32x faster than realesrgan-x4 per
        /// CPU tile. This ladder is what the branch fallbacks cannot express: quality
        /// order WITHIN a weight class.
        /// </summary>
        public static string[] AffordableLadder(Weight max, bool isAnime)
        {
            if (max >= Weight.Medium)
            {
                return isAnime
                    ? new[] { "realesrgan-animevideo-x4", "apisr-anime-x2", "anime-compact-x4", "span-x2" }
                    : new[] { "nomos2-realplksr-x4", "span-x4", "lsdir-compact-x4", "span-x2" };
            }
            return isAnime
                ? new[] { "anime-compact-x4", "nomosuni-compact-x2", "fsrcnn-x2" }
                : new[] { "clearreality-x4", "nomosuni-compact-x2", "fsrcnn-x2" };
        }

        /// <summary>Human-readable tier label for signals and substitution reasons.</summary>
        public static string DescribeTier(string? tier) => (tier ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "strong-gpu" => "strong GPU",
            "mid-gpu" => "GPU",
            "igpu" => "integrated GPU",
            "strong-cpu" => "multi-core CPU (no GPU)",
            "weak-cpu" => "weak CPU (no GPU)",
            _ => "unknown hardware",
        };
    }
}
