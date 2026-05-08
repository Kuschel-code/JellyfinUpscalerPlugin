using System;
using System.Collections.Generic;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Single source of truth (C# side) for which model IDs the Docker AI service has marked
    /// as <c>available: False</c> upstream. Any resolver that hardcodes model IDs (auto-mode
    /// resolvers, hardware-recommendation heuristics, fallback chains) MUST consult this class
    /// before emitting an ID, otherwise it silently leaks self-host-required models into UX
    /// flows where Jellyfin can't fulfill the request.
    ///
    /// Source of truth on the Python side is <c>docker-ai-service/app/main.py</c> AVAILABLE_MODELS
    /// (entries with <c>"available": False</c>). The list is mirrored here as a HashSet because
    /// the Plugin doesn't always have HTTP access to the Docker service at startup.
    ///
    /// Drift-protection: <c>JellyfinUpscalerPlugin.Tests.Services.UpscalerCoreAutoModelTests</c>
    /// contains regression-guards that will fail the build if a resolver emits an ID in this set.
    /// If you flip a model in main.py from <c>available: False</c> to <c>True</c>, you MUST also
    /// remove it here, or the next CI run will fail with a clear "live registry says available
    /// but plugin still treats it as unavailable" mismatch.
    /// </summary>
    /// <remarks>
    /// Introduced in v1.6.1.19 as the structural fix for the drift class that v1.6.1.17 and
    /// v1.6.1.18 patched only point-by-point. Before v1.6.1.19, this set was duplicated as
    /// <c>UpscalerCore._knownUnavailable</c> (gated since v1.6.1.17) and was missing entirely
    /// from <c>HardwareBenchmarkService.CalculateOptimalSettings</c> (caught by the v1.6.1.18
    /// post-release audit). Centralising into one class collapses 3+ potential drift sites
    /// into 1.
    ///
    /// As of v1.6.1.19:
    ///   nomos8k-hat-x4    — HAT LayerNorm dynamic-shape ops fail on CPUExecutionProvider
    ///   apisr-x3          — Xenova HF repo returns 401 anonymously (gated)
    ///   edvr-m-x4         — Multi-frame VSR, no public ONNX mirror (self-host)
    ///   realbasicvsr-x4   — Multi-frame VSR, no public ONNX mirror (self-host)
    ///   animesr-v2-x4     — Multi-frame Anime VSR, no public ONNX mirror (self-host)
    /// </remarks>
    internal static class ModelAvailability
    {
        /// <summary>
        /// Models the Docker AI service has marked as <c>available: False</c>. Case-insensitive
        /// because saved configs and HTTP request bodies sometimes mismatch on letter casing.
        /// </summary>
        public static readonly HashSet<string> KnownUnavailable = new(StringComparer.OrdinalIgnoreCase)
        {
            "nomos8k-hat-x4",
            "apisr-x3",
            "edvr-m-x4",
            "realbasicvsr-x4",
            "animesr-v2-x4"
        };

        /// <summary>
        /// True if <paramref name="modelId"/> is known to be unavailable (would 404 on download
        /// or fail on inference). Use to gate any auto-suggested or default model ID before
        /// emitting it to a user-visible code path.
        /// </summary>
        public static bool IsKnownUnavailable(string? modelId)
        {
            return !string.IsNullOrWhiteSpace(modelId) && KnownUnavailable.Contains(modelId);
        }

        /// <summary>
        /// Pick the first available model from a preferred → fallback chain.
        /// Skips any model in <see cref="KnownUnavailable"/>; falls back to <c>realesrgan-x4</c>
        /// (Plugin default, always available) when every candidate is unavailable.
        /// Caller is responsible for logging — this method is pure.
        /// </summary>
        /// <param name="preferred">First-choice model ID</param>
        /// <param name="fallbacks">Ordered fallback IDs (later entries are lower priority)</param>
        /// <returns>The first non-unavailable ID from <c>{preferred} ++ fallbacks</c>, or
        /// <c>realesrgan-x4</c> if every candidate is unavailable.</returns>
        public static string PickAvailable(string preferred, params string[] fallbacks)
        {
            if (!IsKnownUnavailable(preferred))
                return preferred;

            foreach (var fb in fallbacks)
            {
                if (string.IsNullOrWhiteSpace(fb)) continue;
                if (!IsKnownUnavailable(fb))
                    return fb;
            }

            return "realesrgan-x4";
        }
    }
}
