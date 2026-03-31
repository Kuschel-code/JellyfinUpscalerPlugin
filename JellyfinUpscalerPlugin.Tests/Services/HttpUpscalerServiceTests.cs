using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// Unit tests for HttpUpscalerService.
    /// Uses MockHttp to avoid real HTTP calls and Moq for ILogger.
    /// </summary>
    public class HttpUpscalerServiceTests : IDisposable
    {
        private readonly Mock<ILogger<JellyfinUpscalerPlugin.Services.HttpUpscalerService>> _loggerMock;

        public HttpUpscalerServiceTests()
        {
            _loggerMock = new Mock<ILogger<JellyfinUpscalerPlugin.Services.HttpUpscalerService>>();
        }

        // Helper: create a service backed by the provided HttpClient via an IHttpClientFactory mock
        private JellyfinUpscalerPlugin.Services.HttpUpscalerService CreateService(HttpClient? httpClient = null)
        {
            if (httpClient == null)
            {
                return new JellyfinUpscalerPlugin.Services.HttpUpscalerService(_loggerMock.Object);
            }

            var factoryMock = new Mock<IHttpClientFactory>();
            factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            return new JellyfinUpscalerPlugin.Services.HttpUpscalerService(_loggerMock.Object, factoryMock.Object);
        }

        // ── GetServiceUrl (tested indirectly) ─────────────────────────────────────

        [Fact]
        public async Task IsServiceAvailableAsync_UsesDefaultUrl_WhenPluginInstanceIsNull()
        {
            // Plugin.Instance is null in unit test context, so GetServiceUrl() falls back to
            // "http://localhost:5000". We verify the method runs without throwing and
            // returns false when there is no real service listening.
            using var service = CreateService();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            var result = await service.IsServiceAvailableAsync(cts.Token);

            result.Should().BeFalse();
        }

        // ── UpscaleImageAsync ──────────────────────────────────────────────────────

        [Fact]
        public async Task UpscaleImageAsync_ReturnsNull_WhenImageDataIsEmpty()
        {
            using var service = CreateService();

            var result = await service.UpscaleImageAsync(Array.Empty<byte>());

            result.Should().BeNull();
        }

        [Fact]
        public async Task UpscaleImageAsync_ReturnsNull_WhenImageDataIsNull()
        {
            using var service = CreateService();

#pragma warning disable CS8625 // intentional null for test
            var result = await service.UpscaleImageAsync(null!);
#pragma warning restore CS8625

            result.Should().BeNull();
        }

        [Fact]
        public async Task UpscaleImageAsync_RetriesOnHttpRequestException_AndMakesThreeAttempts()
        {
            // maxRetries = 2, so total attempts = 3 (attempt 0, 1, 2)
            var mockHttp = new MockHttpMessageHandler();
            var matcher = mockHttp.When("http://localhost:5000/upscale")
                                  .Throw(new HttpRequestException("Connection refused"));

            using var httpClient = mockHttp.ToHttpClient();
            using var service = CreateService(httpClient);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var imageData = new byte[] { 0xFF, 0xD8, 0xFF };

            var result = await service.UpscaleImageAsync(imageData, 2, cts.Token);

            result.Should().BeNull("all retries exhausted with HttpRequestException");
            mockHttp.GetMatchCount(matcher).Should().Be(3);
        }

        [Fact]
        public async Task UpscaleImageAsync_DoesNotRetry_WhenCancelled()
        {
            var mockHttp = new MockHttpMessageHandler();
            var matcher = mockHttp.When("http://localhost:5000/upscale")
                                  .Throw(new TaskCanceledException("cancelled"));

            using var httpClient = mockHttp.ToHttpClient();
            using var service = CreateService(httpClient);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var imageData = new byte[] { 0x01, 0x02, 0x03 };

            var result = await service.UpscaleImageAsync(imageData, 2, cts.Token);

            result.Should().BeNull();
            // TaskCanceledException breaks out of the retry loop immediately
            mockHttp.GetMatchCount(matcher).Should().Be(1);
        }

        // ── DownloadModelAsync ─────────────────────────────────────────────────────

        [Fact]
        public async Task DownloadModelAsync_ReturnsFalse_AfterAllRetriesExhausted()
        {
            // maxRetries = 1 → 2 total attempts
            var mockHttp = new MockHttpMessageHandler();
            var matcher = mockHttp.When("http://localhost:5000/models/download")
                                  .Throw(new HttpRequestException("Network unreachable"));

            using var httpClient = mockHttp.ToHttpClient();
            using var service = CreateService(httpClient);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await service.DownloadModelAsync("realesrgan-x4", cts.Token);

            result.Should().BeFalse();
            mockHttp.GetMatchCount(matcher).Should().Be(2);
        }

        [Fact]
        public async Task DownloadModelAsync_ReturnsFalse_OnClientError_WithoutRetry()
        {
            // 4xx client error → no retry, returns false immediately after 1 call
            var mockHttp = new MockHttpMessageHandler();
            var matcher = mockHttp.When("http://localhost:5000/models/download")
                                  .Respond(HttpStatusCode.BadRequest);

            using var httpClient = mockHttp.ToHttpClient();
            using var service = CreateService(httpClient);

            var result = await service.DownloadModelAsync("nonexistent-model");

            result.Should().BeFalse();
            mockHttp.GetMatchCount(matcher).Should().Be(1);
        }

        // ── LoadModelAsync ─────────────────────────────────────────────────────────

        [Fact]
        public async Task LoadModelAsync_ReturnsFalse_OnNetworkError()
        {
            var mockHttp = new MockHttpMessageHandler();
            mockHttp.When("http://localhost:5000/models/load")
                    .Throw(new HttpRequestException("Connection refused"));

            using var httpClient = mockHttp.ToHttpClient();
            using var service = CreateService(httpClient);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await service.LoadModelAsync("realesrgan-x4", true, 0, cts.Token);

            result.Should().BeFalse();
        }

        [Fact]
        public async Task LoadModelAsync_ReturnsTrue_OnSuccess()
        {
            var mockHttp = new MockHttpMessageHandler();
            mockHttp.When("http://localhost:5000/models/load")
                    .Respond(HttpStatusCode.OK);

            using var httpClient = mockHttp.ToHttpClient();
            using var service = CreateService(httpClient);

            var result = await service.LoadModelAsync("realesrgan-x4", true, 0);

            result.Should().BeTrue();
        }

        // ── IsServiceAvailableAsync — health caching ───────────────────────────────

        [Fact]
        public async Task IsServiceAvailableAsync_ReturnsCachedResult_WithinTtl()
        {
            var mockHttp = new MockHttpMessageHandler();
            var matcher = mockHttp.When("http://localhost:5000/health")
                                  .Respond(HttpStatusCode.OK);

            using var httpClient = mockHttp.ToHttpClient();
            using var service = CreateService(httpClient);

            var first = await service.IsServiceAvailableAsync();
            var second = await service.IsServiceAvailableAsync(); // should use cached result

            first.Should().BeTrue();
            second.Should().BeTrue();
            // Only one real HTTP call despite two invocations
            mockHttp.GetMatchCount(matcher).Should().Be(1);
        }

        [Fact]
        public async Task InvalidateHealthCache_ForcesFreshCheck_OnNextCall()
        {
            var mockHttp = new MockHttpMessageHandler();
            var matcher = mockHttp.When("http://localhost:5000/health")
                                  .Respond(HttpStatusCode.OK);

            using var httpClient = mockHttp.ToHttpClient();
            using var service = CreateService(httpClient);

            // Prime the cache
            await service.IsServiceAvailableAsync();

            // Invalidate — next call must re-hit the network
            service.InvalidateHealthCache();

            await service.IsServiceAvailableAsync();

            mockHttp.GetMatchCount(matcher).Should().Be(2);
        }

        public void Dispose() { }
    }
}
