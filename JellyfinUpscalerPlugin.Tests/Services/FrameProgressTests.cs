using System;
using System.Threading.Tasks;
using FluentAssertions;
using JellyfinUpscalerPlugin.Models;
using JellyfinUpscalerPlugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.7.11 regression guards for the "job stuck at 95%" frame-progress plumbing.
    /// </summary>
    /// <remarks>
    /// Two behaviours are locked here:
    ///   - Gap 2: the pipe-encode loop used to report totalFrames = -1, which the hub cached as 0.
    ///     CalculateJobProgress's `> 0` check then ignored it and fell back to the time estimate
    ///     that pins at 95% on slow hardware. The hub now caches a -1 SENTINEL for unknown totals,
    ///     distinct from a genuine 0%, and CalculateJobProgress lets that sentinel fall through to
    ///     the time estimate WITHOUT rendering -1/0 as a bar value.
    ///   - The core v1.7.10 fix: a real frame fraction must win over the 95%-capped time estimate.
    ///
    /// SendProgressUpdate swallows its own exceptions, so the hub is safe to drive with a null
    /// ISessionManager - the SignalR broadcast no-ops while the static cache is still written.
    /// Every test clears the static cache in a finally so cases can't leak into one another.
    /// </remarks>
    public class FrameProgressTests
    {
        private const double ProgressDefaultPercent = 10.0; // mirrors ProcessingStrategySelector

        private readonly ProcessingStrategySelector _selector = new(NullLogger.Instance);

        private static UpscalerProgressHub Hub() =>
            new(NullLogger<UpscalerProgressHub>.Instance, null!);

        [Fact]
        public async Task SendFrameProgress_UnknownTotal_CachesMinusOneSentinel_NotZero()
        {
            var jobId = Guid.NewGuid().ToString();
            try
            {
                await Hub().SendFrameProgress(jobId, "clip.mkv", currentFrame: 1200, totalFrames: -1, fps: 4.0);

                // Must be the -1 sentinel, NOT 0 - a cached 0 was indistinguishable from "0% done"
                // and made CalculateJobProgress fall back to the 95%-capped time estimate (Gap 2).
                UpscalerProgressHub.GetFrameProgress(jobId).Should().Be(-1);
            }
            finally
            {
                UpscalerProgressHub.ClearFrameProgress(jobId);
            }
        }

        [Fact]
        public async Task SendFrameProgress_KnownTotal_CachesRealFraction()
        {
            var jobId = Guid.NewGuid().ToString();
            try
            {
                await Hub().SendFrameProgress(jobId, "clip.mkv", currentFrame: 50, totalFrames: 200, fps: 4.0);

                UpscalerProgressHub.GetFrameProgress(jobId).Should().Be(25.0);
            }
            finally
            {
                UpscalerProgressHub.ClearFrameProgress(jobId);
            }
        }

        [Fact]
        public async Task CalculateJobProgress_RealFrameProgress_WinsOverTimeEstimate()
        {
            var jobId = Guid.NewGuid().ToString();
            try
            {
                await Hub().SendFrameProgress(jobId, "clip.mkv", currentFrame: 50, totalFrames: 200, fps: 4.0); // 25%

                var job = new ProcessingJob
                {
                    Id = jobId,
                    Status = ProcessingStatus.Processing,
                    // Huge elapsed against a 1-minute clip => the time estimate alone would pin at 95%.
                    StartTime = DateTime.UtcNow.AddHours(-1),
                    InputInfo = new VideoInfo { Width = 1920, Height = 1080, FrameRate = 30, Duration = TimeSpan.FromMinutes(1) }
                };

                _selector.CalculateJobProgress(job)
                    .Should().Be(25.0, "real frame progress must override the 95%-capped time estimate");
            }
            finally
            {
                UpscalerProgressHub.ClearFrameProgress(jobId);
            }
        }

        [Fact]
        public async Task CalculateJobProgress_UnknownTotalSentinel_IsNotRenderedAsBarValue()
        {
            var jobId = Guid.NewGuid().ToString();
            try
            {
                await Hub().SendFrameProgress(jobId, "clip.mkv", currentFrame: 1200, totalFrames: -1, fps: 4.0);

                // No usable duration => the time-estimate branch is skipped too, so the result is the
                // default percent. The point: the -1 sentinel is NEVER returned as -1 or 0.
                var job = new ProcessingJob
                {
                    Id = jobId,
                    Status = ProcessingStatus.Processing,
                    StartTime = DateTime.UtcNow,
                    InputInfo = new VideoInfo { Width = 1920, Height = 1080, FrameRate = 30, Duration = TimeSpan.Zero }
                };

                _selector.CalculateJobProgress(job)
                    .Should().Be(ProgressDefaultPercent, "the unknown-total sentinel must fall through, not render as -1/0");
            }
            finally
            {
                UpscalerProgressHub.ClearFrameProgress(jobId);
            }
        }

        // ---- v1.7.11 Fix B: extraction-phase band + monotonic phase weighting (Gap 1) ----
        private const double Band = 15.0; // mirrors ProcessingStrategySelector's extraction band

        [Fact]
        public async Task SendExtractionProgress_KnownTotal_CachesPct()
        {
            var jobId = Guid.NewGuid().ToString();
            try
            {
                await Hub().SendExtractionProgress(jobId, extracted: 50, estimatedTotal: 100);
                UpscalerProgressHub.GetExtractionProgress(jobId).Should().Be(50.0);
            }
            finally { UpscalerProgressHub.ClearExtractionProgress(jobId); }
        }

        [Fact]
        public async Task SendExtractionProgress_UnknownTotal_DoesNotCache()
        {
            var jobId = Guid.NewGuid().ToString();
            try
            {
                // estimatedTotal <= 0 must NOT cache: keeps hadExtraction=false (time estimate),
                // no divide-by-zero - mirrors the -1 frame sentinel for unknown totals.
                await Hub().SendExtractionProgress(jobId, extracted: 999, estimatedTotal: 0);
                UpscalerProgressHub.GetExtractionProgress(jobId).Should().BeNull();
            }
            finally { UpscalerProgressHub.ClearExtractionProgress(jobId); }
        }

        [Fact]
        public async Task CalculateJobProgress_ExtractionOnly_StaysInBand_NotNinetyFive()
        {
            var jobId = Guid.NewGuid().ToString();
            try
            {
                await Hub().SendExtractionProgress(jobId, extracted: 50, estimatedTotal: 100); // 50% extracted

                var job = new ProcessingJob
                {
                    Id = jobId,
                    Status = ProcessingStatus.Processing,
                    StartTime = DateTime.UtcNow.AddHours(-1), // time estimate alone would pin at 95%
                    InputInfo = new VideoInfo { Width = 1920, Height = 1080, FrameRate = 30, Duration = TimeSpan.FromMinutes(1) }
                };

                _selector.CalculateJobProgress(job)
                    .Should().Be(Band * 0.5, "extraction at 50% sits at half the band (7.5%), never the 95% time cap");
            }
            finally { UpscalerProgressHub.ClearExtractionProgress(jobId); }
        }

        [Fact]
        public async Task CalculateJobProgress_ExtractionThenFrame_MapsAboveBand()
        {
            var jobId = Guid.NewGuid().ToString();
            try
            {
                await Hub().SendExtractionProgress(jobId, extracted: 100, estimatedTotal: 100); // extraction done
                await Hub().SendFrameProgress(jobId, "clip.mkv", currentFrame: 50, totalFrames: 100, fps: 4.0); // 50% upscaled

                var job = new ProcessingJob { Id = jobId, Status = ProcessingStatus.Processing, StartTime = DateTime.UtcNow };

                // band + 0.5 * (99 - band) = 15 + 0.5*84 = 57
                _selector.CalculateJobProgress(job).Should().Be(57.0);
            }
            finally
            {
                UpscalerProgressHub.ClearFrameProgress(jobId);
                UpscalerProgressHub.ClearExtractionProgress(jobId);
            }
        }

        [Fact]
        public async Task CalculateJobProgress_NoExtraction_FrameMappingUnchanged_PipePath()
        {
            var jobId = Guid.NewGuid().ToString();
            try
            {
                // No extraction reported (pipe/realtime): frame mapping must stay EXACTLY v1.7.10 -> 50% maps to 50.
                await Hub().SendFrameProgress(jobId, "clip.mkv", currentFrame: 50, totalFrames: 100, fps: 24.0);

                var job = new ProcessingJob { Id = jobId, Status = ProcessingStatus.Processing, StartTime = DateTime.UtcNow };
                _selector.CalculateJobProgress(job)
                    .Should().Be(50.0, "the pipe path has no extraction band and is unchanged");
            }
            finally { UpscalerProgressHub.ClearFrameProgress(jobId); }
        }

        [Fact]
        public async Task CalculateJobProgress_MonotonicAcrossExtractionToUpscale()
        {
            var jobId = Guid.NewGuid().ToString();
            try
            {
                var job = new ProcessingJob { Id = jobId, Status = ProcessingStatus.Processing, StartTime = DateTime.UtcNow };

                await Hub().SendExtractionProgress(jobId, extracted: 100, estimatedTotal: 100); // extraction complete
                var atExtractionEnd = _selector.CalculateJobProgress(job); // == band (15)

                await Hub().SendFrameProgress(jobId, "clip.mkv", currentFrame: 1, totalFrames: 100, fps: 4.0); // first upscale frame
                var atFirstFrame = _selector.CalculateJobProgress(job); // band + 0.01*84 = 15.84

                atExtractionEnd.Should().Be(Band);
                atFirstFrame.Should().BeGreaterThan(atExtractionEnd,
                    "the bar must rise from extraction into upscaling, never jump back toward 0");
            }
            finally
            {
                UpscalerProgressHub.ClearFrameProgress(jobId);
                UpscalerProgressHub.ClearExtractionProgress(jobId);
            }
        }
    }
}
