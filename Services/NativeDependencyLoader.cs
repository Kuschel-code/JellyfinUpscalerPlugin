using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Native dependency loader for isolating AI/CUDA libraries
    /// Prevents DLL-Hell conflicts with Jellyfin's native dependencies
    /// </summary>
    public class NativeDependencyLoader
    {
        private readonly ILogger<NativeDependencyLoader> _logger;
        private static bool _initialized = false;

        public NativeDependencyLoader(ILogger<NativeDependencyLoader> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Initialize native library paths
        /// </summary>
        public void Initialize()
        {
            if (_initialized)
            {
                _logger.LogDebug("Native dependencies already initialized");
                return;
            }

            try
            {
                _logger.LogInformation("🔧 Initializing native dependency isolation...");

                var pluginDir = GetPluginDirectory();
                var nativeDir = Path.Combine(pluginDir, "native");

                if (!Directory.Exists(nativeDir))
                {
                    Directory.CreateDirectory(nativeDir);
                    _logger.LogInformation($"📁 Created native library directory: {nativeDir}");
                }

                // Platform-specific library path configuration
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    SetupWindowsLibraryPath(nativeDir);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    SetupLinuxLibraryPath(nativeDir);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    SetupMacOSLibraryPath(nativeDir);
                }

                _initialized = true;
                _logger.LogInformation("✅ Native dependency isolation initialized");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize native dependencies");
            }
        }

        /// <summary>
        /// Configure Windows DLL search path
        /// </summary>
        private void SetupWindowsLibraryPath(string nativeDir)
        {
            try
            {
                // Add plugin's native directory to DLL search path
                var cudaPath = Path.Combine(nativeDir, "cuda");
                var onnxPath = Path.Combine(nativeDir, "onnxruntime");
                var opencvPath = Path.Combine(nativeDir, "opencv");

                if (Directory.Exists(cudaPath))
                {
                    SetDllDirectory(cudaPath);
                    _logger.LogInformation($"✅ CUDA library path configured: {cudaPath}");
                }

                if (Directory.Exists(onnxPath))
                {
                    SetDllDirectory(onnxPath);
                    _logger.LogInformation($"✅ ONNX Runtime library path configured: {onnxPath}");
                }

                if (Directory.Exists(opencvPath))
                {
                    SetDllDirectory(opencvPath);
                    _logger.LogInformation($"✅ OpenCV library path configured: {opencvPath}");
                }

                // Add to PATH environment variable for current process
                var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                var newPath = $"{nativeDir};{cudaPath};{onnxPath};{opencvPath};{currentPath}";
                Environment.SetEnvironmentVariable("PATH", newPath);

                _logger.LogInformation("✅ Windows library paths configured");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to configure Windows library paths");
            }
        }

        /// <summary>
        /// Configure Linux library search path
        /// </summary>
        private void SetupLinuxLibraryPath(string nativeDir)
        {
            try
            {
                var cudaPath = Path.Combine(nativeDir, "cuda");
                var onnxPath = Path.Combine(nativeDir, "onnxruntime");
                var opencvPath = Path.Combine(nativeDir, "opencv");

                // Set LD_LIBRARY_PATH for current process
                var currentLdPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "";
                var newLdPath = $"{nativeDir}:{cudaPath}:{onnxPath}:{opencvPath}:{currentLdPath}";
                Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", newLdPath);

                _logger.LogInformation($"✅ Linux library paths configured: LD_LIBRARY_PATH={newLdPath}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to configure Linux library paths");
            }
        }

        /// <summary>
        /// Configure macOS library search path
        /// </summary>
        private void SetupMacOSLibraryPath(string nativeDir)
        {
            try
            {
                var cudaPath = Path.Combine(nativeDir, "cuda");
                var onnxPath = Path.Combine(nativeDir, "onnxruntime");
                var opencvPath = Path.Combine(nativeDir, "opencv");

                // Set DYLD_LIBRARY_PATH for current process
                var currentDyldPath = Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH") ?? "";
                var newDyldPath = $"{nativeDir}:{cudaPath}:{onnxPath}:{opencvPath}:{currentDyldPath}";
                Environment.SetEnvironmentVariable("DYLD_LIBRARY_PATH", newDyldPath);

                _logger.LogInformation($"✅ macOS library paths configured: DYLD_LIBRARY_PATH={newDyldPath}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to configure macOS library paths");
            }
        }

        /// <summary>
        /// Get plugin installation directory
        /// </summary>
        private string GetPluginDirectory()
        {
            var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            return Path.GetDirectoryName(assemblyLocation) ?? Environment.CurrentDirectory;
        }

        /// <summary>
        /// Windows-specific: Set DLL directory for LoadLibrary
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);
    }
}
