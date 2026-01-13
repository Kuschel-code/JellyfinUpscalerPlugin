using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Dlna;
using JellyfinUpscalerPlugin.Models;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Transcoding profile manager for AI upscaling
    /// Injects custom FFmpeg arguments via Device Profiles
    /// </summary>
    public class TranscodingProfileManager
    {
        private readonly ILogger<TranscodingProfileManager> _logger;
        private readonly UpscalerCore _upscalerCore;
        
        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public TranscodingProfileManager(
            ILogger<TranscodingProfileManager> logger,
            UpscalerCore upscalerCore)
        {
            _logger = logger;
            _upscalerCore = upscalerCore;
            
            _logger.LogInformation("🎬 TranscodingProfileManager initialized for AI upscaling");
        }

        /// <summary>
        /// Build custom FFmpeg arguments for upscaling
        /// </summary>
        public string BuildUpscaleArguments(HardwareProfile hardware, int scaleFactor = 2)
        {
            var method = DetermineUpscaleMethod(hardware);
            var filter = method switch
            {
                "NVIDIA_VSR" => $"hwupload_cuda,scale_cuda={scaleFactor}*iw:{scaleFactor}*ih:interp_algo=lanczos,unsharp_cuda=luma_amount=1.5,hwdownload,format=nv12",
                "AMD_FSR" => $"libplacebo=w={scaleFactor}*iw:h={scaleFactor}*ih:upscaler=ewa_lanczos",
                "ANIME4K" => $"libplacebo=w={scaleFactor}*iw:h={scaleFactor}*ih:upscaler=ewa_lanczos",
                _ => $"scale={scaleFactor}*iw:{scaleFactor}*ih:flags=lanczos,unsharp=5:5:1.0:5:5:0.0"
            };
            
            return $"-vf \"{filter}\"";
        }

        private string DetermineUpscaleMethod(HardwareProfile hardware)
        {
            if (hardware.SupportsCUDA && hardware.GpuName?.Contains("RTX") == true)
            {
                return "NVIDIA_VSR";
            }
            
            if (hardware.GpuName?.Contains("AMD") == true || hardware.GpuName?.Contains("Radeon") == true)
            {
                return "AMD_FSR";
            }
            
            return "LANCZOS";
        }
    }
}
