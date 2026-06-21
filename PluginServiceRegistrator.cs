using System;
using Microsoft.Extensions.DependencyInjection;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Model.Tasks;
using JellyfinUpscalerPlugin.Services;
using JellyfinUpscalerPlugin.ScheduledTasks;

namespace JellyfinUpscalerPlugin
{
    /// <summary>
    /// Register plugin services
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            // Core Logic Services
            serviceCollection.AddSingleton<UpscalerCore>();
            serviceCollection.AddSingleton<VideoProcessor>();
            serviceCollection.AddSingleton<CacheManager>();
            // ModelManager removed in v1.6.1.17 - was registered but never consumed (dead code).
            // Live model catalog is fetched dynamically via HttpUpscalerService.GetModelsAsync().
            serviceCollection.AddSingleton<UpscalerProgressHub>();
            serviceCollection.AddSingleton<LibraryScanHelper>();

            // HTTP-based AI Service (Docker)
            serviceCollection.AddSingleton<HttpUpscalerService>();

            // Auth handler (injects X-Api-Token on every AI service call)
            serviceCollection.AddTransient<AiServiceAuthHandler>();

            // Named HttpClients for controller proxy calls (DNS refresh + connection pooling).
            // Extracted so a unit test can assert the named-client timeouts: a typo in a client name
            // silently falls back to the 100s default and would eat the v1.7.12 timeout fix.
            RegisterAiHttpClients(serviceCollection);

            // Background / Hosted Services
            serviceCollection.AddSingleton<HardwareBenchmarkService>();
            serviceCollection.AddHostedService<UpscalerService>();
            serviceCollection.AddHostedService(sp => sp.GetRequiredService<HardwareBenchmarkService>());

            // Processing Queue
            serviceCollection.AddSingleton<ProcessingQueue>();

            // Scheduled Tasks (visible in Dashboard → Scheduled Tasks)
            serviceCollection.AddSingleton<IScheduledTask, LibraryUpscaleScanTask>();
            serviceCollection.AddSingleton<IScheduledTask, ImageUpscaleScanTask>();

            // Video Filters (Camera-Style)
            serviceCollection.AddSingleton<VideoFilterService>();

            // v1.7.3.1 - test-seam adapters (extracted to make Jellyfin-API-dependent
            // logic mockable in unit tests).
            serviceCollection.AddSingleton<IUserManagerAdapter, UserManagerAdapter>();
            serviceCollection.AddSingleton<IUpscalerCore>(sp => sp.GetRequiredService<UpscalerCore>());
        }

        /// <summary>
        /// Registers the named HttpClients used by the controller proxy. Timeouts are tiered:
        /// 120s default, 300s benchmarks/multi-frame, and the download client at 570s.
        /// The 570s download client is deliberately *under* the 600s UI timeout
        /// (configurationpage.html face-restore + models/load): on an over-long download the PROXY
        /// fails first with a readable JSON body (which the UI error parser surfaces) instead of the
        /// browser firing a body-less XHR timeout. v1.7.12.
        /// </summary>
        internal static void RegisterAiHttpClients(IServiceCollection serviceCollection)
        {
            serviceCollection.AddHttpClient("AiUpscaler", c => c.Timeout = TimeSpan.FromSeconds(120))
                .AddHttpMessageHandler<AiServiceAuthHandler>();
            serviceCollection.AddHttpClient("AiUpscalerLongTimeout", c => c.Timeout = TimeSpan.FromSeconds(300))
                .AddHttpMessageHandler<AiServiceAuthHandler>();
            serviceCollection.AddHttpClient("UpscalerHDR", c => c.Timeout = TimeSpan.FromMinutes(5))
                .AddHttpMessageHandler<AiServiceAuthHandler>();
            // First-time model loads auto-download up to ~380MB (GFPGAN/CodeFormer/GPEN, general ONNX).
            // 570s < the 600s UI wait, so an over-long download surfaces a real proxy error, not a
            // body-less browser timeout (#72-class slow boxes/links).
            serviceCollection.AddHttpClient("AiUpscalerDownload", c => c.Timeout = TimeSpan.FromSeconds(570))
                .AddHttpMessageHandler<AiServiceAuthHandler>();
        }
    }
}
