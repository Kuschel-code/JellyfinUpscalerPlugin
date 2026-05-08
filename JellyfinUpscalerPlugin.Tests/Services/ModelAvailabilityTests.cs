using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// Unit tests for the v1.6.1.19 ModelAvailability static class — the single source of truth
    /// for which model IDs the Docker AI service has marked as available:False upstream.
    ///
    /// These tests serve two purposes:
    ///   1. Lock down the contract so refactors don't accidentally relax IsKnownUnavailable.
    ///   2. Cross-check against the C# resolver classes that were previously duplicating this
    ///      list (UpscalerCore.PickAvailable + HardwareBenchmarkService.EnsureModelAvailable).
    /// </summary>
    public class ModelAvailabilityTests
    {
        // ──────────────────────────────────────────────────────────────────────
        // Contract tests — what's in the set, what's the right shape
        // ──────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("nomos8k-hat-x4")]
        [InlineData("apisr-x3")]
        [InlineData("edvr-m-x4")]
        [InlineData("realbasicvsr-x4")]
        [InlineData("animesr-v2-x4")]
        public void IsKnownUnavailable_ReturnsTrue_ForAllSelfHostModels(string modelId)
        {
            ModelAvailability.IsKnownUnavailable(modelId).Should().BeTrue(
                $"{modelId} is documented as self-host required (no public ONNX mirror or CPU-EP issue)");
        }

        [Theory]
        [InlineData("realesrgan-x4")]
        [InlineData("realesrgan-x4-256")]
        [InlineData("anime-compact-x4")]
        [InlineData("ultrasharp-v2-x4")]
        [InlineData("nomos2-realplksr-x4")]
        [InlineData("realesrgan-animevideo-x4")]
        [InlineData("real-cugan-x4")]
        [InlineData("drct-l-x4")]
        [InlineData("bhi-realplksr-x4")]
        [InlineData("rife-v4.25")]
        public void IsKnownUnavailable_ReturnsFalse_ForAvailableModels(string modelId)
        {
            ModelAvailability.IsKnownUnavailable(modelId).Should().BeFalse(
                $"{modelId} is in the public registry as available:True");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void IsKnownUnavailable_HandlesNullEmptyWhitespace_ReturnsFalse(string? modelId)
        {
            ModelAvailability.IsKnownUnavailable(modelId).Should().BeFalse(
                "an empty / null model ID is not 'known unavailable' — it's just empty (caller should validate separately)");
        }

        [Theory]
        [InlineData("EDVR-M-x4")]      // upper case
        [InlineData("Animesr-V2-X4")]  // mixed case
        [InlineData("APISR-X3")]       // all caps
        public void IsKnownUnavailable_IsCaseInsensitive(string modelId)
        {
            ModelAvailability.IsKnownUnavailable(modelId).Should().BeTrue(
                "saved configs and HTTP request bodies sometimes mismatch on letter casing");
        }

        // ──────────────────────────────────────────────────────────────────────
        // PickAvailable — the pure picker logic
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public void PickAvailable_ReturnsPreferred_WhenAvailable()
        {
            var picked = ModelAvailability.PickAvailable("realesrgan-x4", "fsrcnn-x2");
            picked.Should().Be("realesrgan-x4");
        }

        [Fact]
        public void PickAvailable_FallsThroughToFirstAvailable_WhenPreferredUnavailable()
        {
            // edvr-m-x4 is self-host → skip → ultrasharp-v2-x4 is available → pick it
            var picked = ModelAvailability.PickAvailable("edvr-m-x4", "ultrasharp-v2-x4", "realesrgan-x4");
            picked.Should().Be("ultrasharp-v2-x4");
        }

        [Fact]
        public void PickAvailable_FallsThroughMultipleStages()
        {
            // animesr-v2-x4 unavailable → realbasicvsr-x4 unavailable → ultrasharp-v2-x4 available
            var picked = ModelAvailability.PickAvailable(
                "animesr-v2-x4",
                "realbasicvsr-x4",
                "ultrasharp-v2-x4");
            picked.Should().Be("ultrasharp-v2-x4");
        }

        [Fact]
        public void PickAvailable_FallsBackToRealesrganX4_WhenAllCandidatesUnavailable()
        {
            // Pathological case: every entry in chain is in KnownUnavailable
            var picked = ModelAvailability.PickAvailable(
                "edvr-m-x4",
                "realbasicvsr-x4",
                "animesr-v2-x4");
            picked.Should().Be("realesrgan-x4",
                "the ultimate fallback is the plugin default, which must always be in the registry");
        }

        [Fact]
        public void PickAvailable_IgnoresEmptyOrNullFallbackEntries()
        {
            var picked = ModelAvailability.PickAvailable(
                "edvr-m-x4",
                null!,
                "",
                "   ",
                "ultrasharp-v2-x4");
            picked.Should().Be("ultrasharp-v2-x4",
                "null/empty/whitespace fallback entries are skipped, not treated as 'available'");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Drift-protection — KnownUnavailable HashSet shape
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public void KnownUnavailable_ContainsExactlyFiveEntries()
        {
            // If you add or remove an entry from KnownUnavailable, also update:
            //   - docker-ai-service/app/main.py AVAILABLE_MODELS available:False entries
            //   - Resources/models-fallback.json (regen via Scripts/sync-fallback-models.ps1)
            //   - This count assertion (intentional friction so the list is reviewed)
            ModelAvailability.KnownUnavailable.Should().HaveCount(5,
                "the count is locked down so an accidental .Add() in a refactor doesn't slip through");
        }

        [Fact]
        public void KnownUnavailable_DoesNotContainPluginDefault()
        {
            ModelAvailability.KnownUnavailable.Should().NotContain("realesrgan-x4",
                "the plugin default model must always be in the available registry — " +
                "PickAvailable's last-resort fallback depends on it");
        }
    }
}
