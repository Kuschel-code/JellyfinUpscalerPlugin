using System;
using System.Collections.Generic;

namespace JellyfinUpscalerPlugin.Models
{
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
    /// Cache result
    /// </summary>
    public class CacheResult
    {
        public bool Hit { get; set; }
        public string FilePath { get; set; } = "";
        public CacheEntry Entry { get; set; } = new();
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
    /// Cache stats for internal tracking
    /// </summary>
    public class CacheStats
    {
        public long TotalSize { get; set; }
        public long UsedSize { get; set; }
        public double HitRate { get; set; }
        public double MissRate { get; set; }
        public int FileCount { get; set; }
    }
}
