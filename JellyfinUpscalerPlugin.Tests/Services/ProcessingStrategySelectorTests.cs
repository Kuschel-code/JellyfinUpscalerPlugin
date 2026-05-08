using FluentAssertions;
using JellyfinUpscalerPlugin.Models;
using JellyfinUpscalerPlugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.6.1.21 (P0c) regression guards for the ProcessingStrategySelector substring-matcher fix.
    /// </summary>
    /// <remarks>
    /// Background:
    /// v1.6.1.18 introduced two substring matchers (`compact` and `realplksr`) intended as a forgiving
    /// safety-net for future fast/video-fast model additions. v1.6.1.21 audit caught both as too broad:
    ///   - "compact" matched anime-compact-x4 (category=anime, NOT video-fast → frame drops)
    ///   - "realplksr" matched nomos2-realplksr-x4 (category=video-quality, 30 MB DAT2-class)
    /// Both led to RealTime-AI accepting models that drop frames mid-playback.
    ///
    /// These tests lock the post-v1.6.1.21 behavior:
    /// - Models that legitimately belong to fast/video-fast (in HashSet) → accepted.
    /// - Architecture-prefix matches (fsrcnn/espcn/span) → accepted (unambiguous CPU-friendly arch).
    /// - Models matching only the v1.6.1.18 substring matchers (compact/realplksr) but NOT in
    ///   HashSet → rejected. This is the regression-guard.
    /// </remarks>
    public class ProcessingStrategySelectorTests
    {
        private readonly ProcessingStrategySelector _selector;

        public ProcessingStrategySelectorTests()
        {
            _selector = new ProcessingStrategySelector(NullLogger.Instance);
        }

        private static VideoInfo HdInput() =>
            new() { Width = 1920, Height = 1080, FrameRate = 30 };

        private static HardwareProfile CapableHardware() =>
            new() { CudaAvailable = true, BenchmarkFps = 60 };

        private static VideoProcessingOptions OptionsFor(string model) =>
            new() { Model = model, EnableRealTimeProcessing = true };

        // ──────────────────────────────────────────────────────────────────────
        // Models that ARE in fastModels HashSet must be accepted.
        // ──────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("bhi-realplksr-x4")]      // category=video-fast, "Speed Champion" since v1.6.1.17
        [InlineData("nomosuni-compact-x2")]   // category=video-fast, 2.4 MB compact
        [InlineData("lsdir-compact-x4")]      // category=video-fast, 2.5 MB compact
        [InlineData("swinir-small-x2")]       // category=video-fast, 8 MB lightweight transformer
        [InlineData("clearreality-x4")]       // category=video-fast, 1.7 MB
        public void ModelInHashSet_IsRealTimeFeasible(string modelId)
        {
            var feasible = _selector.IsRealTimeAIFeasible(HdInput(), CapableHardware(), OptionsFor(modelId));
            feasible.Should().BeTrue($"{modelId} is in the v1.6.1.18 fastModels HashSet");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Architecture-prefix matchers (kept after v1.6.1.21).
        // ──────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("fsrcnn-x2")]  // explicit fsrcnn architecture
        [InlineData("fsrcnn-x4")]
        [InlineData("espcn-x2")]
        [InlineData("span-x4")]    // explicit span architecture
        public void ModelWithFastArchPrefix_IsRealTimeFeasible(string modelId)
        {
            var feasible = _selector.IsRealTimeAIFeasible(HdInput(), CapableHardware(), OptionsFor(modelId));
            feasible.Should().BeTrue($"{modelId} matches the fsrcnn/espcn/span prefix matcher");
        }

        // ──────────────────────────────────────────────────────────────────────
        // v1.6.1.21 P0c: substring matchers `compact` and `realplksr` REMOVED.
        // Models that matched ONLY via those substrings (but aren't in HashSet) must be rejected.
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public void AnimeCompactX4_IsNotRealTimeFeasible_NotVideoFast()
        {
            // anime-compact-x4 is category=anime, NOT video-fast — used to be falsely accepted by
            // the v1.6.1.18 "compact" substring matcher. v1.6.1.21 removed that matcher.
            var feasible = _selector.IsRealTimeAIFeasible(HdInput(), CapableHardware(), OptionsFor("anime-compact-x4"));
            feasible.Should().BeFalse(
                "anime-compact-x4 is anime-category, not video-fast — frame drops at runtime");
        }

        [Fact]
        public void Nomos2RealplksrX4_IsNotRealTimeFeasible_NotVideoFast()
        {
            // nomos2-realplksr-x4 is category=video-quality (DAT2-class, 30 MB) — used to be falsely
            // accepted by the v1.6.1.18 "realplksr" substring matcher. v1.6.1.21 removed that matcher.
            var feasible = _selector.IsRealTimeAIFeasible(HdInput(), CapableHardware(), OptionsFor("nomos2-realplksr-x4"));
            feasible.Should().BeFalse(
                "nomos2-realplksr-x4 is video-quality, not video-fast — frame drops at runtime");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Existing guards that must keep working post-v1.6.1.21.
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public void RealTimeProcessingDisabled_IsNotFeasible()
        {
            var opts = new VideoProcessingOptions { Model = "fsrcnn-x2", EnableRealTimeProcessing = false };
            _selector.IsRealTimeAIFeasible(HdInput(), CapableHardware(), opts).Should().BeFalse();
        }

        [Fact]
        public void Resolution4K_IsNotFeasible_ExceedsThreshold()
        {
            var fourK = new VideoInfo { Width = 3840, Height = 2160, FrameRate = 30 };
            _selector.IsRealTimeAIFeasible(fourK, CapableHardware(), OptionsFor("fsrcnn-x2")).Should().BeFalse();
        }
    }
}
