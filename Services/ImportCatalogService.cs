using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// v1.8.3.6 — resolves the OpenModelDB import catalog (site/models-import.json,
    /// regenerated weekly by CI). The importer accepts ONLY ids from this catalog:
    /// no free-form URLs reach the download path, every file is sha256-pinned, and
    /// the download host must be on a small allowlist. See docs section 2 of the
    /// importer spec ("OMDB-ID-basierter Import, KEINE freien URLs").
    /// </summary>
    public class ImportCatalogService
    {
        private const string PrimaryUrl = "https://kuschel-code.github.io/JellyfinUpscalerPlugin/models-import.json";
        private const string FallbackUrl = "https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/site/models-import.json";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

        /// <summary>Hosts a pinned download URL may point at. Everything else is refused.</summary>
        internal static readonly string[] AllowedHosts =
        {
            "github.com",
            "raw.githubusercontent.com",
            "huggingface.co",
            "objectstorage.us-phoenix-1.oraclecloud.com"
        };

        /// <summary>500 MB — the largest curated model is ~380 MB; anything bigger is suspicious.</summary>
        internal const long MaxImportBytes = 500L * 1024 * 1024;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ImportCatalogService> _logger;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private List<ImportableModel>? _cache;
        private DateTimeOffset _cacheTime = DateTimeOffset.MinValue;

        public ImportCatalogService(IHttpClientFactory httpClientFactory, ILogger<ImportCatalogService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// A direct download the PLUGIN can perform: https, allowlisted host, a plain
        /// .onnx file (zip bundles and interactive hosts like mega/gdrive need a browser).
        /// </summary>
        internal static bool IsDirectlyImportable(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps) return false;
            if (!uri.AbsolutePath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)) return false;
            return AllowedHosts.Any(h =>
                uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// OMDB occasionally records browser VIEWER urls: /blob/ pages on GitHub and
        /// HuggingFace serve HTML, not the file, so the download would fail the sha256
        /// pin. Rewrite them to the raw-content equivalents (same file, same hash).
        /// The catalog generator does the same; this covers stale catalog data.
        /// </summary>
        internal static string? NormalizeDownloadUrl(string? url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            if (url.StartsWith("https://github.com/", StringComparison.Ordinal) && url.Contains("/blob/", StringComparison.Ordinal))
            {
                var raw = "https://raw.githubusercontent.com/" + url.Substring("https://github.com/".Length);
                var idx = raw.IndexOf("/blob/", StringComparison.Ordinal);
                return raw.Remove(idx, "/blob".Length);
            }
            if (url.StartsWith("https://huggingface.co/", StringComparison.Ordinal) && url.Contains("/blob/", StringComparison.Ordinal))
            {
                var idx = url.IndexOf("/blob/", StringComparison.Ordinal);
                return url.Substring(0, idx) + "/resolve/" + url.Substring(idx + "/blob/".Length);
            }
            return url;
        }

        /// <summary>
        /// Catalog id -> service model name. The AI service's /models/upload accepts
        /// ^[a-zA-Z0-9_-]{1,64}$; the omdb- prefix namespaces imports so a community
        /// model can never shadow a curated catalog entry.
        /// </summary>
        internal static string ToModelName(string catalogId)
        {
            var cleaned = Regex.Replace(catalogId.ToLowerInvariant(), "[^a-z0-9-]+", "-").Trim('-');
            var name = "omdb-" + cleaned;
            return name.Length <= 64 ? name : name.Substring(0, 64).TrimEnd('-');
        }

        internal static bool IsNonCommercial(string? license) =>
            !string.IsNullOrEmpty(license) && license.Contains("NC", StringComparison.OrdinalIgnoreCase);

        /// <summary>All direct-ONNX catalog entries (cached ~6h). Null on fetch failure with an empty cache.</summary>
        public async Task<IReadOnlyList<ImportableModel>?> GetCatalogAsync(CancellationToken ct)
        {
            if (_cache != null && DateTimeOffset.UtcNow - _cacheTime < CacheTtl) return _cache;
            await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_cache != null && DateTimeOffset.UtcNow - _cacheTime < CacheTtl) return _cache;
                foreach (var url in new[] { PrimaryUrl, FallbackUrl })
                {
                    try
                    {
                        var client = _httpClientFactory.CreateClient("ExternalModelDownload");
                        using var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode) continue;
                        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        var doc = JsonSerializer.Deserialize<ImportCatalogDoc>(json);
                        if (doc?.DirectOnnx == null || doc.DirectOnnx.Count == 0) continue;
                        foreach (var m in doc.DirectOnnx)
                        {
                            m.DownloadUrl = NormalizeDownloadUrl(m.DownloadUrl);
                        }
                        _cache = doc.DirectOnnx;
                        _cacheTime = DateTimeOffset.UtcNow;
                        _logger.LogInformation("AI Upscaler: import catalog loaded ({Count} direct-ONNX entries, generated {Gen})",
                            _cache.Count, doc.Generated ?? "?");
                        return _cache;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning("AI Upscaler: import catalog fetch failed from {Url}: {Message}", url, ex.Message);
                    }
                }
                return _cache; // possibly stale or null — caller surfaces the error
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        public async Task<ImportableModel?> ResolveAsync(string id, CancellationToken ct)
        {
            var catalog = await GetCatalogAsync(ct).ConfigureAwait(false);
            return catalog?.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public class ImportCatalogDoc
    {
        [JsonPropertyName("generated")] public string? Generated { get; set; }
        [JsonPropertyName("direct_onnx")] public List<ImportableModel>? DirectOnnx { get; set; }
    }

    public class ImportableModel
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("scale")] public JsonElement Scale { get; set; }
        [JsonPropertyName("architecture")] public string? Architecture { get; set; }
        [JsonPropertyName("license")] public string? License { get; set; }
        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
        [JsonPropertyName("size_bytes")] public long SizeBytes { get; set; }
        [JsonPropertyName("download_url")] public string? DownloadUrl { get; set; }
        [JsonPropertyName("omdb_url")] public string? OmdbUrl { get; set; }

        /// <summary>Scale as int (the JSON occasionally carries "?" for unknown).</summary>
        public int ScaleInt => Scale.ValueKind == JsonValueKind.Number && Scale.TryGetInt32(out var s) ? Math.Clamp(s, 1, 8) : 2;
    }
}
