using System;
using System.Collections.Generic;

namespace JellyfinUpscalerPlugin.Services
{
    public static class PluginInfoService
    {
        public static List<object> GetAvailableModels()
        {
            return new List<object>
            {
                new { id = "realesrgan", name = "Real-ESRGAN", description = "Best Overall Quality (Anime/Photo)", scale = new[] { 2, 3, 4 } },
                new { id = "esrgan-pro", name = "ESRGAN Pro", description = "Optimized for Movies & TV Shows", scale = new[] { 2, 4 } },
                new { id = "swinir", name = "SwinIR", description = "State-of-the-art Transformer based", scale = new[] { 2, 4, 8 } },
                new { id = "srcnn-light", name = "SRCNN Light", description = "Fast processing for low-end hardware", scale = new[] { 2, 3 } },
                new { id = "waifu2x", name = "Waifu2x", description = "Classic Anime upscaling", scale = new[] { 2 } },
                new { id = "hat", name = "HAT", description = "High Detail Enhancement", scale = new[] { 2, 4 } },
                new { id = "edsr", name = "EDSR", description = "Precise Super-Resolution", scale = new[] { 2, 3, 4 } },
                new { id = "vdsr", name = "VDSR", description = "Deep Learning approach", scale = new[] { 2, 3, 4 } },
                new { id = "rdn", name = "RDN", description = "Enhanced Texture Detail", scale = new[] { 2, 4 } },
                new { id = "srresnet", name = "SRResNet", description = "Balanced Performance", scale = new[] { 2, 4 } },
                new { id = "carn", name = "CARN", description = "Compact & Fast", scale = new[] { 2, 3, 4 } },
                new { id = "rrdbnet", name = "RRDBNet", description = "High Fidelity Quality", scale = new[] { 2, 4 } },
                new { id = "drln", name = "DRLN", description = "Advanced Noise Reduction", scale = new[] { 2, 4 } },
                new { id = "fsrcnn", name = "FSRCNN", description = "Lightweight Real-time capable", scale = new[] { 2, 3, 4 } }
            };
        }

        public static object GetPluginMetadata()
        {
            var assembly = typeof(Plugin).Assembly;
            var version = assembly.GetName().Version?.ToString(3) ?? "1.4.1";

            return new
            {
                name = "AI Upscaler Plugin",
                version = version,
                description = "AI-powered video upscaling with modern UI integration and hardware benchmarking",
                author = "Kuschel-code",
                features = new[]
                {
                    "Real-time AI video upscaling",
                    "Multiple AI models (Real-ESRGAN, ESRGAN, SwinIR, Waifu2x)",
                    "Hardware acceleration support",
                    "Player integration with control buttons",
                    "Cross-platform compatibility",
                    "Performance optimization",
                    "Automated hardware benchmarking",
                    "Low-end hardware fallback system",
                    "Pre-processing cache for better performance",
                    "TV remote optimization",
                    "Comparison view for quality testing"
                },
                supportedPlatforms = new[]
                {
                    "Windows", "Linux", "macOS", "Docker",
                    "Smart TVs", "Android TV", "iOS", "Android",
                    "NAS (Synology, QNAP, Unraid, TrueNAS)",
                    "ARM devices (Raspberry Pi, ARM64)"
                }
            };
        }
    }
}
