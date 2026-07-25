using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.3.14 — the plugin had two recommenders that never spoke to each other:
    /// the content heuristic in <see cref="UpscalerCore"/> ("what suits this material?")
    /// and the hardware advisor in the Python service ("what can this box do?").
    /// <see cref="HardwareBudget"/> is the join. These tests pin the two properties the
    /// join must never lose: an unknown tier caps nothing, and no model is silently
    /// treated as free just because it is new.
    /// </summary>
    public class HardwareBudgetTests
    {
        [Theory]
        [InlineData("strong-gpu", "Heavy")]
        [InlineData("mid-gpu", "Heavy")]
        [InlineData("igpu", "Medium")]
        [InlineData("strong-cpu", "Medium")]
        [InlineData("weak-cpu", "Light")]
        public void MaxWeightFor_maps_every_tier_the_service_can_report(string tier, string expected)
        {
            // These five strings come from docker-ai-service/app/main.py /recommend.
            // If the service ever renames one, this test keeps failing until the map follows.
            HardwareBudget.MaxWeightFor(tier)!.Value.ToString().Should().Be(expected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("quantum-tpu")]     // a tier string from a future service version
        public void MaxWeightFor_unknown_tier_means_no_cap(string? tier)
        {
            HardwareBudget.MaxWeightFor(tier).Should().BeNull(
                "auto-mode must never block on a missing or unrecognised hardware tier");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("quantum-tpu")]
        public void FitsTier_unknown_tier_admits_even_the_heaviest_model(string? tier)
        {
            HardwareBudget.FitsTier("realbasicvsr-x4", tier).Should().BeTrue(
                "an unreachable service must leave behaviour exactly as it was before v1.8.3.14");
        }

        [Theory]
        [InlineData("fsrcnn-x2", "Light")]
        [InlineData("clearreality-x4", "Light")]
        [InlineData("span-x2", "Medium")]
        [InlineData("realesrgan-animevideo-x4", "Medium")]
        [InlineData("realesrgan-x4", "Heavy")]
        [InlineData("nomos2-dat2-x4", "Heavy")]
        [InlineData("realbasicvsr-x4", "Heavy")]
        public void WeightOf_classifies_the_curated_catalog(string modelId, string expected)
        {
            HardwareBudget.WeightOf(modelId).ToString().Should().Be(expected);
        }

        [Theory]
        [InlineData("some-new-dat2-variant-x4")]
        [InlineData("swinir-huge-x8")]
        [InlineData("myvsr-x4")]
        public void WeightOf_unlisted_restoration_architectures_fall_back_to_Heavy(string modelId)
        {
            // A newly imported DAT/SwinIR/VSR model must not be mistaken for cheap just
            // because nobody added it to the explicit table yet.
            HardwareBudget.WeightOf(modelId).Should().Be(HardwareBudget.Weight.Heavy);
        }

        [Theory]
        [InlineData("fsrcnn-x9")]
        [InlineData("espcn-experimental")]
        [InlineData("something-compact-x2")]
        public void WeightOf_unlisted_compact_architectures_fall_back_to_Light(string modelId)
        {
            HardwareBudget.WeightOf(modelId).Should().Be(HardwareBudget.Weight.Light);
        }

        [Fact]
        public void WeightOf_imported_community_model_defaults_to_Medium()
        {
            // omdb-* models come from OpenModelDB via the importer; their cost is unknown,
            // so they sit in the middle instead of being banned or waved through.
            HardwareBudget.WeightOf("omdb-4x-nomos8k-atd").Should().Be(HardwareBudget.Weight.Medium);
        }

        [Fact]
        public void WeightOf_null_or_empty_is_Medium_not_a_crash()
        {
            HardwareBudget.WeightOf(null).Should().Be(HardwareBudget.Weight.Medium);
            HardwareBudget.WeightOf("").Should().Be(HardwareBudget.Weight.Medium);
        }

        [Fact]
        public void A_weak_cpu_admits_Light_and_refuses_everything_above()
        {
            HardwareBudget.FitsTier("fsrcnn-x2", "weak-cpu").Should().BeTrue();
            HardwareBudget.FitsTier("span-x2", "weak-cpu").Should().BeFalse();
            HardwareBudget.FitsTier("realesrgan-x4", "weak-cpu").Should().BeFalse();
        }

        [Fact]
        public void A_strong_cpu_admits_Medium_but_still_refuses_full_restoration_nets()
        {
            HardwareBudget.FitsTier("span-x2", "strong-cpu").Should().BeTrue();
            HardwareBudget.FitsTier("realesrgan-x4", "strong-cpu").Should().BeFalse();
        }

        [Fact]
        public void A_gpu_admits_everything()
        {
            HardwareBudget.FitsTier("realbasicvsr-x4", "strong-gpu").Should().BeTrue();
            HardwareBudget.FitsTier("nomos2-dat2-x4", "mid-gpu").Should().BeTrue();
        }

        [Theory]
        [InlineData("weak-cpu", "weak CPU (no GPU)")]
        [InlineData("strong-gpu", "strong GPU")]
        [InlineData("", "unknown hardware")]
        public void DescribeTier_produces_text_fit_for_the_UI(string tier, string expected)
        {
            // This string lands verbatim in the substitution reason the user reads.
            HardwareBudget.DescribeTier(tier).Should().Be(expected);
        }
    }
}
