using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.7.0 (P0) regression guards for ButtonPositionRegistry &lt;-&gt; UI dropdown sync.
    /// </summary>
    public class ButtonPositionRegistryTests
    {
        [Fact]
        public void Positions_HasExactly3Entries_LockingDriftAgainstUI()
        {
            ButtonPositionRegistry.Positions.Should().HaveCount(3,
                "the settings UI #ButtonPosition dropdown advertises 3 positions (left, right, center)");
        }

        [Theory]
        [InlineData("left")]
        [InlineData("right")]
        [InlineData("center")]
        public void Positions_ContainsEachUIDropdownOption(string position)
        {
            ButtonPositionRegistry.Positions.Should().Contain(position,
                "this position is offered in the #ButtonPosition dropdown and must be accepted by Settings-Import");
        }

        [Fact]
        public void Positions_IsCaseInsensitive()
        {
            ButtonPositionRegistry.Positions.Contains("LEFT").Should().BeTrue();
            ButtonPositionRegistry.Positions.Contains("Right").Should().BeTrue();
            ButtonPositionRegistry.Positions.Contains("CENTER").Should().BeTrue();
        }

        [Fact]
        public void HtmlDropdown_ListsExactlyThePositionsInRegistry()
        {
            var html = ReadEmbeddedHtml();
            var selectMatch = Regex.Match(html,
                @"<select\s+id=""ButtonPosition""[^>]*>(.*?)</select>",
                RegexOptions.Singleline);
            selectMatch.Success.Should().BeTrue("the #ButtonPosition select must exist in the embedded HTML");

            var optionValues = Regex.Matches(selectMatch.Groups[1].Value, @"<option\s+value=""([^""]+)""")
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

            optionValues.Should().BeEquivalentTo(ButtonPositionRegistry.Positions,
                "every UI <option> value must be in ButtonPositionRegistry.Positions and vice versa");
        }

        private static string ReadEmbeddedHtml()
        {
            var asm = typeof(ButtonPositionRegistry).Assembly;
            var resourceName = "JellyfinUpscalerPlugin.Configuration.configurationpage.html";
            using var stream = asm.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException(
                    $"Embedded resource '{resourceName}' not found. Available: {string.Join(", ", asm.GetManifestResourceNames())}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
