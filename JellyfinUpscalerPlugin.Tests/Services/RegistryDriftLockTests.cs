using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.7.1 - DRY consolidation of the per-registry HTML drift-lock tests.
    /// Replaces three separate HtmlDropdown_ListsExactlyTheXInRegistry test methods
    /// (one each in CodecRegistryTests / QualityLevelRegistryTests / ButtonPositionRegistryTests)
    /// with a single [Theory]-driven check that covers every &lt;select&gt; backed by a Registry.
    ///
    /// Adding a new Registry: add one MemberData entry, no new test method needed.
    /// </summary>
    public class RegistryDriftLockTests
    {
        public static IEnumerable<object[]> RegistryDropdownPairs()
        {
            yield return new object[] { "OutputCodec",        CodecRegistry.OutputCodecs };
            yield return new object[] { "QualityLevel",       QualityLevelRegistry.Levels };
            yield return new object[] { "ButtonPosition",     ButtonPositionRegistry.Positions };
            // RealtimeMode: UI-set, NOT AcceptedAtImport (which additionally has v1.6.x aliases).
            yield return new object[] { "RealtimeMode",       RealtimeModeRegistry.UiModes };
            yield return new object[] { "ActiveFilterPreset", VideoFilterService.SupportedPresets };
        }

        [Theory]
        [MemberData(nameof(RegistryDropdownPairs))]
        public void HtmlDropdown_MatchesRegistry(string selectId, IReadOnlySet<string> expectedValues)
        {
            var html = ReadEmbeddedHtml();

            var selectMatch = Regex.Match(html,
                @"<select\s+id=""" + Regex.Escape(selectId) + @"""[^>]*>(.*?)</select>",
                RegexOptions.Singleline);
            selectMatch.Success.Should().BeTrue(
                $"the #{selectId} <select> must exist in the embedded configurationpage.html");

            var optionValues = Regex.Matches(selectMatch.Groups[1].Value, @"<option\s+value=""([^""]+)""")
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

            optionValues.Should().BeEquivalentTo(expectedValues,
                $"every UI <option> value for #{selectId} must be in the registry and vice versa");
        }

        internal static string ReadEmbeddedHtml()
        {
            var asm = typeof(CodecRegistry).Assembly;
            const string resourceName = "JellyfinUpscalerPlugin.Configuration.configurationpage.html";
            using var stream = asm.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException(
                    $"Embedded resource '{resourceName}' not found. Available: {string.Join(", ", asm.GetManifestResourceNames())}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
