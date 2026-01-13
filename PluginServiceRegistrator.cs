using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            // Build temporary provider for initialization
            var tempProvider = serviceCollection.BuildServiceProvider();
            
            // Native Dependency Isolation (v1.4.3)
            var dependencyLoader = new NativeDependencyLoader(
                tempProvider.GetRequiredService<ILogger<NativeDependencyLoader>>()
            );
            dependencyLoader.Initialize();
            serviceCollection.AddSingleton(dependencyLoader);
            
            // FFmpeg Wrapper Auto-Configuration (v1.4.4)
            var configHelper = new JellyfinConfigHelper(
                tempProvider.GetRequiredService<ILogger<JellyfinConfigHelper>>()
            );
            configHelper.SetupFFmpegWrapper();
            serviceCollection.AddSingleton(configHelper);
            
            // Core AI Services (Phase 1)
            serviceCollection.AddSingleton<UpscalerCore>();
            
            // Video Processing Services (Phase 2)
            serviceCollection.AddSingleton<VideoProcessor>();
            
            // Cache Management Services (Phase 3)
            serviceCollection.AddSingleton<CacheManager>();
            
            // Background Services
            serviceCollection.AddHostedService<UpscalerService>();
            
            // Hardware Benchmark Service (v1.4.1)
            serviceCollection.AddSingleton<HardwareBenchmarkService>();
            serviceCollection.AddHostedService<HardwareBenchmarkService>(provider => provider.GetRequiredService<HardwareBenchmarkService>());
            
            // Transcoding Profile Manager (v1.4.2)
            serviceCollection.AddSingleton<TranscodingProfileManager>();
            
            // Progress & Library Integration (v1.4.3)
            serviceCollection.AddSingleton<UpscalerProgressHub>();
            serviceCollection.AddSingleton<LibraryScanHelper>();
        }
    }
}