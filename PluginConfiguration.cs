using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace JellyfinUpscalerPlugin
{
    /// <summary>
    /// Plugin Configuration - v1.4.1 Stable
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        // Basic Settings
        public bool EnablePlugin { get; set; } = true;
        public string Model { get; set; } = "realesrgan";
        public int ScaleFactor { get; set; } = 2;
        public string QualityLevel { get; set; } = "balanced";
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
        public bool EnableAutoBenchmarking { get; set; } = true;
        
        // Cache Settings
        public int MaxCacheAgeDays { get; set; } = 30;
        public int CacheSizeMB { get; set; } = 5120; // 5GB default
        
        // Scheduled Task Settings - v1.4.1 NEW
        public bool EnableScheduledUpscaling { get; set; } = false;
        public int UpscaleResolutionThreshold { get; set; } = 1080;
        public int MaxItemsPerTask { get; set; } = 5;
        public string[] AutoUpscaleFolders { get; set; } = Array.Empty<string>();
        
        // Version tracking
        public string PluginVersion { get; set; } = "1.4.1";
        public DateTime LastConfigUpdate { get; set; } = DateTime.UtcNow;

        public PluginConfiguration()
        {
            PluginVersion = "1.4.1";
            LastConfigUpdate = DateTime.UtcNow;
        }
    }
}
