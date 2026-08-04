using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.3.22 — the UI's JSON bodies must use the DTO's property names.
    ///
    /// This class exists because of a defect that shipped and survived two releases:
    /// the filter-suggestion Apply button posted <c>{ ActiveFilterPreset, EnableVideoFilters }</c>
    /// — the CONFIG field names — while the endpoint binds <c>FilterConfigUpdate { Preset,
    /// Enabled }</c>. ASP.NET silently ignores unknown properties, so every field stayed
    /// null, nothing was saved, and the endpoint still answered <c>success: true</c>. The
    /// user got a toast saying the preset was set, the config was untouched, and the
    /// suggestion reappeared on the next render.
    ///
    /// Nothing catches that: not the compiler (different languages), not the tests (no
    /// endpoint is exercised end-to-end), not a code review that reads one side at a time.
    /// The only cheap guard is to read both sides and compare the names.
    /// </summary>
    public class JsPayloadContractTests
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

        private static string Read(params string[] parts) =>
            File.ReadAllText(Path.Combine(new[] { RepoRoot().FullName }.Concat(parts).ToArray()));

        /// <summary>Property names of a C# DTO class in UpscalerController.cs.</summary>
        private static HashSet<string> DtoProperties(string className)
        {
            var src = Read("Controllers", "UpscalerController.cs");
            var start = src.IndexOf($"public class {className}");
            start.Should().BeGreaterThan(-1, $"{className} must exist");
            var body = src.Substring(start);
            body = body.Substring(0, body.IndexOf("\n    }"));
            return Regex.Matches(body, @"public\s+[\w?<>\[\]]+\s+(\w+)\s*\{\s*get;")
                        .Select(m => m.Groups[1].Value)
                        .ToHashSet();
        }

        /// <summary>Keys of every object literal passed to JSON.stringify next to the given URL.</summary>
        private static IEnumerable<string> PostedKeys(string jsFile, string urlFragment)
        {
            var src = Read("Configuration", jsFile);
            foreach (Match call in Regex.Matches(src, @"JSON\.stringify\(\s*\{([^}]*)\}"))
            {
                // Only the calls whose surrounding statement targets this endpoint. The
                // window is generous on purpose: a comment between the url: and data: lines
                // is enough to push them apart, and a detector that silently matches nothing
                // is worse than no detector - it passes vacuously.
                const int Back = 1500, Forward = 300;
                var from = System.Math.Max(0, call.Index - Back);
                var window = src.Substring(from, System.Math.Min(Back + Forward, src.Length - from));
                if (!window.Contains(urlFragment)) continue;

                foreach (Match key in Regex.Matches(call.Groups[1].Value, @"(\w+)\s*:"))
                {
                    yield return key.Groups[1].Value;
                }
            }
        }

        [Fact]
        public void The_filter_config_payloads_use_the_dto_property_names()
        {
            var dto = DtoProperties("FilterConfigUpdate");
            dto.Should().Contain("Preset").And.Contain("Enabled");

            foreach (var file in new[] { "player-integration.js", "sidebar-upscaler.js", "configurationpage.html" })
            {
                var posted = PostedKeys(file, "Upscaler/filter-config").ToList();
                var unknown = posted.Where(k => !dto.Contains(k)).ToList();

                unknown.Should().BeEmpty(
                    $"{file} posts to /filter-config with key(s) the DTO does not bind: " +
                    string.Join(", ", unknown) + " — ASP.NET drops them silently and still returns success");
            }
        }

        [Fact]
        public void The_apply_button_sends_something_the_endpoint_can_actually_bind()
        {
            // A payload of only unknown keys is the exact failure mode above: it binds to an
            // all-null DTO, saves nothing, and reports success.
            var posted = PostedKeys("player-integration.js", "Upscaler/filter-config").ToList();
            posted.Should().NotBeEmpty("the Apply button must post a body");
            posted.Should().Contain("Preset", "otherwise nothing is written");
        }
    }
}
