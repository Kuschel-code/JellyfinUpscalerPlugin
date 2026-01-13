using System;
using System.Collections.Generic;

namespace JellyfinUpscalerPlugin.Models
{
    /// <summary>
    /// Upscaler settings model
    /// </summary>
    public class UpscalerSettings
    {
        public string? Model { get; set; }
        public int? ScaleFactor { get; set; }
        public string? QualityLevel { get; set; }
        public bool? EnablePlugin { get; set; }
        public bool? HardwareAcceleration { get; set; }
        public bool? PlayerButton { get; set; }
        public int? MaxVRAMUsage { get; set; }
        public int? CpuThreads { get; set; }
        public bool? AutoRetryButton { get; set; }
        public string? ButtonPosition { get; set; }
    }

    /// <summary>
    /// Video processing request model
    /// </summary>
    public class VideoProcessRequest
    {
        public string InputPath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public string? Model { get; set; }
        public int? Scale { get; set; }
        public string? Quality { get; set; }
    }

    /// <summary>
    /// Video processing options
    /// </summary>
    public class VideoProcessingOptions
    {
        public string Model { get; set; } = "auto";
        public int ScaleFactor { get; set; } = 2;
        public string QualityLevel { get; set; } = "medium";
        
        public int Scale { get => ScaleFactor; set => ScaleFactor = value; }
        public string Quality { get => QualityLevel; set => QualityLevel = value; }
        
        public string HardwareAcceleration { get; set; } = "auto";
        public bool EnableRealTimeProcessing { get; set; } = false;
        public bool PreserveAudio { get; set; } = true;
        public bool PreserveSubtitles { get; set; } = true;
        public bool EnableAIUpscaling { get; set; } = true;
        
        public VideoProcessingOptions() { }
        
        public VideoProcessingOptions(VideoProcessingOptions other)
        {
            Model = other.Model;
            ScaleFactor = other.ScaleFactor;
            QualityLevel = other.QualityLevel;
            HardwareAcceleration = other.HardwareAcceleration;
            EnableRealTimeProcessing = other.EnableRealTimeProcessing;
            PreserveAudio = other.PreserveAudio;
            PreserveSubtitles = other.PreserveSubtitles;
            EnableAIUpscaling = other.EnableAIUpscaling;
        }
    }

    /// <summary>
    /// Video information
    /// </summary>
    public class VideoInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public double FrameRate { get; set; }
        public TimeSpan Duration { get; set; }
        public string Codec { get; set; } = "";
        public long BitRate { get; set; }
        public string PixelFormat { get; set; } = "";
        public string ColorSpace { get; set; } = "";
        public string ColorRange { get; set; } = "";
        public long FileSize { get; set; }
        public bool HasAudio { get; set; }
        public bool HasSubtitles { get; set; }
        public VideoQuality EstimatedQuality { get; set; }
        public bool IsHDR { get; set; }
        public double AspectRatio { get; set; }
    }

    /// <summary>
    /// Processing job information
    /// </summary>
    public class ProcessingJob
    {
        public string Id { get; set; } = "";
        public string InputPath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public VideoProcessingOptions Options { get; set; } = new();
        public VideoProcessingOptions OptimizedOptions { get; set; } = new();
        public VideoInfo InputInfo { get; set; } = new();
        public HardwareProfile HardwareProfile { get; set; } = new();
        public ProcessingMethod ProcessingMethod { get; set; }
        public ProcessingStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public VideoProcessingResult Result { get; set; } = new();
        public string Error { get; set; } = "";
        
        public TimeSpan ProcessingDuration => EndTime - StartTime;
    }

    /// <summary>
    /// Video processing result
    /// </summary>
    public class VideoProcessingResult
    {
        public bool Success { get; set; }
        public string OutputPath { get; set; } = "";
        public TimeSpan ProcessingTime { get; set; }
        public ProcessingMethod Method { get; set; }
        public string Error { get; set; } = "";
        public Dictionary<string, object> Metrics { get; set; } = new();
    }

    /// <summary>
    /// Video processing metrics
    /// </summary>
    public class VideoProcessingMetrics
    {
        public string JobId { get; set; } = "";
        public TimeSpan ProcessingTime { get; set; }
        public string InputResolution { get; set; } = "";
        public string OutputResolution { get; set; } = "";
        public string Model { get; set; } = "";
        public int Scale { get; set; }
        public ProcessingMethod Method { get; set; }
        public bool Success { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class PerformanceMetrics
    {
        public string ModelName { get; set; } = "";
        public long TotalFrames { get; set; }
        public TimeSpan TotalTime { get; set; }
        public double AverageFps { get; set; }
        public double PeakMemoryMB { get; set; }
    }

    /// <summary>
    /// Hardware profile information
    /// </summary>
    public class HardwareProfile
    {
        public string GpuVendor { get; set; } = "";
        public string GpuModel { get; set; } = "";
        public string GpuName { get; set; } = "";
        public string DriverVersion { get; set; } = "";
        public int VramMB { get; set; }
        public int CpuCores { get; set; }
        public int SystemRamMB { get; set; }
        public int TempDiskSpaceGB { get; set; }
        
        public bool SupportsCUDA { get; set; }
        public bool SupportsDirectML { get; set; }
        public bool OpenCVSupportsCUDA { get; set; }
        
        public List<string> AvailableProviders { get; set; } = new();
        public List<string> AvailableHwAccels { get; set; } = new();
        public string OpenCVInfo { get; set; } = "";
        
        public string RecommendedModel { get; set; } = "";
        public int RecommendedScale { get; set; } = 2;
        public int MaxConcurrentStreams { get; set; } = 1;
    }

    /// <summary>
    /// Cache entry information
    /// </summary>
    public class CacheEntry
    {
        public string Key { get; set; } = "";
        public string InputPath { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string Model { get; set; } = "";
        public int Scale { get; set; }
        public string Quality { get; set; } = "";
        public long FileSize { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessedAt { get; set; }
        public int AccessCount { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Cache lookup result
    /// </summary>
    public class CacheResult
    {
        public bool Hit { get; set; }
        public string FilePath { get; set; } = "";
        public CacheEntry? Entry { get; set; }
    }

    /// <summary>
    /// Cache statistics
    /// </summary>
    public class CacheStatistics
    {
        public int TotalEntries { get; set; }
        public long TotalSize { get; set; }
        public long MaxSize { get; set; }
        public double HitRate { get; set; }
        public int TotalHits { get; set; }
        public int TotalMisses { get; set; }
        public double UsagePercentage { get; set; }
    }

    /// <summary>
    /// Processing method enumeration
    /// </summary>
    public enum ProcessingMethod
    {
        RealTime,
        FrameByFrame,
        Batch
    }

    /// <summary>
    /// Processing status enumeration
    /// </summary>
    public enum ProcessingStatus
    {
        Starting,
        Analyzing,
        Processing,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Video quality enumeration
    /// </summary>
    public enum VideoQuality
    {
        VeryLow,
        Low,
        Medium,
        High,
        VeryHigh
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
        public bool HardwareAcceleration { get; set; }
        public bool EnableAutoFallback { get; set; }
        public string FallbackModel { get; set; } = "";
        public int MaxConcurrentStreams { get; set; }
    }

    public class PreProcessRequest
    {
        public string InputPath { get; set; } = "";
        public string? Model { get; set; }
        public int? Scale { get; set; }
        public string? Quality { get; set; }
    }

    public class PreProcessingCacheRequest
    {
        public bool Enabled { get; set; }
    }
}
