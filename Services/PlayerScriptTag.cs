using System;
using System.Text.RegularExpressions;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// The one &lt;script&gt; tag that loads the player integration into jellyfin-web's index.html.
    /// Shared by the file edit in <see cref="Plugin"/> and by <see cref="PlayerScriptMiddleware"/>,
    /// so both put the same tag in the same place.
    /// </summary>
    public static class PlayerScriptTag
    {
        private static readonly Regex AnyRelease = new(
            @"<script src=""configurationpage\?name=UPSCALERPlayerIntegration[^""]*""></script>",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

        private static readonly Regex HeadEnd = new(@"</head>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

        /// <summary>
        /// The tag for this plugin build. The release query makes browsers fetch the new script after an update.
        /// </summary>
        public static string For(Version? version) =>
            $"<script src=\"configurationpage?name=UPSCALERPlayerIntegration&release={version}\"></script>";

        /// <summary>
        /// Puts exactly <paramref name="tag"/> before &lt;/head&gt;, replacing tags of earlier releases.
        /// Returns false when the page has no &lt;/head&gt;. <paramref name="result"/> is
        /// <paramref name="html"/> itself when the page already carries the tag and nothing else.
        /// </summary>
        public static bool TryInject(string html, string tag, out string result)
        {
            result = html;
            var tags = AnyRelease.Matches(html);
            if (tags.Count == 1 && string.Equals(tags[0].Value, tag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var stripped = tags.Count == 0 ? html : AnyRelease.Replace(html, string.Empty);
            if (!HeadEnd.IsMatch(stripped))
            {
                return false;
            }

            result = HeadEnd.Replace(stripped, tag + "</head>", 1);
            return true;
        }
    }
}
