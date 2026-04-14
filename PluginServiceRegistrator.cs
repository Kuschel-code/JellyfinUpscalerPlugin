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
            serviceCollection.AddSingleton<ModelManager>();
            serviceCollection.AddSingleton<UpscalerProgressHub>();
            serviceCollection.AddSingleton<LibraryScanHelper>();

            // HTTP-based AI Service (Docker)
            serviceCollection.AddSingleton<HttpUpscalerService>();

            // Auth handler (injects X-Api-Token on every AI service call)
            serviceCollection.AddTransient<AiServiceAuthHandler>();

            // Named HttpClients for controller proxy calls (DNS refresh + connection pooling)
            serviceCollection.AddHttpClient("AiUpscaler", c => c.Timeout = TimeSpan.FromSeconds(120))
                .AddHttpMessageHandler<AiServiceAuthHandler>();
            serviceCollection.AddHttpClient("AiUpscalerLongTimeout", c => c.Timeout = TimeSpan.FromSeconds(300))
                .AddHttpMessageHandler<AiServiceAuthHandler>();
            serviceCollection.AddHttpClient("UpscalerHDR", c => c.Timeout = TimeSpan.FromMinutes(5))
                .AddHttpMessageHandler<AiServiceAuthHandler>();

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

            // Platform & Interop
            serviceCollection.AddSingleton<IPlatformDetectionService, PlatformDetectionService>();
            serviceCollection.AddSingleton<IFFmpegWrapperService, FFmpegWrapperService>();
        }
    }
}
