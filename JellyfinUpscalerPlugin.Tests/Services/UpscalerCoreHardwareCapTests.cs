using System;
using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.3.14 — the auto decision must respect the hardware it runs on. Before this,
    /// a NAS with no GPU could be handed a full restoration net because the content
    /// heuristic only ever asked "what suits this material?".
    ///
    /// The cap is a correction, never a gate: an unreachable service (unknown tier)
    /// must reproduce the pre-1.8.3.14 pick exactly. Every test below therefore runs
    /// the same input twice — once capped, once uncapped — instead of asserting a
    /// hard-coded model name that a future heuristic tweak would break.
    /// </summary>
    [Collection(UpscalerCoreStaticCollection.Name)]
    public class UpscalerCoreHardwareCapTests : IDisposable
    {
        private readonly UpscalerCore _core;

        public UpscalerCoreHardwareCapTests()
        {
            var logger = new Mock<ILogger<UpscalerCore>>().Object;
            var mediaEncoder = new Mock<IMediaEncoder>().Object;
            var fileSystem = new Mock<IFileSystem>().Object;
            var appPaths = new Mock<IApplicationPaths>().Object;
            var httpUpscaler = new HttpUpscalerService(
                new Mock<ILogger<HttpUpscalerService>>().Object,
                new Mock<System.Net.Http.IHttpClientFactory>().Object);

            _core = new UpscalerCore(logger, mediaEncoder, fileSystem, appPaths, httpUpscaler);
            UpscalerCore.UpdateHardwareTier(null);
        }

        // The tier lives in a static field (the resolver is synchronous and must not do
        // network I/O), so every test hands it back the way it found it.
        public void Dispose() => UpscalerCore.UpdateHardwareTier(null);

        private AutoPick LiveActionRealtimeSd() => _core.ResolveModelForVideoDetailed(
            genres: null, width: 640, height: 480, isBatch: false, inputFrames: 1, forceAuto: true);

        private AutoPick LiveActionBatchHd() => _core.ResolveModelForVideoDetailed(
            genres: null, width: 1920, height: 1080, isBatch: true, inputFrames: 1, forceAuto: true);

        [Fact]
        public void Unknown_tier_reproduces_the_pre_cap_pick_and_says_so()
        {
            var pick = LiveActionBatchHd();

            pick.SubstitutedFrom.Should().BeNull("no tier means no cap, so nothing is substituted for hardware reasons");
            pick.Signals.Should().Contain(s => s.StartsWith("Hardware: unknown"),
                "the UI has to be able to tell 'no cap' apart from 'cap allowed it'");
        }

        [Fact]
        public void Weak_cpu_never_returns_a_model_above_its_budget()
        {
            UpscalerCore.UpdateHardwareTier("weak-cpu");

            foreach (var pick in new[] { LiveActionRealtimeSd(), LiveActionBatchHd() })
            {
                HardwareBudget.WeightOf(pick.Model).Should().Be(HardwareBudget.Weight.Light,
                    $"a weak CPU cannot finish '{pick.Model}' in reasonable time");
            }
        }

        [Fact]
        public void Strong_cpu_never_returns_a_full_restoration_net()
        {
            UpscalerCore.UpdateHardwareTier("strong-cpu");

            HardwareBudget.WeightOf(LiveActionBatchHd().Model)
                .Should().BeOneOf(HardwareBudget.Weight.Light, HardwareBudget.Weight.Medium);
        }

        [Fact]
        public void A_capped_pick_explains_itself_instead_of_looking_like_a_wrong_choice()
        {
            var uncapped = LiveActionBatchHd().Model;
            UpscalerCore.UpdateHardwareTier("weak-cpu");
            var capped = LiveActionBatchHd();

            capped.Model.Should().NotBe(uncapped, "the uncapped pick is heavier than a weak CPU allows");
            capped.SubstitutedFrom.Should().Be(uncapped, "the UI shows what the heuristic actually wanted");
            capped.SubstitutionReason.Should().Contain("too heavy");
            capped.SubstitutionReason.Should().Contain("weak CPU (no GPU)",
                "a bare model id tells the user nothing about why their machine got something else");
            capped.Signals.Should().Contain(s => s.StartsWith("Hardware: weak CPU"));
        }

        [Fact]
        public void A_gpu_gets_the_unrestricted_content_pick()
        {
            var uncapped = LiveActionBatchHd().Model;
            UpscalerCore.UpdateHardwareTier("strong-gpu");
            var onGpu = LiveActionBatchHd();

            onGpu.Model.Should().Be(uncapped, "a strong GPU affords everything, so the cap must be invisible");
            onGpu.SubstitutedFrom.Should().BeNull();
        }

        [Fact]
        public void An_unrecognised_tier_string_is_treated_as_unknown_not_as_zero_budget()
        {
            var uncapped = LiveActionBatchHd().Model;
            UpscalerCore.UpdateHardwareTier("quantum-tpu");   // a future service version

            LiveActionBatchHd().Model.Should().Be(uncapped,
                "an unparseable tier must degrade to old behaviour, never to the lightest model");
        }

        [Fact]
        public void UpdateHardwareTier_normalises_blank_input_back_to_unknown()
        {
            UpscalerCore.UpdateHardwareTier("weak-cpu");
            UpscalerCore.UpdateHardwareTier("   ");

            UpscalerCore.HardwareTier.Should().BeNull(
                "a service answering with an empty tier must clear the cap, not cap at whitespace");
        }

        [Fact]
        public void Auto_pick_reports_the_scale_the_model_will_really_apply()
        {
            // The service ignores the requested scale, so a pick that claims nothing
            // leaves the caller reporting the configured value - the original bug.
            var pick = LiveActionBatchHd();

            pick.Scale.Should().Be(ModelScale.NativeScaleOf(pick.Model),
                "the reported factor must be the model's own, not a wish");
            pick.Scale.Should().BeGreaterThan(0, "every curated catalog id encodes its scale");
        }

        [Fact]
        public void A_1080p_batch_job_no_longer_targets_8K()
        {
            // Before v1.8.3.14 this branch returned realesrgan-x4: 1920x1080 -> 7680x4320,
            // four times the compute of a 2x pass for an output no client can show.
            var pick = LiveActionBatchHd();

            pick.Scale.Should().Be(2, "1080x2 is 4K; 1080x4 is 8K and pointless");
        }

        [Fact]
        public void An_SD_batch_job_still_gets_the_full_4x_restore()
        {
            var pick = _core.ResolveModelForVideoDetailed(
                genres: null, width: 640, height: 480, isBatch: true, inputFrames: 1, forceAuto: true);

            pick.Scale.Should().Be(4, "small sources are exactly where a 4x restore pays off");
        }

        // ------------------------------------------------------------------
        // Found by live-testing v1.8.3.14 on a CPU-only NAS (tier weak-cpu).
        // Every one of these passed the unit tests and still misbehaved in
        // production, so they are pinned here.
        // ------------------------------------------------------------------

        [Fact]
        public void Anime_batch_on_1080p_does_not_target_8K_either()
        {
            // The first cut only fixed the live-action branch; anime 1920x1080 still
            // resolved to a 4x model, i.e. 7680x4320.
            var pick = _core.ResolveModelForVideoDetailed(
                genres: new[] { "Animation" }, width: 1920, height: 1080,
                isBatch: true, inputFrames: 1, forceAuto: true);

            pick.Scale.Should().BeLessThanOrEqualTo(2,
                "anime is not exempt from 4x-on-1080p being 8K");
        }

        [Fact]
        public void A_weak_cpu_gets_the_best_light_model_not_the_crudest()
        {
            // Live finding: the branch fallback chains contain no Light model, so the
            // walk fell through to the last-resort fsrcnn-x2 for EVERY live-action job -
            // while clearreality-x4 is Light, 4x and ~32x faster than realesrgan-x4.
            UpscalerCore.UpdateHardwareTier("weak-cpu");

            var pick = _core.ResolveModelForVideoDetailed(
                genres: null, width: 640, height: 480, isBatch: true, inputFrames: 1, forceAuto: true);

            pick.Model.Should().NotBe("fsrcnn-x2",
                "the last resort must stay a last resort, not the everyday answer");
            HardwareBudget.WeightOf(pick.Model).Should().Be(HardwareBudget.Weight.Light);
        }

        [Fact]
        public void A_weak_cpu_anime_job_gets_an_anime_model()
        {
            UpscalerCore.UpdateHardwareTier("weak-cpu");

            var pick = _core.ResolveModelForVideoDetailed(
                genres: new[] { "Animation" }, width: 1920, height: 1080,
                isBatch: true, inputFrames: 1, forceAuto: true);

            HardwareBudget.WeightOf(pick.Model).Should().Be(HardwareBudget.Weight.Light);
            pick.Model.Should().NotBe("fsrcnn-x2", "a generic 2x model is a poor answer for anime");
        }

        [Fact]
        public void A_reason_never_names_a_model_that_was_substituted_away()
        {
            // Live finding: the reason read "-> Real-ESRGAN 4x." next to a fsrcnn-x2
            // pick. Reasons now describe the MATERIAL, so they stay true whatever the
            // budget filter does with them.
            UpscalerCore.UpdateHardwareTier("weak-cpu");

            foreach (var pick in new[] { LiveActionRealtimeSd(), LiveActionBatchHd() })
            {
                if (pick.SubstitutedFrom == null) continue;
                pick.Reason.Should().NotContain(pick.SubstitutedFrom,
                    $"the reason must not advertise '{pick.SubstitutedFrom}', which is not what runs");
            }
        }

        [Fact]
        public void The_capped_fallback_is_always_a_model_the_service_can_actually_load()
        {
            UpscalerCore.UpdateHardwareTier("weak-cpu");

            foreach (var pick in new[] { LiveActionRealtimeSd(), LiveActionBatchHd() })
            {
                ModelAvailability.IsKnownUnavailable(pick.Model).Should().BeFalse(
                    $"'{pick.Model}' has no public ONNX build — the cap must not route users into a dead end");
            }
        }
    }
}
