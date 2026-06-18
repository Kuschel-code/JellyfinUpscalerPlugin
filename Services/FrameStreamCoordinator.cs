using System;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// v1.8.3 — ordering/completion core for overlapping frame <i>extraction</i> with
    /// <i>upscaling</i> (pipeline parallelism). To overlap them, a consumer upscales frames
    /// as the extractor writes them — but only frames that are <b>fully written</b>.
    ///
    /// ffmpeg writes <c>frame_000001.png, frame_000002.png, …</c> strictly in order, so
    /// frame N is proven complete once frame N+1 appears. The HIGHEST present frame has no
    /// successor: a clean <see cref="MarkExtractionComplete"/> (ffmpeg exited after finishing
    /// the last frame) makes it safe; a <see cref="MarkExtractionFailed"/> does NOT — ffmpeg
    /// may have died mid-write on that file, so it stays unproven and is never handed out.
    /// That COMPLETE-vs-FAILED asymmetry is the whole point.
    ///
    /// Thread-safe: the producer/watcher thread calls <see cref="UpdateAvailable"/> /
    /// <see cref="MarkExtractionComplete"/> / <see cref="MarkExtractionFailed"/>; the consumer
    /// thread calls <see cref="Next"/> / reads <see cref="Error"/>. One lock guards all shared
    /// state, so a failure exception published by the producer is always visible (no torn read)
    /// to the consumer when <see cref="Failed"/> is returned.
    /// </summary>
    public sealed class FrameStreamCoordinator
    {
        /// <summary>No frame is safely complete yet — the consumer should wait and retry.</summary>
        public const int NoneReady = -1;
        /// <summary>Extraction finished cleanly and every frame has been handed out.</summary>
        public const int AllDone = -2;
        /// <summary>Extraction failed; all proven frames handed out. Consumer should throw <see cref="Error"/>.</summary>
        public const int Failed = -3;

        private readonly object _gate = new();
        private int _nextIndex;          // 0-based index of the next frame to hand out
        private int _availableCount;     // frames currently present on disk (monotonic)
        private bool _completed;         // ffmpeg exited cleanly (terminal)
        private bool _failed;            // ffmpeg errored/was cancelled (terminal)
        private Exception? _error;       // failure cause, published under the lock

        /// <summary>0-based index of the next frame that will be handed out.</summary>
        public int NextIndex { get { lock (_gate) return _nextIndex; } }

        /// <summary>Number of frames the producer has reported as present so far.</summary>
        public int AvailableCount { get { lock (_gate) return _availableCount; } }

        /// <summary>True once extraction has finished cleanly.</summary>
        public bool ExtractionComplete { get { lock (_gate) return _completed; } }

        /// <summary>True once extraction has failed.</summary>
        public bool ExtractionFailed { get { lock (_gate) return _failed; } }

        /// <summary>The failure cause when <see cref="Next"/> returns <see cref="Failed"/>; read under the lock.</summary>
        public Exception? Error { get { lock (_gate) return _error; } }

        /// <summary>Signal a clean extraction finish — the last present frame is now safe. First terminal wins.</summary>
        public void MarkExtractionComplete()
        {
            lock (_gate)
            {
                if (!_completed && !_failed)
                    _completed = true;
            }
        }

        /// <summary>Signal an extraction failure — the unproven highest frame is dropped. First terminal wins.</summary>
        public void MarkExtractionFailed(Exception ex)
        {
            lock (_gate)
            {
                if (_completed || _failed)
                    return;                                   // a late call cannot mask the first terminal state
                _failed = true;
                _error = ex ?? new InvalidOperationException("Frame extraction failed.");
            }
        }

        /// <summary>Report how many frames are present on disk. Monotonic — never moves backwards.</summary>
        public void UpdateAvailable(int availableCount)
        {
            lock (_gate)
            {
                if (availableCount > _availableCount)
                    _availableCount = availableCount;
            }
        }

        /// <summary>
        /// Returns the next 0-based frame index that is safe to consume, or <see cref="NoneReady"/>
        /// when nothing new is proven yet, <see cref="AllDone"/> when extraction finished cleanly and
        /// every frame is handed out, or <see cref="Failed"/> when extraction failed (proven frames
        /// already handed out, the unproven highest dropped).
        /// </summary>
        public int Next()
        {
            lock (_gate)
            {
                // Proven frames first, in BOTH states: COMPLETE exposes the highest frame
                // (clean exit = whole); running/FAILED holds it back (unproven).
                int readyUpTo = _completed ? _availableCount : _availableCount - 1;

                if (_nextIndex < readyUpTo)
                    return _nextIndex++;
                if (_completed && _nextIndex >= _availableCount)
                    return AllDone;
                if (_failed)
                    return Failed;                            // unproven highest frame falls through to here
                return NoneReady;
            }
        }
    }
}
