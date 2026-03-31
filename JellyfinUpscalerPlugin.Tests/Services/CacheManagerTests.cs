using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Security.Cryptography;
using FluentAssertions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// Unit tests for CacheManager.
    ///
    /// CacheManager performs real file-system I/O in its constructor
    /// (creates cache directories, loads the index file).  We redirect
    /// all I/O to a per-test temp directory so tests are hermetic and
    /// leave no artefacts.
    /// </summary>
    public class CacheManagerTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly Mock<ILogger<JellyfinUpscalerPlugin.Services.CacheManager>> _loggerMock;
        private readonly Mock<IApplicationPaths> _appPathsMock;
        private readonly Mock<IFileSystem> _fileSystemMock;

        public CacheManagerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"CacheManagerTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);

            _loggerMock = new Mock<ILogger<JellyfinUpscalerPlugin.Services.CacheManager>>();

            _appPathsMock = new Mock<IApplicationPaths>();
            _appPathsMock.Setup(p => p.CachePath).Returns(_tempDir);

            _fileSystemMock = new Mock<IFileSystem>();
        }

        private JellyfinUpscalerPlugin.Services.CacheManager CreateCacheManager()
        {
            return new JellyfinUpscalerPlugin.Services.CacheManager(
                _loggerMock.Object,
                _appPathsMock.Object,
                _fileSystemMock.Object);
        }

        // ── GenerateCacheKey — tested via reflection because the method is private ──

        /// <summary>
        /// Computes the expected key the same way the production code does:
        /// SHA-256 of "inputPath|model|scale|quality".
        /// </summary>
        private static string ExpectedKey(string inputPath, string model, int scale, string quality)
        {
            var input = $"{inputPath}|{model}|{scale}|{quality}";
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string InvokeGenerateCacheKey(
            JellyfinUpscalerPlugin.Services.CacheManager manager,
            string inputPath, string model, int scale, string quality)
        {
            var method = typeof(JellyfinUpscalerPlugin.Services.CacheManager)
                .GetMethod("GenerateCacheKey", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("GenerateCacheKey method not found via reflection");

            return (string)method.Invoke(manager, new object[] { inputPath, model, scale, quality })!;
        }

        [Fact]
        public void GenerateCacheKey_ProducesDeterministicKey_ForSameInputs()
        {
            using var manager = CreateCacheManager();

            var key1 = InvokeGenerateCacheKey(manager, "/media/video.mkv", "realesrgan-x4", 2, "medium");
            var key2 = InvokeGenerateCacheKey(manager, "/media/video.mkv", "realesrgan-x4", 2, "medium");

            key1.Should().Be(key2, "same inputs must always produce the same cache key");
        }

        [Fact]
        public void GenerateCacheKey_MatchesExpectedSha256Hash()
        {
            using var manager = CreateCacheManager();

            var key = InvokeGenerateCacheKey(manager, "/media/video.mkv", "realesrgan-x4", 2, "medium");
            var expected = ExpectedKey("/media/video.mkv", "realesrgan-x4", 2, "medium");

            key.Should().Be(expected);
        }

        [Theory]
        [InlineData("/media/a.mkv", "/media/b.mkv", "realesrgan-x4", "realesrgan-x4", 2, 2, "medium", "medium")]
        [InlineData("/media/a.mkv", "/media/a.mkv", "realesrgan-x4", "span-x4",      2, 2, "medium", "medium")]
        [InlineData("/media/a.mkv", "/media/a.mkv", "realesrgan-x4", "realesrgan-x4", 2, 4, "medium", "medium")]
        [InlineData("/media/a.mkv", "/media/a.mkv", "realesrgan-x4", "realesrgan-x4", 2, 2, "medium", "high")]
        public void GenerateCacheKey_ProducesDifferentKey_ForDifferentInputs(
            string path1, string path2,
            string model1, string model2,
            int scale1, int scale2,
            string quality1, string quality2)
        {
            using var manager = CreateCacheManager();

            var key1 = InvokeGenerateCacheKey(manager, path1, model1, scale1, quality1);
            var key2 = InvokeGenerateCacheKey(manager, path2, model2, scale2, quality2);

            key1.Should().NotBe(key2, "different inputs must yield different cache keys");
        }

        // ── IsCacheValid — tested indirectly through GetCachedContentAsync ────────

        [Fact]
        public async System.Threading.Tasks.Task GetCachedContentAsync_ReturnsMiss_WhenKeyNotPresent()
        {
            using var manager = CreateCacheManager();

            var result = await manager.GetCachedContentAsync("/nonexistent/video.mkv", "realesrgan-x4", 2, "medium");

            result.Hit.Should().BeFalse("an empty cache should never return a hit");
        }

        [Fact]
        public async System.Threading.Tasks.Task GetCachedContentAsync_ReturnsMiss_ForNonExistentFilePath()
        {
            using var manager = CreateCacheManager();

            // Request a path that could not possibly exist in the index.
            var result = await manager.GetCachedContentAsync(
                Path.Combine(_tempDir, "ghost_video_that_does_not_exist.mkv"),
                "span-x4", 4, "high");

            result.Hit.Should().BeFalse();
            result.FilePath.Should().BeEmpty();
        }

        // ── GetCacheSize ───────────────────────────────────────────────────────────

        [Fact]
        public void GetCacheStatistics_ReturnsTotalSize0_ForFreshEmptyCache()
        {
            using var manager = CreateCacheManager();

            var stats = manager.GetCacheStatistics();

            stats.TotalSize.Should().Be(0, "a freshly created cache has no stored content");
            stats.TotalEntries.Should().Be(0);
        }

        [Fact]
        public void GetCacheStatistics_HitRateIsZero_BeforeAnyLookups()
        {
            using var manager = CreateCacheManager();

            var stats = manager.GetCacheStatistics();

            stats.HitRate.Should().Be(0.0);
            stats.TotalHits.Should().Be(0);
            stats.TotalMisses.Should().Be(0);
        }

        // ── Dispose ────────────────────────────────────────────────────────────────

        [Fact]
        public void Dispose_DoesNotThrow()
        {
            var manager = CreateCacheManager();

            var act = () => manager.Dispose();

            act.Should().NotThrow();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup — do not fail the test run
            }
        }
    }
}
