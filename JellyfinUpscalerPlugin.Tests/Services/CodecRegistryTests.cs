using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.6.1.23 (P0) regression guards for CodecRegistry &lt;-&gt; UI dropdown sync.
    /// </summary>
    /// <remarks>
    /// Background:
    /// Before v1.6.1.23 four code paths each had their own inline OutputCodec allowlist:
    ///   - UpscalerController save endpoint accepted only 3 of 12 UI options (libx264, libx265, copy)
    ///   - VideoFrameProcessor.ReconstructVideoAsync accepted 7
    ///   - ProcessingMethodExecutor realtime path accepted 6 (no "copy")
    ///   - ProcessingMethodExecutor batch path accepted all 12
    /// User-impact: picking AV1/NVENC/QSV in the dropdown silently fell back to libx264 on save.
    ///
    /// CodecRegistry consolidates the allowlist. These tests prevent two failure modes:
    /// 1. Drift: UI gets a new &lt;option&gt; without updating CodecRegistry.OutputCodecs
    ///    (or vice versa). The HTML-parsing test would catch that.
    /// 2. Realtime/Batch confusion: someone adds a slow-software codec to the realtime set.
    ///    The subset test would catch that.
    /// </remarks>
    public class CodecRegistryTests
    {
        [Fact]
        public void OutputCodecs_HasExactly12Entries_LockingDriftAgainstUI()
        {
            // Drift-lock: if you add or remove a codec you MUST update both the UI and this count.
            CodecRegistry.OutputCodecs.Should().HaveCount(12,
                "the settings UI #OutputCodec dropdown advertises 12 codecs across 4 optgroups");
        }

        [Theory]
        [InlineData("libx264")]
        [InlineData("libx265")]
        [InlineData("libsvtav1")]
        [InlineData("libaom-av1")]
        [InlineData("libvpx-vp9")]
        [InlineData("h264_nvenc")]
        [InlineData("hevc_nvenc")]
        [InlineData("av1_nvenc")]
        [InlineData("h264_qsv")]
        [InlineData("hevc_qsv")]
        [InlineData("av1_qsv")]
        [InlineData("copy")]
        public void OutputCodecs_ContainsEachUIDropdownOption(string codec)
        {
            CodecRegistry.OutputCodecs.Should().Contain(codec,
                "this codec is offered in the #OutputCodec dropdown and must be accepted by Save");
        }

        [Fact]
        public void OutputCodecs_IsCaseInsensitive_SoUIOrUserCannotBreakSaveByCasing()
        {
            CodecRegistry.OutputCodecs.Contains("LIBX264").Should().BeTrue();
            CodecRegistry.OutputCodecs.Contains("H264_NVENC").Should().BeTrue();
            CodecRegistry.OutputCodecs.Contains("Copy").Should().BeTrue();
        }

        [Fact]
        public void RealtimeOutputCodecs_IsStrictSubsetOfOutputCodecs()
        {
            CodecRegistry.RealtimeOutputCodecs.Should()
                .BeSubsetOf(CodecRegistry.OutputCodecs,
                    "every realtime-eligible codec must also be valid in batch/save paths");
            CodecRegistry.RealtimeOutputCodecs.Count
                .Should().BeLessThan(CodecRegistry.OutputCodecs.Count,
                    "realtime is intentionally narrower (no copy, no software AV1/VP9)");
        }

        [Fact]
        public void RealtimeOutputCodecs_DoesNotContainCopyOrSlowSoftwareCodecs()
        {
            CodecRegistry.RealtimeOutputCodecs.Should().NotContain("copy",
                "stream-copy is meaningless when re-encoding upscaled frames into a pipe");
            CodecRegistry.RealtimeOutputCodecs.Should().NotContain("libsvtav1",
                "software AV1 cannot keep up with realtime frame rates");
            CodecRegistry.RealtimeOutputCodecs.Should().NotContain("libaom-av1");
            CodecRegistry.RealtimeOutputCodecs.Should().NotContain("libvpx-vp9");
        }

        // v1.7.1 - HtmlDropdown_ListsExactlyTheCodecsInRegistry was moved to
        // RegistryDriftLockTests.HtmlDropdown_MatchesRegistry [Theory] for DRY across registries.
    }
}
