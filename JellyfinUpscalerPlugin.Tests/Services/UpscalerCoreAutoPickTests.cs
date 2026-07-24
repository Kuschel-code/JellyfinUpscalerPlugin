using JellyfinUpscalerPlugin;
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
    /// v1.8.3.13 — the auto decision must EXPLAIN itself. Before this the reasoning
    /// existed only as LogDebug output (off by default in Jellyfin) and the resolver
    /// returned a bare string, so a substituted model looked like a wrong pick.
    ///
    /// These tests pin the contract the UI depends on: a reason is always present,
    /// the signals reflect the inputs, and a substitution is reported instead of
    /// silently swallowed. The bare <see cref="UpscalerCore.ResolveModelForVideo"/>
    /// must keep returning exactly what the detailed variant picks (no behaviour change).
    /// </summary>
    public class UpscalerCoreAutoPickTests
    {
        private readonly UpscalerCore _core;

        public UpscalerCoreAutoPickTests()
        {
            // Same minimal-mock construction as UpscalerCoreAutoModelTests: the
            // resolver is a pure heuristic and touches none of these dependencies.
            var logger = new Mock<ILogger<UpscalerCore>>().Object;
            var mediaEncoder = new Mock<IMediaEncoder>().Object;
            var fileSystem = new Mock<IFileSystem>().Object;
            var appPaths = new Mock<IApplicationPaths>().Object;
            var httpUpscaler = new HttpUpscalerService(
                new Mock<ILogger<HttpUpscalerService>>().Object,
                new Mock<System.Net.Http.IHttpClientFactory>().Object);

            _core = new UpscalerCore(logger, mediaEncoder, fileSystem, appPaths, httpUpscaler);
        }

        [Fact]
        public void Detailed_always_carries_a_reason_and_signals()
        {
            var pick = _core.ResolveModelForVideoDetailed(
                genres: new[] { "Drama" }, width: 1920, height: 1080, isBatch: true, forceAuto: true);

            Assert.False(string.IsNullOrWhiteSpace(pick.Model));
            Assert.False(string.IsNullOrWhiteSpace(pick.Reason));
            Assert.NotEmpty(pick.Signals);
            Assert.Contains(pick.Signals, s => s.StartsWith("Content:"));
            Assert.Contains(pick.Signals, s => s.StartsWith("Resolution:"));
            Assert.Contains(pick.Signals, s => s.StartsWith("Job:"));
        }

        [Fact]
        public void Signals_report_anime_and_low_resolution()
        {
            var pick = _core.ResolveModelForVideoDetailed(
                genres: new[] { "Animation" }, width: 480, height: 360, isBatch: true, forceAuto: true);

            Assert.Contains(pick.Signals, s => s.Contains("anime", System.StringComparison.OrdinalIgnoreCase));
            Assert.Contains(pick.Signals, s => s.Contains("480x360"));
        }

        [Fact]
        public void Multi_frame_substitution_is_reported_not_silent()
        {
            // edvr-m-x4 / animesr-v2-x4 / realbasicvsr-x4 have no public ONNX build, so
            // the resolver falls back. That fallback is exactly what used to confuse users.
            var pick = _core.ResolveModelForVideoDetailed(
                genres: new[] { "Drama" }, width: 1280, height: 720,
                isBatch: true, inputFrames: 5, forceAuto: true);

            Assert.NotNull(pick.SubstitutedFrom);
            Assert.Equal("edvr-m-x4", pick.SubstitutedFrom);
            Assert.NotEqual(pick.SubstitutedFrom, pick.Model);
            Assert.False(string.IsNullOrWhiteSpace(pick.SubstitutionReason));
            Assert.False(ModelAvailability.IsKnownUnavailable(pick.Model));
        }

        [Fact]
        public void No_substitution_reported_when_the_preferred_model_is_available()
        {
            var pick = _core.ResolveModelForVideoDetailed(
                genres: new[] { "Drama" }, width: 1920, height: 1080, isBatch: true, forceAuto: true);

            Assert.Null(pick.SubstitutedFrom);
            Assert.Null(pick.SubstitutionReason);
        }

        [Theory]
        [InlineData("Animation", 480, 360, true, 5)]
        [InlineData("Drama", 1920, 1080, true, 1)]
        [InlineData("Animation", 1920, 1080, false, 1)]
        [InlineData("Drama", 640, 480, false, 1)]
        public void Wrapper_returns_exactly_what_the_detailed_resolver_picks(
            string genre, int width, int height, bool isBatch, int frames)
        {
            var bare = _core.ResolveModelForVideo(
                genres: new[] { genre }, width: width, height: height,
                isBatch: isBatch, inputFrames: frames, forceAuto: true);
            var detailed = _core.ResolveModelForVideoDetailed(
                genres: new[] { genre }, width: width, height: height,
                isBatch: isBatch, inputFrames: frames, forceAuto: true);

            Assert.Equal(bare, detailed.Model);
        }

        [Fact]
        public void Custom_mode_is_labelled_as_such()
        {
            // forceAuto=false with a configured model = the user's explicit choice.
            var pick = _core.ResolveModelForVideoDetailed(
                genres: new[] { "Animation" }, width: 480, height: 360, isBatch: true, forceAuto: false);

            Assert.Contains("Custom", pick.Reason);
            Assert.Contains(pick.Signals, s => s.Contains("Custom"));
            Assert.Null(pick.SubstitutedFrom);
        }
    }
}
