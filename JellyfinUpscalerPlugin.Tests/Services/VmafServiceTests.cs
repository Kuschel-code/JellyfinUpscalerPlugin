using System.Linq;
using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.2 — tests for the pure VMAF helpers. The runtime ffmpeg call needs libvmaf
    /// + real video files (not unit-testable here), but the arg builder and the JSON
    /// parser — the parts that silently rot — are covered.
    /// </summary>
    public class VmafServiceTests
    {
        [Fact]
        public void BuildArgs_PutsDistortedFirstAndReferenceSecond()
        {
            var args = VmafService.BuildVmafArgs("dist.mp4", "ref.mp4", "/tmp/out.json").ToList();
            // -i dist.mp4 must come before -i ref.mp4 (libvmaf [main][ref] order)
            var firstInput = args.IndexOf("dist.mp4");
            var secondInput = args.IndexOf("ref.mp4");
            firstInput.Should().BeGreaterThan(-1);
            secondInput.Should().BeGreaterThan(firstInput);
            args.Should().Contain("-lavfi");
            args.Should().Contain("-f");
            args.Should().Contain("null");
        }

        [Fact]
        public void BuildArgs_LavfiContainsLibvmafAndLogPath()
        {
            var args = VmafService.BuildVmafArgs("d.mp4", "r.mp4", "/tmp/out.json");
            var lavfi = args.SkipWhile(a => a != "-lavfi").Skip(1).First();
            lavfi.Should().Contain("libvmaf");
            lavfi.Should().Contain("log_fmt=json");
            lavfi.Should().Contain("log_path=/tmp/out.json");
        }

        [Fact]
        public void BuildArgs_EscapesWindowsLogPath()
        {
            var args = VmafService.BuildVmafArgs("d.mp4", "r.mp4", @"C:\Temp\out.json");
            var lavfi = args.SkipWhile(a => a != "-lavfi").Skip(1).First();
            // backslash -> forward slash, drive colon -> escaped colon
            lavfi.Should().Contain("log_path=C\\:/Temp/out.json");
        }

        [Fact]
        public void ParseScore_ReadsPooledMetrics()
        {
            const string json = @"{
                ""frames"": [],
                ""pooled_metrics"": {
                    ""vmaf"": { ""min"": 90.1, ""max"": 99.2, ""mean"": 95.3, ""harmonic_mean"": 95.0 }
                }
            }";
            var r = VmafService.ParseVmafScore(json);
            r.Should().NotBeNull();
            r!.Mean.Should().BeApproximately(95.3, 0.001);
            r.Min.Should().BeApproximately(90.1, 0.001);
            r.Max.Should().BeApproximately(99.2, 0.001);
            r.Harmonic.Should().BeApproximately(95.0, 0.001);
        }

        [Fact]
        public void ParseScore_MissingPooledMetrics_ReturnsNull()
        {
            VmafService.ParseVmafScore(@"{""frames"": []}").Should().BeNull();
        }

        [Fact]
        public void ParseScore_Garbage_ReturnsNull()
        {
            VmafService.ParseVmafScore("not json at all").Should().BeNull();
        }

        [Fact]
        public void ParseScore_Empty_ReturnsNull()
        {
            VmafService.ParseVmafScore("").Should().BeNull();
            VmafService.ParseVmafScore("   ").Should().BeNull();
        }
    }
}
