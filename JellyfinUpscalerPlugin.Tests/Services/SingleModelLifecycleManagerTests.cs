using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    public class SingleModelLifecycleManagerTests
    {
        private readonly Mock<ILogger<SingleModelLifecycleManager>> _logger = new();
        private readonly Mock<IUpscalerHttpClient> _http = new();
        private readonly Mock<IServiceUrlProvider> _urls = new();

        public SingleModelLifecycleManagerTests()
        {
            _urls.Setup(u => u.GetServiceUrl()).Returns("http://localhost:5000");
        }

        [Fact]
        public async Task EnsureModelLoadedAsync_SkipsDownload_WhenStatusReportsModelAlreadyLoaded()
        {
            // Service status says the model is already current → skip download/load entirely
            _http.Setup(h => h.GetServiceStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new ServiceStatus { CurrentModel = "realesrgan-x4" });

            var sut = new SingleModelLifecycleManager(_logger.Object, _http.Object, _urls.Object);
            var result = await sut.EnsureModelLoadedAsync("realesrgan-x4", CancellationToken.None);

            result.Should().BeTrue();
            _http.Verify(h => h.DownloadModelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _http.Verify(h => h.LoadModelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task EnsureModelLoadedAsync_CallsDownloadAndLoad_WhenStatusReportsDifferentModel()
        {
            _http.Setup(h => h.GetServiceStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new ServiceStatus { CurrentModel = "old-model" });
            _http.Setup(h => h.DownloadModelAsync(It.IsAny<string>(), "newmodel", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);
            _http.Setup(h => h.LoadModelAsync(It.IsAny<string>(), "newmodel", It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

            var sut = new SingleModelLifecycleManager(_logger.Object, _http.Object, _urls.Object);
            var result = await sut.EnsureModelLoadedAsync("newmodel", CancellationToken.None);

            result.Should().BeTrue();
            _http.Verify(h => h.DownloadModelAsync(It.IsAny<string>(), "newmodel", It.IsAny<CancellationToken>()), Times.Once);
            _http.Verify(h => h.LoadModelAsync(It.IsAny<string>(), "newmodel", It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EnsureModelLoadedAsync_CachesLocally_AfterFirstSuccessfulLoad()
        {
            _http.Setup(h => h.GetServiceStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((ServiceStatus?)null);
            _http.Setup(h => h.DownloadModelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);
            _http.Setup(h => h.LoadModelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

            var sut = new SingleModelLifecycleManager(_logger.Object, _http.Object, _urls.Object);

            var first = await sut.EnsureModelLoadedAsync("modelA", CancellationToken.None);
            var second = await sut.EnsureModelLoadedAsync("modelA", CancellationToken.None);

            first.Should().BeTrue();
            second.Should().BeTrue();
            // Second call hits the volatile cache before the gate; no extra download/load
            _http.Verify(h => h.DownloadModelAsync(It.IsAny<string>(), "modelA", It.IsAny<CancellationToken>()), Times.Once);
            _http.Verify(h => h.LoadModelAsync(It.IsAny<string>(), "modelA", It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
