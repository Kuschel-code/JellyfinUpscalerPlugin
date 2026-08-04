using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.3.22 — regression guards for the code-review findings fixed in this release.
    ///
    /// Several of these live in code an xUnit test cannot execute without a running
    /// Jellyfin (controller endpoints, an ffmpeg invocation, a live queue worker), so they
    /// are pinned at source level — the same technique <c>check_ui_field_consistency.py</c>
    /// uses for element ids and <c>FilterSuggestionTests</c> uses for the player JS. A
    /// source assertion is weaker than a behavioural one, and it is far stronger than
    /// trusting that nobody re-introduces a one-line omission.
    /// </summary>
    public class ReviewFixTests
    {
        private static string RepoFile(params string[] parts)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JellyfinUpscalerPlugin.csproj")))
            {
                dir = dir.Parent;
            }
            dir.Should().NotBeNull("the test must be able to find the repository root");
            return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
        }

        // ── CRITICAL: /process could take any path on the server ──────────────

        [Fact]
        public void Every_endpoint_taking_a_path_checks_the_media_library()
        {
            // ProcessVideo had no allowlist at all while its two siblings did, and the class
            // carries only [Authorize] - so any authenticated non-admin could name any file.
            var ctrl = RepoFile("Controllers", "UpscalerController.cs");
            Regex.Matches(ctrl, @"IsInsideMediaLibrary\(").Count
                .Should().BeGreaterThanOrEqualTo(4,
                    "the helper is defined once and must be called by ProcessVideo, EnqueueJob and PreProcessVideo");
        }

        [Fact]
        public void The_library_check_compares_with_a_directory_separator()
        {
            // A bare StartsWith lets "/media/mov-private" pass an allowlist holding
            // "/media/mov" - a neighbouring-directory bypass.
            RepoFile("Controllers", "UpscalerController.cs")
                .Should().Contain("rootWithSep",
                    "prefix matching without a separator matches sibling directories");
        }

        [Fact]
        public void Neither_job_entry_point_may_overwrite_an_existing_file()
        {
            // The output allowlist constrains the DIRECTORY only, and ffmpeg runs with -y,
            // so "the other film in this folder" was a legal - and destructive - target.
            var ctrl = RepoFile("Controllers", "UpscalerController.cs");
            Regex.Matches(ctrl, @"WouldOverwriteExistingFile\(").Count
                .Should().BeGreaterThanOrEqualTo(3, "defined once, called by ProcessVideo and EnqueueJob");
        }

        // ── The auto-mode root: a shipped default posing as a user choice ─────

        [Fact]
        public void The_model_default_is_empty_so_the_batch_gate_can_fire()
        {
            // The nightly scan gates on (EnableAutoModelSelection && Model empty or "auto").
            // With the old default "realesrgan-x4" that condition was false on every real
            // install, so the hardware cap and the 8K guard never ran in batch.
            RepoFile("PluginConfiguration.cs")
                .Should().Contain("private const string DefaultModel = \"\";",
                    "an override field's default must be empty - same rule as PreferredAnimeModel");
        }

        [Fact]
        public void The_model_dropdown_offers_a_way_back_to_auto()
        {
            // Without an auto entry the user could never return to it, and an empty config
            // value rendered as a blank select.
            RepoFile("Configuration", "configurationpage.html")
                .Should().Contain("Auto (pick the model per video)");
        }

        // ── Locale: a comma is the ffmpeg filter separator ────────────────────

        [Fact]
        public void Every_number_that_reaches_ffmpeg_is_formatted_invariantly()
        {
            // "fps=23,976" on de-DE made ffmpeg fail with "No such filter: '976'" - every
            // frame-by-frame job on any comma-decimal server.
            var src = RepoFile("Services", "VideoFrameProcessor.cs");
            src.Should().NotMatchRegex(@"fps=\{effectiveFps\}",
                "the fps filter value must carry InvariantCulture");
            src.Should().NotContain("TotalSeconds.ToString(\"F2\")",
                "-ss must carry InvariantCulture too");
        }

        // ── Queue: two permit leaks fed a 100%-CPU spin ───────────────────────

        [Fact]
        public void The_empty_queue_branch_does_not_hand_the_permit_back()
        {
            // Releasing it turned a surplus permit into a hot loop: wait succeeds instantly,
            // queue still empty, release, repeat.
            var src = RepoFile("Services", "ProcessingQueue.cs");
            src.Should().NotContain("_signal.Release(); // Restore consumed signal to prevent deadlock");
        }

        [Fact]
        public void Cancelling_a_pending_job_takes_its_permit_back()
        {
            RepoFile("Services", "ProcessingQueue.cs")
                .Should().Contain("if (wasPending) _signal.Wait(0);",
                    "a removed pending job leaves its permit behind otherwise");
        }

        [Fact]
        public void Resume_does_not_mint_a_permit_that_has_no_job()
        {
            // Enqueue already released exactly one permit per queued job.
            var src = RepoFile("Services", "ProcessingQueue.cs");
            var resume = src.Substring(src.IndexOf("public void Resume()"));
            resume = resume.Substring(0, resume.IndexOf("public bool IsPaused"));
            resume.Should().NotContain("_signal.Release()");
        }

        // ── Silent misreporting ───────────────────────────────────────────────

        [Fact]
        public void The_image_scan_task_can_tell_ai_from_a_lanczos_resize()
        {
            // UpscaleImageAsync never returns null; it falls back to a resize, or to the
            // untouched original. Storing that as "_upscaled" made the scan filter skip the
            // image forever, so one outage poisoned the rest of the library.
            RepoFile("ScheduledTasks", "ImageUpscaleScanTask.cs")
                .Should().Contain("UpscaleImageDetailedAsync");
            RepoFile("ScheduledTasks", "ImageUpscaleScanTask.cs")
                .Should().Contain("if (!upscale.UsedAi)", "a non-AI result must not be written");
        }

        [Fact]
        public void The_hdr_endpoint_url_survives_a_trailing_slash()
        {
            // "http://host:5000//upscale-hdr" is a 404, and the caller then copied every
            // original frame through and reported the job successful.
            RepoFile("Services", "VideoFrameProcessor.cs")
                .Should().Contain("TrimEnd('/')");
        }

        [Fact]
        public void An_expired_cache_entry_takes_its_file_with_it()
        {
            // Removing the index entry alone orphaned multi-GB files that nothing would ever
            // look at again, while the inflated size counter evicted valid entries early.
            var src = RepoFile("Services", "CacheManager.cs");
            src.Should().Contain("staleEntry.FilePath");
            src.Should().Contain("-staleEntry.FileSize");
        }

        // ── The destructive script that would have deleted every current tag ──

        [Fact]
        public void The_dockerhub_cleanup_derives_its_version_instead_of_carrying_one()
        {
            // It was pinned to v1.7.8 while the repo shipped v1.8.3.21, so -Execute would
            // have deleted every v1.8.x tag and re-pointed :latest - which Watchtower users
            // follow - back to a v1.7.8 image.
            var src = RepoFile("Scripts", "cleanup-dockerhub-tags.ps1");
            src.Should().NotMatchRegex(@"\$CurrentNvidiaTag\s*=\s*'v\d",
                "a destructive script must not carry a version that can go stale");
            src.Should().Contain("meta.json", "the current version is derived from the repo");
            src.Should().Contain("Refusing to run", "it must refuse if the target tag is absent");
        }

        // ── Found by checking the report's remaining highs myself ────────────

        [Fact]
        public void The_face_restore_preview_route_exists_on_the_plugin_too()
        {
            // The config page has POSTed to this since the feature shipped; only
            // load/status/unload were ever defined, so every click was a 404.
            RepoFile("Controllers", "UpscalerController.cs")
                .Should().Contain("[HttpPost(\"face-restore/frame\")]");
        }

        [Fact]
        public void The_preview_proxy_forwards_the_face_count_header()
        {
            // The UI reads X-Face-Count to report how many faces were found; dropping it
            // would make the feature "work" while always reporting "?".
            RepoFile("Controllers", "UpscalerController.cs")
                .Should().Contain("X-Face-Count");
        }

        [Fact]
        public void Model_downloads_use_the_client_registered_for_them()
        {
            // /models/download is synchronous server-side and runs to ~380 MB. On the 120s
            // client it aborted with TaskCanceledException, which the retry loop treats as
            // a cancellation - break, no retry - so large models never downloaded at all.
            var src = RepoFile("Services", "HttpUpscalerService.cs");
            src.Should().Contain("GetDownloadClient()");
            src.Should().NotContain("GetClient().PostAsync($\"{baseUrl}/models/download\"",
                "the 120s client must not be used for a multi-hundred-MB download");
        }

        [Fact]
        public void The_global_body_limit_does_not_apply_to_model_uploads()
        {
            // MAX_UPLOAD_BYTES defaults to 50 MB and was applied to EVERY request, so
            // /models/upload could never accept a real model (GFPGAN ~340 MB) and its own
            // 500 MB check was dead code.
            var src = RepoFile("docker-ai-service", "app", "main.py");
            src.Should().Contain("_is_model_upload_path");
            src.Should().Contain("MAX_MODEL_UPLOAD_BYTES if _is_model_upload_path");
        }

        [Fact]
        public void The_cleanup_keeps_rolling_tags_by_shape_not_by_a_list()
        {
            // The enumerated list predated docker7-converter and would have deleted it.
            RepoFile("Scripts", "cleanup-dockerhub-tags.ps1")
                .Should().Contain("Test-IsRollingTag");
        }
    }
}
