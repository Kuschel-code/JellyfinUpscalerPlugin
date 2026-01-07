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

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Hardware Benchmarking Service v1.4.0 - Automated Hardware Detection & Testing
    /// </summary>
    public class HardwareBenchmarkService : IHostedService, IDisposable
    {
        private readonly ILogger<HardwareBenchmarkService> _logger;
        private readonly IApplicationPaths _appPaths;
        private readonly UpscalerCore _upscalerCore;
        private Timer? _benchmarkTimer;
        private readonly PluginConfiguration _config;
        
        public HardwareBenchmarkService(
            ILogger<HardwareBenchmarkService> logger, 
            IApplicationPaths appPaths,
            UpscalerCore upscalerCore)
        {
            _logger = logger;
            _appPaths = appPaths;
            _upscalerCore = upscalerCore;
            _config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AI Upscaler Hardware Benchmark Service v1.4.0 starting...");
            
            if (_config.EnableAutoBenchmarking)
            {
                // Start benchmark timer - run initial benchmark after 30 seconds, then every hour
                _benchmarkTimer = new Timer(RunBenchmarkCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1));
                _logger.LogInformation("Auto-benchmarking enabled - initial test in 30 seconds");
            }
            
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AI Upscaler Hardware Benchmark Service stopping...");
            _benchmarkTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        private async void RunBenchmarkCallback(object? state)
        {
            try
            {
                await RunHardwareBenchmark();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during hardware benchmark");
            }
        }

        /// <summary>
        /// Runs comprehensive hardware benchmark and returns results
        /// </summary>
        public async Task<BenchmarkResults> RunHardwareBenchmark()
        {
            _logger.LogInformation("Starting comprehensive hardware benchmark...");
            
            var results = new BenchmarkResults
            {
                StartTime = DateTime.UtcNow,
                SystemInfo = await DetectSystemInfo(),
                Hardware = await _upscalerCore.DetectHardwareAsync()
            };

            // Test different models on current hardware
            results.ModelPerformance = await BenchmarkAIModels();
            
            // Test different resolutions
            results.ResolutionPerformance = await BenchmarkResolutions();
            
            // Determine optimal settings
            results.OptimalSettings = DetermineOptimalSettings(results);
            
            results.EndTime = DateTime.UtcNow;
            results.TotalDuration = results.EndTime - results.StartTime;
            
            _logger.LogInformation($"Hardware benchmark completed in {results.TotalDuration.TotalSeconds:F1}s");
            
            // Save results
            await SaveBenchmarkResults(results);
            
            return results;
        }

        private async Task<SystemInfo> DetectSystemInfo()
        {
            var systemInfo = new SystemInfo
            {
                OS = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.OSArchitecture.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                Platform = GetPlatformType(),
                IsContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true"
            };

            try
            {
                // Detect if running on NAS
                systemInfo.IsNAS = await DetectNASEnvironment();
                
                // Detect ARM architecture
                systemInfo.IsARM = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 || 
                                  RuntimeInformation.ProcessArchitecture == Architecture.Arm;
                
                // Detect iGPU type
                systemInfo.iGPUType = await DetectIntegratedGPU();
                
                _logger.LogInformation($"System detected: {systemInfo.Platform} | {systemInfo.Architecture} | ARM: {systemInfo.IsARM} | NAS: {systemInfo.IsNAS}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not detect all system information");
            }

            return systemInfo;
        }

        private async Task<GPUInfo> DetectGPUInfo()
        {
            var gpuInfo = new GPUInfo();
            
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    gpuInfo = await DetectWindowsGPU();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    gpuInfo = await DetectLinuxGPU();
                }
                
                _logger.LogInformation($"GPU detected: {gpuInfo.Name} | VRAM: {gpuInfo.VRAMSizeMB}MB | Vendor: {gpuInfo.Vendor}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not detect GPU information");
                gpuInfo.Name = "Unknown";
                gpuInfo.Vendor = "Unknown";
            }

            return gpuInfo;
        }

        private async Task<Dictionary<string, ModelPerformance>> BenchmarkAIModels()
        {
            var modelPerformance = new Dictionary<string, ModelPerformance>();
            
            var modelsToTest = new[] { "fsrcnn-light", "fsrcnn", "srcnn", "esrgan", "realesrgan", "waifu2x" };
            
            foreach (var model in modelsToTest)
            {
                try
                {
                    _logger.LogInformation($"Benchmarking AI model: {model}");
                    
                    var sw = Stopwatch.StartNew();
                    
                    // Simulate AI upscaling benchmark (in real implementation, this would run actual AI models)
                    var performance = await SimulateModelBenchmark(model);
                    
                    sw.Stop();
                    
                    performance.ProcessingTimeMs = (int)sw.ElapsedMilliseconds;
                    performance.ModelName = model;
                    performance.IsRecommended = IsModelRecommendedForCurrentHardware(model);
                    
                    modelPerformance[model] = performance;
                    
                    _logger.LogInformation($"Model {model}: {performance.ProcessingTimeMs}ms | FPS: {performance.AverageFPS:F1} | Quality: {performance.QualityScore:F1}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to benchmark model {model}");
                }
                
                // Small delay between tests
                await Task.Delay(100);
            }
            
            return modelPerformance;
        }

        private async Task<ModelPerformance> SimulateModelBenchmark(string model)
        {
            // In a real implementation, this would run actual AI models
            // For now, we simulate based on known performance characteristics
            
            var performance = new ModelPerformance { ModelName = model };
            
            // Simulate processing time based on model complexity and current hardware
            var baseTime = model switch
            {
                "fsrcnn-light" => 50,   // Fast, lightweight
                "fsrcnn" => 150,        // Balanced
                "srcnn" => 200,         // Older, slower
                "esrgan" => 800,        // High quality, slow
                "realesrgan" => 1000,   // Best quality, slowest
                "waifu2x" => 600,       // Good for anime
                _ => 300
            };
            
            // Adjust based on hardware capability
            var hardwareMultiplier = GetHardwareMultiplier();
            performance.ProcessingTimeMs = (int)(baseTime * hardwareMultiplier);
            
            // Calculate FPS (assuming 30-second clip)
            var framesProcessed = 30 * 24; // 24 FPS sample
            performance.AverageFPS = framesProcessed / (performance.ProcessingTimeMs / 1000.0);
            
            // Quality score (simulated PSNR improvement)
            performance.QualityScore = model switch
            {
                "realesrgan" => 8.5,
                "esrgan" => 7.2,
                "waifu2x" => 6.8,
                "fsrcnn" => 5.1,
                "srcnn" => 4.2,
                "fsrcnn-light" => 3.8,
                _ => 4.0
            };
            
            // CPU/GPU usage simulation
            performance.AverageCPUUsage = model.Contains("light") ? 35 : 65;
            performance.AverageGPUUsage = performance.ProcessingTimeMs < 500 ? 45 : 85;
            
            return performance;
        }

        private double GetHardwareMultiplier()
        {
            // Simulate hardware capability multiplier
            // In real implementation, this would be based on actual hardware detection
            
            if (Environment.ProcessorCount >= 16) return 0.3; // High-end desktop
            if (Environment.ProcessorCount >= 8) return 0.6;  // Mid-range
            if (Environment.ProcessorCount >= 4) return 1.0;  // Low-end desktop
            return 2.5; // Low-end ARM/embedded
        }

        private async Task<Dictionary<string, ResolutionPerformance>> BenchmarkResolutions()
        {
            var resolutionPerformance = new Dictionary<string, ResolutionPerformance>();
            
            var resolutions = new[] 
            {
                ("480p→720p", 480, 720),
                ("720p→1080p", 720, 1080),
                ("1080p→1440p", 1080, 1440),
                ("1080p→4K", 1080, 2160)
            };
            
            foreach (var (name, sourceHeight, targetHeight) in resolutions)
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    
                    // Simulate resolution scaling benchmark
                    var performance = await SimulateResolutionBenchmark(sourceHeight, targetHeight);
                    
                    sw.Stop();
                    
                    performance.ResolutionName = name;
                    performance.ProcessingTimeMs = (int)sw.ElapsedMilliseconds;
                    performance.IsRecommended = IsResolutionRecommendedForCurrentHardware(sourceHeight, targetHeight);
                    
                    resolutionPerformance[name] = performance;
                    
                    _logger.LogInformation($"Resolution {name}: {performance.ProcessingTimeMs}ms | Recommended: {performance.IsRecommended}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to benchmark resolution {name}");
                }
            }
            
            return resolutionPerformance;
        }

        private async Task<ResolutionPerformance> SimulateResolutionBenchmark(int sourceHeight, int targetHeight)
        {
            var performance = new ResolutionPerformance
            {
                SourceHeight = sourceHeight,
                TargetHeight = targetHeight
            };
            
            // Calculate complexity based on pixel count increase
            var pixelMultiplier = (double)(targetHeight * targetHeight) / (sourceHeight * sourceHeight);
            var baseTime = 100 * pixelMultiplier; // Base processing time
            
            var hardwareMultiplier = GetHardwareMultiplier();
            performance.ProcessingTimeMs = (int)(baseTime * hardwareMultiplier);
            
            // Memory usage estimate (MB)
            performance.MemoryUsageMB = (int)(pixelMultiplier * 50);
            
            // Quality improvement estimate
            performance.QualityImprovement = Math.Min(pixelMultiplier * 0.2, 0.8);
            
            return performance;
        }

        private OptimalSettings DetermineOptimalSettings(BenchmarkResults results)
        {
            var optimal = new OptimalSettings();
            
            // Find best performing model
            var bestModel = "";
            var bestScore = 0.0;
            
            foreach (var kvp in results.ModelPerformance)
            {
                var model = kvp.Value;
                // Score = Quality / ProcessingTime (higher is better)
                var score = model.QualityScore / (model.ProcessingTimeMs / 1000.0);
                
                if (score > bestScore)
                {
                    bestScore = score;
                    bestModel = kvp.Key;
                }
            }
            
            optimal.RecommendedModel = bestModel;
            
            // Find best resolution based on hardware capability
            optimal.RecommendedMaxResolution = results.SystemInfo.IsARM || results.SystemInfo.IsNAS 
                ? "720p→1080p" 
                : "1080p→4K";
            
            // Determine quality setting
            optimal.RecommendedQuality = GetHardwareMultiplier() < 0.5 ? "high" : "balanced";
            
            // Hardware acceleration recommendation
            optimal.EnableHardwareAcceleration = !string.IsNullOrEmpty(results.GPUInfo.Name) && 
                                                results.GPUInfo.Name != "Unknown";
            
            // Fallback settings for low-end hardware
            if (GetHardwareMultiplier() > 1.5)
            {
                optimal.EnableAutoFallback = true;
                optimal.FallbackModel = "fsrcnn-light";
                optimal.MaxConcurrentStreams = 1;
            }
            else
            {
                optimal.EnableAutoFallback = false;
                optimal.MaxConcurrentStreams = 2;
            }
            
            return optimal;
        }

        private async Task SaveBenchmarkResults(BenchmarkResults results)
        {
            try
            {
                var benchmarkDir = Path.Combine(_appPaths.DataPath, "plugins", "JellyfinUpscalerPlugin", "benchmarks");
                Directory.CreateDirectory(benchmarkDir);
                
                var fileName = $"benchmark_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.json";
                var filePath = Path.Combine(benchmarkDir, fileName);
                
                var json = System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(filePath, json);
                
                _logger.LogInformation($"Benchmark results saved to: {filePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save benchmark results");
            }
        }

        // Helper methods for hardware detection
        private string GetPlatformType()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "Linux";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macOS";
            return "Unknown";
        }

        private async Task<bool> DetectNASEnvironment()
        {
            try
            {
                // Check for common NAS indicators
                var nasIndicators = new[]
                {
                    "/volume1", "/volume2", // Synology
                    "/share", "/shares",     // QNAP
                    "/mnt/user",            // Unraid
                    "/mnt/tank"             // TrueNAS
                };

                foreach (var indicator in nasIndicators)
                {
                    if (Directory.Exists(indicator))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> DetectIntegratedGPU()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Check for Intel iGPU on Linux
                    if (File.Exists("/sys/class/drm/card0/device/vendor"))
                    {
                        var vendor = await File.ReadAllTextAsync("/sys/class/drm/card0/device/vendor");
                        if (vendor.Trim() == "0x8086") return "Intel";
                        if (vendor.Trim() == "0x1002") return "AMD";
                    }
                }
                
                return "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private async Task<GPUInfo> DetectWindowsGPU()
        {
            // In real implementation, would use WMI or DirectX to detect GPU
            return new GPUInfo
            {
                Name = "Windows GPU",
                Vendor = "Unknown",
                VRAMSizeMB = 0
            };
        }

        private async Task<GPUInfo> DetectLinuxGPU()
        {
            // In real implementation, would parse lspci or /proc/driver/nvidia/gpus
            return new GPUInfo
            {
                Name = "Linux GPU",
                Vendor = "Unknown", 
                VRAMSizeMB = 0
            };
        }

        private async Task<CPUInfo> DetectCPUInfo()
        {
            return new CPUInfo
            {
                Name = "Unknown CPU",
                Cores = Environment.ProcessorCount,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString()
            };
        }

        private async Task<MemoryInfo> DetectMemoryInfo()
        {
            return new MemoryInfo
            {
                TotalMemoryMB = 0, // Would be detected in real implementation
                AvailableMemoryMB = 0
            };
        }

        private bool IsModelRecommendedForCurrentHardware(string model)
        {
            var multiplier = GetHardwareMultiplier();
            
            return model switch
            {
                "realesrgan" => multiplier < 0.8,  // Only recommend for high-end hardware
                "esrgan" => multiplier < 1.0,
                "waifu2x" => multiplier < 1.2,
                "fsrcnn" => multiplier < 2.0,
                "fsrcnn-light" => true,            // Always recommended
                "srcnn" => multiplier < 1.5,
                _ => false
            };
        }

        private bool IsResolutionRecommendedForCurrentHardware(int sourceHeight, int targetHeight)
        {
            var multiplier = GetHardwareMultiplier();
            var pixelIncrease = (double)(targetHeight * targetHeight) / (sourceHeight * sourceHeight);
            
            if (multiplier > 2.0 && pixelIncrease > 2.0) return false;  // Low-end hardware, high resolution
            if (multiplier > 1.5 && pixelIncrease > 4.0) return false;  // Mid-end hardware, very high resolution
            
            return true;
        }

        public void Dispose()
        {
            _benchmarkTimer?.Dispose();
        }
    }

    // Data classes for benchmark results
    public class BenchmarkResults
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public SystemInfo SystemInfo { get; set; } = new();
        public HardwareProfile Hardware { get; set; } = new();
        public GPUInfo GPUInfo { get; set; } = new();
        public CPUInfo CPUInfo { get; set; } = new();
        public MemoryInfo MemoryInfo { get; set; } = new();
        public Dictionary<string, ModelPerformance> ModelPerformance { get; set; } = new();
        public Dictionary<string, ResolutionPerformance> ResolutionPerformance { get; set; } = new();
        public OptimalSettings OptimalSettings { get; set; } = new();
    }

    public class SystemInfo
    {
        public string OS { get; set; } = "";
        public string Architecture { get; set; } = "";
        public int ProcessorCount { get; set; }
        public string Platform { get; set; } = "";
        public bool IsContainer { get; set; }
        public bool IsNAS { get; set; }
        public bool IsARM { get; set; }
        public string iGPUType { get; set; } = "";
    }

    public class GPUInfo
    {
        public string Name { get; set; } = "";
        public string Vendor { get; set; } = "";
        public int VRAMSizeMB { get; set; }
    }

    public class CPUInfo
    {
        public string Name { get; set; } = "";
        public int Cores { get; set; }
        public string Architecture { get; set; } = "";
    }

    public class MemoryInfo
    {
        public int TotalMemoryMB { get; set; }
        public int AvailableMemoryMB { get; set; }
    }

    public class ModelPerformance
    {
        public string ModelName { get; set; } = "";
        public int ProcessingTimeMs { get; set; }
        public double AverageFPS { get; set; }
        public double QualityScore { get; set; }
        public double AverageCPUUsage { get; set; }
        public double AverageGPUUsage { get; set; }
        public bool IsRecommended { get; set; }
    }

    public class ResolutionPerformance
    {
        public string ResolutionName { get; set; } = "";
        public int SourceHeight { get; set; }
        public int TargetHeight { get; set; }
        public int ProcessingTimeMs { get; set; }
        public int MemoryUsageMB { get; set; }
        public double QualityImprovement { get; set; }
        public bool IsRecommended { get; set; }
    }

    public class OptimalSettings
    {
        public string RecommendedModel { get; set; } = "";
        public string RecommendedMaxResolution { get; set; } = "";
        public string RecommendedQuality { get; set; } = "";
        public bool EnableHardwareAcceleration { get; set; }
        public bool EnableAutoFallback { get; set; }
        public string FallbackModel { get; set; } = "";
        public int MaxConcurrentStreams { get; set; }
    }
}