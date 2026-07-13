using JellyfinUpscalerPlugin.Services;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests
{
    /// <summary>
    /// v1.8.3.6 — invariants of the import-catalog gates. These are the pure
    /// security decisions of the one-click importer: which URLs the plugin will
    /// ever download from, and how catalog ids map into the service's
    /// model-name namespace.
    /// </summary>
    public class ImportCatalogServiceTests
    {
        [Theory]
        [InlineData("https://github.com/Phhofm/models/releases/download/x/y_fp32.onnx", true)]
        [InlineData("https://raw.githubusercontent.com/terrainer/AI-Upscaling-Models/main/4xSPANkendata/4xSPANkendata_fp32.onnx", true)]
        [InlineData("https://huggingface.co/Kim2091/UltraSharpV2/blob/main/4x-UltraSharpV2_fp32_op17.onnx", true)]
        [InlineData("https://objectstorage.us-phoenix-1.oraclecloud.com/n/x/b/y/o/model.onnx", true)]
        [InlineData("http://github.com/x/y.onnx", false)]                       // https only
        [InlineData("https://mega.nz/folder/abc#def", false)]                   // interactive host
        [InlineData("https://drive.google.com/file/d/abc/view", false)]        // interactive host
        [InlineData("https://github.com/x/releases/download/v1/bundle.zip", false)] // not a plain .onnx
        [InlineData("https://evil-github.com/x/y.onnx", false)]                 // host must MATCH, not contain
        [InlineData("https://github.com.evil.example/x/y.onnx", false)]         // suffix spoof
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsDirectlyImportable_gates_scheme_host_and_filetype(string? url, bool expected)
        {
            Assert.Equal(expected, ImportCatalogService.IsDirectlyImportable(url));
        }

        [Theory]
        [InlineData("4x-UltraSharpV2", "omdb-4x-ultrasharpv2")]
        [InlineData("2x-StarSample-V2-Lite-NS", "omdb-2x-starsample-v2-lite-ns")]
        [InlineData("Weird__Name..v1", "omdb-weird-name-v1")]
        public void ToModelName_namespaces_and_sanitizes(string id, string expected)
        {
            var name = ImportCatalogService.ToModelName(id);
            Assert.Equal(expected, name);
            // must satisfy the AI service's /models/upload name contract
            Assert.Matches("^[a-zA-Z0-9_-]{1,64}$", name);
            Assert.StartsWith("omdb-", name); // never shadows a curated catalog id
        }

        [Fact]
        public void ToModelName_caps_at_64_chars()
        {
            var name = ImportCatalogService.ToModelName(new string('a', 100));
            Assert.True(name.Length <= 64);
            Assert.Matches("^[a-zA-Z0-9_-]{1,64}$", name);
        }

        [Theory]
        // /blob/ pages serve HTML, not the file — must be rewritten to raw-content urls
        [InlineData("https://github.com/Phhofm/models/blob/main/x/y_fp32.onnx",
                    "https://raw.githubusercontent.com/Phhofm/models/main/x/y_fp32.onnx")]
        [InlineData("https://huggingface.co/Kim2091/UltraSharpV2/blob/main/4x-UltraSharpV2_fp32_op17.onnx",
                    "https://huggingface.co/Kim2091/UltraSharpV2/resolve/main/4x-UltraSharpV2_fp32_op17.onnx")]
        [InlineData("https://github.com/x/releases/download/v1/y.onnx",
                    "https://github.com/x/releases/download/v1/y.onnx")] // already direct — untouched
        [InlineData(null, null)]
        public void NormalizeDownloadUrl_rewrites_viewer_pages_to_raw_content(string? url, string? expected)
        {
            var normalized = ImportCatalogService.NormalizeDownloadUrl(url);
            Assert.Equal(expected, normalized);
            if (expected != null)
            {
                Assert.True(ImportCatalogService.IsDirectlyImportable(normalized));
            }
        }

        [Theory]
        [InlineData("CC-BY-NC-SA-4.0", true)]
        [InlineData("CC-BY-NC-4.0", true)]
        [InlineData("CC-BY-4.0", false)]
        [InlineData("Apache-2.0", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsNonCommercial_flags_nc_variants(string? license, bool expected)
        {
            Assert.Equal(expected, ImportCatalogService.IsNonCommercial(license));
        }
    }
}
