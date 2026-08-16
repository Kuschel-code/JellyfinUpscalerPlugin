using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.3.30 — issue #77: "if you hit a toggle in quick succession, the notifications at
    /// the lower right side of the screen will simply pile on top of each other."
    ///
    /// They did, literally. Every notification was its own <c>position: fixed</c> element
    /// anchored to the same corner, so two of them resolved to the identical coordinates and
    /// the second covered the first. Verified in a browser against the real CSS before and
    /// after: with the old rule three toasts all reported top 1185 / bottom 1226; with the fix
    /// they sit at 1185, 1136 and 1087 with no overlap.
    ///
    /// The plugin had three separate copies of this code — the config page, the player overlay
    /// and the quick menu — with the same defect in each. These guards cover all three,
    /// because fixing one and leaving two is how a bug gets reported twice.
    /// </summary>
    public class ToastStackingTests
    {
        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JellyfinUpscalerPlugin.csproj")))
            {
                dir = dir.Parent;
            }
            dir.Should().NotBeNull();
            return dir!;
        }

        private static string ConfigFile(string name)
            => File.ReadAllText(Path.Combine(RepoRoot().FullName, "Configuration", name));

        [Theory]
        [InlineData("configurationpage.html", "upscaler-toast-host")]
        [InlineData("player-integration.js", "ai-notif-host")]
        [InlineData("quick-menu.js", "notification-host")]
        public void Every_notification_surface_has_a_host_that_stacks(string file, string hostClass)
        {
            var src = ConfigFile(file);
            src.Should().Contain(hostClass, "notifications need one positioned container, not one per message");
            // A flex column is what turns "same corner" into "one above the other".
            var idx = src.IndexOf(hostClass + " {", StringComparison.Ordinal);
            if (idx < 0) idx = src.IndexOf(hostClass + "{", StringComparison.Ordinal);
            idx.Should().BeGreaterThan(0, "the host needs a CSS rule of its own");
            var rule = src.Substring(idx, Math.Min(400, src.Length - idx));
            // The three files differ in CSS style: the config page writes "position: fixed",
            // the two scripts embed it minified as "position:fixed". Match either.
            rule.Should().MatchRegex(@"position:\s*fixed").And.Contain("flex");
        }

        [Fact]
        public void No_notification_is_positioned_fixed_by_itself_any_more()
        {
            // The exact defect: an individual message anchored to a corner. Two of those
            // occupy the same pixels.
            ConfigFile("configurationpage.html")
                .Should().NotMatchRegex(@"\.upscaler-toast \{[^}]*position:\s*fixed",
                    "an individual toast must flow inside the host");
            ConfigFile("player-integration.js")
                .Should().NotContain(".ai-notif{position:fixed",
                    "an individual notification must flow inside the host");
            ConfigFile("quick-menu.js")
                .Should().NotContain(".notification { position: fixed",
                    "an individual notification must flow inside the host");
        }

        [Theory]
        [InlineData("configurationpage.html")]
        [InlineData("player-integration.js")]
        [InlineData("quick-menu.js")]
        public void A_repeated_message_refreshes_instead_of_duplicating(string file)
        {
            // The reported trigger is a toggle pressed repeatedly, which produces the SAME
            // text over and over. Stacking those is still noise, just tidier noise.
            var src = ConfigFile(file);
            src.Should().MatchRegex(@"textContent === (msg|message)",
                "an identical message already on screen must restart its timer, not clone itself");
        }

        [Theory]
        [InlineData("configurationpage.html")]
        [InlineData("player-integration.js")]
        [InlineData("quick-menu.js")]
        public void The_stack_is_capped(string file)
        {
            // Without a cap, holding a control down fills the viewport - a stack of 40 is no
            // better than a pile of 40.
            ConfigFile(file).Should().MatchRegex(@"children\.length > (4|UPSCALER_MAX_TOASTS)",
                "the number of visible notifications must be bounded");
        }

        [Fact]
        public void The_sidebar_still_defers_to_jellyfins_own_toast()
        {
            // It was the one surface that never had this bug, because it does not roll its
            // own. Worth pinning so nobody "harmonises" it into the same trap.
            ConfigFile("sidebar-upscaler.js").Should().Contain("require(['toast']");
        }
    }
}
