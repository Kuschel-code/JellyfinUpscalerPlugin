using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Services
{
    public interface INativeLibraryLoader
    {
        void LoadPlatformSpecificLibraries();
        bool TryLoadLibrary(string libraryName, out IntPtr handle);
    }

    public class NativeLibraryLoader : INativeLibraryLoader
    {
        private readonly ILogger<NativeLibraryLoader> _logger;
        private readonly IPlatformDetectionService _platformService;
        private readonly string _pluginDirectory;

        public NativeLibraryLoader(
            ILogger<NativeLibraryLoader> logger,
            IPlatformDetectionService platformService)
        {
            _logger = logger;
            _platformService = platformService;
            _pluginDirectory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? string.Empty;
        }

        public void LoadPlatformSpecificLibraries()
        {
            var runtimesPath = Path.Combine(_pluginDirectory, "runtimes", _platformService.RuntimeIdentifier, "native");

            if (!Directory.Exists(runtimesPath))
            {
                _logger.LogWarning($"Native library directory not found: {runtimesPath}");
                return;
            }

            _logger.LogInformation($"Loading native libraries from: {runtimesPath}");

            var extension = _platformService.GetNativeLibraryExtension();
            var nativeFiles = Directory.GetFiles(runtimesPath, $"*{extension}");

            foreach (var nativeFile in nativeFiles)
            {
                try
                {
                    if (NativeLibrary.TryLoad(nativeFile, out var handle))
                    {
                        _logger.LogInformation($"Successfully loaded native library: {Path.GetFileName(nativeFile)}");
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to load native library: {Path.GetFileName(nativeFile)}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error loading native library: {Path.GetFileName(nativeFile)}");
                }
            }
        }

        public bool TryLoadLibrary(string libraryName, out IntPtr handle)
        {
            handle = IntPtr.Zero;

            var runtimesPath = Path.Combine(_pluginDirectory, "runtimes", _platformService.RuntimeIdentifier, "native");
            var extension = _platformService.GetNativeLibraryExtension();
            var libraryPath = Path.Combine(runtimesPath, $"{libraryName}{extension}");

            if (!File.Exists(libraryPath))
            {
                _logger.LogWarning($"Library not found: {libraryPath}");
                return false;
            }

            try
            {
                var success = NativeLibrary.TryLoad(libraryPath, out handle);
                if (success)
                {
                    _logger.LogInformation($"Successfully loaded library: {libraryName}");
                }
                else
                {
                    _logger.LogWarning($"Failed to load library: {libraryName}");
                }
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading library: {libraryName}");
                return false;
            }
        }
    }
}
