using System;
using Microsoft.Extensions.DependencyInjection;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller;
using JellyfinUpscalerPlugin.Services;

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

            // Background / Hosted Services
            // Register HardwareBenchmarkService as Singleton FIRST so it can be injected into controllers
            serviceCollection.AddSingleton<HardwareBenchmarkService>();
            serviceCollection.AddHostedService<UpscalerService>();
            serviceCollection.AddHostedService(sp => sp.GetRequiredService<HardwareBenchmarkService>());

            // Platform & Interop
            serviceCollection.AddSingleton<IPlatformDetectionService, PlatformDetectionService>();
            serviceCollection.AddSingleton<INativeLibraryLoader, NativeLibraryLoader>();
            serviceCollection.AddSingleton<IFFmpegWrapperService, FFmpegWrapperService>();
        }
    }
}
