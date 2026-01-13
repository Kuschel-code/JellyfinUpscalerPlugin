using System.Threading.Tasks;
using JellyfinUpscalerPlugin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Api
{
    [ApiController]
    [Authorize(Policy = "RequiresElevation")]
    [Route("api/upscaler/wrapper")]
    public class FFmpegWrapperController : ControllerBase
    {
        private readonly ILogger<FFmpegWrapperController> _logger;
        private readonly IFFmpegWrapperService _wrapperService;
        private readonly IPlatformDetectionService _platformService;

        public FFmpegWrapperController(
            ILogger<FFmpegWrapperController> logger,
            IFFmpegWrapperService wrapperService,
            IPlatformDetectionService platformService)
        {
            _logger = logger;
            _wrapperService = wrapperService;
            _platformService = platformService;
        }

        [HttpGet("status")]
        public ActionResult<WrapperStatusResponse> GetStatus()
        {
            var isInstalled = _wrapperService.IsWrapperInstalled();
            var wrapperPath = _wrapperService.GetWrapperPath();

            return Ok(new WrapperStatusResponse
            {
                IsInstalled = isInstalled,
                WrapperPath = wrapperPath,
                Platform = _platformService.CurrentPlatform.ToString(),
                RuntimeIdentifier = _platformService.RuntimeIdentifier
            });
        }

        [HttpPost("install")]
        public async Task<ActionResult<WrapperInstallResponse>> Install()
        {
            _logger.LogInformation("Installing FFmpeg wrapper");

            var success = await _wrapperService.InstallWrapperAsync();

            if (!success)
            {
                return StatusCode(500, new WrapperInstallResponse
                {
                    Success = false,
                    Message = "Failed to install FFmpeg wrapper"
                });
            }

            var wrapperPath = _wrapperService.GetWrapperPath();

            return Ok(new WrapperInstallResponse
            {
                Success = true,
                WrapperPath = wrapperPath,
                Message = $"FFmpeg wrapper installed successfully. Please update Jellyfin's FFmpeg path to: {wrapperPath}"
            });
        }

        [HttpPost("uninstall")]
        public async Task<ActionResult<WrapperInstallResponse>> Uninstall()
        {
            _logger.LogInformation("Uninstalling FFmpeg wrapper");

            var success = await _wrapperService.UninstallWrapperAsync();

            if (!success)
            {
                return StatusCode(500, new WrapperInstallResponse
                {
                    Success = false,
                    Message = "Failed to uninstall FFmpeg wrapper"
                });
            }

            return Ok(new WrapperInstallResponse
            {
                Success = true,
                Message = "FFmpeg wrapper uninstalled successfully. Restore original FFmpeg path in Jellyfin settings."
            });
        }

        [HttpPost("regenerate")]
        public async Task<ActionResult<WrapperInstallResponse>> Regenerate()
        {
            _logger.LogInformation("Regenerating FFmpeg wrapper script");

            var wrapperPath = await _wrapperService.GenerateWrapperScriptAsync();

            return Ok(new WrapperInstallResponse
            {
                Success = true,
                WrapperPath = wrapperPath,
                Message = "FFmpeg wrapper script regenerated successfully"
            });
        }
    }

    public class WrapperStatusResponse
    {
        public bool IsInstalled { get; set; }
        public string WrapperPath { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string RuntimeIdentifier { get; set; } = string.Empty;
    }

    public class WrapperInstallResponse
    {
        public bool Success { get; set; }
        public string? WrapperPath { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
