using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    public class CachedHealthMonitorTests
    {
        private readonly Mock<ILogger<CachedHealthMonitor>> _logger = new();
        private readonly Mock<IUpscalerHttpClient> _http = new();
        private readonly Mock<IServiceUrlProvider> _urls = new();

        public CachedHealthMonitorTests()
        {
            _urls.Setup(u => u.GetServiceUrl()).Returns("http://localhost:5000");
        }

        [Fact]
        public async Task IsServiceAvailableAsync_CachesPositiveResult_WithinTtl()
        {
            _http.Setup(h => h.CheckHealthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

            var sut = new CachedHealthMonitor(_logger.Object, _http.Object, _urls.Object);

            var first = await sut.IsServiceAvailableAsync(CancellationToken.None);
            var second = await sut.IsServiceAvailableAsync(CancellationToken.None);

            first.Should().BeTrue();
            second.Should().BeTrue();
            _http.Verify(h => h.CheckHealthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task InvalidateCache_ForcesFreshCheck()
        {
            _http.Setup(h => h.CheckHealthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

            var sut = new CachedHealthMonitor(_logger.Object, _http.Object, _urls.Object);

            await sut.IsServiceAvailableAsync(CancellationToken.None);
            sut.InvalidateCache();
            await sut.IsServiceAvailableAsync(CancellationToken.None);

            _http.Verify(h => h.CheckHealthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task IsServiceAvailableAsync_ReturnsFalse_WhenHttpClientReportsFailure()
        {
            _http.Setup(h => h.CheckHealthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);

            var sut = new CachedHealthMonitor(_logger.Object, _http.Object, _urls.Object);
            var result = await sut.IsServiceAvailableAsync(CancellationToken.None);

            result.Should().BeFalse();
        }
    }
}
