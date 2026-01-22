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
            // Core Services
            serviceCollection.AddSingleton<UpscalerCore>();
            serviceCollection.AddSingleton<VideoProcessor>();
            serviceCollection.AddSingleton<CacheManager>();
            serviceCollection.AddSingleton<UpscalerProgressHub>();
            serviceCollection.AddSingleton<LibraryScanHelper>();
            serviceCollection.AddSingleton<ModelManager>();

            // Hosted Services (Background Tasks)
            serviceCollection.AddHostedService<UpscalerService>();
            serviceCollection.AddHostedService<HardwareBenchmarkService>();

            // Platform Detection
            serviceCollection.AddSingleton<IPlatformDetectionService, PlatformDetectionService>();
            serviceCollection.AddSingleton<INativeLibraryLoader, NativeLibraryLoader>();
            serviceCollection.AddSingleton<IFFmpegWrapperService, FFmpegWrapperService>();
        }
    }
}
