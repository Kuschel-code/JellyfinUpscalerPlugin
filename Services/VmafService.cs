using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.Buffered;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Raised when VMAF cannot be computed because the ffmpeg build lacks libvmaf
    /// (or the run produced no usable output). Lets callers return a clear 501 instead
    /// of a generic 500.
    /// </summary>
    public class VmafUnavailableException : Exception
    {
        public VmafUnavailableException(string message) : base(message) { }
    }

    /// <summary>
    /// v1.8.2 — VMAF (Video Multi-Method Assessment Fusion) quality comparison.
    /// Runs ffmpeg's libvmaf filter to score a distorted/upscaled video against a
    /// reference, turning "looks sharper to me" into an objective 0–100 number — the
    /// metric Netflix uses to judge encode quality.
    ///
    /// Requires the ffmpeg build to include libvmaf (jellyfin-ffmpeg does on most
    /// platforms). The pure helpers (<see cref="BuildVmafArgs"/>, <see cref="ParseVmafScore"/>)
    /// are deterministic and unit-tested; the runtime call is best-effort with graceful
    /// detection of a libvmaf-less build.
    /// </summary>
    public class VmafService
    {
        public class VmafResult
        {
            public double Mean { get; set; }
            public double Min { get; set; }
            public double Max { get; set; }
            public double Harmonic { get; set; }
        }

        /// <summary>
        /// Builds the ffmpeg arguments for a VMAF run. Distorted input is first, reference
        /// second (libvmaf's [main][ref] convention). Both PTS are reset so streams with
        /// different start times still align frame-for-frame. Pure → unit-testable.
        /// </summary>
        public static IReadOnlyList<string> BuildVmafArgs(string distortedPath, string referencePath, string jsonLogPath)
        {
            // log_path is a filtergraph option value: backslashes and colons must be escaped.
            var logEsc = jsonLogPath.Replace("\\", "/").Replace(":", "\\:");
            var lavfi = "[0:v]setpts=PTS-STARTPTS[dist];[1:v]setpts=PTS-STARTPTS[ref];" +
                        $"[dist][ref]libvmaf=log_fmt=json:log_path={logEsc}";
            return new List<string>
            {
                "-i", distortedPath,
                "-i", referencePath,
                "-lavfi", lavfi,
                "-f", "null", "-"
            };
        }

        /// <summary>
        /// Parses the pooled VMAF metrics from libvmaf's JSON log. Returns null when the
        /// expected structure is absent. Pure → unit-testable.
        /// </summary>
        public static VmafResult? ParseVmafScore(string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(jsonContent)) return null;
            try
            {
                using var doc = JsonDocument.Parse(jsonContent);
                if (!doc.RootElement.TryGetProperty("pooled_metrics", out var pooled)) return null;
                if (!pooled.TryGetProperty("vmaf", out var vmaf)) return null;

                var r = new VmafResult();
                if (vmaf.TryGetProperty("mean", out var m)) r.Mean = m.GetDouble();
                if (vmaf.TryGetProperty("min", out var mn)) r.Min = mn.GetDouble();
                if (vmaf.TryGetProperty("max", out var mx)) r.Max = mx.GetDouble();
                if (vmaf.TryGetProperty("harmonic_mean", out var h)) r.Harmonic = h.GetDouble();
                return r;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Runs ffmpeg+libvmaf and returns the pooled score. Throws
        /// <see cref="VmafUnavailableException"/> when ffmpeg is missing or built without libvmaf.
        /// </summary>
        public async Task<VmafResult> ComputeVmafAsync(string ffmpegPath, string distortedPath, string referencePath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
                throw new VmafUnavailableException("FFmpeg path is not available from the media encoder.");

            var jsonLog = Path.Combine(Path.GetTempPath(), $"vmaf_{Guid.NewGuid():N}.json");
            try
            {
                var args = BuildVmafArgs(distortedPath, referencePath, jsonLog);
                var result = await Cli.Wrap(ffmpegPath)
                    .WithArguments(args)
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync(ct);

                var stderr = result.StandardError ?? string.Empty;
                if (stderr.Contains("No such filter") && stderr.Contains("libvmaf"))
                    throw new VmafUnavailableException("This ffmpeg build was compiled without libvmaf.");

                if (!File.Exists(jsonLog))
                    throw new VmafUnavailableException("VMAF run produced no log (check that both files have a comparable video stream).");

                var parsed = ParseVmafScore(await File.ReadAllTextAsync(jsonLog, ct));
                if (parsed == null)
                    throw new VmafUnavailableException("Could not parse libvmaf JSON output.");
                return parsed;
            }
            finally
            {
                try { if (File.Exists(jsonLog)) File.Delete(jsonLog); }
                catch (IOException) { /* temp file cleanup is best-effort */ }
            }
        }
    }
}
