using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.3.20 (Phase 2) — guards for the i18n groundwork.
    ///
    /// English is the SOURCE language: strings.en.json is where these strings live, not
    /// a translation of them. Adding a locale means copying the file and translating the
    /// values, with no UI change — which only holds if every key a call site asks for
    /// actually exists. A missing key renders as the key name (loud on purpose, rather
    /// than an empty label), but it should never reach a release, so it is checked here.
    ///
    /// This is the same technique check_ui_field_consistency.py uses for element ids: the
    /// guarantee lives in JavaScript, where xUnit cannot execute, so the source is read.
    /// </summary>
    public class I18nCatalogueTests
    {
        private static DirectoryInfo RepoRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JellyfinUpscalerPlugin.csproj")))
            {
                dir = dir.Parent;
            }
            dir.Should().NotBeNull("the test must be able to find the repository root");
            return dir!;
        }

        private static string ConfigFile(string name) =>
            File.ReadAllText(Path.Combine(RepoRoot().FullName, "Configuration", name));

        private static Dictionary<string, string> Catalogue()
        {
            using var doc = JsonDocument.Parse(ConfigFile("strings.en.json"));
            return doc.RootElement.EnumerateObject()
                .Where(p => p.Name != "_meta")
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        }

        private static IEnumerable<string> UsedKeys()
        {
            // t('some.key') / t("some.key") and data-i18n="some.key"
            foreach (var file in new[] { "configurationpage.html", "player-integration.js", "sidebar-upscaler.js" })
            {
                var src = ConfigFile(file);
                foreach (Match m in Regex.Matches(src, @"\bt\(\s*['""]([a-zA-Z0-9._]+)['""]"))
                    yield return m.Groups[1].Value;
                foreach (Match m in Regex.Matches(src, @"data-i18n(?:-title)?=['""]([a-zA-Z0-9._]+)['""]"))
                    yield return m.Groups[1].Value;
            }
        }

        [Fact]
        public void Every_key_the_ui_asks_for_exists_in_the_catalogue()
        {
            var catalogue = Catalogue();
            var missing = UsedKeys().Distinct().Where(k => !catalogue.ContainsKey(k)).ToList();

            missing.Should().BeEmpty(
                "a key with no entry renders as its own name in the UI: " + string.Join(", ", missing));
        }

        [Fact]
        public void The_catalogue_is_valid_json_with_only_string_values()
        {
            // A nested object or a number would silently break interpolation, which only
            // shows up as a mangled label at runtime.
            using var doc = JsonDocument.Parse(ConfigFile("strings.en.json"));
            foreach (var prop in doc.RootElement.EnumerateObject().Where(p => p.Name != "_meta"))
            {
                prop.Value.ValueKind.Should().Be(JsonValueKind.String,
                    $"'{prop.Name}' must be a plain string a translator can edit");
            }
        }

        [Fact]
        public void No_catalogue_entry_is_empty()
        {
            Catalogue().Where(kv => string.IsNullOrWhiteSpace(kv.Value))
                .Should().BeEmpty("an empty value deletes a label as effectively as a missing key");
        }

        [Fact]
        public void Both_i18n_assets_are_embedded_and_registered()
        {
            // A catalogue that is not embedded 404s at runtime and every string falls
            // back to its key - the UI would look broken in a way no unit test sees.
            var csproj = File.ReadAllText(Path.Combine(RepoRoot().FullName, "JellyfinUpscalerPlugin.csproj"));
            csproj.Should().Contain("Configuration\\i18n.js");
            csproj.Should().Contain("Configuration\\strings.en.json");

            var plugin = File.ReadAllText(Path.Combine(RepoRoot().FullName, "Plugin.cs"));
            plugin.Should().Contain("UPSCALERI18n", "the helper needs a page route to be fetchable");
            plugin.Should().Contain("UPSCALERStringsEn", "the catalogue needs a page route to be fetchable");
        }

        [Fact]
        public void The_loader_requests_the_route_that_is_actually_registered()
        {
            // The catalogue is served under its PluginPageInfo NAME, not its filename.
            // Getting this wrong 404s silently and every label becomes its key.
            ConfigFile("i18n.js").Should().Contain("'UPSCALERStringsEn'");
        }

        [Fact]
        public void A_missing_key_falls_back_to_the_key_not_to_an_empty_string()
        {
            // The single most important property of this helper: degrade loudly.
            var js = ConfigFile("i18n.js");
            js.Should().Contain("return key;");
            js.Should().Contain("missing key");
        }

        [Fact]
        public void Interpolation_placeholders_are_declared_in_the_catalogue_they_belong_to()
        {
            // A {placeholder} left in the rendered text means the call site forgot a
            // parameter. Catch the reverse here: a key whose braces are malformed.
            foreach (var (key, value) in Catalogue().Select(kv => (kv.Key, kv.Value)))
            {
                value.Count(c => c == '{').Should().Be(value.Count(c => c == '}'),
                    $"'{key}' has unbalanced interpolation braces");
                foreach (Match m in Regex.Matches(value, @"\{([^}]*)\}"))
                {
                    m.Groups[1].Value.Should().MatchRegex("^[a-zA-Z][a-zA-Z0-9]*$",
                        $"'{key}' has an unusable placeholder '{{{m.Groups[1].Value}}}'");
                }
            }
        }
    }
}
