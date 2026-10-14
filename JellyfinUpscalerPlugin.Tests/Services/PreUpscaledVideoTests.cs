using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using JellyfinUpscalerPlugin.Controllers;
using JellyfinUpscalerPlugin.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// The dashboard's Pre-upscaled tab (GET /Upscaler/preupscaled). Its first tests asserted
    /// that strings occurred in the sources: removing the admin check and breaking the paging
    /// left all of them green. These call the scanner and the endpoint.
    /// </summary>
    public class PreUpscaledVideoTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "upscaler-preupscaled-" + Guid.NewGuid().ToString("N"));

        public PreUpscaledVideoTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { /* temp dir */ }
        }

        private string File(string relative, int bytes = 1)
        {
            var path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, new byte[bytes]);
            return path;
        }

        private static PreUpscaledSource Source(string path, int width = 720, int height = 480) =>
            new(Guid.NewGuid(), Path.GetFileNameWithoutExtension(path), path, width, height);

        private static List<PreUpscaledEntry> Scan(params PreUpscaledSource[] videos) =>
            PreUpscaledScanner.Scan(videos, 1920, 1080);

        private static string RepoFile(params string[] parts)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !System.IO.File.Exists(Path.Combine(dir.FullName, "JellyfinUpscalerPlugin.csproj")))
            {
                dir = dir.Parent;
            }
            dir.Should().NotBeNull("the test must be able to find the repository root");
            return System.IO.File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
        }

        // ── Who may call it ──────────────────────────────────────────────

        [Fact]
        public void Only_administrators_may_list_the_library_with_its_paths()
        {
            var method = typeof(UpscalerController).GetMethod(nameof(UpscalerController.GetPreUpscaledVideos))!;
            method.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("preupscaled");
            method.GetCustomAttributes<AuthorizeAttribute>().Should().Contain(a => a.Policy == "RequiresElevation",
                "every row carries a file system path, from every library, whatever the caller may access");
        }

        // ── What the scan reports ────────────────────────────────────────

        [Fact]
        public void A_copy_next_to_its_source_marks_the_source_and_is_not_a_row_of_its_own()
        {
            var movie = File("movies/Movie.mkv");
            var copy = File("movies/Movie_upscaled.mkv", 4096);

            var rows = Scan(Source(movie), Source(copy, 2880, 1920));

            rows.Should().ContainSingle("the copy is shown on its source's row, with no Re-upscale button of its own");
            rows[0].Path.Should().Be(movie);
            rows[0].Status.Should().Be(PreUpscaledScanner.UpscaledStatus);
            rows[0].UpscaledPath.Should().Be(copy);
            rows[0].UpscaledSize.Should().Be(4096);
            rows[0].UpscaledDate.Should().NotBeNull();
        }

        [Fact]
        public void A_copy_with_another_video_extension_counts_but_a_sidecar_file_does_not()
        {
            var avi = File("old/Clip.avi");
            var copy = File("old/Clip_upscaled.mkv");
            var show = File("tv/Show.mkv");
            File("tv/Show_upscaled.nfo"); // Jellyfin writes these for any item, upscaled or not

            var rows = Scan(Source(avi), Source(show));

            rows.Single(r => r.Path == avi).UpscaledPath.Should().Be(copy);
            rows.Single(r => r.Path == show).Status.Should().Be(PreUpscaledScanner.PendingStatus);
        }

        [Fact]
        public void An_output_whose_source_is_not_in_the_library_is_listed_on_its_own()
        {
            var orphan = File("movies/Gone_upscaled.mkv");
            File("movies/Gone.nfo");

            var rows = Scan(Source(orphan, 2880, 1920));

            rows.Should().ContainSingle();
            rows[0].Status.Should().Be(PreUpscaledScanner.UpscaledStatus);
            rows[0].UpscaledPath.Should().Be(orphan);
        }

        [Theory]
        [InlineData(720, 480, "Pending")]
        [InlineData(1919, 1080, "Pending")]
        [InlineData(1920, 1079, "Pending")]
        [InlineData(1920, 1080, "Native HD")]
        [InlineData(3840, 2160, "Native HD")]
        [InlineData(0, 0, "Native HD")] // unknown size: never queued as low resolution
        public void Below_the_minimum_resolution_is_pending(int width, int height, string status)
        {
            var path = File("movies/Film.mkv");

            Scan(Source(path, width, height)).Single().Status.Should().Be(status);
        }

        [Fact]
        public void Wildcard_characters_in_a_name_match_only_themselves()
        {
            if (OperatingSystem.IsWindows())
            {
                return; // '*' and '?' are not legal in Windows file names
            }

            // The first version looked for "<name>_upscaled.*" with EnumerateFiles, which
            // reads '?' as "any character": "A?B" then claimed AxB's copy as its own.
            var wild = File("odd/A?B.mkv");
            File("odd/AxB_upscaled.mkv");

            Scan(Source(wild)).Single().Status.Should().Be(PreUpscaledScanner.PendingStatus);
        }

        // ── The endpoint over the scan ───────────────────────────────────

        private (UpscalerController Controller, List<BaseItem> Library) Controller()
        {
            var library = new List<BaseItem>();
            var libraryManager = new Mock<ILibraryManager>();
            libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(() => library.ToList());
            var controller = new UpscalerController(
                NullLogger<UpscalerController>.Instance,
                libraryManager.Object,
                null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
            return (controller, library);
        }

        private static Video Video(string path, int width, int height) =>
            new() { Id = Guid.NewGuid(), Name = Path.GetFileNameWithoutExtension(path), Path = path, Width = width, Height = height };

        private static JsonElement Json(ActionResult<object> result)
        {
            var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            return JsonDocument.Parse(JsonSerializer.Serialize(ok.Value)).RootElement;
        }

        [Fact]
        public void The_endpoint_counts_filters_searches_and_pages_over_the_scan()
        {
            var (controller, library) = Controller();
            for (int i = 1; i <= 25; i++)
            {
                library.Add(Video(File($"show/Ep{i:00}.mkv"), 720, 480));
            }
            library.Add(Video(File("show/Ep03_upscaled.mkv"), 2880, 1920));
            library.Add(Video(File("movies/Hd1.mkv"), 1920, 1080));
            library.Add(Video(File("movies/Hd2.mkv"), 3840, 2160));

            var all = Json(controller.GetPreUpscaledVideos(page: 2, pageSize: 10, refresh: true));
            all.GetProperty("total").GetInt32().Should().Be(27, "Ep03_upscaled is shown on Ep03's row");
            all.GetProperty("upscaledCount").GetInt32().Should().Be(1);
            all.GetProperty("pendingCount").GetInt32().Should().Be(24);
            all.GetProperty("highResCount").GetInt32().Should().Be(2);
            all.GetProperty("totalPages").GetInt32().Should().Be(3);
            var page2 = all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList();
            page2.Should().Equal(Enumerable.Range(11, 10).Select(i => $"Ep{i:00}"));

            var ep03 = Json(controller.GetPreUpscaledVideos(search: "ep03")).GetProperty("items").EnumerateArray().Single();
            ep03.GetProperty("isPreUpscaled").GetBoolean().Should().BeTrue();
            ep03.GetProperty("upscaledFileName").GetString().Should().Be("Ep03_upscaled.mkv");

            Json(controller.GetPreUpscaledVideos(filter: "pending")).GetProperty("filteredTotal").GetInt32().Should().Be(24);
            Json(controller.GetPreUpscaledVideos(filter: "highres")).GetProperty("filteredTotal").GetInt32().Should().Be(2);
            Json(controller.GetPreUpscaledVideos(search: "EP2")).GetProperty("filteredTotal").GetInt32().Should().Be(6, "Ep20 to Ep25");
        }

        [Fact]
        public void Paging_and_search_reuse_the_scan_until_refresh_asks_for_a_new_one()
        {
            var (controller, library) = Controller();
            library.Add(Video(File("movies/First.mkv"), 720, 480));
            Json(controller.GetPreUpscaledVideos(refresh: true)).GetProperty("total").GetInt32().Should().Be(1);

            library.Add(Video(File("movies/Second.mkv"), 720, 480));
            Json(controller.GetPreUpscaledVideos(page: 1)).GetProperty("total").GetInt32().Should().Be(1,
                "a page change must not list every library folder again");
            Json(controller.GetPreUpscaledVideos(refresh: true)).GetProperty("total").GetInt32().Should().Be(2);
        }

        // ── The tab's wiring ─────────────────────────────────────────────

        [Fact]
        public void The_dashboard_tab_requests_the_endpoint_and_has_its_controls()
        {
            var html = RepoFile("Configuration", "configurationpage.html");
            html.Should().Contain("Upscaler/preupscaled");
            html.Should().Contain("data-tab=\"preupscaled\"");
            html.Should().Contain("id=\"tab-preupscaled\"");
            html.Should().Contain("id=\"preupscaled-search\"");
            html.Should().Contain("id=\"preupscaled-filter\"");
            html.Should().Contain("id=\"btn-preupscaled-prev\"");
            html.Should().Contain("id=\"btn-preupscaled-next\"");
            html.Should().Contain("data-action=\"enqueue-video\"");
            html.Should().Contain("url += '&refresh=true'", "the Refresh button must ask for a new scan");
        }
    }
}
