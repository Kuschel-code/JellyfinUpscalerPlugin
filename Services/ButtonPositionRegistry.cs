using System;
using System.Collections.Generic;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Single source of truth for the ButtonPosition value set. Mirrors the
    /// &lt;option value="..."&gt; values inside &lt;select id="ButtonPosition"&gt; in
    /// configurationpage.html. Used by Settings-Import (TryApply) validation and
    /// player-integration.js (renders the player button at this position via the
    /// matching <c>.ai-menu--{value}</c> CSS class).
    /// </summary>
    /// <remarks>
    /// Pre-v1.7.0 the import allowlist was {left, right} while the UI offered
    /// {left, right, center} and player-integration.js shipped full <c>.ai-menu--center</c>
    /// CSS plus an <c>aiMenuInCenter</c> keyframe animation. Picking "Center" in the
    /// UI then importing settings reverted to "right" silently. v1.7.0 unifies the
    /// allowlist; keep this set equal to the UI dropdown via the drift-lock test.
    /// </remarks>
    internal static class ButtonPositionRegistry
    {
        /// <summary>
        /// Button positions exposed in the UI and accepted by Settings-Import.
        /// Each value has a matching <c>.ai-menu--{value}</c> CSS class in
        /// player-integration.js (around line 1838).
        /// </summary>
        internal static readonly HashSet<string> Positions = new(StringComparer.OrdinalIgnoreCase)
        {
            "left", "right", "center"
        };
    }
}
