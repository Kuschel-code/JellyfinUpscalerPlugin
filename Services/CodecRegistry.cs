using System;
using System.Collections.Generic;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Single source of truth for FFmpeg output-codec allowlists.
    ///
    /// The settings UI (Configuration/configurationpage.html, #OutputCodec) offers 12 codec
    /// options across 4 optgroups (Software / NVIDIA / Intel / Stream-Copy). Before v1.6.1.23
    /// four code paths each carried their own inline allowlist:
    ///
    ///   - UpscalerController save endpoint: 3 entries  (silently rejected 9 of 12 UI options!)
    ///   - VideoFrameProcessor.ReconstructVideoAsync:  7 entries
    ///   - ProcessingMethodExecutor realtime path:     6 entries
    ///   - ProcessingMethodExecutor batch path:       12 entries (correct)
    ///
    /// User-impact of the worst case: a user picking "AV1 NVENC (RTX 40+)" or "h264_qsv" in
    /// the dropdown clicked Save -- the value was silently discarded, leaving OutputCodec at
    /// libx264 (CPU). On NVIDIA hardware that is a 5-20x encoding-speed regression.
    ///
    /// v1.6.1.23 consolidates the allowlists here. The settings UI &lt;option&gt; values must
    /// stay in lockstep with <see cref="OutputCodecs"/>; CodecRegistryTests enforces this
    /// via parsing the embedded HTML resource and asserting set-equality.
    /// </summary>
    internal static class CodecRegistry
    {
        /// <summary>
        /// Full output-codec set surfaced in the Settings UI (#OutputCodec dropdown) and
        /// accepted by the save endpoint, the offline frame-reconstruction path, and the
        /// batch FFmpeg encoder. ProcessingMethodExecutor.BuildEncoderArgs has a switch
        /// branch for every entry in this set.
        /// </summary>
        internal static readonly HashSet<string> OutputCodecs = new(StringComparer.OrdinalIgnoreCase)
        {
            // Software (CPU)
            "libx264", "libx265", "libsvtav1", "libaom-av1", "libvpx-vp9",
            // NVIDIA NVENC (GPU)
            "h264_nvenc", "hevc_nvenc", "av1_nvenc",
            // Intel Quick Sync (GPU)
            "h264_qsv", "hevc_qsv", "av1_qsv",
            // Stream copy (no re-encode)
            "copy"
        };

        /// <summary>
        /// Realtime-streaming subset (frame-by-frame pipe encoding). Excludes:
        ///   - "copy" -- meaningless when re-encoding upscaled frames into a stream
        ///   - libsvtav1, libaom-av1, libvpx-vp9 -- software AV1/VP9 cannot keep up with
        ///     realtime frame rates even on fast CPUs
        ///   - av1_nvenc, av1_qsv -- excluded conservatively pending validation on RTX 40+
        ///     and Arc hardware in the realtime pipe path; users who pick AV1-HW for batch
        ///     still get it via <see cref="OutputCodecs"/>, just not for realtime today
        ///
        /// Picking a codec outside this set in the UI silently falls back to libx264 in the
        /// realtime path while the batch path honors the user choice. This is documented in
        /// the realtime fallback log at ProcessingMethodExecutor.cs.
        /// </summary>
        internal static readonly HashSet<string> RealtimeOutputCodecs = new(StringComparer.OrdinalIgnoreCase)
        {
            "libx264", "libx265",
            "h264_nvenc", "hevc_nvenc",
            "h264_qsv", "hevc_qsv"
        };
    }
}
