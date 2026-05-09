using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.7.0 (P0) regression guards for QualityLevelRegistry &lt;-&gt; UI dropdown sync.
    /// </summary>
    public class QualityLevelRegistryTests
    {
        [Fact]
        public void Levels_HasExactly3Entries_LockingDriftAgainstUI()
        {
            QualityLevelRegistry.Levels.Should().HaveCount(3,
                "the settings UI #QualityLevel dropdown advertises 3 levels (low, medium, high)");
        }

        [Theory]
        [InlineData("low")]
        [InlineData("medium")]
        [InlineData("high")]
        public void Levels_ContainsEachUIDropdownOption(string level)
        {
            QualityLevelRegistry.Levels.Should().Contain(level,
                "this level is offered in the #QualityLevel dropdown and must be accepted by Settings-Import");
        }

        [Fact]
        public void Levels_IsCaseInsensitive()
        {
            QualityLevelRegistry.Levels.Contains("LOW").Should().BeTrue();
            QualityLevelRegistry.Levels.Contains("Medium").Should().BeTrue();
            QualityLevelRegistry.Levels.Contains("HIGH").Should().BeTrue();
        }

        [Fact]
        public void Levels_RejectsLegacyFastValue_WhichWasInThePreV170Allowlist()
        {
            QualityLevelRegistry.Levels.Should().NotContain("fast",
                "fast is an internal-mapped value (low->fast in ProcessingStrategySelector), not a UI-exposed level");
        }

        [Fact]
        public void HtmlDropdown_ListsExactlyTheLevelsInRegistry()
        {
            var html = ReadEmbeddedHtml();
            var selectMatch = Regex.Match(html,
                @"<select\s+id=""QualityLevel""[^>]*>(.*?)</select>",
                RegexOptions.Singleline);
            selectMatch.Success.Should().BeTrue("the #QualityLevel select must exist in the embedded HTML");

            var optionValues = Regex.Matches(selectMatch.Groups[1].Value, @"<option\s+value=""([^""]+)""")
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

            optionValues.Should().BeEquivalentTo(QualityLevelRegistry.Levels,
                "every UI <option> value must be in QualityLevelRegistry.Levels and vice versa");
        }

        private static string ReadEmbeddedHtml()
        {
            var asm = typeof(QualityLevelRegistry).Assembly;
            var resourceName = "JellyfinUpscalerPlugin.Configuration.configurationpage.html";
            using var stream = asm.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException(
                    $"Embedded resource '{resourceName}' not found. Available: {string.Join(", ", asm.GetManifestResourceNames())}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
