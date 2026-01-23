using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using MediaBrowser.Common.Configuration;
using JellyfinUpscalerPlugin.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Hardware Benchmarking Service v1.4.9.5 - Docker-based Hardware Detection
    /// </summary>
    public class HardwareBenchmarkService : IHostedService, IDisposable
    {
        private readonly ILogger<HardwareBenchmarkService> _logger;
        private readonly IApplicationPaths _appPaths;
        private readonly HttpUpscalerService _httpUpscaler;
        private Timer? _benchmarkTimer;
        private bool _disposed;
        
        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();
        
        public HardwareBenchmarkService(
            ILogger<HardwareBenchmarkService> logger, 
            IApplicationPaths appPaths,
            HttpUpscalerService httpUpscaler)
        {
            _logger = logger;
            _appPaths = appPaths;
            _httpUpscaler = httpUpscaler;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AI Upscaler Hardware Benchmark Service v1.4.9.5 (Docker) starting...");
            
            if (Config.EnableAutoBenchmarking)
            {
                // Start benchmark timer - run initial check after 30 seconds, then every hour
                _benchmarkTimer = new Timer(RunBenchmarkCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1));
                _logger.LogInformation("Auto-benchmarking enabled - initial check in 30 seconds");
            }
            
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AI Upscaler Hardware Benchmark Service stopping...");
            _benchmarkTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        private void RunBenchmarkCallback(object? state)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunHardwareBenchmark();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during hardware benchmark");
                }
            });
        }

        /// <summary>
        /// Runs hardware detection via Docker AI service
        /// </summary>
        public async Task<BenchmarkResults> RunHardwareBenchmark()
        {
            _logger.LogInformation("Starting hardware detection via Docker AI service...");
            
            var results = new BenchmarkResults
            {
                StartTime = DateTime.UtcNow,
                SystemInfo = DetectSystemInfo()
            };
            
            try
            {
                // Check if Docker AI service is available
                var isAvailable = await _httpUpscaler.IsServiceAvailableAsync();
                
                if (!isAvailable)
                {
                    _logger.LogWarning("Docker AI service not available at {Url}", Config.AiServiceUrl);
                    results.Hardware = new HardwareProfile
                    {
                        ServiceAvailable = false,
                        CpuCores = Environment.ProcessorCount,
                        DetectionTime = DateTime.UtcNow
                    };
                    results.EndTime = DateTime.UtcNow;
                    return results;
                }
                
                // Get status from Docker service
                var status = await _httpUpscaler.GetServiceStatusAsync();
                
                if (status != null)
                {
                    results.Hardware = new HardwareProfile
                    {
                        ServiceAvailable = true,
                        CpuCores = Environment.ProcessorCount,
                        AvailableProviders = new List<string>(status.AvailableProviders),
                        CudaAvailable = HasProvider(status.AvailableProviders, "CUDA", "Tensorrt"),
                        DirectMlAvailable = HasProvider(status.AvailableProviders, "DirectML"),
                        SupportsCUDA = HasProvider(status.AvailableProviders, "CUDA", "Tensorrt"),
                        SupportsDirectML = HasProvider(status.AvailableProviders, "DirectML"),
                        DetectionTime = DateTime.UtcNow,
                        RecommendedModel = status.CurrentModel ?? "realesrgan-x2",
                        RecommendedScale = 2,
                        MaxConcurrentStreams = status.MaxConcurrent
                    };
                    
                    results.GPUInfo = new GPUInfo
                    {
                        Vendor = results.Hardware.CudaAvailable ? "NVIDIA" : 
                                 results.Hardware.DirectMlAvailable ? "AMD/Intel" : "CPU",
                        Name = status.UsingGpu ? "GPU Enabled" : "CPU Mode"
                    };
                    
                    _logger.LogInformation("Hardware detection complete: GPU={Gpu}, Providers={Providers}",
                        status.UsingGpu, string.Join(", ", status.AvailableProviders));
                }
                else
                {
                    _logger.LogWarning("Could not get status from Docker AI service");
                    results.Hardware = CreateDefaultHardwareProfile();
                }
                
                // Calculate optimal settings
                results.OptimalSettings = CalculateOptimalSettings(results.Hardware);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hardware detection failed");
                results.Hardware = CreateDefaultHardwareProfile();
                results.OptimalSettings = CalculateOptimalSettings(results.Hardware);
            }
            
            results.EndTime = DateTime.UtcNow;
            results.TotalDuration = results.EndTime - results.StartTime;
            
            _logger.LogInformation("Hardware benchmark completed in {Duration}ms", results.TotalDuration.TotalMilliseconds);
            
            return results;
        }

        /// <summary>
        /// Quick check if Docker AI service is reachable
        /// </summary>
        public async Task<bool> IsServiceAvailableAsync()
        {
            return await _httpUpscaler.IsServiceAvailableAsync();
        }

        /// <summary>
        /// Get current service status
        /// </summary>
        public async Task<ServiceStatus?> GetServiceStatusAsync()
        {
            return await _httpUpscaler.GetServiceStatusAsync();
        }

        private bool HasProvider(string[] providers, params string[] names)
        {
            foreach (var provider in providers)
            {
                foreach (var name in names)
                {
                    if (provider.Contains(name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private SystemInfo DetectSystemInfo()
        {
            return new SystemInfo
            {
                OS = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                Platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" :
                          RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" :
                          RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "Unknown",
                IsContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true",
                IsARM = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ||
                       RuntimeInformation.ProcessArchitecture == Architecture.Arm
            };
        }

        private HardwareProfile CreateDefaultHardwareProfile()
        {
            return new HardwareProfile
            {
                ServiceAvailable = false,
                CpuCores = Environment.ProcessorCount,
                DetectionTime = DateTime.UtcNow,
                RecommendedModel = "fsrcnn-x2",
                RecommendedScale = 2,
                MaxConcurrentStreams = 1
            };
        }

        private OptimalSettings CalculateOptimalSettings(HardwareProfile hw)
        {
            var settings = new OptimalSettings
            {
                HardwareAcceleration = hw.CudaAvailable || hw.DirectMlAvailable,
                EnableAutoFallback = true
            };

            if (hw.CudaAvailable)
            {
                settings.RecommendedModel = "realesrgan-x2";
                settings.RecommendedQuality = "high";
                settings.MaxConcurrentStreams = Math.Min(4, hw.CpuCores / 2);
                settings.FallbackModel = "fsrcnn-x2";
            }
            else if (hw.DirectMlAvailable)
            {
                settings.RecommendedModel = "realesrgan-x2";
                settings.RecommendedQuality = "medium";
                settings.MaxConcurrentStreams = 2;
                settings.FallbackModel = "fsrcnn-x2";
            }
            else
            {
                settings.RecommendedModel = "fsrcnn-x2";
                settings.RecommendedQuality = "low";
                settings.MaxConcurrentStreams = 1;
                settings.FallbackModel = "fsrcnn-x2";
            }

            // Resolution recommendation based on hardware
            if (hw.CudaAvailable)
                settings.RecommendedMaxResolution = "4K (3840x2160)";
            else if (hw.DirectMlAvailable)
                settings.RecommendedMaxResolution = "1440p (2560x1440)";
            else
                settings.RecommendedMaxResolution = "1080p (1920x1080)";

            return settings;
        }

        /// <summary>
        /// Get hardware recommendations (used by UpscalerController)
        /// </summary>
        public async Task<object> GetRecommendationsAsync()
        {
            var benchmark = await RunHardwareBenchmark();
            return new
            {
                success = true,
                hardware = benchmark.Hardware,
                recommendations = benchmark.OptimalSettings,
                system = benchmark.SystemInfo
            };
        }

        /// <summary>
        /// Get fallback status (used by UpscalerController)
        /// </summary>
        public async Task<object> GetFallbackStatusAsync()
        {
            var isAvailable = await _httpUpscaler.IsServiceAvailableAsync();
            var status = await _httpUpscaler.GetServiceStatusAsync();
            
            return new
            {
                success = true,
                serviceAvailable = isAvailable,
                currentModel = status?.CurrentModel,
                usingGpu = status?.UsingGpu ?? false,
                fallbackEnabled = !isAvailable,
                fallbackReason = isAvailable ? null : "Docker AI service not reachable"
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _benchmarkTimer?.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}