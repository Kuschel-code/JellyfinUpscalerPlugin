using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.3.20 — the three "recommend" routes were never variants of each other.
    ///
    /// <c>/recommend</c> proxies the AI service's hardware model pick, <c>/recommend-model</c>
    /// picks a model for one video, and <c>/recommendations</c> runs the LOCAL benchmark and
    /// returns <c>hardware.cpuCores</c>, <c>hardware.cudaAvailable</c>, <c>system.platform</c>.
    /// Three names that read as siblings, one answering a different question entirely.
    ///
    /// Folding them onto one schema — the obvious "consolidation" — would delete the
    /// hardware.* and system.* fields the sidebar, quick menu and System tab render. So the
    /// payload is untouched and only the misleading NAME is retired, behind an alias.
    /// These tests pin that: same handler, no UI left on the old route, warning fires once.
    /// </summary>
    public class EndpointDeprecationTests
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

        private static string Controller() => RepoFile("Controllers", "UpscalerController.cs");

        [Fact]
        public void The_new_route_exists_under_a_name_that_says_what_it_returns()
        {
            Controller().Should().Contain("[HttpGet(\"hardware-benchmark\")]",
                "the payload is a hardware benchmark; calling it a recommendation is what caused the confusion");
        }

        [Fact]
        public void The_old_route_still_exists_so_external_callers_keep_working()
        {
            // Scripts, Home Assistant and the :5000 dashboard are not in this repo and
            // cannot be migrated by renaming a string here.
            Controller().Should().Contain("[HttpGet(\"recommendations\")]");
        }

        [Fact]
        public void The_alias_delegates_instead_of_duplicating_the_body()
        {
            // A copied body is a payload that drifts. The deprecated route must call the
            // new handler, so the two can never answer differently.
            var ctrl = Controller();
            var alias = ctrl.Substring(ctrl.IndexOf("[HttpGet(\"recommendations\")]"));
            alias = alias.Substring(0, alias.IndexOf("\n        }"));
            alias.Should().Contain("return await GetHardwareBenchmark();");
            alias.Should().NotContain("_benchmarkService.GetRecommendationsAsync",
                "the alias must not re-implement what it aliases");
        }

        [Fact]
        public void The_deprecation_warning_fires_once_not_once_per_request()
        {
            // The sidebar polls this route. Logging per request would bury the very log
            // line it exists to make visible.
            var ctrl = Controller();
            ctrl.Should().Contain("Interlocked.Exchange(ref _deprecatedRecommendationsHits, 1) == 0",
                "a polled endpoint must not log its deprecation on every call");
            ctrl.Should().Contain("is deprecated");
        }

        [Theory]
        [InlineData("configurationpage.html")]
        [InlineData("quick-menu.js")]
        [InlineData("sidebar-upscaler.js")]
        public void No_bundled_ui_still_calls_the_deprecated_route(string file)
        {
            // Shipping a deprecation warning that our own UI triggers would make the log
            // useless for spotting real external callers.
            RepoFile("Configuration", file).Should().NotContain("Upscaler/recommendations",
                $"{file} must use /Upscaler/hardware-benchmark");
        }

        [Fact]
        public void The_three_recommend_routes_are_documented_as_distinct()
        {
            // The audit is the artefact that stops someone "consolidating" them again.
            var audit = RepoFile("docs", "ENDPOINT-AUDIT.md");
            audit.Should().Contain("/recommend-model");
            audit.Should().Contain("hardware-benchmark");
        }

        [Fact]
        public void The_audit_reports_route_counts_it_can_be_checked_against()
        {
            // A route count that drifts silently is a report nobody can trust. If this
            // fails, the audit needs regenerating - the instructions are in the file.
            var actual = Regex.Matches(Controller(), @"\[Http(Get|Post|Put|Delete|Patch)\(").Count;
            RepoFile("docs", "ENDPOINT-AUDIT.md").Should().Contain($"| **{actual}** |",
                $"the controller now has {actual} routes; the audit's summary table must say so");
        }
    }
}
