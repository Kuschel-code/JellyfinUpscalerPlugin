using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>A library video as the scanner needs it.</summary>
    internal sealed record PreUpscaledSource(Guid Id, string Name, string Path, int Width, int Height);

    /// <summary>One row of the dashboard's Pre-upscaled tab.</summary>
    internal sealed record PreUpscaledEntry(
        Guid Id, string Name, string Path, int Width, int Height,
        string? UpscaledPath, long UpscaledSize, DateTime? UpscaledDate, string Status);

    /// <summary>
    /// Works out which library videos already have an upscaled copy. The convention is the
    /// scan task's: "&lt;name&gt;_upscaled&lt;ext&gt;" in the source's folder; a copy with another
    /// video extension counts too.
    /// </summary>
    /// <remarks>
    /// Each folder is listed once, however many videos it holds. The first version checked
    /// every video's folder up to three times per request (Exists, Exists, a wildcard
    /// EnumerateFiles that also read '*' and '?' in a file name as wildcards) and redid it
    /// for every page and search keystroke. An output whose source is in the library is not
    /// a row of its own: it showed up as a second "Pre-Upscaled" video whose Re-upscale
    /// button queued the upscaled file again.
    /// </remarks>
    internal static class PreUpscaledScanner
    {
        internal const string UpscaledStatus = "Pre-Upscaled";
        internal const string PendingStatus = "Pending";
        internal const string NativeHdStatus = "Native HD";
        private const string Suffix = "_upscaled";

        // Jellyfin writes .nfo and image files with the video's name, so only these count as an
        // upscaled copy when its extension differs from the source's.
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".webm", ".ts", ".m2ts", ".mts",
            ".mpg", ".mpeg", ".flv", ".ogv", ".3gp", ".vob", ".divx", ".asf"
        };

        internal static List<PreUpscaledEntry> Scan(IEnumerable<PreUpscaledSource> videos, int minWidth, int minHeight)
        {
            var sources = videos.Where(v => !string.IsNullOrEmpty(v.Path)).ToList();
            var inLibrary = new HashSet<string>(
                sources.Select(v => Key(v.Path, Path.GetFileNameWithoutExtension(v.Path))), StringComparer.OrdinalIgnoreCase);
            var folders = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
            var rows = new List<PreUpscaledEntry>(sources.Count);

            foreach (var video in sources)
            {
                var baseName = Path.GetFileNameWithoutExtension(video.Path);
                var ext = Path.GetExtension(video.Path);

                string? upscaledPath;
                if (baseName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
                {
                    if (inLibrary.Contains(Key(video.Path, baseName.Substring(0, baseName.Length - Suffix.Length))))
                    {
                        continue; // shown on its source's row
                    }
                    upscaledPath = video.Path; // its source is not in the library; the output is all there is
                }
                else
                {
                    upscaledPath = null;
                    var files = ListFolder(folders, Path.GetDirectoryName(video.Path) ?? string.Empty);
                    if (files.TryGetValue(baseName + Suffix, out var named))
                    {
                        upscaledPath = named.FirstOrDefault(p => string.Equals(Path.GetExtension(p), ext, StringComparison.OrdinalIgnoreCase))
                                       ?? named.Where(p => VideoExtensions.Contains(Path.GetExtension(p)))
                                               .OrderBy(p => p, StringComparer.Ordinal)
                                               .FirstOrDefault();
                    }
                }

                long size = 0;
                DateTime? date = null;
                if (upscaledPath != null)
                {
                    try
                    {
                        var info = new FileInfo(upscaledPath);
                        if (info.Exists)
                        {
                            size = info.Length;
                            date = info.LastWriteTimeUtc;
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                bool lowRes = video.Width > 0 && video.Height > 0 && (video.Width < minWidth || video.Height < minHeight);
                var status = upscaledPath != null ? UpscaledStatus : lowRes ? PendingStatus : NativeHdStatus;
                rows.Add(new PreUpscaledEntry(video.Id, video.Name, video.Path, video.Width, video.Height, upscaledPath, size, date, status));
            }

            return rows;
        }

        private static string Key(string path, string baseName) =>
            (Path.GetDirectoryName(path) ?? string.Empty) + "\0" + baseName;

        // File names without extension (case-insensitive) -> full paths, listed once per folder.
        private static Dictionary<string, List<string>> ListFolder(Dictionary<string, Dictionary<string, List<string>>> cache, string dir)
        {
            if (cache.TryGetValue(dir, out var known))
            {
                return known;
            }

            var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (dir.Length > 0 && Directory.Exists(dir))
                {
                    foreach (var file in Directory.EnumerateFiles(dir))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        if (!byName.TryGetValue(name, out var list))
                        {
                            byName[name] = list = new List<string>();
                        }
                        list.Add(file);
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            cache[dir] = byName;
            return byName;
        }
    }
}
