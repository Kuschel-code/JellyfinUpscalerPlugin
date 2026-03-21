using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace JellyfinUpscalerPlugin
{
    /// <summary>
    /// Plugin Configuration - v1.5.3.5 (Docker-based AI Service + Real-Time Upscaling)
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        // Basic Settings
        public bool EnablePlugin { get; set; } = true;
        public string Model { get; set; } = "realesrgan-x4";
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
        public int GpuDeviceIndex { get; set; } = 0;
        
        // Remote Transcoding Configuration (SSH/rffmpeg style)
        public bool EnableRemoteTranscoding { get; set; } = false;
        public string RemoteHost { get; set; } = "localhost";
        public int RemoteSshPort { get; set; } = 2222;
        public string RemoteUser { get; set; } = "root";
        public string RemoteSshKeyFile { get; set; } = ""; // Path to private key
        public string LocalMediaMountPoint { get; set; } = ""; // e.g., "C:\Media"
        public string RemoteMediaMountPoint { get; set; } = ""; // e.g., "/media"
        public string RemoteTranscodePath { get; set; } = "/transcode"; // Shared transcode dir on remote
        
        // Scheduled Task: Library Scan
        public int MinResolutionWidth { get; set; } = 1920;
        public int MinResolutionHeight { get; set; } = 1080;
        public int MaxItemsPerScan { get; set; } = 0; // 0 = unlimited

        // Real-Time Upscaling
        public bool EnableRealtimeUpscaling { get; set; } = true;
        public string RealtimeMode { get; set; } = "auto"; // "auto", "webgl", "server"
        public int RealtimeTargetFps { get; set; } = 24;
        public int RealtimeCaptureWidth { get; set; } = 480;

        // Output Settings
        public string OutputCodec { get; set; } = "libx264"; // "libx264", "libx265", "copy"

        // Version tracking
        public string PluginVersion { get; set; } = "1.5.3.5";

        public PluginConfiguration()
        {
        }
    }
}
