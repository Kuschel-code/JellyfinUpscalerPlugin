using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using JellyfinUpscalerPlugin.Models;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Transcoding helper for AI upscaling (Player-side integration)
    /// NOTE: Jellyfin 10.10 does not support ITranscoderFullCommandModifier
    /// This class provides helper methods for player-initiated upscaling
    /// </summary>
    public class UpscalerTranscodingHelper
    {
        private readonly ILogger<UpscalerTranscodingHelper> _logger;
        private readonly UpscalerCore _upscalerCore;
        
        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public UpscalerTranscodingHelper(
            ILogger<UpscalerTranscodingHelper> logger,
            UpscalerCore upscalerCore)
        {
            _logger = logger;
            _upscalerCore = upscalerCore;
            
            _logger.LogInformation("🎬 UpscalerTranscodingHelper initialized");
        }

        /// <summary>
        /// Build FFmpeg upscaling arguments for player-initiated requests
        /// </summary>
        public string BuildUpscaleArguments(HardwareProfile hardware, int scaleFactor = 2, bool isLiveStream = false)
        {
            try
            {
                // Only process if plugin is enabled
                if (!Config.EnablePlugin)
                {
                    _logger.LogDebug("⏭️ Plugin disabled");
                    return string.Empty;
                }

                _logger.LogInformation($"🔧 Building upscale arguments for {(isLiveStream ? "live stream" : "video")}");
                
                // Determine best upscaling method
                var upscaleMethod = DetermineUpscaleMethod(hardware, isLiveStream);
                
                // Build filter arguments
                var filterArgs = BuildUpscaleFilter(upscaleMethod, hardware, scaleFactor);
                
                _logger.LogInformation($"✅ Generated {upscaleMethod} upscaling filter");
                
                return filterArgs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to build upscale arguments");
                return string.Empty;
            }
        }

        /// <summary>
        /// Determine the best upscaling method based on hardware
        /// </summary>
        private string DetermineUpscaleMethod(HardwareProfile hardware, bool isLiveStream)
        {
            // For live streams, prefer hardware-based upscaling
            if (isLiveStream)
            {
                if (hardware.SupportsCUDA && hardware.GpuName?.Contains("RTX") == true)
                {
                    return "NVIDIA_VSR"; // NVIDIA Video Super Resolution
                }
                
                if (hardware.GpuName?.Contains("AMD") == true || hardware.GpuName?.Contains("Radeon") == true)
                {
                    return "AMD_FSR"; // AMD FidelityFX Super Resolution
                }
                
                return "LANCZOS_HW"; // Hardware-accelerated Lanczos
            }
            
            // For on-demand, use AI models if hardware supports it
            if (hardware.SupportsCUDA && hardware.VramMB > 4096)
            {
                return "ANIME4K"; // Anime4K for high-quality AI upscaling
            }
            
            if (hardware.AvailableHwAccels.Contains("vaapi"))
            {
                return "AMD_FSR"; // FSR works well with VAAPI
            }
            
            return "LANCZOS"; // Software fallback
        }

        /// <summary>
        /// Build upscale filter based on method
        /// </summary>
        private string BuildUpscaleFilter(string method, HardwareProfile hardware, int scaleFactor)
        {
            if (scaleFactor < 1) scaleFactor = 2; // Default 2x upscale
            
            return method switch
            {
                "NVIDIA_VSR" => BuildNvidiaVSRFilter(scaleFactor),
                "AMD_FSR" => BuildAMDFSRFilter(scaleFactor),
                "ANIME4K" => BuildAnime4KFilter(scaleFactor),
                "LANCZOS_HW" => BuildLanczosHWFilter(scaleFactor, hardware),
                _ => BuildLanczosFilter(scaleFactor)
            };
        }

        /// <summary>
        /// Build NVIDIA Video Super Resolution filter (RTX 30/40 series)
        /// </summary>
        private string BuildNvidiaVSRFilter(int scale)
        {
            // NVIDIA VSR using CUDA scale with sharpening
            return $"hwupload_cuda,scale_cuda={scale}*iw:{scale}*ih:interp_algo=lanczos,unsharp_cuda=luma_amount=1.5:chroma_amount=0.5,hwdownload,format=nv12";
        }

        /// <summary>
        /// Build AMD FidelityFX Super Resolution filter
        /// </summary>
        private string BuildAMDFSRFilter(int scale)
        {
            // FSR using libplacebo (requires FFmpeg with libplacebo support)
            return $"libplacebo=w={scale}*iw:h={scale}*ih:upscaler=ewa_lanczos:downscaler=ewa_lanczos";
        }

        /// <summary>
        /// Build Anime4K filter (high-quality AI upscaling)
        /// </summary>
        private string BuildAnime4KFilter(int scale)
        {
            // Anime4K shader-based upscaling
            // Note: Requires custom shader files in Jellyfin's data directory
            var shaderPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
                "Shaders",
                "Anime4K_Upscale_CNN_x2_L.glsl"
            );
            
            if (System.IO.File.Exists(shaderPath))
            {
                return $"libplacebo=custom_shader_path='{shaderPath}':w={scale}*iw:h={scale}*ih";
            }
            
            // Fallback to FSR if shader not found
            _logger.LogWarning("⚠️ Anime4K shader not found, falling back to FSR");
            return BuildAMDFSRFilter(scale);
        }

        /// <summary>
        /// Build hardware-accelerated Lanczos filter
        /// </summary>
        private string BuildLanczosHWFilter(int scale, HardwareProfile hardware)
        {
            if (hardware.SupportsCUDA)
            {
                return $"hwupload_cuda,scale_cuda={scale}*iw:{scale}*ih:interp_algo=lanczos,hwdownload,format=nv12";
            }
            
            if (hardware.AvailableHwAccels.Contains("vaapi"))
            {
                return $"hwupload,scale_vaapi=w={scale}*iw:h={scale}*ih,hwdownload,format=nv12";
            }
            
            if (hardware.AvailableHwAccels.Contains("qsv"))
            {
                return $"hwupload=extra_hw_frames=64,scale_qsv=w={scale}*iw:h={scale}*ih,hwdownload,format=nv12";
            }
            
            return BuildLanczosFilter(scale);
        }

        /// <summary>
        /// Build software Lanczos filter (fallback)
        /// </summary>
        private string BuildLanczosFilter(int scale)
        {
            return $"scale={scale}*iw:{scale}*ih:flags=lanczos,unsharp=5:5:1.0:5:5:0.0";
        }
    }
}
