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

        // v1.7.1 - HtmlDropdown drift-lock moved to RegistryDriftLockTests [Theory].
    }
}
