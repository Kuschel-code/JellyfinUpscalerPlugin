using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace JellyfinUpscalerPlugin
{
    /// <summary>
    /// Plugin Configuration - v1.5.4.0 (Docker-based AI Service + Multi-Frame VSR)
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

        // Auto Model Selection
        public bool EnableAutoModelSelection { get; set; } = true; // true = pick best model per content, false = always use configured Model
        public string ModelFallbackChain { get; set; } = ""; // Comma-separated: "realesrgan-x4,span-x4,edsr-x4" — try next if current fails
        public string PreferredAnimeModel { get; set; } = ""; // Override for anime content (empty = auto)
        public string PreferredLiveActionModel { get; set; } = ""; // Override for live-action (empty = auto)

        // Output Settings
        public string OutputCodec { get; set; } = "libx264"; // "libx264", "libx265", "copy"
        public long MaxUpscaledFileSizeMB { get; set; } = 0; // 0 = unlimited. Skip videos that would exceed this size

        // Processing Queue
        public bool EnableProcessingQueue { get; set; } = true;
        public int MaxQueueSize { get; set; } = 100; // Max pending jobs
        public bool PauseQueueDuringPlayback { get; set; } = true; // Pause batch jobs when user is streaming
        public bool PersistQueueAcrossRestarts { get; set; } = false; // Save queue state to disk

        // Notifications & Webhooks
        public bool EnableProgressNotifications { get; set; } = true;
        public string WebhookUrl { get; set; } = ""; // URL to POST job completion/failure events
        public bool WebhookOnComplete { get; set; } = true;
        public bool WebhookOnFailure { get; set; } = true;

        // Model Management
        public bool EnableModelPreloading { get; set; } = false; // Preload preferred model on startup
        public int ModelDiskQuotaMB { get; set; } = 2048; // Max disk space for downloaded models (0 = unlimited)
        public bool EnableModelAutoCleanup { get; set; } = true; // Delete unused models after ModelCleanupDays
        public int ModelCleanupDays { get; set; } = 30; // Days before unused models are cleaned up

        // Health & Monitoring
        public bool EnableHealthMonitoring { get; set; } = true;
        public int HealthCheckIntervalSeconds { get; set; } = 60;
        public bool EnableGpuFallbackToCpu { get; set; } = true; // If GPU lockup detected, switch to CPU
        public int CircuitBreakerThreshold { get; set; } = 5; // Consecutive failures before circuit opens
        public int CircuitBreakerResetSeconds { get; set; } = 60; // Time before circuit half-opens

        // Scan Filtering
        public bool RestrictToUnwatchedContent { get; set; } = false; // Only upscale unwatched items
        public bool SkipUpscaledOnRescan { get; set; } = true; // Skip items that already have _upscaled version

        // Version tracking
        public string PluginVersion { get; set; } = "1.5.4.0";

        public PluginConfiguration()
        {
        }
    }
}
