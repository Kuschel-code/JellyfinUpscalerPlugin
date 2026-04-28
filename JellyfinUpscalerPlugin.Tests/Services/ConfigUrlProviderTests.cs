using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    public class ConfigUrlProviderTests
    {
        private readonly Mock<ILogger<ConfigUrlProvider>> _logger = new();

        private ConfigUrlProvider WithConfig(string? url) =>
            new(_logger.Object, () => url);

        [Fact]
        public void GetServiceUrl_ReturnsFallback_WhenConfigUrlIsNull()
        {
            var provider = WithConfig(null);
            provider.GetServiceUrl().Should().Be("http://localhost:5000");
        }

        [Fact]
        public void GetServiceUrl_TrimsTrailingSlash()
        {
            var provider = WithConfig("http://upscaler.lan:5000/");
            provider.GetServiceUrl().Should().Be("http://upscaler.lan:5000");
        }

        [Fact]
        public void GetServiceUrl_RejectsInvalidScheme_AndReturnsFallback()
        {
            // file:// URI is parseable but not http/https
            var provider = WithConfig("file:///etc/passwd");
            provider.GetServiceUrl().Should().Be("http://localhost:5000");
        }
    }
}
