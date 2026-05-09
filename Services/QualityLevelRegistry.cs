using System;
using System.Collections.Generic;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Single source of truth for the QualityLevel value set. Mirrors the
    /// &lt;option value="..."&gt; values inside &lt;select id="QualityLevel"&gt; in
    /// configurationpage.html. Used by Settings-Import (TryApply) validation and
    /// kept in lockstep with the UI via QualityLevelRegistryTests.HtmlDropdown_*.
    /// </summary>
    /// <remarks>
    /// Pre-v1.7.0 the import allowlist was {fast, medium, high} while the UI
    /// offered {low, medium, high}. "low" was silently rejected on import; the
    /// `low =&gt; fast` normalization in <see cref="ProcessingStrategySelector"/> ran on
    /// the regular-Save path (Jellyfin native config) but never on the import path
    /// (TryApply filtered it out). v1.7.0 unifies the import allowlist with the UI.
    /// </remarks>
    internal static class QualityLevelRegistry
    {
        /// <summary>
        /// Quality levels exposed in the UI and accepted by Settings-Import.
        /// </summary>
        internal static readonly HashSet<string> Levels = new(StringComparer.OrdinalIgnoreCase)
        {
            "low", "medium", "high"
        };
    }
}
