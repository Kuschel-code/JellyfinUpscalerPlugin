using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>Availability and pipeline suitability come from the embedded service catalog.</summary>
    internal static class ModelAvailability
    {
        private static readonly Dictionary<string, JsonElement> Models = ReadCatalog();
        public static readonly HashSet<string> KnownUnavailable = Models
            .Where(pair => pair.Value.TryGetProperty("available", out var flag) && flag.ValueKind == JsonValueKind.False)
            .Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, JsonElement> ReadCatalog()
        {
            using var stream = typeof(ModelAvailability).Assembly.GetManifestResourceStream(
                "JellyfinUpscalerPlugin.Resources.models-fallback.json")
                ?? throw new InvalidDataException("Embedded model catalog missing");
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.GetProperty("models").EnumerateArray().ToDictionary(
                model => model.GetProperty("id").GetString()!, model => model.Clone(), StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsKnownUnavailable(string? modelId) =>
            !string.IsNullOrWhiteSpace(modelId) && KnownUnavailable.Contains(modelId);

        public static bool IsUsableUpscaler(string? modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId) || IsKnownUnavailable(modelId)) return false;
            // Custom imports do not need to be present in the bundled catalog.
            if (!Models.TryGetValue(modelId, out var model)) return true;
            var category = model.GetProperty("category").GetString()?.ToLowerInvariant();
            return category is not ("interpolation" or "face_restore" or "face-restore" or "object-detection");
        }

        public static string PickAvailable(string preferred, params string[] fallbacks)
        {
            if (IsUsableUpscaler(preferred)) return preferred;
            foreach (var fallback in fallbacks)
                if (IsUsableUpscaler(fallback)) return fallback;
            return "realesrgan-x4";
        }
    }
}
