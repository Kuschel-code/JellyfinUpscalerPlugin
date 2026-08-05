using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using FluentAssertions;
using JellyfinUpscalerPlugin.Controllers;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.3.24 — the playback wiring for object masking (discussion #11).
    ///
    /// v1.8.3.23 shipped the masking as a service endpoint that nothing called. What the
    /// requester actually asked for was a filter during playback, so this release connects
    /// the capture loop the player already ran to an endpoint that masks.
    /// </summary>
    public class ObjectMaskingTests
    {
        private static string RepoFile(params string[] parts)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JellyfinUpscalerPlugin.csproj")))
            {
                dir = dir.Parent;
            }
            dir.Should().NotBeNull();
            return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
        }

        // ── The query string, which is where a locale bug would land ──────────

        [Fact]
        public void The_confidence_is_formatted_invariantly()
        {
            // Same class of bug as fps=23,976 in v1.8.3.22: on de-DE "0.35" becomes "0,35",
            // and the AI service parses that as a float and fails the request. It is a query
            // parameter rather than an ffmpeg filter, so it fails differently - but it fails.
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var config = new PluginConfiguration { ObjectMaskConfidence = 0.35 };

                UpscalerController.BuildObjectMaskQuery(config)
                    .Should().Contain("confidence=0.35").And.NotContain("0,35");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Fact]
        public void A_class_list_survives_as_a_list()
        {
            var config = new PluginConfiguration { ObjectMaskClasses = "dog,cat" };

            var query = UpscalerController.BuildObjectMaskQuery(config);

            // The service splits on a comma, so the comma has to arrive - encoded is fine,
            // dropped or turned into a second parameter is not.
            query.Should().MatchRegex(@"classes=dog(,|%2C)cat");
            Regex.Matches(query, "classes=").Count.Should().Be(1);
        }

        [Fact]
        public void A_class_field_holding_an_ampersand_cannot_inject_a_parameter()
        {
            // The value comes from a text box in the admin UI. Even trusted input should not
            // be able to grow the query.
            var config = new PluginConfiguration { ObjectMaskClasses = "dog&mode=blur" };

            var query = UpscalerController.BuildObjectMaskQuery(config);

            Regex.Matches(query, "mode=").Count.Should().Be(1, "the injected mode must not become a parameter");
            query.Should().Contain("mode=box", "the configured mode must win");
        }

        [Fact]
        public void An_empty_class_list_falls_back_to_the_animal_group()
        {
            // An empty list would make the service reject every frame, which reads as
            // "masking is broken" rather than "you cleared the field".
            UpscalerController.BuildObjectMaskQuery(new PluginConfiguration { ObjectMaskClasses = "  " })
                .Should().Contain("classes=animals");
        }

        [Fact]
        public void An_unknown_mode_falls_back_to_box_rather_than_reaching_the_service()
        {
            UpscalerController.BuildObjectMaskQuery(new PluginConfiguration { ObjectMaskMode = "nonsense" })
                .Should().Contain("mode=box");
            UpscalerController.BuildObjectMaskQuery(new PluginConfiguration { ObjectMaskMode = "blur" })
                .Should().Contain("mode=blur");
        }

        // ── Configuration bounds ─────────────────────────────────────────────

        [Theory]
        [InlineData(-50, 0)]
        [InlineData(12, 12)]
        [InlineData(9999, 200)]
        public void Padding_is_clamped(int input, int expected)
        {
            new PluginConfiguration { ObjectMaskPadding = input }.ObjectMaskPadding.Should().Be(expected);
        }

        [Theory]
        [InlineData(-1.0, 0.05)]
        [InlineData(0.5, 0.5)]
        [InlineData(5.0, 0.95)]
        public void Confidence_is_clamped(double input, double expected)
        {
            new PluginConfiguration { ObjectMaskConfidence = input }.ObjectMaskConfidence.Should().Be(expected);
        }

        [Fact]
        public void No_detection_model_is_configured_by_default()
        {
            // None is bundled, because every catalog entry carries a verified sha256 pin.
            // A default naming a model would promise something that is not on disk.
            new PluginConfiguration().ObjectMaskModel.Should().BeEmpty();
            new PluginConfiguration().EnableObjectMasking.Should().BeFalse();
        }

        // ── The wiring, which is the whole point of this release ─────────────

        [Fact]
        public void The_player_capture_loop_can_reach_the_masking_endpoint()
        {
            // v1.8.3.23's gap: masking existed and nothing called it.
            var js = RepoFile("Configuration", "player-integration.js");
            js.Should().Contain("Upscaler/detect-mask");
            js.Should().Contain("_objectMaskEnabled",
                "the loop must choose its endpoint from configuration");
        }

        [Fact]
        public void The_endpoint_choice_is_made_once_per_playback_not_per_frame()
        {
            // Flipping the target mid-stream would interleave masked and unmasked frames on
            // the same overlay, which looks like flicker rather than a setting change.
            var js = RepoFile("Configuration", "player-integration.js");
            js.Should().Contain("this._objectMaskEnabled = config.EnableObjectMasking === true;");
        }

        [Fact]
        public void The_proxy_route_exists_on_the_plugin()
        {
            var ctrl = RepoFile("Controllers", "UpscalerController.cs");
            ctrl.Should().Contain("[HttpPost(\"detect-mask\")]");
            ctrl.Should().Contain("[HttpPost(\"object-mask/load-model\")]");
        }

        [Fact]
        public void Loading_a_detection_model_requires_an_administrator()
        {
            // It makes the service read a file and hold it in memory; the frame proxy is for
            // every viewer, this is not.
            var ctrl = RepoFile("Controllers", "UpscalerController.cs");
            var idx = ctrl.IndexOf("[HttpPost(\"object-mask/load-model\")]");
            idx.Should().BeGreaterThan(0);
            ctrl.Substring(idx, 200).Should().Contain("RequiresElevation");
        }

        [Fact]
        public void The_frame_proxy_caps_the_request_body()
        {
            // Same reason as the face-restore proxy: an unbounded Request.Body copy runs
            // into the Jellyfin heap.
            var ctrl = RepoFile("Controllers", "UpscalerController.cs");
            var idx = ctrl.IndexOf("[HttpPost(\"detect-mask\")]");
            ctrl.Substring(idx, 900).Should().Contain("MaxFrameBytes");
        }

        [Fact]
        public void Masking_is_refused_when_it_is_switched_off()
        {
            // Otherwise a stale player tab keeps hammering the service after the admin
            // turned the feature off.
            var ctrl = RepoFile("Controllers", "UpscalerController.cs");
            var idx = ctrl.IndexOf("[HttpPost(\"detect-mask\")]");
            ctrl.Substring(idx, 2000).Should().Contain("EnableObjectMasking != true");
        }
    }
}
