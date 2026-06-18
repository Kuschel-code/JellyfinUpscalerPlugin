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
    }
}
