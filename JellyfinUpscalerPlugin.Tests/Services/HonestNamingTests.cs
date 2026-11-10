using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// The batch ffmpeg path may not name a filter after somebody else's product.
    ///
    /// It used to log "Using NVIDIA VSR (Video Super Resolution)" while running
    /// <c>scale_cuda …interp_algo=lanczos</c> + <c>unsharp_cuda</c>. That is Lanczos with
    /// sharpening. "AMD FSR-style" was <c>scale_vaapi</c>, and both "FSR (FidelityFX Super
    /// Resolution)" and "Anime4K-style shader upscaling" were the same <c>libplacebo …
    /// ewa_lanczos</c> call, differing only in a downscaler argument.
    ///
    /// Three of the four were renamed in v1.8.3.31. The fourth survived, and it is the worst
    /// one: the plugin genuinely ships Anime4K as a client-side GLSL shader, so a server log
    /// line claiming "Anime4K" makes a real feature ambiguous.
    ///
    /// These names are still legitimate where they describe what they actually are — a driver
    /// or client feature the plugin does not control. <c>site/hardware.html</c> uses them that
    /// way and must keep being allowed to.
    /// </summary>
    public class HonestNamingTests
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

        private static string RepoFile(params string[] parts)
            => File.ReadAllText(Path.Combine(new[] { RepoRoot().FullName }.Concat(parts).ToArray()));

        [Theory]
        [InlineData("NVIDIA VSR")]
        [InlineData("FidelityFX")]
        [InlineData("FSR-style")]
        [InlineData("Anime4K")]
        public void The_batch_executor_does_not_name_its_filters_after_other_products(string claim)
        {
            // The executor picks ffmpeg filters. Whatever it logs must describe the filter it
            // just added, because that log line is what a user reads when asking "what did it
            // actually do to my video".
            RepoFile("Services", "ProcessingMethodExecutor.cs")
                .Should().NotContain(claim,
                    $"'{claim}' names somebody else's product, not the filter that runs");
        }

        [Fact]
        public void The_log_lines_say_what_the_filter_is()
        {
            var src = RepoFile("Services", "ProcessingMethodExecutor.cs");

            src.Should().Contain("Using CUDA-Lanczos scaling and sharpening");
            src.Should().Contain("Using VAAPI scaling and sharpening");
            Regex.Matches(src, @"Using libplacebo EWA Lanczos scaling").Count
                .Should().Be(2, "both libplacebo branches run EWA Lanczos and must both say so");
        }

        [Fact]
        public void Renaming_the_log_lines_changed_no_filter()
        {
            // The whole point of this change is that it alters no pixel. If a filter string
            // moved while the names were being fixed, that is a different change wearing this
            // one's clothes.
            var src = RepoFile("Services", "ProcessingMethodExecutor.cs");

            src.Should().Contain("scale_cuda={options.ScaleFactor}*iw:{options.ScaleFactor}*ih:interp_algo=lanczos");
            src.Should().Contain("unsharp_cuda=luma_amount=1.5:chroma_amount=0.5");
            src.Should().Contain("upscaler=ewa_lanczos:downscaler=ewa_lanczos");
            Regex.Matches(src, @"scale_vaapi").Count.Should().BeGreaterThanOrEqualTo(2);
        }

        [Fact]
        public void Documentation_may_still_call_driver_features_by_their_real_names()
        {
            // The guard above must not push the docs into pretending RTX VSR does not exist.
            // Naming it as a CLIENT feature the server cannot invoke is exactly right.
            var hardware = RepoFile("site", "hardware.html");

            hardware.Should().Contain("RTX Video Super Resolution");
            hardware.Should().Contain("driver/GPU features of the viewing device",
                "the page must keep saying whose feature it is");
        }
    }
}