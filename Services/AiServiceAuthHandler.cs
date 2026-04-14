using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Injects the X-Api-Token header on every outbound call to the Docker AI service.
    /// Token is read per-request from PluginConfiguration so admins can rotate it without restart.
    /// </summary>
    public sealed class AiServiceAuthHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = Plugin.Instance?.Configuration?.AiServiceApiToken;
            if (!string.IsNullOrWhiteSpace(token) && !request.Headers.Contains("X-Api-Token"))
            {
                request.Headers.Add("X-Api-Token", token);
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}
