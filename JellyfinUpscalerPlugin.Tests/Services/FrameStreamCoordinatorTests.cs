using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.2 — tests for the extraction⟂upscale ordering/completion state machine.
    /// The whole point is to never hand out a frame that isn't fully written, while not
    /// stalling once extraction is done. These guard both invariants.
    /// </summary>
    public class FrameStreamCoordinatorTests
    {
        [Fact]
        public void Fresh_NothingAvailable_IsNoneReady()
        {
            var c = new FrameStreamCoordinator();
            c.Next().Should().Be(FrameStreamCoordinator.NoneReady);
        }

        [Fact]
        public void HoldsBackLastFrameUntilSuccessorOrCompletion()
        {
            var c = new FrameStreamCoordinator();
            c.UpdateAvailable(3);          // frames 0,1,2 present, extraction still running
            c.Next().Should().Be(0);
            c.Next().Should().Be(1);
            // frame 2 is the last present frame — not proven complete yet
            c.Next().Should().Be(FrameStreamCoordinator.NoneReady);
        }

        [Fact]
        public void ReleasesLastFrameOnceExtractionComplete()
        {
            var c = new FrameStreamCoordinator();
            c.UpdateAvailable(3);
            c.Next(); c.Next();            // consume 0,1
            c.MarkExtractionComplete();
            c.Next().Should().Be(2);       // last frame now safe
            c.Next().Should().Be(FrameStreamCoordinator.AllDone);
        }

        [Fact]
        public void StreamsAsFramesArrive()
        {
            var c = new FrameStreamCoordinator();
            c.UpdateAvailable(1);
            c.Next().Should().Be(FrameStreamCoordinator.NoneReady); // only frame 0, no successor yet
            c.UpdateAvailable(2);
            c.Next().Should().Be(0);                                // frame 0 now provably complete
            c.Next().Should().Be(FrameStreamCoordinator.NoneReady);
            c.UpdateAvailable(5);
            c.Next().Should().Be(1);
            c.Next().Should().Be(2);
            c.Next().Should().Be(3);
            c.Next().Should().Be(FrameStreamCoordinator.NoneReady); // frame 4 held back
        }

        [Fact]
        public void UpdateAvailable_NeverMovesBackwards()
        {
            var c = new FrameStreamCoordinator();
            c.UpdateAvailable(5);
            c.UpdateAvailable(2);          // a late/stale count must not shrink the window
            c.AvailableCount.Should().Be(5);
        }

        [Fact]
        public void ExtractionCompleteWithZeroFrames_IsAllDone()
        {
            var c = new FrameStreamCoordinator();
            c.MarkExtractionComplete();
            c.Next().Should().Be(FrameStreamCoordinator.AllDone);
        }

        [Fact]
        public void EachFrameHandedOutExactlyOnce()
        {
            var c = new FrameStreamCoordinator();
            c.UpdateAvailable(4);
            c.MarkExtractionComplete();
            var handed = new System.Collections.Generic.List<int>();
            int x;
            while ((x = c.Next()) >= 0) handed.Add(x);
            handed.Should().Equal(0, 1, 2, 3);
            c.Next().Should().Be(FrameStreamCoordinator.AllDone);
        }

        // --- v1.8.3 COMPLETE vs FAILED asymmetry (the correctness core) ---

        [Fact]
        public void Complete_DeliversTheLastFrame_ThenAllDone()
        {
            var c = new FrameStreamCoordinator();
            c.UpdateAvailable(3);
            c.MarkExtractionComplete();
            c.Next().Should().Be(0);
            c.Next().Should().Be(1);
            c.Next().Should().Be(2);   // clean ffmpeg exit -> the highest frame is whole
            c.Next().Should().Be(FrameStreamCoordinator.AllDone);
        }

        [Fact]
        public void Failed_DropsTheUnprovenHighestFrame_AndExposesError()
        {
            var c = new FrameStreamCoordinator();
            c.UpdateAvailable(3);                          // frames 0,1,2 present
            var boom = new System.IO.IOException("ffmpeg died mid-write");
            c.MarkExtractionFailed(boom);
            c.Next().Should().Be(0);                       // proven complete by successor 1
            c.Next().Should().Be(1);                       // proven complete by successor 2
            c.Next().Should().Be(FrameStreamCoordinator.Failed);  // frame 2 unproven -> dropped, NOT delivered
            c.Error.Should().BeSameAs(boom);               // published under the lock, visible to the consumer
        }

        [Fact]
        public void Failed_NeverHandsOutTheHighestIndex()
        {
            var c = new FrameStreamCoordinator();
            c.UpdateAvailable(3);
            c.MarkExtractionFailed(new System.Exception("x"));
            var handed = new System.Collections.Generic.List<int>();
            int v;
            while ((v = c.Next()) >= 0) handed.Add(v);
            handed.Should().Equal(0, 1);                   // 2 (the maybe-half-written frame) is never upscaled
            c.Next().Should().Be(FrameStreamCoordinator.Failed);
        }

        [Fact]
        public void Terminal_FirstWins_LateCompleteCannotMaskFailure()
        {
            var c = new FrameStreamCoordinator();
            c.UpdateAvailable(3);
            c.MarkExtractionFailed(new System.Exception("first"));
            c.MarkExtractionComplete();                    // late -> no-op
            c.ExtractionFailed.Should().BeTrue();
            c.ExtractionComplete.Should().BeFalse();
            c.Next(); c.Next();
            c.Next().Should().Be(FrameStreamCoordinator.Failed);  // never AllDone
        }

        [Fact]
        public void Terminal_FirstWins_LateFailureCannotOverrideComplete()
        {
            var c = new FrameStreamCoordinator();
            c.UpdateAvailable(2);
            c.MarkExtractionComplete();
            c.MarkExtractionFailed(new System.Exception("late"));  // no-op
            c.ExtractionComplete.Should().BeTrue();
            c.ExtractionFailed.Should().BeFalse();
            c.Error.Should().BeNull();
        }
    }
}
