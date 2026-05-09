using System;
using System.Collections.Generic;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Single source of truth for the RealtimeMode value set.
    /// Mirrors &lt;option value="..."&gt; values inside &lt;select id="RealtimeMode"&gt;
    /// in configurationpage.html. Used by Settings-Import (TryApply) validation.
    /// </summary>
    /// <remarks>
    /// Two distinct sets exist:
    ///
    /// <see cref="UiModes"/> - what the UI dropdown advertises (HTML drift-lock asserts equality).
    /// <see cref="AcceptedAtImport"/> - UI modes PLUS backwards-compat aliases for v1.6.x configs.
    ///
    /// Pre-v1.7.1 the import allowlist was a 5-entry inline array in
    /// <c>UpscalerController.cs:1466</c>. v1.7.1 lifts it here for symmetry with
    /// CodecRegistry / QualityLevelRegistry / ButtonPositionRegistry.
    /// </remarks>
    internal static class RealtimeModeRegistry
    {
        /// <summary>
        /// Modes the UI dropdown exposes. v1.7.0 introduced 4 honest options
        /// (auto / lanczos / anime4k / server). v1.7.1 adds "ai-webgpu" for
        /// onnxruntime-web Real-ESRGAN realtime.
        /// </summary>
        internal static readonly HashSet<string> UiModes = new(StringComparer.OrdinalIgnoreCase)
        {
            "auto", "lanczos", "anime4k", "ai-webgpu", "server"
        };

        /// <summary>
        /// Backwards-compat aliases accepted at Settings-Import but NOT exposed in UI.
        /// "webgl" was the v1.6.x label for what is now "lanczos" - player-integration.js
        /// re-maps it at runtime so users with a v1.6.x saved config keep working.
        /// </summary>
        internal static readonly HashSet<string> BackwardsCompatAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            "webgl"
        };

        /// <summary>
        /// Union of UI modes and backwards-compat aliases. This is what TryApply
        /// validates against - the import endpoint accepts both new and legacy values.
        /// </summary>
        internal static readonly HashSet<string> AcceptedAtImport = BuildAccepted();

        private static HashSet<string> BuildAccepted()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in UiModes) set.Add(m);
            foreach (var a in BackwardsCompatAliases) set.Add(a);
            return set;
        }
    }
}
