using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Caches health-check results for 30 seconds (thread-safe via lock).
    /// Delegates the raw HTTP check to <see cref="IUpscalerHttpClient"/>.
    /// </summary>
    public interface IServiceHealthMonitor
    {
        Task<bool> IsServiceAvailableAsync(CancellationToken ct);
        void InvalidateCache();
    }

    public class CachedHealthMonitor : IServiceHealthMonitor
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
        private readonly object _lock = new();
        private readonly ILogger<CachedHealthMonitor> _logger;
        private readonly IUpscalerHttpClient _http;
        private readonly IServiceUrlProvider _urls;
        private bool? _cached;
        private DateTime _expiry = DateTime.MinValue;

        public CachedHealthMonitor(ILogger<CachedHealthMonitor> logger,
                                   IUpscalerHttpClient http,
                                   IServiceUrlProvider urls)
        {
            _logger = logger;
            _http = http;
            _urls = urls;
        }

        public async Task<bool> IsServiceAvailableAsync(CancellationToken ct)
        {
            lock (_lock)
            {
                if (_cached.HasValue && DateTime.UtcNow < _expiry)
                {
                    return _cached.Value;
                }
            }

            var baseUrl = _urls.GetServiceUrl();
            var ok = await _http.CheckHealthAsync(baseUrl, ct);
            if (ok)
            {
                _logger.LogDebug("AI Service health check OK at {Url}", baseUrl);
            }

            lock (_lock)
            {
                _cached = ok;
                _expiry = DateTime.UtcNow + CacheDuration;
            }
            return ok;
        }

        public void InvalidateCache()
        {
            lock (_lock)
            {
                _cached = null;
                _expiry = DateTime.MinValue;
            }
            _logger.LogDebug("Health cache invalidated");
        }
    }
}
