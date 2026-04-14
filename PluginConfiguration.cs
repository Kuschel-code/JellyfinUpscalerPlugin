using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace JellyfinUpscalerPlugin
{
    /// <summary>
    /// Plugin configuration with validated defaults and documented properties.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        // ── Default Constants ────────────────────────────────────────────
        private const string DefaultModel = "realesrgan-x4";
        private const int DefaultScaleFactor = 2;
        private const string DefaultQualityLevel = "medium";
        private const int DefaultMaxVRAM = 2048;
        private const int DefaultCpuThreads = 4;
        private const int DefaultMaxConcurrentStreams = 1;
        private const int DefaultMaxCacheAgeDays = 30;
        private const int DefaultCacheSizeMB = 5120;
        private const int DefaultMaxQueueSize = 100;
        private const int DefaultCircuitBreakerThreshold = 5;
        private const int DefaultCircuitBreakerResetSeconds = 60;
        private const int DefaultHealthCheckIntervalSeconds = 60;
        private const int DefaultMinResolutionWidth = 1920;
        private const int DefaultMinResolutionHeight = 1080;
        private const int DefaultRemoteSshPort = 2222;
        private const int DefaultRealtimeTargetFps = 24;
        private const int DefaultRealtimeCaptureWidth = 480;
        private const int DefaultModelDiskQuotaMB = 2048;
        private const int DefaultModelCleanupDays = 30;

        // ── Backing Fields (validated properties) ────────────────────────
        private int _scaleFactor = DefaultScaleFactor;
        private int _maxVRAMUsage = DefaultMaxVRAM;
        private int _cpuThreads = DefaultCpuThreads;
        private int _maxConcurrentStreams = DefaultMaxConcurrentStreams;
        private int _maxCacheAgeDays = DefaultMaxCacheAgeDays;
        private int _cacheSizeMB = DefaultCacheSizeMB;
        private int _gpuDeviceIndex;
        private int _remoteSshPort = DefaultRemoteSshPort;
        private int _minResolutionWidth = DefaultMinResolutionWidth;
        private int _minResolutionHeight = DefaultMinResolutionHeight;
        private int _maxItemsPerScan;
        private int _realtimeTargetFps = DefaultRealtimeTargetFps;
        private int _realtimeCaptureWidth = DefaultRealtimeCaptureWidth;
        private int _maxQueueSize = DefaultMaxQueueSize;
        private int _healthCheckIntervalSeconds = DefaultHealthCheckIntervalSeconds;
        private int _circuitBreakerThreshold = DefaultCircuitBreakerThreshold;
        private int _circuitBreakerResetSeconds = DefaultCircuitBreakerResetSeconds;
        private int _modelDiskQuotaMB = DefaultModelDiskQuotaMB;
        private int _modelCleanupDays = DefaultModelCleanupDays;

        // ── Basic Settings ───────────────────────────────────────────────

        /// <summary>Master switch to enable/disable the upscaler plugin.</summary>
        public bool EnablePlugin { get; set; } = true;

        /// <summary>Default AI model name (e.g. "realesrgan-x4", "span-x4").</summary>
        public string Model { get; set; } = DefaultModel;

        /// <summary>Upscaling factor (1-8x). Clamped to valid range.</summary>
        public int ScaleFactor
        {
            get => _scaleFactor;
            set => _scaleFactor = Math.Clamp(value, 1, 8);
        }

        /// <summary>Output quality preset: "fast", "medium", or "high".</summary>
        public string QualityLevel { get; set; } = DefaultQualityLevel;

        /// <summary>Enable GPU hardware acceleration for inference.</summary>
        public bool HardwareAcceleration { get; set; } = true;

        /// <summary>Show upscale toggle button in the video player UI.</summary>
        public bool PlayerButton { get; set; } = true;

        /// <summary>Show browser notifications for upscaling events.</summary>
        public bool Notifications { get; set; } = true;

        // ── Performance Settings ─────────────────────────────────────────

        /// <summary>Maximum VRAM usage in MB (0 = unlimited).</summary>
        public int MaxVRAMUsage
        {
            get => _maxVRAMUsage;
            set => _maxVRAMUsage = Math.Max(value, 0);
        }

        /// <summary>Number of CPU threads for processing (minimum 1).</summary>
        public int CpuThreads
        {
            get => _cpuThreads;
            set => _cpuThreads = Math.Max(value, 1);
        }

        /// <summary>Maximum concurrent upscaling streams (minimum 1).</summary>
        public int MaxConcurrentStreams
        {
            get => _maxConcurrentStreams;
            set => _maxConcurrentStreams = Math.Max(value, 1);
        }

        // ── UI Settings ──────────────────────────────────────────────────

        /// <summary>Player button position: "left" or "right".</summary>
        public string ButtonPosition { get; set; } = "right";

        /// <summary>Automatically retry upscaling on transient failures.</summary>
        public bool AutoRetryButton { get; set; } = true;

        // ── Features ─────────────────────────────────────────────────────

        /// <summary>Enable side-by-side before/after comparison view.</summary>
        public bool EnableComparisonView { get; set; } = true;

        /// <summary>Pre-process and cache upscaled content for instant playback.</summary>
        public bool EnablePreProcessingCache { get; set; } = false;

        /// <summary>Collect and expose performance metrics (FPS, processing time).</summary>
        public bool EnablePerformanceMetrics { get; set; } = true;

        /// <summary>Automatically benchmark hardware on service startup.</summary>
        public bool EnableAutoBenchmarking { get; set; } = false;

        // ── Cache Settings ───────────────────────────────────────────────

        /// <summary>Maximum age of cached content in days before expiry (minimum 1).</summary>
        public int MaxCacheAgeDays
        {
            get => _maxCacheAgeDays;
            set => _maxCacheAgeDays = Math.Max(value, 1);
        }

        /// <summary>Maximum cache size in MB (0 = unlimited). Default 5120 (5 GB).</summary>
        public int CacheSizeMB
        {
            get => _cacheSizeMB;
            set => _cacheSizeMB = Math.Max(value, 0);
        }

        // ── AI Service Configuration (Docker) ────────────────────────────

        /// <summary>Base URL of the Docker AI upscaler service.</summary>
        public string AiServiceUrl { get; set; } = "http://localhost:5000";

        /// <summary>URL prefix for downloading AI model files.</summary>
        public string ModelDownloadUrl { get; set; } = "https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/models-v1.0";

        /// <summary>GPU device index for multi-GPU systems (0-based).</summary>
        public int GpuDeviceIndex
        {
            get => _gpuDeviceIndex;
            set => _gpuDeviceIndex = Math.Max(value, 0);
        }

        // ── Remote Transcoding Configuration (SSH/rffmpeg style) ─────────

        /// <summary>Enable SSH-based remote transcoding to the Docker container.</summary>
        public bool EnableRemoteTranscoding { get; set; } = false;

        /// <summary>Remote host address for SSH transcoding.</summary>
        public string RemoteHost { get; set; } = "localhost";

        /// <summary>SSH port for remote transcoding (1-65535).</summary>
        public int RemoteSshPort
        {
            get => _remoteSshPort;
            set => _remoteSshPort = Math.Clamp(value, 1, 65535);
        }

        /// <summary>SSH username for remote transcoding.</summary>
        public string RemoteUser { get; set; } = "root";

        /// <summary>Path to SSH private key file for remote authentication.</summary>
        public string RemoteSshKeyFile { get; set; } = "";

        /// <summary>Local media mount point for path mapping (e.g. "C:\Media").</summary>
        public string LocalMediaMountPoint { get; set; } = "";

        /// <summary>Remote media mount point for path mapping (e.g. "/media").</summary>
        public string RemoteMediaMountPoint { get; set; } = "";

        /// <summary>Shared transcode directory on the remote host.</summary>
        public string RemoteTranscodePath { get; set; } = "/transcode";

        // ── Scheduled Task: Library Scan ─────────────────────────────────

        /// <summary>Minimum video width to skip upscaling (videos wider are considered high-res).</summary>
        public int MinResolutionWidth
        {
            get => _minResolutionWidth;
            set => _minResolutionWidth = Math.Max(value, 0);
        }

        /// <summary>Minimum video height to skip upscaling (videos taller are considered high-res).</summary>
        public int MinResolutionHeight
        {
            get => _minResolutionHeight;
            set => _minResolutionHeight = Math.Max(value, 0);
        }

        /// <summary>Maximum items to process per scan run (0 = unlimited).</summary>
        public int MaxItemsPerScan
        {
            get => _maxItemsPerScan;
            set => _maxItemsPerScan = Math.Max(value, 0);
        }

        // ── Real-Time Upscaling ──────────────────────────────────────────

        /// <summary>Enable real-time upscaling during video playback.</summary>
        public bool EnableRealtimeUpscaling { get; set; } = true;

        /// <summary>Real-time mode: "auto", "webgl" (client-side), or "server" (AI service).</summary>
        public string RealtimeMode { get; set; } = "auto";

        /// <summary>Target frames per second for real-time upscaling.</summary>
        public int RealtimeTargetFps
        {
            get => _realtimeTargetFps;
            set => _realtimeTargetFps = Math.Max(value, 1);
        }

        /// <summary>Capture width for real-time server-side upscaling.</summary>
        public int RealtimeCaptureWidth
        {
            get => _realtimeCaptureWidth;
            set => _realtimeCaptureWidth = Math.Max(value, 64);
        }

        // ── Auto Model Selection ─────────────────────────────────────────

        /// <summary>Automatically pick the best model per content type (anime, live-action, resolution).</summary>
        public bool EnableAutoModelSelection { get; set; } = true;

        /// <summary>Comma-separated fallback models if primary fails (e.g. "realesrgan-x4,span-x4").</summary>
        public string ModelFallbackChain { get; set; } = "";

        /// <summary>Override model for anime content (empty = auto-select).</summary>
        public string PreferredAnimeModel { get; set; } = "";

        /// <summary>Override model for live-action content (empty = auto-select).</summary>
        public string PreferredLiveActionModel { get; set; } = "";

        // ── Output Settings ──────────────────────────────────────────────

        /// <summary>Video output codec: "libx264", "libx265", or "copy".</summary>
        public string OutputCodec { get; set; } = "libx264";

        /// <summary>Maximum upscaled file size in MB (0 = unlimited, skip larger videos).</summary>
        public long MaxUpscaledFileSizeMB
        {
            get => _maxUpscaledFileSizeMB;
            set => _maxUpscaledFileSizeMB = Math.Max(value, 0);
        }
        private long _maxUpscaledFileSizeMB;

        // ── Processing Queue ─────────────────────────────────────────────

        /// <summary>Enable background processing queue for batch upscaling.</summary>
        public bool EnableProcessingQueue { get; set; } = true;

        /// <summary>Maximum number of pending jobs in the queue (minimum 1).</summary>
        public int MaxQueueSize
        {
            get => _maxQueueSize;
            set => _maxQueueSize = Math.Max(value, 1);
        }

        /// <summary>Pause batch processing when a user is actively streaming.</summary>
        public bool PauseQueueDuringPlayback { get; set; } = true;

        /// <summary>Persist queue state to disk so jobs survive plugin restarts.</summary>
        public bool PersistQueueAcrossRestarts { get; set; } = false;

        // ── Notifications and Webhooks ───────────────────────────────────

        /// <summary>Send SignalR progress updates to connected clients.</summary>
        public bool EnableProgressNotifications { get; set; } = true;

        /// <summary>Webhook URL to POST job events (empty = disabled, must be http/https).</summary>
        public string WebhookUrl { get; set; } = "";

        /// <summary>Fire webhook on successful job completion.</summary>
        public bool WebhookOnComplete { get; set; } = true;

        /// <summary>Fire webhook on job failure.</summary>
        public bool WebhookOnFailure { get; set; } = true;

        // ── Model Management ─────────────────────────────────────────────

        /// <summary>Preload the preferred model into VRAM on service startup.</summary>
        public bool EnableModelPreloading { get; set; } = false;

        /// <summary>Maximum disk space for downloaded models in MB (0 = unlimited).</summary>
        public int ModelDiskQuotaMB
        {
            get => _modelDiskQuotaMB;
            set => _modelDiskQuotaMB = Math.Max(value, 0);
        }

        /// <summary>Automatically delete models not used within ModelCleanupDays.</summary>
        public bool EnableModelAutoCleanup { get; set; } = true;

        /// <summary>Days before unused models are cleaned up (minimum 1).</summary>
        public int ModelCleanupDays
        {
            get => _modelCleanupDays;
            set => _modelCleanupDays = Math.Max(value, 1);
        }

        // ── Health and Monitoring ────────────────────────────────────────

        /// <summary>Periodically check Docker AI service health.</summary>
        public bool EnableHealthMonitoring { get; set; } = true;

        /// <summary>Health check polling interval in seconds (minimum 5).</summary>
        public int HealthCheckIntervalSeconds
        {
            get => _healthCheckIntervalSeconds;
            set => _healthCheckIntervalSeconds = Math.Max(value, 5);
        }

        /// <summary>Fall back to CPU inference if GPU becomes unavailable.</summary>
        public bool EnableGpuFallbackToCpu { get; set; } = true;

        /// <summary>Number of consecutive failures before the circuit breaker opens (minimum 1).</summary>
        public int CircuitBreakerThreshold
        {
            get => _circuitBreakerThreshold;
            set => _circuitBreakerThreshold = Math.Max(value, 1);
        }

        /// <summary>Seconds before a tripped circuit breaker attempts recovery (minimum 1).</summary>
        public int CircuitBreakerResetSeconds
        {
            get => _circuitBreakerResetSeconds;
            set => _circuitBreakerResetSeconds = Math.Max(value, 1);
        }

        // ── Scan Filtering ───────────────────────────────────────────────

        /// <summary>Only upscale unwatched content during library scans.</summary>
        public bool RestrictToUnwatchedContent { get; set; } = false;

        /// <summary>Skip items that already have an _upscaled version on rescan.</summary>
        public bool SkipUpscaledOnRescan { get; set; } = true;

        // ── Quality Metrics ───────────────────────────────────────────────

        /// <summary>Enable PSNR/SSIM quality metrics computation after upscaling.</summary>
        public bool EnableQualityMetrics { get; set; } = true;

        // ── Face Enhancement ─────────────────────────────────────────────

        /// <summary>Enable AI face enhancement (GFPGAN/CodeFormer) post-processing.</summary>
        public bool EnableFaceEnhancement { get; set; } = true;

        /// <summary>Face enhancement blend strength (0.0 = off, 1.0 = full). Default 0.7.</summary>
        public double FaceEnhanceStrength
        {
            get => _faceEnhanceStrength;
            set => _faceEnhanceStrength = Math.Clamp(value, 0.0, 1.0);
        }
        private double _faceEnhanceStrength = 0.7;

        // ── Film Grain Management ────────────────────────────────────────

        /// <summary>Enable film grain removal before upscaling and optional re-addition after.</summary>
        public bool EnableGrainManagement { get; set; } = true;

        /// <summary>Denoise strength for grain removal (1-30). Higher = more smoothing.</summary>
        public int GrainDenoiseStrength
        {
            get => _grainDenoiseStrength;
            set => _grainDenoiseStrength = Math.Clamp(value, 1, 30);
        }
        private int _grainDenoiseStrength = 5;

        /// <summary>Grain re-addition intensity after upscaling (0 = off, 1-50 = noise σ).</summary>
        public double GrainReaddIntensity
        {
            get => _grainReaddIntensity;
            set => _grainReaddIntensity = Math.Clamp(value, 0.0, 50.0);
        }
        private double _grainReaddIntensity = 0.0;

        // ── Custom Model Upload ──────────────────────────────────────────

        /// <summary>Allow users to upload custom ONNX models to the AI service.</summary>
        public bool EnableCustomModelUpload { get; set; } = true;

        // ── API Documentation ────────────────────────────────────────────

        /// <summary>Expose OpenAPI/Swagger docs on the AI service (/docs, /redoc).</summary>
        public bool EnableApiDocs { get; set; } = true;

        // ── Video Filters (Camera-Style) ────────────────────────────────

        /// <summary>Master switch to enable camera-style video filters.</summary>
        public bool EnableVideoFilters { get; set; } = false;

        /// <summary>Active filter preset: "none","cinematic","vintage","vivid","noir","warm","cool","hdr-pop","sepia","pastel","cyberpunk","drama","soft-glow","sharp-hd","retrogame","teal-orange","custom".</summary>
        public string ActiveFilterPreset { get; set; } = "none";

        /// <summary>Brightness adjustment (-1.0 dark to 1.0 bright). Only used when preset is "custom".</summary>
        public double FilterBrightness
        {
            get => _filterBrightness;
            set => _filterBrightness = Math.Clamp(value, -1.0, 1.0);
        }
        private double _filterBrightness = 0.0;

        /// <summary>Contrast multiplier (0.0 flat to 3.0 extreme). Only used when preset is "custom".</summary>
        public double FilterContrast
        {
            get => _filterContrast;
            set => _filterContrast = Math.Clamp(value, 0.0, 3.0);
        }
        private double _filterContrast = 1.0;

        /// <summary>Saturation multiplier (0.0 grayscale to 3.0 vivid). Only used when preset is "custom".</summary>
        public double FilterSaturation
        {
            get => _filterSaturation;
            set => _filterSaturation = Math.Clamp(value, 0.0, 3.0);
        }
        private double _filterSaturation = 1.0;

        /// <summary>Gamma correction (0.1 dark to 10.0 bright). Only used when preset is "custom".</summary>
        public double FilterGamma
        {
            get => _filterGamma;
            set => _filterGamma = Math.Clamp(value, 0.1, 10.0);
        }
        private double _filterGamma = 1.0;

        /// <summary>Sharpness amount for unsharp mask (0.0 off to 5.0 extreme). Only used when preset is "custom".</summary>
        public double FilterSharpness
        {
            get => _filterSharpness;
            set => _filterSharpness = Math.Clamp(value, 0.0, 5.0);
        }
        private double _filterSharpness = 0.0;

        /// <summary>Color temperature in Kelvin (1000 warm to 15000 cool). Only used when preset is "custom".</summary>
        public int FilterColorTemperature
        {
            get => _filterColorTemperature;
            set => _filterColorTemperature = Math.Clamp(value, 1000, 15000);
        }
        private int _filterColorTemperature = 6500;

        /// <summary>Vignette strength (0.0 off to 5.0 heavy). Only used when preset is "custom".</summary>
        public double FilterVignette
        {
            get => _filterVignette;
            set => _filterVignette = Math.Clamp(value, 0.0, 5.0);
        }
        private double _filterVignette = 0.0;

        /// <summary>Film grain noise amount (0 off to 100 heavy). Only used when preset is "custom".</summary>
        public int FilterFilmGrain
        {
            get => _filterFilmGrain;
            set => _filterFilmGrain = Math.Clamp(value, 0, 100);
        }
        private int _filterFilmGrain = 0;

        /// <summary>Denoise strength via hqdn3d (0.0 off to 25.0 heavy). Only used when preset is "custom".</summary>
        public double FilterDenoise
        {
            get => _filterDenoise;
            set => _filterDenoise = Math.Clamp(value, 0.0, 25.0);
        }
        private double _filterDenoise = 0.0;

        /// <summary>Path to a .cube LUT file for color grading (empty = disabled). Only used when preset is "custom".</summary>
        public string FilterLutPath { get; set; } = "";

        // ── Face Restoration (v1.6.1.7 — GFPGAN / CodeFormer) ────────────

        /// <summary>Enable AI face restoration for low-quality / old content.</summary>
        public bool EnableFaceRestore { get; set; } = false;

        /// <summary>Face-restore model ID: "gfpgan-v1.4" or "codeformer".</summary>
        public string FaceRestoreModel { get; set; } = "gfpgan-v1.4";

        /// <summary>Maximum faces to restore per frame (1-20). Protects against runaway detection.</summary>
        public int FaceRestoreMaxPerFrame
        {
            get => _faceRestoreMaxPerFrame;
            set => _faceRestoreMaxPerFrame = Math.Clamp(value, 1, 20);
        }
        private int _faceRestoreMaxPerFrame = 6;

        /// <summary>Only run face restore on frames where the upscaled resolution is below this width (pixels). 0 = always. Keeps 4K content out.</summary>
        public int FaceRestoreMaxWidth
        {
            get => _faceRestoreMaxWidth;
            set => _faceRestoreMaxWidth = Math.Clamp(value, 0, 7680);
        }
        private int _faceRestoreMaxWidth = 1920;

        // ── Version Tracking ─────────────────────────────────────────────

        /// <summary>Current plugin version string for webhook payloads and diagnostics.</summary>
        public string PluginVersion { get; set; } = "1.6.1.7";
    }
}
