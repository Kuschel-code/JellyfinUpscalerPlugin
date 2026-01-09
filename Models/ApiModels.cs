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
    /// Video processing request model - v1.4.1 NEW
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
    /// Pre-processing request model - v1.4.1 NEW
    /// </summary>
    public class PreProcessRequest
    {
        public string InputPath { get; set; } = "";
        public string? Model { get; set; }
        public int? Scale { get; set; }
        public string? Quality { get; set; }
    }

    /// <summary>
    /// Pre-processing cache request model
    /// </summary>
    public class PreProcessingCacheRequest
    {
        public bool Enabled { get; set; }
        public int? SizeMB { get; set; }
        public bool? ProcessOnIdle { get; set; }
        public List<string>? Resolutions { get; set; }
    }
}
