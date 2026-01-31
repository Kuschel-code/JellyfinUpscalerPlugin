using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace JellyfinUpscalerPlugin
{
    /// <summary>
    /// Plugin Configuration - v1.4.9.5 (Docker-based AI Service)
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        // Basic Settings
        public bool EnablePlugin { get; set; } = true;
        public string Model { get; set; } = "realesrgan";
        public int ScaleFactor { get; set; } = 2;
        public string QualityLevel { get; set; } = "medium";
        public bool HardwareAcceleration { get; set; } = true;
        public bool PlayerButton { get; set; } = true;
        public bool Notifications { get; set; } = true;
        
        // Performance Settings
        public int MaxVRAMUsage { get; set; } = 2048;
        public int CpuThreads { get; set; } = 4;
        public int MaxConcurrentStreams { get; set; } = 1;
        
        // UI Settings
        public string ButtonPosition { get; set; } = "right";
        public bool AutoRetryButton { get; set; } = true;

        // Features
        public bool EnableComparisonView { get; set; } = true;
        public bool EnablePreProcessingCache { get; set; } = false;
        public bool EnablePerformanceMetrics { get; set; } = true;
        public bool EnableAutoBenchmarking { get; set; } = false;
        
        // Cache Settings
        public int MaxCacheAgeDays { get; set; } = 30;
        public int CacheSizeMB { get; set; } = 5120; // 5GB default
        
        // AI Service Configuration (Docker)
        public string AiServiceUrl { get; set; } = "http://localhost:5000";
        public string ModelDownloadUrl { get; set; } = "https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/models-v1.0";
        
        // Version tracking
        public string PluginVersion { get; set; } = "1.5.0.1";
        public DateTime LastConfigUpdate { get; set; } = DateTime.UtcNow;

        public PluginConfiguration()
        {
            PluginVersion = "1.5.0.1";
            LastConfigUpdate = DateTime.UtcNow;
        }
    }
}
