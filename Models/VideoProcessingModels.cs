using System;
using System.Collections.Generic;

namespace JellyfinUpscalerPlugin.Models
{
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

    /// <summary>
    /// Hardware profile information
    /// </summary>
    public class HardwareProfile
    {
        public string GpuVendor { get; set; } = "";
        public string GpuModel { get; set; } = "";
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
}
