namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// v1.8.2 — ordering/completion core for overlapping frame <i>extraction</i> with
    /// <i>upscaling</i> (pipeline parallelism). Today extraction is a separate phase that
    /// fully completes before upscaling begins; to overlap them, a consumer must upscale
    /// frames as the extractor writes them — but only frames that are <b>fully written</b>.
    ///
    /// ffmpeg writes <c>frame_000001.png, frame_000002.png, …</c> strictly in order, so
    /// frame N is guaranteed complete once frame N+1 exists, OR once extraction has finished
    /// (then every present frame is complete). This class is the pure, deterministic decision
    /// logic for that hand-off — no I/O, no threads, no locks — which makes the tricky part
    /// unit-testable. The producer (extraction Task) calls <see cref="UpdateAvailable"/> /
    /// <see cref="MarkExtractionComplete"/>; the consumer (upscale loop) calls <see cref="Next"/>.
    ///
    /// Kept independent of the live pipeline so the verified core can land without putting
    /// an unverified concurrent path on the default playback route.
    /// </summary>
    public sealed class FrameStreamCoordinator
    {
        /// <summary>No frame is safely complete yet — the consumer should wait and retry.</summary>
        public const int NoneReady = -1;
        /// <summary>Extraction finished and every frame has been handed out.</summary>
        public const int AllDone = -2;

        private int _nextIndex;          // 0-based index of the next frame to hand out
        private bool _extractionComplete;
        private int _availableCount;     // frames currently present on disk (monotonic)

        /// <summary>0-based index of the next frame that will be handed out.</summary>
        public int NextIndex => _nextIndex;

        /// <summary>Number of frames the producer has reported as present so far.</summary>
        public int AvailableCount => _availableCount;

        public bool ExtractionComplete => _extractionComplete;

        /// <summary>Signal that extraction has finished — the last present frame is now safe.</summary>
        public void MarkExtractionComplete() => _extractionComplete = true;

        /// <summary>Report how many frames are present on disk. Never moves backwards.</summary>
        public void UpdateAvailable(int availableCount)
        {
            if (availableCount > _availableCount)
                _availableCount = availableCount;
        }

        /// <summary>
        /// Returns the next 0-based frame index that is safe to consume, or
        /// <see cref="NoneReady"/> when nothing new is complete yet, or <see cref="AllDone"/>
        /// when extraction is finished and every frame has been handed out.
        /// </summary>
        public int Next()
        {
            // The last present frame isn't proven complete until its successor exists,
            // unless extraction has finished (then all present frames are complete).
            int readyUpTo = _extractionComplete ? _availableCount : _availableCount - 1;

            if (_nextIndex < readyUpTo)
                return _nextIndex++;

            if (_extractionComplete && _nextIndex >= _availableCount)
                return AllDone;

            return NoneReady;
        }
    }
}
