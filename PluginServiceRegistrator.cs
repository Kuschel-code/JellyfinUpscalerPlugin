using Microsoft.Extensions.DependencyInjection;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using JellyfinUpscalerPlugin.Services;

namespace JellyfinUpscalerPlugin
{
    /// <summary>
    /// Plugin service registrator for dependency injection
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <summary>
        /// Register plugin services
        /// </summary>
        /// <param name="serviceCollection">Service collection</param>
        /// <param name="serverApplicationHost">Server application host</param>
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost serverApplicationHost)
        {
            // Core AI Services (Phase 1)
            serviceCollection.AddSingleton<UpscalerCore>();
            
            // Video Processing Services (Phase 2)
            serviceCollection.AddSingleton<VideoProcessor>();
            
            // Cache Management Services (Phase 3)
            serviceCollection.AddSingleton<CacheManager>();
            
            // Background Services
            serviceCollection.AddHostedService<UpscalerService>();
            
            // Hardware Benchmark Service (v1.4.0)
            serviceCollection.AddSingleton<HardwareBenchmarkService>();
            serviceCollection.AddHostedService<HardwareBenchmarkService>(provider => provider.GetService<HardwareBenchmarkService>());
        }
    }
}