using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using Microsoft.Extensions.Logging;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    public class UpscalerHttpClientTests
    {
        private readonly Mock<ILogger<UpscalerHttpClient>> _logger = new();

        private UpscalerHttpClient CreateClient(HttpClient httpClient)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            return new UpscalerHttpClient(_logger.Object, factory.Object);
        }

        [Fact]
        public async Task UpscaleImageAsync_ReturnsNull_WhenInputEmpty()
        {
            var mockHttp = new MockHttpMessageHandler();
            using var http = mockHttp.ToHttpClient();
            var sut = CreateClient(http);

            var result = await sut.UpscaleImageAsync("http://localhost:5000", Array.Empty<byte>(), 2, CancellationToken.None);
            result.Should().BeNull();
        }

        [Fact]
        public async Task DownloadModelAsync_RetriesOnce_OnServerError()
        {
            // maxRetries=1 → 2 total attempts on 5xx
            var mockHttp = new MockHttpMessageHandler();
            var matcher = mockHttp.When("http://localhost:5000/models/download")
                                  .Respond(HttpStatusCode.InternalServerError);

            using var http = mockHttp.ToHttpClient();
            var sut = CreateClient(http);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await sut.DownloadModelAsync("http://localhost:5000", "anymodel", cts.Token);

            result.Should().BeFalse();
            mockHttp.GetMatchCount(matcher).Should().Be(2);
        }

        [Fact]
        public async Task LoadModelAsync_ReturnsTrue_OnSuccess()
        {
            var mockHttp = new MockHttpMessageHandler();
            mockHttp.When("http://localhost:5000/models/load").Respond(HttpStatusCode.OK);

            using var http = mockHttp.ToHttpClient();
            var sut = CreateClient(http);

            var result = await sut.LoadModelAsync("http://localhost:5000", "realesrgan-x4", true, 0, CancellationToken.None);
            result.Should().BeTrue();
        }
    }
}
