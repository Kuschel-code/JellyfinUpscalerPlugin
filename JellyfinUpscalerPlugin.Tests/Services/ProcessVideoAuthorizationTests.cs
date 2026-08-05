using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Jellyfin.Data.Enums;
using JellyfinUpscalerPlugin.Controllers;
using JellyfinUpscalerPlugin.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// The critical fix of v1.8.3.22, tested by calling the endpoint.
    ///
    /// It was guarded only by counting occurrences of <c>IsInsideMediaLibrary(</c> in the
    /// source. Mutation testing showed what that is worth: making the call unreachable while
    /// leaving its text in place kept the count at four and the suite green, with the
    /// arbitrary-path hole fully reopened.
    ///
    /// I had written off this endpoint as untestable without a running Jellyfin. That was
    /// wrong, and worth correcting rather than working around: the allowlist check runs after
    /// nothing but File.Exists, Path.GetFullPath and a log call, so every other dependency is
    /// still null when control reaches it. One mocked ILibraryManager is the whole fixture.
    /// </summary>
    public class ProcessVideoAuthorizationTests : IDisposable
    {
        private readonly string _libraryRoot;
        private readonly string _outsideRoot;

        public ProcessVideoAuthorizationTests()
        {
            var baseDir = Path.Combine(Path.GetTempPath(), "upscaler-auth-" + Guid.NewGuid().ToString("N"));
            _libraryRoot = Path.Combine(baseDir, "media", "movies");
            _outsideRoot = Path.Combine(baseDir, "elsewhere");
            Directory.CreateDirectory(_libraryRoot);
            Directory.CreateDirectory(_outsideRoot);
        }

        public void Dispose()
        {
            var parent = Directory.GetParent(_libraryRoot)?.Parent;
            try { if (parent?.Exists == true) parent.Delete(true); } catch { /* temp dir */ }
        }

        private UpscalerController BuildController(string libraryLocation)
        {
            var libraryManager = new Mock<ILibraryManager>();
            libraryManager.Setup(m => m.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo>
                {
                    new VirtualFolderInfo
                    {
                        Name = "Movies",
                        Locations = new[] { libraryLocation },
                        CollectionType = CollectionTypeOptions.movies,
                    },
                });

            // Everything below the allowlist check is unreachable in these tests, so the
            // remaining dependencies stay null on purpose: if a future change starts touching
            // one of them BEFORE authorising the path, this fixture fails loudly instead of
            // quietly authorising something.
            return new UpscalerController(
                NullLogger<UpscalerController>.Instance,
                libraryManager.Object,
                null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        }

        private static string ErrorOf(ActionResult<object> result)
        {
            var bad = result.Result as BadRequestObjectResult;
            bad.Should().NotBeNull("the request must be rejected");
            return bad!.Value!.ToString()!;
        }

        [Fact]
        public async Task A_file_outside_every_library_is_rejected()
        {
            // The hole: any authenticated user could name any readable path on the server.
            var outsideFile = Path.Combine(_outsideRoot, "secret.mkv");
            File.WriteAllText(outsideFile, "x");
            var controller = BuildController(_libraryRoot);

            var result = await controller.ProcessVideo(new VideoProcessRequest
            {
                InputPath = outsideFile,
                OutputPath = Path.Combine(_outsideRoot, "out.mkv"),
            });

            ErrorOf(result).Should().Contain("within a Jellyfin media library");
        }

        [Fact]
        public async Task A_sibling_directory_does_not_count_as_inside()
        {
            // "/media/movies-private" must not pass an allowlist holding "/media/movies".
            var sibling = _libraryRoot + "-private";
            Directory.CreateDirectory(sibling);
            var file = Path.Combine(sibling, "film.mkv");
            File.WriteAllText(file, "x");
            var controller = BuildController(_libraryRoot);

            var result = await controller.ProcessVideo(new VideoProcessRequest
            {
                InputPath = file,
                OutputPath = Path.Combine(sibling, "out.mkv"),
            });

            ErrorOf(result).Should().Contain("within a Jellyfin media library");
        }

        [Fact]
        public async Task A_file_inside_the_library_gets_past_the_allowlist()
        {
            // Proves the two tests above fail for the RIGHT reason. This one must reach the
            // next gate rather than being rejected as outside the library - otherwise a check
            // that rejects everything would look like a passing security test.
            var inside = Path.Combine(_libraryRoot, "film.mkv");
            File.WriteAllText(inside, "x");
            var existingOutput = Path.Combine(_libraryRoot, "already-there.mkv");
            File.WriteAllText(existingOutput, "x");
            var controller = BuildController(_libraryRoot);

            var result = await controller.ProcessVideo(new VideoProcessRequest
            {
                InputPath = inside,
                OutputPath = existingOutput,
            });

            var error = ErrorOf(result);
            error.Should().Contain("already exists", "it must be stopped by the overwrite guard");
            error.Should().NotContain("within a Jellyfin media library");
        }

        [Fact]
        public async Task An_existing_output_file_is_never_overwritten()
        {
            // ffmpeg runs with -y, so "output" naming the film next to the input was a
            // silent-replacement primitive.
            var inside = Path.Combine(_libraryRoot, "film.mkv");
            File.WriteAllText(inside, "x");
            var victim = Path.Combine(_libraryRoot, "the-other-film.mkv");
            File.WriteAllText(victim, "original content");
            var controller = BuildController(_libraryRoot);

            var result = await controller.ProcessVideo(new VideoProcessRequest
            {
                InputPath = inside,
                OutputPath = victim,
            });

            ErrorOf(result).Should().Contain("already exists");
            File.ReadAllText(victim).Should().Be("original content", "the file must be untouched");
        }

        [Fact]
        public async Task A_nonexistent_input_is_rejected_before_anything_else()
        {
            var controller = BuildController(_libraryRoot);

            var result = await controller.ProcessVideo(new VideoProcessRequest
            {
                InputPath = Path.Combine(_libraryRoot, "no-such-file.mkv"),
                OutputPath = Path.Combine(_libraryRoot, "out.mkv"),
            });

            ErrorOf(result).Should().Contain("Input file not found");
        }
    }
}
