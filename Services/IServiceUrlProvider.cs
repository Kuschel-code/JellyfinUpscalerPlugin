using System;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Resolves the AI service base URL from plugin configuration with validation.
    /// Localhost and private IPs are intentionally allowed (the AI service runs
    /// on the same host or LAN); the strict SSRF blocklist used for webhooks does
    /// not apply here.
    /// </summary>
    public interface IServiceUrlProvider
    {
        string GetServiceUrl();
    }

    public class ConfigUrlProvider : IServiceUrlProvider
    {
        private const string Fallback = "http://localhost:5000";
        private readonly ILogger<ConfigUrlProvider> _logger;
        private readonly Func<string?> _readConfigUrl;

        /// <summary>
        /// Production constructor: reads from Plugin.Instance.Configuration.
        /// </summary>
        public ConfigUrlProvider(ILogger<ConfigUrlProvider> logger)
            : this(logger, () => Plugin.Instance?.Configuration?.AiServiceUrl) { }

        /// <summary>
        /// Test seam: pass an explicit URL source to validate without needing Plugin.Instance.
        /// </summary>
        public ConfigUrlProvider(ILogger<ConfigUrlProvider> logger, Func<string?> readConfigUrl)
        {
            _logger = logger;
            _readConfigUrl = readConfigUrl;
        }

        public string GetServiceUrl()
        {
            var url = _readConfigUrl() ?? Fallback;
            if (url.Contains('\n') || url.Contains('\r') || url.Contains('\t') ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                _logger.LogWarning("Invalid AiServiceUrl in config, falling back to default");
                return Fallback;
            }
            return url.TrimEnd('/');
        }
    }
}
