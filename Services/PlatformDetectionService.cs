using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Services
{
    public enum PlatformType
    {
        Windows,
        Linux,
        MacOS,
        Unknown
    }

    public interface IPlatformDetectionService
    {
        PlatformType CurrentPlatform { get; }
        string RuntimeIdentifier { get; }
        bool IsWindows { get; }
        bool IsLinux { get; }
        bool IsMacOS { get; }
        string GetNativeLibraryExtension();
        string GetScriptExtension();
    }

    public class PlatformDetectionService : IPlatformDetectionService
    {
        private readonly ILogger<PlatformDetectionService> _logger;
        private readonly Lazy<PlatformType> _currentPlatform;
        private readonly Lazy<string> _runtimeIdentifier;

        public PlatformDetectionService(ILogger<PlatformDetectionService> logger)
        {
            _logger = logger;
            _currentPlatform = new Lazy<PlatformType>(DetectPlatform);
            _runtimeIdentifier = new Lazy<string>(DetectRuntimeIdentifier);
        }

        public PlatformType CurrentPlatform => _currentPlatform.Value;
        public string RuntimeIdentifier => _runtimeIdentifier.Value;
        public bool IsWindows => CurrentPlatform == PlatformType.Windows;
        public bool IsLinux => CurrentPlatform == PlatformType.Linux;
        public bool IsMacOS => CurrentPlatform == PlatformType.MacOS;

        private PlatformType DetectPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _logger.LogInformation("Platform detected: Windows");
                return PlatformType.Windows;
            }
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _logger.LogInformation("Platform detected: Linux");
                return PlatformType.Linux;
            }
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _logger.LogInformation("Platform detected: macOS");
                return PlatformType.MacOS;
            }

            _logger.LogWarning("Platform could not be detected, defaulting to Unknown");
            return PlatformType.Unknown;
        }

        private string DetectRuntimeIdentifier()
        {
            var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            
            var rid = CurrentPlatform switch
            {
                PlatformType.Windows => $"win-{architecture}",
                PlatformType.Linux => $"linux-{architecture}",
                PlatformType.MacOS => $"osx-{architecture}",
                _ => "unknown"
            };

            _logger.LogInformation($"Runtime Identifier: {rid}");
            return rid;
        }

        public string GetNativeLibraryExtension()
        {
            return CurrentPlatform switch
            {
                PlatformType.Windows => ".dll",
                PlatformType.Linux => ".so",
                PlatformType.MacOS => ".dylib",
                _ => ""
            };
        }

        public string GetScriptExtension()
        {
            return CurrentPlatform switch
            {
                PlatformType.Windows => ".bat",
                PlatformType.Linux => ".sh",
                PlatformType.MacOS => ".sh",
                _ => ""
            };
        }
    }
}
