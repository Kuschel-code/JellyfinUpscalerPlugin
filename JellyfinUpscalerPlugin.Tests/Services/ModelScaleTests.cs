using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.3.14 — regression guards for the scale-reporting bug: the AI service uses the
    /// loaded model's native factor and only logs a warning about the requested one
    /// (docker-ai-service/app/main.py, <c>upscale_endpoint</c>). The library scan therefore
    /// logged "scale=2x" from the config while auto-mode handed it a 4x model — a 1080p
    /// batch job silently produced 8K frames.
    /// </summary>
    public class ModelScaleTests
    {
        [Theory]
        [InlineData("realesrgan-x4", 4)]
        [InlineData("span-x2", 2)]
        [InlineData("lapsrn-x8", 8)]
        [InlineData("nomosuni-compact-x2", 2)]
        [InlineData("realesrgan-x2-plus", 2)]     // factor sits in the middle, not at the end
        [InlineData("realesrgan-x4-256", 4)]      // tiled variant: 4x, not 256x
        [InlineData("anime-compact-x4", 4)]
        public void NativeScaleOf_reads_the_catalog_naming_convention(string modelId, int expected)
        {
            ModelScale.NativeScaleOf(modelId).Should().Be(expected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("clearreality")]              // no suffix at all
        [InlineData("omdb-4x-nomos8k-atd")]       // imported ids do not follow the convention
        public void NativeScaleOf_says_unknown_instead_of_guessing(string? modelId)
        {
            // Returning a plausible-looking 2 here would recreate the exact bug this
            // class exists to fix: a number the pipeline reports but cannot honour.
            ModelScale.NativeScaleOf(modelId).Should().Be(0);
        }

        [Fact]
        public void NativeScaleOf_rejects_out_of_range_factors()
        {
            ModelScale.NativeScaleOf("weird-x99").Should().Be(0);
            ModelScale.NativeScaleOf("weird-x0").Should().Be(0);
        }

        [Theory]
        [InlineData(640, 480, 4)]      // SD -> a real restore is worth the compute
        [InlineData(720, 576, 4)]      // PAL
        [InlineData(1280, 720, 2)]     // 720p -> 1440p
        [InlineData(1920, 1080, 2)]    // 1080p -> 4K, NOT 8K
        [InlineData(3840, 2160, 1)]    // already 4K: nothing to gain
        public void TargetScaleFor_never_aims_past_4K(int width, int height, int expected)
        {
            ModelScale.TargetScaleFor(width, height).Should().Be(expected);
        }

        [Fact]
        public void TargetScaleFor_unknown_resolution_picks_the_safe_middle()
        {
            ModelScale.TargetScaleFor(0, 0).Should().Be(2);
        }

        [Fact]
        public void DescribeOutput_shows_the_size_the_user_will_actually_get()
        {
            ModelScale.DescribeOutput(1920, 1080, 2).Should().Be("1920x1080 -> 3840x2160");
            ModelScale.DescribeOutput(1920, 1080, 0).Should().Be("unknown output size");
            ModelScale.DescribeOutput(0, 0, 2).Should().Be("unknown output size");
        }
    }
}
