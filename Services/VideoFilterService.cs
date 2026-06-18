using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Builds FFmpeg video filter chains for camera-style presets and custom filter parameters.
    /// </summary>
    public class VideoFilterService
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// v1.7.1 - Single source of truth for the supported camera-style filter presets.
        /// Mirrors the &lt;option value="..."&gt; values in &lt;select id="ActiveFilterPreset"&gt;
        /// (configurationpage.html). Replaces the previously-duplicated _validFilterPresets array
        /// that lived inside UpscalerController and shipped 5 inline references.
        /// </summary>
        public static readonly IReadOnlySet<string> SupportedPresets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "none", "cinematic", "vintage", "vivid", "noir", "warm", "cool",
            "hdr-pop", "sepia", "pastel", "cyberpunk", "drama", "soft-glow",
            "sharp-hd", "retrogame", "teal-orange", "custom"
        };

        /// <summary>
        /// v1.8.2 — supported denoise-prefilter engines. Mirrors the
        /// &lt;option&gt; values of &lt;select id="DenoisePrefilterMethod"&gt; in configurationpage.html.
        /// </summary>
        public static readonly IReadOnlySet<string> SupportedDenoiseMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hqdn3d", "nlmeans"
        };

        /// <summary>
        /// v1.8.2 — builds the denoise-before-encode prefilter (the "Netflix lesson":
        /// denoise the source before upscaling/encoding so the SR model gets a clean
        /// input and you don't waste output bitrate encoding compression noise).
        ///
        /// Deliberately independent of the camera-style filter system — gated only by
        /// <see cref="PluginConfiguration.EnableDenoisePrefilter"/>, so it works with
        /// preset = "none". Returns null when disabled or strength is ~0.
        /// </summary>
        public string? BuildDenoisePrefilter(PluginConfiguration config)
        {
            if (!config.EnableDenoisePrefilter)
                return null;

            var strength = config.DenoisePrefilterStrength;
            if (strength <= 0.001)
                return null;

            var method = (config.DenoisePrefilterMethod ?? "hqdn3d").ToLowerInvariant();
            if (!SupportedDenoiseMethods.Contains(method))
                method = "hqdn3d";

            if (method == "nlmeans")
            {
                // nlmeans s= denoising strength (≈1.0 light … higher = stronger, much slower).
                return $"nlmeans=s={strength.ToString("F1", Inv)}";
            }

            // hqdn3d luma_spatial:chroma_spatial:luma_tmp:chroma_tmp — driven from one knob:
            // chroma a touch lower, temporal a touch higher (standard hqdn3d tuning).
            var ls = strength.ToString("F1", Inv);
            var cs = (strength * 0.75).ToString("F1", Inv);
            var lt = (strength * 1.5).ToString("F1", Inv);
            var ct = (strength * 1.5).ToString("F1", Inv);
            return $"hqdn3d={ls}:{cs}:{lt}:{ct}";
        }

        /// <summary>
        /// Builds the complete FFmpeg filter chain string from plugin configuration.
        /// Returns null when no filters are active.
        /// </summary>
        public string? BuildFilterChain(PluginConfiguration config)
        {
            if (!config.EnableVideoFilters)
                return null;

            var preset = (config.ActiveFilterPreset ?? "none").ToLowerInvariant();
            if (preset == "none")
                return null;

            return preset == "custom"
                ? BuildCustomFilters(config)
                : GetPresetFilters(preset);
        }

        /// <summary>
        /// Returns the FFmpeg filter chain for a named preset.
        /// </summary>
        public string? GetPresetFilters(string presetName)
        {
            return presetName.ToLowerInvariant() switch
            {
                "cinematic" => "eq=contrast=1.15:saturation=0.85:gamma=1.1,colortemperature=temperature=5500,vignette=PI/4",
                "vintage"   => "eq=contrast=0.9:saturation=0.6:brightness=0.05,curves=preset=vintage,noise=c0s=30:c0f=t+u",
                "vivid"     => "eq=contrast=1.2:saturation=1.5:gamma=0.95",
                "noir"      => "eq=saturation=0:contrast=1.3,curves=preset=darker,vignette=PI/3,noise=c0s=20:c0f=t+u",
                "warm"      => "colortemperature=temperature=6500,eq=saturation=1.1:brightness=0.03",
                "cool"      => "colortemperature=temperature=4000,eq=saturation=0.9:contrast=1.05",
                "hdr-pop"   => "eq=contrast=1.25:saturation=1.3:gamma=0.85,unsharp=5:5:1.5:5:5:0",
                // v1.6.1.7 — additional presets
                "sepia"     => "eq=saturation=0,curves=r='0/0 0.5/0.6 1/0.95':g='0/0 0.5/0.45 1/0.75':b='0/0 0.5/0.25 1/0.5'",
                "pastel"    => "eq=contrast=0.85:saturation=0.65:brightness=0.08:gamma=1.05,colortemperature=temperature=7200",
                "cyberpunk" => "eq=contrast=1.3:saturation=1.6:gamma=0.9,colortemperature=temperature=3800,unsharp=5:5:0.8:5:5:0",
                "drama"     => "eq=contrast=1.35:saturation=0.75:gamma=0.95,curves=preset=darker,vignette=PI/5",
                "soft-glow" => "eq=contrast=0.95:saturation=1.05:brightness=0.05,gblur=sigma=1.2,unsharp=5:5:0.4:5:5:0",
                "sharp-hd"  => "unsharp=7:7:2.0:7:7:0.5,eq=contrast=1.1:saturation=1.1",
                "retrogame" => "eq=contrast=1.2:saturation=1.35:gamma=0.92,noise=c0s=15:c0f=t+u",
                "teal-orange" => "eq=saturation=1.2:contrast=1.1,curves=b='0/0 0.4/0.5 1/0.85':r='0/0 0.5/0.55 1/1'",
                _ => null
            };
        }

        /// <summary>
        /// Builds an FFmpeg filter chain from individual custom filter parameters.
        /// Only includes filters that differ from their neutral/default values.
        /// </summary>
        public string? BuildCustomFilters(PluginConfiguration config)
        {
            var filters = new List<string>();

            // eq filter: brightness, contrast, saturation, gamma — combined into one eq= call
            var eqParts = new List<string>();
            if (Math.Abs(config.FilterBrightness) > 0.001)
                eqParts.Add($"brightness={config.FilterBrightness.ToString("F2", Inv)}");
            if (Math.Abs(config.FilterContrast - 1.0) > 0.001)
                eqParts.Add($"contrast={config.FilterContrast.ToString("F2", Inv)}");
            if (Math.Abs(config.FilterSaturation - 1.0) > 0.001)
                eqParts.Add($"saturation={config.FilterSaturation.ToString("F2", Inv)}");
            if (Math.Abs(config.FilterGamma - 1.0) > 0.001)
                eqParts.Add($"gamma={config.FilterGamma.ToString("F2", Inv)}");

            if (eqParts.Count > 0)
                filters.Add($"eq={string.Join(":", eqParts)}");

            // Sharpness via unsharp mask
            if (config.FilterSharpness > 0.001)
                filters.Add($"unsharp=5:5:{config.FilterSharpness.ToString("F1", Inv)}:5:5:0");

            // Color temperature
            if (config.FilterColorTemperature != 6500)
                filters.Add($"colortemperature=temperature={config.FilterColorTemperature}");

            // Vignette
            if (config.FilterVignette > 0.001)
                filters.Add($"vignette=PI/{config.FilterVignette.ToString("F1", Inv)}");

            // Film grain via noise filter
            if (config.FilterFilmGrain > 0)
                filters.Add($"noise=c0s={config.FilterFilmGrain}:c0f=t+u");

            // Denoise via hqdn3d
            if (config.FilterDenoise > 0.001)
                filters.Add($"hqdn3d={config.FilterDenoise.ToString("F1", Inv)}");

            // LUT color grading
            if (!string.IsNullOrWhiteSpace(config.FilterLutPath) && File.Exists(config.FilterLutPath))
            {
                var safePath = config.FilterLutPath.Replace("\\", "/").Replace("'", "'\\''");
                filters.Add($"lut3d=file='{safePath}'");
            }

            return filters.Count > 0 ? string.Join(",", filters) : null;
        }
    }
}
