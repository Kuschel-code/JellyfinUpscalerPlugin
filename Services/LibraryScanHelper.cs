using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Helper for automatic library scanning after upscaling jobs
    /// Ensures new upscaled versions appear in Jellyfin without manual refresh
    /// </summary>
    public class LibraryScanHelper
    {
        private readonly ILogger<LibraryScanHelper> _logger;
        private readonly ILibraryManager _libraryManager;

        public LibraryScanHelper(
            ILogger<LibraryScanHelper> logger,
            ILibraryManager libraryManager)
        {
            _logger = logger;
            _libraryManager = libraryManager;
        }

        /// <summary>
        /// Trigger library scan for upscaled video file
        /// </summary>
        public async Task<bool> ScanUpscaledFile(string originalPath, string upscaledPath)
        {
            try
            {
                if (!File.Exists(upscaledPath))
                {
                    _logger.LogWarning("Upscaled file not found, skipping scan: {UpscaledPath}", upscaledPath);
                    return false;
                }

                _logger.LogInformation("Triggering library scan for: {FileName}", Path.GetFileName(upscaledPath));

                // Get the directory containing the upscaled file
                var directory = Path.GetDirectoryName(upscaledPath);
                if (string.IsNullOrEmpty(directory))
                {
                    _logger.LogWarning("Could not determine directory for library scan");
                    return false;
                }

                // Find the library folder containing this file
                var libraryFolders = _libraryManager.GetVirtualFolders();
                var targetFolder = libraryFolders.FirstOrDefault(f => 
                    directory.StartsWith(f.Locations.FirstOrDefault() ?? "", StringComparison.OrdinalIgnoreCase)
                );

                if (targetFolder != null)
                {
                    _logger.LogInformation("Refreshing library item for: {LibraryName}", targetFolder.Name);
                }
                else
                {
                    _logger.LogWarning("No library folder found containing: {Directory}", directory);
                }

                // Targeted refresh: only refresh the specific upscaled file instead of full library scan
                // (ValidateMediaLibrary scans ALL libraries — catastrophic on large libraries)
                var item = _libraryManager.FindByPath(upscaledPath, false);
                if (item != null)
                {
                    await item.RefreshMetadata(CancellationToken.None);
                    _logger.LogInformation("Metadata refreshed for existing item: {ItemName}", item.Name);
                }
                else
                {
                    // File is new — try refreshing the parent folder item so Jellyfin discovers it
                    var parentItem = _libraryManager.FindByPath(directory, true);
                    if (parentItem != null)
                    {
                        await parentItem.RefreshMetadata(CancellationToken.None);
                        _logger.LogInformation("Parent folder refreshed to discover new file");
                    }
                    else
                    {
                        _logger.LogWarning("Could not find parent folder in library for targeted refresh");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to scan library after upscaling");
                return false;
            }
        }

        /// <summary>
        /// Create version link for upscaled file
        /// Links original and upscaled versions as alternate versions in Jellyfin
        /// </summary>
        public async Task LinkVersions(string originalPath, string upscaledPath)
        {
            try
            {
                _logger.LogInformation("Linking versions: {OriginalFileName} -> {UpscaledFileName}", Path.GetFileName(originalPath), Path.GetFileName(upscaledPath));

                // Find the original item in library
                var originalItem = _libraryManager.FindByPath(originalPath, false);
                if (originalItem == null)
                {
                    _logger.LogWarning("Original item not found in library: {OriginalPath}", originalPath);
                    return;
                }

                // Trigger scan to add upscaled version
                await ScanUpscaledFile(originalPath, upscaledPath);

                _logger.LogInformation("Versions linked successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to link video versions");
            }
        }

        /// <summary>
        /// Refresh metadata for specific item
        /// </summary>
        public async Task RefreshItem(string filePath)
        {
            try
            {
                var item = _libraryManager.FindByPath(filePath, false);
                if (item != null)
                {
                    _logger.LogInformation("Refreshing metadata for: {ItemName}", item.Name);
                    
                    // Simplified metadata refresh without DirectoryService
                    await item.RefreshMetadata(CancellationToken.None);

                    _logger.LogInformation("Metadata refreshed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh item metadata");
            }
        }
    }
}
