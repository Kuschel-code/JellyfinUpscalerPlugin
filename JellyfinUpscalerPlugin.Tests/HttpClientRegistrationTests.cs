using System;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests
{
    /// <summary>
    /// v1.7.12 guard for the named-HttpClient wiring. The fix routes face-restore/models downloads
    /// to "AiUpscalerDownload" (570s) and benchmarks to "AiUpscalerLongTimeout" (300s). A typo
    /// between AddHttpClient("X") and CreateClient("X") fails SILENTLY onto the default 100s client
    /// (no exception), which would quietly re-introduce the very timeout wall v1.7.12 removes.
    /// These tests assert the registered timeouts so that regression can't ship unnoticed.
    /// </summary>
    public class HttpClientRegistrationTests
    {
        /// <summary>
        /// Resolve the configure-actions registered for a named client and apply them to a probe
        /// HttpClient. An unknown/typo'd name yields options with no actions -> the probe keeps the
        /// 100s default, which is exactly the silent-fallback bug we are guarding against.
        /// </summary>
        private static TimeSpan TimeoutOf(string name)
        {
            var services = new ServiceCollection();
            PluginServiceRegistrator.RegisterAiHttpClients(services);
            using var provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(name);
            using var probe = new HttpClient();
            foreach (var configure in options.HttpClientActions)
            {
                configure(probe);
            }
            return probe.Timeout;
        }

        [Theory]
        [InlineData("AiUpscaler", 120)]
        [InlineData("AiUpscalerLongTimeout", 300)]
        [InlineData("AiUpscalerDownload", 570)]
        public void NamedClient_HasExpectedTimeout(string name, int expectedSeconds)
        {
            TimeoutOf(name).Should().Be(TimeSpan.FromSeconds(expectedSeconds),
                $"the '{name}' proxy client must carry its intended timeout (a typo silently falls back to the 100s default)");
        }

        [Fact]
        public void DownloadClient_StaysUnderUiTimeout()
        {
            // The UI (configurationpage.html, face-restore + models/load) waits 600s. The download
            // PROXY must be strictly under that, so an over-long download fails on the proxy with a
            // readable JSON body (surfaced by the UI error parser) rather than a body-less XHR timeout.
            TimeoutOf("AiUpscalerDownload").Should().BeLessThan(TimeSpan.FromSeconds(600));
        }
    }
}
