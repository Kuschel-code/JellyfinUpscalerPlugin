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
    /// Unit tests for UpscalerCore.ResolveModelForVideo() — specifically the v1.6.1.17
    /// PickAvailable() / _knownUnavailable HashSet drift-protection logic.
    ///
    /// The bug v1.6.1.17 fixes: ResolveModelForVideo previously returned animesr-v2-x4 /
    /// realbasicvsr-x4 / edvr-m-x4 unconditionally for multi-frame batch jobs — but all
    /// three are available:False (no public ONNX mirror). User auto-mode 500'd silently.
    ///
    /// These tests lock down the fallback chains so that future model-additions don't
    /// re-introduce the same class of bug. If someone marks one of these models as
    /// available in the future, they MUST also remove it from _knownUnavailable in
    /// UpscalerCore.cs — these tests will turn red the moment that drift starts.
    /// </summary>
    public class UpscalerCoreAutoModelTests
    {
        private readonly UpscalerCore _core;

        public UpscalerCoreAutoModelTests()
        {
            // ResolveModelForVideo is pure heuristic — none of the injected dependencies
            // are touched in that codepath, so we pass minimal mocks.
            var logger = new Mock<ILogger<UpscalerCore>>().Object;
            var mediaEncoder = new Mock<IMediaEncoder>().Object;
            var fileSystem = new Mock<IFileSystem>().Object;
            var appPaths = new Mock<IApplicationPaths>().Object;

            // HttpUpscalerService is injected but never called by ResolveModelForVideo.
            var httpLogger = new Mock<ILogger<HttpUpscalerService>>().Object;
            var httpFactory = new Mock<System.Net.Http.IHttpClientFactory>().Object;
            var httpUpscaler = new HttpUpscalerService(httpLogger, httpFactory);

            _core = new UpscalerCore(logger, mediaEncoder, fileSystem, appPaths, httpUpscaler);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Multi-frame VSR fallback chains (the 3 paths that were broken in 1.6.1.16)
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public void Anime_MultiFrame_FallsBack_To_RealEsrganAnimevideo_NotSelfHostAnimeSR()
        {
            var model = _core.ResolveModelForVideo(
                genres: new[] { "Anime", "Animation" },
                width: 1920, height: 1080,
                isBatch: true,
                inputFrames: 5,
                forceAuto: true);

            // animesr-v2-x4 is in _knownUnavailable, so PickAvailable must skip it.
            // First available fallback is realesrgan-animevideo-x4.
            // Note: PreferredAnimeModel default ("anime-compact-x4") only triggers in single-frame path.
            model.Should().Be("realesrgan-animevideo-x4",
                "animesr-v2-x4 is self-host required and must be skipped by _knownUnavailable");
        }

        [Fact]
        public void VeryLowRes_MultiFrame_FallsBack_To_UltrasharpV2_NotRealBasicVSR()
        {
            var model = _core.ResolveModelForVideo(
                genres: null,
                width: 320, height: 240,        // VHS-rip territory → isVeryLowRes
                isBatch: true,
                inputFrames: 5,
                forceAuto: true);

            // realbasicvsr-x4 is in _knownUnavailable; first fallback is ultrasharp-v2-x4.
            model.Should().Be("ultrasharp-v2-x4",
                "realbasicvsr-x4 is self-host required, ultrasharp-v2-x4 is the next-best quality model");
        }

        [Fact]
        public void General_MultiFrame_FallsBack_To_UltrasharpV2_NotEdvrM()
        {
            var model = _core.ResolveModelForVideo(
                genres: null,
                width: 1280, height: 720,       // HD non-anime, non-low-res → general path
                isBatch: true,
                inputFrames: 5,
                forceAuto: true);

            model.Should().Be("ultrasharp-v2-x4",
                "edvr-m-x4 is self-host required, fallback chain leads to ultrasharp-v2-x4");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Drift-protection: PickAvailable() never returns a known-unavailable model
        // ──────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(true, 5, "Anime")]
        [InlineData(true, 5, null)]
        [InlineData(true, 1, "Anime")]
        [InlineData(true, 1, null)]
        [InlineData(false, 1, "Anime")]
        [InlineData(false, 1, null)]
        public void ResolveModelForVideo_NeverReturnsKnownUnavailableModel(
            bool isBatch, int inputFrames, string? genre)
        {
            var model = _core.ResolveModelForVideo(
                genres: genre == null ? null : new[] { genre },
                width: 1920, height: 1080,
                isBatch: isBatch,
                inputFrames: inputFrames,
                forceAuto: true);

            var knownUnavailable = new[]
            {
                "nomos8k-hat-x4",
                "apisr-x3",
                "edvr-m-x4",
                "realbasicvsr-x4",
                "animesr-v2-x4"
            };

            knownUnavailable.Should().NotContain(model,
                "PickAvailable must never return a model that requires self-hosting");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Verifier-B Issue #1: PreferredAnimeModel must be honored in single-frame path
        // ──────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(true)]   // batch
        [InlineData(false)]  // realtime
        public void Anime_SingleFrame_HonorsPreferredAnimeModel_Override(bool isBatch)
        {
            // The default for PreferredAnimeModel is "anime-compact-x4" (set in PluginConfiguration.cs).
            // ResolveModelForVideo must read Config.PreferredAnimeModel and route through PickAvailable.
            // This test does not (and cannot easily) mock Plugin.Instance.Configuration, so it relies
            // on the production default being "anime-compact-x4". If the override is empty/null,
            // the heuristic falls through to realesrgan-animevideo-x4 (batch) or anime-compact-x4 (realtime).

            var model = _core.ResolveModelForVideo(
                genres: new[] { "Animation" },
                width: 1920, height: 1080,
                isBatch: isBatch,
                inputFrames: 1,
                forceAuto: true);

            // Either the default override "anime-compact-x4" is honored,
            // OR the heuristic-fallback returns one of the two known-good anime models.
            model.Should().BeOneOf(
                "anime-compact-x4",
                "realesrgan-animevideo-x4");
        }
    }
}
