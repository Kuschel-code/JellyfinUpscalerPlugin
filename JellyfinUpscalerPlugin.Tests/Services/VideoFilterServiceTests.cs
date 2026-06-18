using FluentAssertions;
using JellyfinUpscalerPlugin;
using JellyfinUpscalerPlugin.Services;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.2 — tests for the denoise-before-encode prefilter (Netflix lesson).
    /// The prefilter is deliberately independent of the camera-style filter system,
    /// so these guard that EnableVideoFilters does NOT gate it and that the FFmpeg
    /// filter string is well-formed for both engines.
    /// </summary>
    public class VideoFilterServiceTests
    {
        private readonly VideoFilterService _svc = new();

        [Fact]
        public void Denoise_Disabled_ReturnsNull()
        {
            var cfg = new PluginConfiguration { EnableDenoisePrefilter = false, DenoisePrefilterStrength = 5.0 };
            _svc.BuildDenoisePrefilter(cfg).Should().BeNull();
        }

        [Fact]
        public void Denoise_EnabledButZeroStrength_ReturnsNull()
        {
            var cfg = new PluginConfiguration { EnableDenoisePrefilter = true, DenoisePrefilterStrength = 0.0 };
            _svc.BuildDenoisePrefilter(cfg).Should().BeNull();
        }

        [Fact]
        public void Denoise_Hqdn3d_BuildsFourParamChain()
        {
            var cfg = new PluginConfiguration
            {
                EnableDenoisePrefilter = true,
                DenoisePrefilterMethod = "hqdn3d",
                DenoisePrefilterStrength = 4.0
            };
            // 4.0 : 4.0*0.75=3.0 : 4.0*1.5=6.0 : 6.0
            _svc.BuildDenoisePrefilter(cfg).Should().Be("hqdn3d=4.0:3.0:6.0:6.0");
        }

        [Fact]
        public void Denoise_Nlmeans_BuildsSigmaChain()
        {
            var cfg = new PluginConfiguration
            {
                EnableDenoisePrefilter = true,
                DenoisePrefilterMethod = "nlmeans",
                DenoisePrefilterStrength = 2.5
            };
            _svc.BuildDenoisePrefilter(cfg).Should().Be("nlmeans=s=2.5");
        }

        [Fact]
        public void Denoise_UnknownMethod_FallsBackToHqdn3d()
        {
            var cfg = new PluginConfiguration
            {
                EnableDenoisePrefilter = true,
                DenoisePrefilterMethod = "bogus",
                DenoisePrefilterStrength = 2.0
            };
            _svc.BuildDenoisePrefilter(cfg).Should().StartWith("hqdn3d=");
        }

        [Fact]
        public void Denoise_IsIndependentOfCameraFilterSystem()
        {
            // EnableVideoFilters off + preset none: creative chain is null, but the
            // denoise prefilter must STILL apply (that's the whole point).
            var cfg = new PluginConfiguration
            {
                EnableVideoFilters = false,
                ActiveFilterPreset = "none",
                EnableDenoisePrefilter = true,
                DenoisePrefilterStrength = 3.0
            };
            _svc.BuildFilterChain(cfg).Should().BeNull("camera filters are off");
            _svc.BuildDenoisePrefilter(cfg).Should().NotBeNull("denoise prefilter is independent");
        }

        [Fact]
        public void Denoise_StrengthClampedToMax10()
        {
            var cfg = new PluginConfiguration
            {
                EnableDenoisePrefilter = true,
                DenoisePrefilterMethod = "nlmeans",
                DenoisePrefilterStrength = 999.0   // clamped by the property setter
            };
            _svc.BuildDenoisePrefilter(cfg).Should().Be("nlmeans=s=10.0");
        }

        [Fact]
        public void SupportedDenoiseMethods_AreExactlyHqdn3dAndNlmeans()
        {
            VideoFilterService.SupportedDenoiseMethods.Should().BeEquivalentTo(new[] { "hqdn3d", "nlmeans" });
        }
    }
}
