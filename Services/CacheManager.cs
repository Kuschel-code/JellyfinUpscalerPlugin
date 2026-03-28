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
using JellyfinUpscalerPlugin.Models;

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
        
        // Cache metadata
        private readonly ConcurrentDictionary<string, CacheEntry> _cacheIndex = new();
        private readonly string _cacheDirectory;
        private readonly string _indexFile;
        
        // Cache monitoring
        private readonly Timer? _cleanupTimer;
        private readonly Timer? _statsTimer;

        // Synchronization for cache cleanup vs store operations
        private readonly SemaphoreSlim _cleanupLock = new(1, 1);

        // Performance tracking
        private long _totalCacheSize;
        private int _cacheHits;
        private int _cacheMisses;
        private readonly object _statsLock = new();
        
        private const double CacheCleanupTargetRatio = 0.8; // Clean to 80% capacity to avoid frequent cleanups

        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();
        
        public CacheManager(
            ILogger<CacheManager> logger,
            IApplicationPaths appPaths,
            IFileSystem fileSystem)
        {
            _logger = logger;
            _appPaths = appPaths;
            _fileSystem = fileSystem;
            
            // Initialize cache directory
            _cacheDirectory = Path.Combine(_appPaths.CachePath, "JellyfinUpscaler");
            _indexFile = Path.Combine(_cacheDirectory, "cache_index.json");
            
            InitializeCacheDirectory();
            LoadCacheIndex();
            
            // Start cleanup timer (every hour)
            _cleanupTimer = new Timer(CleanupCallback, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            
            // Start stats timer (every 5 minutes)
            _statsTimer = new Timer(StatsCallback, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            
            _logger.LogInformation("Cache manager initialized: {CacheDirectory}", _cacheDirectory);
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
                
                _logger.LogInformation("Cache directory structure initialized");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize cache directory");
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
                    _logger.LogInformation("No cache index found, starting fresh");
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
                    
                    _logger.LogInformation("Loaded {EntryCount} cache entries", _cacheIndex.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load cache index");
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

                // Atomic write: write to temp file then rename to prevent corruption on crash
                var tempFile = _indexFile + ".tmp";
                await File.WriteAllTextAsync(tempFile, jsonContent);
                File.Move(tempFile, _indexFile, overwrite: true);
                
                _logger.LogDebug("Cache index saved");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save cache index");
            }
        }

        /// <summary>
        /// Validate cache entries and remove invalid ones
        /// </summary>
        private void ValidateCacheEntries()
        {
            var invalidEntries = new List<string>();
            long validSize = 0;

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

                // Accumulate valid cache size
                validSize += entry.FileSize;
            }

            // Remove invalid entries and clean up files
            foreach (var key in invalidEntries)
            {
                if (_cacheIndex.TryRemove(key, out var removed))
                {
                    try { if (File.Exists(removed.FilePath)) File.Delete(removed.FilePath); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete expired cache file: {Path}", removed.FilePath); }
                }
            }

            // Set total from validated entries (thread-safe)
            Interlocked.Exchange(ref _totalCacheSize, validSize);
            
            if (invalidEntries.Count > 0)
            {
                _logger.LogInformation("Removed {InvalidCount} invalid cache entries", invalidEntries.Count);
            }
        }

        /// <summary>
        /// Check if an entry is expired
        /// </summary>
        private bool IsEntryExpired(CacheEntry entry)
        {
            var maxAge = TimeSpan.FromDays(Config.MaxCacheAgeDays);
            return DateTime.UtcNow - entry.CreatedAt > maxAge;
        }

        /// <summary>
        /// Generate deterministic cache key using SHA256 hash of input parameters for collision-free lookups.
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
        public Task<CacheResult> GetCachedContentAsync(string inputPath, string model, int scale, string quality)
        {
            var cacheKey = GenerateCacheKey(inputPath, model, scale, quality);
            
            if (_cacheIndex.TryGetValue(cacheKey, out var entry))
            {
                // Validate entry
                if (File.Exists(entry.FilePath) && !IsEntryExpired(entry))
                {
                    // Update access time under lock to prevent race conditions
                    lock (_statsLock)
                    {
                        entry.LastAccessedAt = DateTime.UtcNow;
                        entry.AccessCount++;
                        _cacheHits++;
                    }
                    
                    _logger.LogDebug("Cache hit: {FileName}", Path.GetFileName(inputPath));
                    
                    return Task.FromResult(new CacheResult
                    {
                        Hit = true,
                        FilePath = entry.FilePath,
                        Entry = entry
                    });
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

            _logger.LogDebug("Cache miss: {FileName}", Path.GetFileName(inputPath));

            return Task.FromResult(new CacheResult { Hit = false });
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
            Dictionary<string, object>? metadata = null)
        {
            try
            {
                var cacheKey = GenerateCacheKey(inputPath, model, scale, quality);
                
                // Check cache size limit
                if (!CheckCacheSizeLimit())
                {
                    _logger.LogWarning("Cache size limit exceeded, cleaning up");
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
                    CreatedAt = DateTime.UtcNow,
                    LastAccessedAt = DateTime.UtcNow,
                    AccessCount = 1,
                    ProcessingTime = processingTime,
                    Metadata = metadata ?? new Dictionary<string, object>()
                };
                
                _cacheIndex[cacheKey] = entry;

                // Update total cache size under cleanup lock to prevent race with cleanup recalculation
                await _cleanupLock.WaitAsync();
                try
                {
                    Interlocked.Add(ref _totalCacheSize, entry.FileSize);
                }
                finally
                {
                    _cleanupLock.Release();
                }

                // Save index
                await SaveCacheIndexAsync();
                
                _logger.LogInformation("Cached: {InputFileName} -> {CacheFileName} ({SizeMB:F1}MB)", Path.GetFileName(inputPath), fileName, entry.FileSize / 1024 / 1024);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cache content: {InputPath}", inputPath);
                return false;
            }
        }

        /// <summary>
        /// Check cache size limit
        /// </summary>
        private bool CheckCacheSizeLimit()
        {
            var maxCacheSize = (long)Config.CacheSizeMB * 1024 * 1024;
            return Interlocked.Read(ref _totalCacheSize) < maxCacheSize;
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
                    _logger.LogDebug("Content already cached: {FileName}", Path.GetFileName(inputPath));
                    return true;
                }
                
                _logger.LogInformation("Pre-processing: {FileName}", Path.GetFileName(inputPath));
                
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
                    _logger.LogError("Pre-processing failed: {Error}", result.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pre-processing failed: {InputPath}", inputPath);
                return false;
            }
        }

        /// <summary>
        /// Clean up old cache entries
        /// </summary>
        private async Task CleanupOldEntriesAsync()
        {
            await _cleanupLock.WaitAsync();
            try
            {
                var maxCacheSize = (long)Config.CacheSizeMB * 1024 * 1024;
                var currentSize = Interlocked.Read(ref _totalCacheSize);

                if (currentSize <= maxCacheSize)
                {
                    return;
                }

                _logger.LogInformation("Cleaning up cache ({CurrentSizeMB:F1}MB > {MaxSizeMB:F1}MB)", currentSize / 1024 / 1024, maxCacheSize / 1024 / 1024);

                // Sort by last accessed time and access count
                var entriesToRemove = _cacheIndex.Values
                    .OrderBy(e => e.LastAccessedAt)
                    .ThenBy(e => e.AccessCount)
                    .ToList();

                var removedCount = 0;

                foreach (var entry in entriesToRemove)
                {
                    if (currentSize <= (long)(maxCacheSize * CacheCleanupTargetRatio)) // Leave buffer to avoid frequent cleanups
                    {
                        break;
                    }

                    // Remove file
                    if (File.Exists(entry.FilePath))
                    {
                        File.Delete(entry.FilePath);
                        currentSize -= entry.FileSize;
                    }

                    // Remove from index
                    _cacheIndex.TryRemove(entry.Key, out _);
                    removedCount++;
                }

                // Recalculate total cache size from remaining entries instead of incremental tracking
                long recalculatedSize = 0;
                foreach (var kvp in _cacheIndex)
                {
                    recalculatedSize += kvp.Value.FileSize;
                }
                Interlocked.Exchange(ref _totalCacheSize, recalculatedSize);

                if (removedCount > 0)
                {
                    await SaveCacheIndexAsync();
                    _logger.LogInformation("Removed {RemovedCount} cache entries, cache now {SizeMB:F1}MB", removedCount, recalculatedSize / 1024.0 / 1024.0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cache cleanup failed");
            }
            finally
            {
                _cleanupLock.Release();
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
                    MaxSize = (long)Config.CacheSizeMB * 1024 * 1024,
                    HitRate = hitRate,
                    TotalHits = _cacheHits,
                    TotalMisses = _cacheMisses,
                    UsagePercentage = ((double)_totalCacheSize / ((long)Config.CacheSizeMB * 1024 * 1024)) * 100
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
                _logger.LogInformation("Clearing all cache");
                
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
                Interlocked.Exchange(ref _totalCacheSize, 0);
                
                lock (_statsLock)
                {
                    _cacheHits = 0;
                    _cacheMisses = 0;
                }
                
                await SaveCacheIndexAsync();
                
                _logger.LogInformation("Cache cleared");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear cache");
            }
        }

        /// <summary>
        /// Cleanup timer callback
        /// </summary>
        private void CleanupCallback(object? state)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await CleanupOldEntriesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Cleanup timer failed");
                }
            });
        }

        /// <summary>
        /// Stats timer callback
        /// </summary>
        private void StatsCallback(object? state)
        {
            try
            {
                var stats = GetCacheStatistics();
                _logger.LogInformation("Cache stats: {TotalEntries} entries, {TotalSizeMB:F1}MB ({UsagePercentage:F1}%), {HitRate:F1}% hit rate", stats.TotalEntries, stats.TotalSize / 1024 / 1024, stats.UsagePercentage, stats.HitRate);
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
            
            // Save index synchronously on dispose to avoid async deadlocks
            try
            {
                var entries = _cacheIndex.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                var jsonContent = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_indexFile, jsonContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save cache index on dispose");
            }
        }
    }
}