using JellyfinUpscalerPlugin.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace JellyfinUpscalerPlugin
{
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<IPlatformDetectionService, PlatformDetectionService>();
            serviceCollection.AddSingleton<INativeLibraryLoader, NativeLibraryLoader>();
            serviceCollection.AddSingleton<IFFmpegWrapperService, FFmpegWrapperService>();
            
            var serviceProvider = serviceCollection.BuildServiceProvider();
            var nativeLoader = serviceProvider.GetRequiredService<INativeLibraryLoader>();
            nativeLoader.LoadPlatformSpecificLibraries();
        }
    }
}
