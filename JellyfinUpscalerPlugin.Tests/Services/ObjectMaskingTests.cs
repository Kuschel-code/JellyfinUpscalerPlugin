using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using FluentAssertions;
using JellyfinUpscalerPlugin.Controllers;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.3.24 — the playback wiring for object masking (discussion #11).
    ///
    /// v1.8.3.23 shipped the masking as a service endpoint that nothing called. What the
    /// requester actually asked for was a filter during playback, so this release connects
    /// the capture loop the player already ran to an endpoint that masks.
    /// </summary>
    public class ObjectMaskingTests
    {
        private static string RepoFile(params string[] parts)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JellyfinUpscalerPlugin.csproj")))
            {
                dir = dir.Parent;
            }
            dir.Should().NotBeNull();
            return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
        }

        // ── The query string, which is where a locale bug would land ──────────

        [Fact]
        public void The_confidence_is_formatted_invariantly()
        {
            // Same class of bug as fps=23,976 in v1.8.3.22: on de-DE "0.35" becomes "0,35",
            // and the AI service parses that as a float and fails the request. It is a query
            // parameter rather than an ffmpeg filter, so it fails differently - but it fails.
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var config = new PluginConfiguration { ObjectMaskConfidence = 0.35 };

                UpscalerController.BuildObjectMaskQuery(config)
                    .Should().Contain("confidence=0.35").And.NotContain("0,35");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Fact]
        public void A_class_list_survives_as_a_list()
        {
            var config = new PluginConfiguration { ObjectMaskClasses = "dog,cat" };

            var query = UpscalerController.BuildObjectMaskQuery(config);

            // The service splits on a comma, so the comma has to arrive - encoded is fine,
            // dropped or turned into a second parameter is not.
            query.Should().MatchRegex(@"classes=dog(,|%2C)cat");
            Regex.Matches(query, "classes=").Count.Should().Be(1);
        }

        [Fact]
        public void A_class_field_holding_an_ampersand_cannot_inject_a_parameter()
        {
            // The value comes from a text box in the admin UI. Even trusted input should not
            // be able to grow the query.
            var config = new PluginConfiguration { ObjectMaskClasses = "dog&mode=blur" };

            var query = UpscalerController.BuildObjectMaskQuery(config);

            Regex.Matches(query, "mode=").Count.Should().Be(1, "the injected mode must not become a parameter");
            query.Should().Contain("mode=box", "the configured mode must win");
        }

        [Fact]
        public void An_empty_class_list_falls_back_to_the_animal_group()
        {
            // An empty list would make the service reject every frame, which reads as
            // "masking is broken" rather than "you cleared the field".
            UpscalerController.BuildObjectMaskQuery(new PluginConfiguration { ObjectMaskClasses = "  " })
                .Should().Contain("classes=animals");
        }

        [Fact]
        public void An_unknown_mode_falls_back_to_box_rather_than_reaching_the_service()
        {
            UpscalerController.BuildObjectMaskQuery(new PluginConfiguration { ObjectMaskMode = "nonsense" })
                .Should().Contain("mode=box");
            UpscalerController.BuildObjectMaskQuery(new PluginConfiguration { ObjectMaskMode = "blur" })
                .Should().Contain("mode=blur");
        }

        // ── Configuration bounds ─────────────────────────────────────────────

        [Theory]
        [InlineData(-50, 0)]
        [InlineData(12, 12)]
        [InlineData(9999, 200)]
        public void Padding_is_clamped(int input, int expected)
        {
            new PluginConfiguration { ObjectMaskPadding = input }.ObjectMaskPadding.Should().Be(expected);
        }

        [Theory]
        [InlineData(-1.0, 0.05)]
        [InlineData(0.5, 0.5)]
        [InlineData(5.0, 0.95)]
        public void Confidence_is_clamped(double input, double expected)
        {
            new PluginConfiguration { ObjectMaskConfidence = input }.ObjectMaskConfidence.Should().Be(expected);
        }

        [Fact]
        public void No_detection_model_is_configured_by_default()
        {
            // None is bundled, because every catalog entry carries a verified sha256 pin.
            // A default naming a model would promise something that is not on disk.
            new PluginConfiguration().ObjectMaskModel.Should().BeEmpty();
            new PluginConfiguration().EnableObjectMasking.Should().BeFalse();
        }

        // ── The wiring, which is the whole point of this release ─────────────

        [Fact]
        public void The_player_capture_loop_can_reach_the_masking_endpoint()
        {
            // v1.8.3.23's gap: masking existed and nothing called it.
            var js = RepoFile("Configuration", "player-integration.js");
            js.Should().Contain("Upscaler/detect-mask");
            js.Should().Contain("_objectMaskEnabled",
                "the loop must choose its endpoint from configuration");
        }

        [Fact]
        public void The_endpoint_choice_is_made_once_per_playback_not_per_frame()
        {
            // Flipping the target mid-stream would interleave masked and unmasked frames on
            // the same overlay, which looks like flicker rather than a setting change.
            var js = RepoFile("Configuration", "player-integration.js");
            js.Should().Contain("this._objectMaskEnabled = config.EnableObjectMasking === true;");
        }

        [Fact]
        public void The_proxy_route_exists_on_the_plugin()
        {
            var ctrl = RepoFile("Controllers", "UpscalerController.cs");
            ctrl.Should().Contain("[HttpPost(\"detect-mask\")]");
            ctrl.Should().Contain("[HttpPost(\"object-mask/load-model\")]");
        }

        [Fact]
        public void Loading_a_detection_model_requires_an_administrator()
        {
            // It makes the service read a file and hold it in memory; the frame proxy is for
            // every viewer, this is not.
            var ctrl = RepoFile("Controllers", "UpscalerController.cs");
            var idx = ctrl.IndexOf("[HttpPost(\"object-mask/load-model\")]");
            idx.Should().BeGreaterThan(0);
            ctrl.Substring(idx, 200).Should().Contain("RequiresElevation");
        }

        [Fact]
        public void The_frame_proxy_caps_the_request_body()
        {
            // Same reason as the face-restore proxy: an unbounded Request.Body copy runs
            // into the Jellyfin heap.
            var ctrl = RepoFile("Controllers", "UpscalerController.cs");
            var idx = ctrl.IndexOf("[HttpPost(\"detect-mask\")]");
            ctrl.Substring(idx, 900).Should().Contain("MaxFrameBytes");
        }

        // ── Reported by the person using it (discussion #11) ─────────────────

        [Fact]
        public void Loading_a_detector_uses_the_id_that_is_on_screen()
        {
            // "The status message would often stick on a previous model name different than
            // the one just entered." The button POSTed nothing and the endpoint read only the
            // SAVED config, so a freshly typed id loaded the PREVIOUS model - and the status
            // truthfully reported that previous model, which looked like a bug in the message.
            var ctrl = RepoFile("Controllers", "UpscalerController.cs");
            ctrl.Should().Contain("LoadObjectMaskModel([FromQuery] string? modelName = null)",
                "the caller has to be able to name the model it means");

            var html = RepoFile("Configuration", "configurationpage.html");
            html.Should().Contain("'?modelName=' + encodeURIComponent(typed)",
                "the button must send what the user typed");
        }

        [Fact]
        public void The_saved_config_still_works_as_a_fallback()
        {
            // Scripts and other clients call this without a parameter and must keep working.
            var ctrl = RepoFile("Controllers", "UpscalerController.cs");
            var idx = ctrl.IndexOf("LoadObjectMaskModel([FromQuery]");
            ctrl.Substring(idx, 900).Should().Contain("config?.ObjectMaskModel");
        }

        [Fact]
        public void Pressing_load_persists_the_masking_settings()
        {
            // The other half of the same report: values "put in previously" were gone after
            // navigating away and back. Nothing on this card was stored until the global Save
            // was pressed, while an action button sat right next to the fields suggesting
            // otherwise.
            var html = RepoFile("Configuration", "configurationpage.html");
            var idx = html.IndexOf("btn-object-mask-load')?.addEventListener");
            idx.Should().BeGreaterThan(0);
            var handler = html.Substring(idx, Math.Min(2600, html.Length - idx));

            handler.Should().Contain("updatePluginConfiguration",
                "the card's fields must be saved by the button that acts on them");
            handler.Should().Contain("cfg.ObjectMaskModel = typed;");
        }

        [Fact]
        public void The_confidence_field_cannot_silently_discard_a_valid_number()
        {
            // step="0.05" makes a type=number input report value === '' for entries the
            // browser rejects, and floating point puts values like 0.6 off that grid
            // ((0.6-0.05)/0.05 = 10.999999999999998). The save path skipped empty strings, so
            // the number was dropped without a word and the old one stayed - reported as
            // "it seemed to ignore the confidence values that I put in".
            var html = RepoFile("Configuration", "configurationpage.html");
            html.Should().Contain("id=\"ObjectMaskConfidence\" min=\"0.05\" max=\"0.95\" step=\"any\"");
            html.Should().NotContain("if (maskConf && maskConf.value !== '')",
                "an empty string here meant 'the browser disliked it', not 'the user left it blank'");
            html.Should().Contain("maskConf.value = cv;",
                "the field must show the value that was actually stored");
        }

        // ── Found on the live server, not by any test ────────────────────────

        [Fact]
        public void A_failed_frame_proxy_passes_the_services_reason_through()
        {
            // Live, on a freshly restarted container: the service answered
            // {"detail":"No model loaded"} - one line naming the fix - and both frame proxies
            // replaced it with "Frame upscaling failed" / "Frame benchmark failed". The user
            // sees a broken feature instead of a model they need to load.
            var ctrl = RepoFile("Controllers", "UpscalerController.cs");
            ctrl.Should().Contain("ReadServiceDetailAsync",
                "the helper that recovers the service's own message");
            Regex.Matches(ctrl, @"ReadServiceDetailAsync\(").Count
                .Should().BeGreaterThanOrEqualTo(3,
                    "defined once and used by the upscale-frame and benchmark-frame proxies");
            ctrl.Should().NotContain("StatusCode((int)response.StatusCode, \"Frame upscaling failed\")",
                "the bare message discarded the diagnosis");
        }

        [Fact]
        public void Hardware_info_reports_what_it_observed_not_what_was_configured()
        {
            // On a CPU-only server this endpoint answered GpuAvailable: true, because the
            // field returned the HardwareAcceleration CONFIG TOGGLE (default on) rather than
            // any observation - while /gpu-verify on the same box reported gpu_list: [],
            // nvidia-smi missing and /dev/dri absent. FFmpegAvailable and OnnxRuntime were
            // literal `true` and `"Available"` and had never checked anything at all.
            var ctrl = RepoFile("Controllers", "UpscalerController.cs");
            var start = ctrl.IndexOf("public async Task<ActionResult<object>> GetHardwareInfo()");
            start.Should().BeGreaterThan(0, "the endpoint has to consult the service, so it is async");
            var body = ctrl.Substring(start, Math.Min(2200, ctrl.Length - start));

            body.Should().NotContain("GpuAvailable = hardwareAcceleration",
                "a configuration toggle is not a hardware observation");
            body.Should().NotContain("FFmpegAvailable = true,", "that literal checked nothing");
            body.Should().NotContain("OnnxRuntime = \"Available\"", "that literal checked nothing");
            body.Should().Contain("status.UsingGpu");
            body.Should().Contain("IOFile.Exists(ffmpegPath)");
            body.Should().Contain("GpuAccelerationRequested",
                "what the user asked for still belongs in the payload - just not as the answer "
                + "to what exists");
        }

        [Fact]
        public void Releasing_the_processing_permit_survives_a_disposed_semaphore()
        {
            // From the test server's log during a shutdown with a job still running:
            //   ObjectDisposedException: 'System.Threading.SemaphoreSlim'
            //     at VideoProcessor.ProcessVideoAsync
            // Release() was the FIRST statement in the finally block, so the throw skipped
            // everything after it - the job stayed in _activeJobs, its CTS was never
            // disposed, and the progress caches were never cleared. A clean shutdown was
            // logged as a processing failure.
            var src = RepoFile("Services", "VideoProcessor.cs");
            // Wide enough for the explanation above the guard. A window sized to today's
            // comment is a test that breaks when someone edits the comment.
            var start = src.IndexOf("if (semaphoreAcquired)");
            var finallyBlock = src.Substring(start, Math.Min(1800, src.Length - start));

            finallyBlock.Should().Contain("catch (ObjectDisposedException)",
                "the release must not abort the rest of the cleanup on shutdown");
        }

        [Fact]
        public void A_shutdown_does_not_log_a_stack_trace_for_a_dropped_progress_update()
        {
            // Same shutdown, same log: SessionManager is disposed before in-flight jobs
            // finish, and every one of them logged a warning with a stack trace for
            // something entirely normal.
            var src = RepoFile("Services", "UpscalerProgressHub.cs");
            var idx = src.IndexOf("public async Task SendProgressUpdate");
            src.Substring(idx, 1600).Should().Contain("catch (ObjectDisposedException)");
        }

        [Fact]
        public void Masking_is_refused_when_it_is_switched_off()
        {
            // Otherwise a stale player tab keeps hammering the service after the admin
            // turned the feature off.
            var ctrl = RepoFile("Controllers", "UpscalerController.cs");
            var idx = ctrl.IndexOf("[HttpPost(\"detect-mask\")]");
            ctrl.Substring(idx, 2000).Should().Contain("EnableObjectMasking != true");
        }
    }
}
