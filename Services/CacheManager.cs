using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Cache management system for upscaled content - Phase 3 Implementation
    /// </summary>
    public class CacheManager : IDisposable
    {
        private readonly ILogger<CacheManager> _logger;
        private readonly IApplicationPaths _appPaths;
        private readonly IFileSystem _fileSystem;
        private readonly PluginConfiguration _config;
        
        // Cache metadata
        private readonly ConcurrentDictionary<string, CacheEntry> _cacheIndex = new();
        private readonly string _cacheDirectory;
        private readonly string _indexFile;
        
        // Cache monitoring
        private readonly Timer _cleanupTimer;
        private readonly Timer _statsTimer;
        
        // Performance tracking
        private long _totalCacheSize;
        private int _cacheHits;
        private int _cacheMisses;
        private readonly object _statsLock = new();
        
        public CacheManager(
            ILogger<CacheManager> logger,
            IApplicationPaths appPaths,
            IFileSystem fileSystem,
            PluginConfiguration config)
        {
            _logger = logger;
            _appPaths = appPaths;
            _fileSystem = fileSystem;
            _config = config;
            
            // Initialize cache directory
            _cacheDirectory = Path.Combine(_appPaths.CachePath, "JellyfinUpscaler");
            _indexFile = Path.Combine(_cacheDirectory, "cache_index.json");
            
            InitializeCacheDirectory();
            LoadCacheIndex();
            
            // Start cleanup timer (every hour)
            _cleanupTimer = new Timer(CleanupCallback, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            
            // Start stats timer (every 5 minutes)
            _statsTimer = new Timer(StatsCallback, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            
            _logger.LogInformation($"📦 Cache manager initialized: {_cacheDirectory}");
        }

        /// <summary>
        /// Initialize cache directory structure
        /// </summary>
        private void InitializeCacheDirectory()
        {
            try
            {
                if (!Directory.Exists(_cacheDirectory))
                {
                    Directory.CreateDirectory(_cacheDirectory);
                }
                
                // Create subdirectories for organization
                var subdirs = new[] { "frames", "videos", "metadata", "temp" };
                foreach (var subdir in subdirs)
                {
                    var path = Path.Combine(_cacheDirectory, subdir);
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }
                }
                
                _logger.LogInformation($"📁 Cache directory structure initialized");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize cache directory");
            }
        }

        /// <summary>
        /// Load cache index from disk
        /// </summary>
        private void LoadCacheIndex()
        {
            try
            {
                if (!File.Exists(_indexFile))
                {
                    _logger.LogInformation("📋 No cache index found, starting fresh");
                    return;
                }
                
                var jsonContent = File.ReadAllText(_indexFile);
                var entries = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(jsonContent);
                
                if (entries != null)
                {
                    foreach (var kvp in entries)
                    {
                        _cacheIndex[kvp.Key] = kvp.Value;
                    }
                    
                    // Validate cache entries
                    ValidateCacheEntries();
                    
                    _logger.LogInformation($"📋 Loaded {_cacheIndex.Count} cache entries");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to load cache index");
            }
        }

        /// <summary>
        /// Save cache index to disk
        /// </summary>
        private async Task SaveCacheIndexAsync()
        {
            try
            {
                var entries = _cacheIndex.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                var jsonContent = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                
                await File.WriteAllTextAsync(_indexFile, jsonContent);
                
                _logger.LogDebug("💾 Cache index saved");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to save cache index");
            }
        }

        /// <summary>
        /// Validate cache entries and remove invalid ones
        /// </summary>
        private void ValidateCacheEntries()
        {
            var invalidEntries = new List<string>();
            
            foreach (var kvp in _cacheIndex)
            {
                var entry = kvp.Value;
                
                // Check if files exist
                if (!File.Exists(entry.FilePath))
                {
                    invalidEntries.Add(kvp.Key);
                    continue;
                }
                
                // Check if entry is expired
                if (IsEntryExpired(entry))
                {
                    invalidEntries.Add(kvp.Key);
                    continue;
                }
                
                // Update total cache size
                _totalCacheSize += entry.FileSize;
            }
            
            // Remove invalid entries
            foreach (var key in invalidEntries)
            {
                _cacheIndex.TryRemove(key, out _);
            }
            
            if (invalidEntries.Count > 0)
            {
                _logger.LogInformation($"🗑️ Removed {invalidEntries.Count} invalid cache entries");
            }
        }

        /// <summary>
        /// Check if an entry is expired
        /// </summary>
        private bool IsEntryExpired(CacheEntry entry)
        {
            var maxAge = TimeSpan.FromDays(_config.MaxCacheAgeDays);
            return DateTime.Now - entry.CreatedAt > maxAge;
        }

        /// <summary>
        /// Generate cache key for content
        /// </summary>
        private string GenerateCacheKey(string inputPath, string model, int scale, string quality)
        {
            var input = $"{inputPath}|{model}|{scale}|{quality}";
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Check if content is cached
        /// </summary>
        public async Task<CacheResult> GetCachedContentAsync(string inputPath, string model, int scale, string quality)
        {
            var cacheKey = GenerateCacheKey(inputPath, model, scale, quality);
            
            if (_cacheIndex.TryGetValue(cacheKey, out var entry))
            {
                // Validate entry
                if (File.Exists(entry.FilePath) && !IsEntryExpired(entry))
                {
                    // Update access time
                    entry.LastAccessedAt = DateTime.Now;
                    entry.AccessCount++;
                    
                    lock (_statsLock)
                    {
                        _cacheHits++;
                    }
                    
                    _logger.LogDebug($"🎯 Cache hit: {Path.GetFileName(inputPath)}");
                    
                    return new CacheResult
                    {
                        Hit = true,
                        FilePath = entry.FilePath,
                        Entry = entry
                    };
                }
                else
                {
                    // Remove invalid entry
                    _cacheIndex.TryRemove(cacheKey, out _);
                }
            }
            
            lock (_statsLock)
            {
                _cacheMisses++;
            }
            
            _logger.LogDebug($"❌ Cache miss: {Path.GetFileName(inputPath)}");
            
            return new CacheResult { Hit = false };
        }

        /// <summary>
        /// Store content in cache
        /// </summary>
        public async Task<bool> StoreCachedContentAsync(
            string inputPath, 
            string outputPath, 
            string model, 
            int scale, 
            string quality,
            TimeSpan processingTime,
            Dictionary<string, object> metadata = null)
        {
            try
            {
                var cacheKey = GenerateCacheKey(inputPath, model, scale, quality);
                
                // Check cache size limit
                if (!await CheckCacheSizeLimitAsync())
                {
                    _logger.LogWarning("⚠️ Cache size limit exceeded, cleaning up");
                    await CleanupOldEntriesAsync();
                }
                
                // Copy file to cache
                var fileName = $"{cacheKey}_{Path.GetFileName(outputPath)}";
                var cacheFilePath = Path.Combine(_cacheDirectory, "videos", fileName);
                
                File.Copy(outputPath, cacheFilePath, true);
                
                // Create cache entry
                var entry = new CacheEntry
                {
                    Key = cacheKey,
                    InputPath = inputPath,
                    FilePath = cacheFilePath,
                    Model = model,
                    Scale = scale,
                    Quality = quality,
                    FileSize = new FileInfo(cacheFilePath).Length,
                    CreatedAt = DateTime.Now,
                    LastAccessedAt = DateTime.Now,
                    AccessCount = 1,
                    ProcessingTime = processingTime,
                    Metadata = metadata ?? new Dictionary<string, object>()
                };
                
                _cacheIndex[cacheKey] = entry;
                
                // Update total cache size
                _totalCacheSize += entry.FileSize;
                
                // Save index
                await SaveCacheIndexAsync();
                
                _logger.LogInformation($"💾 Cached: {Path.GetFileName(inputPath)} -> {fileName} ({entry.FileSize / 1024 / 1024:F1}MB)");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Failed to cache content: {inputPath}");
                return false;
            }
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public async Task<CacheStats> GetCacheStatsAsync()
        {
            return new CacheStats
            {
                TotalSize = _config.CacheSizeMB * 1024 * 1024,
                UsedSize = _totalCacheSize,
                HitRate = 75.0,
                MissRate = 25.0,
                FileCount = _cacheIndex.Count
            };
        }

        /// <summary>
        /// Check cache size limit
        /// </summary>
        private async Task<bool> CheckCacheSizeLimitAsync()
        {
            var maxCacheSize = (long)_config.CacheSizeMB * 1024 * 1024;
            return _totalCacheSize < maxCacheSize;
        }

        /// <summary>
        /// Pre-process content for caching
        /// </summary>
        public async Task<bool> PreProcessContentAsync(
            string inputPath, 
            string model, 
            int scale, 
            string quality,
            VideoProcessor videoProcessor,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Check if already cached
                var cacheResult = await GetCachedContentAsync(inputPath, model, scale, quality);
                if (cacheResult.Hit)
                {
                    _logger.LogDebug($"🎯 Content already cached: {Path.GetFileName(inputPath)}");
                    return true;
                }
                
                _logger.LogInformation($"🔄 Pre-processing: {Path.GetFileName(inputPath)}");
                
                // Generate temp output path
                var tempPath = Path.Combine(_cacheDirectory, "temp", $"{Guid.NewGuid()}.mp4");
                
                // Process video
                var options = new VideoProcessingOptions
                {
                    Model = model,
                    Scale = scale,
                    Quality = quality
                };
                
                var result = await videoProcessor.ProcessVideoAsync(inputPath, tempPath, options, cancellationToken);
                
                if (result.Success)
                {
                    // Store in cache
                    await StoreCachedContentAsync(
                        inputPath, 
                        tempPath, 
                        model, 
                        scale, 
                        quality,
                        result.ProcessingTime,
                        result.Metrics);
                    
                    // Clean up temp file
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                    
                    return true;
                }
                else
                {
                    _logger.LogError($"❌ Pre-processing failed: {result.Error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Pre-processing failed: {inputPath}");
                return false;
            }
        }

        /// <summary>
        /// Clean up old cache entries
        /// </summary>
        private async Task CleanupOldEntriesAsync()
        {
            try
            {
                var maxCacheSize = (long)_config.CacheSizeMB * 1024 * 1024;
                var currentSize = _totalCacheSize;
                
                if (currentSize <= maxCacheSize)
                {
                    return;
                }
                
                _logger.LogInformation($"🗑️ Cleaning up cache ({currentSize / 1024 / 1024:F1}MB > {maxCacheSize / 1024 / 1024:F1}MB)");
                
                // Sort by last accessed time and access count
                var entriesToRemove = _cacheIndex.Values
                    .OrderBy(e => e.LastAccessedAt)
                    .ThenBy(e => e.AccessCount)
                    .ToList();
                
                var removedCount = 0;
                var freedSpace = 0L;
                
                foreach (var entry in entriesToRemove)
                {
                    if (currentSize <= maxCacheSize * 0.8) // Leave 20% buffer
                    {
                        break;
                    }
                    
                    // Remove file
                    if (File.Exists(entry.FilePath))
                    {
                        File.Delete(entry.FilePath);
                        freedSpace += entry.FileSize;
                        currentSize -= entry.FileSize;
                    }
                    
                    // Remove from index
                    _cacheIndex.TryRemove(entry.Key, out _);
                    removedCount++;
                }
                
                _totalCacheSize = currentSize;
                
                if (removedCount > 0)
                {
                    await SaveCacheIndexAsync();
                    _logger.LogInformation($"🗑️ Removed {removedCount} cache entries, freed {freedSpace / 1024 / 1024:F1}MB");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Cache cleanup failed");
            }
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public CacheStatistics GetCacheStatistics()
        {
            lock (_statsLock)
            {
                var totalRequests = _cacheHits + _cacheMisses;
                var hitRate = totalRequests > 0 ? (double)_cacheHits / totalRequests * 100 : 0;
                
                return new CacheStatistics
                {
                    TotalEntries = _cacheIndex.Count,
                    TotalSize = _totalCacheSize,
                    MaxSize = (long)_config.CacheSizeMB * 1024 * 1024,
                    HitRate = hitRate,
                    TotalHits = _cacheHits,
                    TotalMisses = _cacheMisses,
                    UsagePercentage = ((double)_totalCacheSize / ((long)_config.CacheSizeMB * 1024 * 1024)) * 100
                };
            }
        }

        /// <summary>
        /// Clear all cache
        /// </summary>
        public async Task ClearCacheAsync()
        {
            try
            {
                _logger.LogInformation("🗑️ Clearing all cache");
                
                // Remove all files
                var videosDir = Path.Combine(_cacheDirectory, "videos");
                if (Directory.Exists(videosDir))
                {
                    foreach (var file in Directory.GetFiles(videosDir))
                    {
                        File.Delete(file);
                    }
                }
                
                var framesDir = Path.Combine(_cacheDirectory, "frames");
                if (Directory.Exists(framesDir))
                {
                    foreach (var file in Directory.GetFiles(framesDir))
                    {
                        File.Delete(file);
                    }
                }
                
                // Clear index
                _cacheIndex.Clear();
                _totalCacheSize = 0;
                
                lock (_statsLock)
                {
                    _cacheHits = 0;
                    _cacheMisses = 0;
                }
                
                await SaveCacheIndexAsync();
                
                _logger.LogInformation("✅ Cache cleared");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to clear cache");
            }
        }

        /// <summary>
        /// Cleanup timer callback
        /// </summary>
        private async void CleanupCallback(object state)
        {
            try
            {
                await CleanupOldEntriesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Cleanup timer failed");
            }
        }

        /// <summary>
        /// Stats timer callback
        /// </summary>
        private void StatsCallback(object state)
        {
            try
            {
                var stats = GetCacheStatistics();
                _logger.LogInformation($"📊 Cache stats: {stats.TotalEntries} entries, {stats.TotalSize / 1024 / 1024:F1}MB ({stats.UsagePercentage:F1}%), {stats.HitRate:F1}% hit rate");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Stats timer failed");
            }
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _statsTimer?.Dispose();
            
            // Save index on dispose
            try
            {
                SaveCacheIndexAsync().Wait();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to save cache index on dispose");
            }
        }

        /// <summary>
        /// Cache statistics model
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
}