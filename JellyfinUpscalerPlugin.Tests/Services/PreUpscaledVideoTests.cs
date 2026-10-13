using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// Contract tests ensuring the Pre-upscaled Videos management UI
    /// and its corresponding controller endpoint stay strictly aligned.
    /// </summary>
    public class PreUpscaledVideoTests
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

        [Fact]
        public void Controller_exposes_preupscaled_endpoint()
        {
            var controllerSource = RepoFile("Controllers", "UpscalerController.cs");
            controllerSource.Should().Contain("[HttpGet(\"preupscaled\")]");
            controllerSource.Should().Contain("GetPreUpscaledVideos");
        }

        [Fact]
        public void ConfigurationPage_requests_preupscaled_endpoint()
        {
            var htmlSource = RepoFile("Configuration", "configurationpage.html");
            htmlSource.Should().Contain("Upscaler/preupscaled");
            htmlSource.Should().Contain("data-tab=\"preupscaled\"");
            htmlSource.Should().Contain("id=\"tab-preupscaled\"");
        }

        [Fact]
        public void PreUpscaled_tab_has_pagination_and_search_controls()
        {
            var htmlSource = RepoFile("Configuration", "configurationpage.html");
            htmlSource.Should().Contain("id=\"preupscaled-search\"");
            htmlSource.Should().Contain("id=\"preupscaled-filter\"");
            htmlSource.Should().Contain("id=\"btn-preupscaled-prev\"");
            htmlSource.Should().Contain("id=\"btn-preupscaled-next\"");
            htmlSource.Should().Contain("data-action=\"enqueue-video\"");
        }

        [Fact]
        public void WebGL_upscaler_synchronizes_aspect_ratio_with_video_element()
        {
            var webglSource = RepoFile("Configuration", "webgl-upscaler.js");
            webglSource.Should().Contain("this.canvas.style.objectFit");
            webglSource.Should().Contain("texSubImage2D");
            webglSource.Should().Contain("requestVideoFrameCallback");
        }
    }
}
