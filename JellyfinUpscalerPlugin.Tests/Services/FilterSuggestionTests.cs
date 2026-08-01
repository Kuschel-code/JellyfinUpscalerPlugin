using System.IO;
using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.3.20 — the filter is a SUGGESTION, never an action.
    ///
    /// The UI promised "AI picks model + filter per video" and the code half-kept it:
    /// v1.8.3.14 stopped auto overwriting a preset the user had chosen, but it still
    /// wrote its own choice whenever the field was empty — so the first playback quietly
    /// decided the user's look, and the setting then read as if they had picked it.
    ///
    /// A model swap is a correction the user cannot make better themselves. A filter is
    /// taste. These tests pin that separation: the suggestion carries a reason so it can
    /// be judged, and nothing in the resolver applies it.
    /// </summary>
    public class FilterSuggestionTests
    {
        private readonly UpscalerCore _core;

        public FilterSuggestionTests()
        {
            var httpUpscaler = new HttpUpscalerService(
                new Mock<ILogger<HttpUpscalerService>>().Object,
                new Mock<System.Net.Http.IHttpClientFactory>().Object);
            _core = new UpscalerCore(
                new Mock<ILogger<UpscalerCore>>().Object,
                new Mock<IMediaEncoder>().Object,
                new Mock<IFileSystem>().Object,
                new Mock<IApplicationPaths>().Object,
                httpUpscaler);
        }

        [Theory]
        [InlineData("Animation", "vivid")]
        [InlineData("Horror", "drama")]
        [InlineData("Thriller", "drama")]
        [InlineData("Sci-Fi", "cyberpunk")]
        [InlineData("Documentary", "sharp-hd")]
        public void A_suggestion_always_names_the_signal_it_came_from(string genre, string expectedPreset)
        {
            // A suggestion the user cannot judge is worse than none: they would have to
            // guess whether "vivid" came from the genre, the resolution or a coin flip.
            var pick = _core.ResolveFilterForVideoDetailed(new[] { genre }, 1920, 1080);

            pick.Preset.Should().Be(expectedPreset);
            pick.Reason.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void Without_a_signal_the_suggestion_is_none_not_a_guess()
        {
            var pick = _core.ResolveFilterForVideoDetailed(genres: null, width: 1920, height: 1080);

            pick.Preset.Should().Be("none", "no filter beats a wrong filter");
            pick.Reason.Should().Contain("No strong signal");
        }

        [Fact]
        public void A_low_resolution_source_is_a_signal_in_its_own_right()
        {
            var pick = _core.ResolveFilterForVideoDetailed(genres: null, width: 640, height: 480);

            pick.Preset.Should().Be("sharp-hd");
            pick.Reason.Should().Contain("640x480", "the reason has to name the numbers it reacted to");
        }

        [Fact]
        public void The_bare_overload_still_returns_exactly_what_the_detailed_one_picks()
        {
            // ResolveFilterForVideo is public API with an existing caller. Splitting the
            // reason out must not change a single decision.
            foreach (var (genres, w, h) in new[]
            {
                ((string[]?)new[] { "Animation" }, 1920, 1080),
                (new[] { "Horror" }, 1920, 1080),
                (new[] { "Documentary" }, 1280, 720),
                (null, 640, 480),
                (null, 1920, 1080),
            })
            {
                _core.ResolveFilterForVideo(genres, w, h)
                    .Should().Be(_core.ResolveFilterForVideoDetailed(genres, w, h).Preset);
            }
        }

        // ── The other half of the contract: nothing applies a filter behind the user's
        //    back. That lives in JS, so it is guarded as a source assertion — the same
        //    technique check_ui_field_consistency.py uses for element ids.

        private static string PlayerJs()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JellyfinUpscalerPlugin.csproj")))
            {
                dir = dir.Parent;
            }
            dir.Should().NotBeNull("the test must be able to find the repository root");
            return File.ReadAllText(Path.Combine(dir!.FullName, "Configuration", "player-integration.js"));
        }

        [Fact]
        public void The_player_no_longer_carries_an_automatic_filter_applier()
        {
            // _applyAutoFilter was the function that wrote a preset during playback.
            // Its replacement, _applySuggestedFilter, only ever runs from a click.
            PlayerJs().Should().NotContain("_applyAutoFilter",
                "auto must not write a filter preset on its own any more");
        }

        [Fact]
        public void The_suggestion_is_rendered_only_when_it_differs_from_the_current_preset()
        {
            // Re-offering the preset the user already has is noise, and its Apply button
            // would be a no-op that looks like a broken control.
            PlayerJs().Should().Contain("suggested.toLowerCase() !== currentPreset",
                "the suggestion block must be gated on an actual difference");
        }

        [Fact]
        public void The_apply_path_is_the_same_endpoint_a_manual_pick_uses()
        {
            // A second write path would drift from the manual one; this is the reason
            // _applySuggestedFilter posts to /filter-config rather than patching config.
            PlayerJs().Should().Contain("_applySuggestedFilter");
            PlayerJs().Should().Contain("Upscaler/filter-config");
        }
    }
}
