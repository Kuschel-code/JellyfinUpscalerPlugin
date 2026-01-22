using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;

namespace JellyfinUpscalerPlugin.Services
{
    public interface IFFmpegWrapperService
    {
        Task<string> GenerateWrapperScriptAsync();
        Task<bool> InstallWrapperAsync();
        Task<bool> UninstallWrapperAsync();
        bool IsWrapperInstalled();
        string GetWrapperPath();
    }

    public class FFmpegWrapperService : IFFmpegWrapperService
    {
        private readonly ILogger<FFmpegWrapperService> _logger;
        private readonly IPlatformDetectionService _platformService;
        private readonly IServerConfigurationManager _serverConfig;
        private readonly IMediaEncoder _mediaEncoder; // Added
        private readonly string _pluginDirectory;

        public FFmpegWrapperService(
            ILogger<FFmpegWrapperService> logger,
            IPlatformDetectionService platformService,
            IServerConfigurationManager serverConfig,
            IMediaEncoder mediaEncoder) // Added
        {
            _logger = logger;
            _platformService = platformService;
            _serverConfig = serverConfig;
            _mediaEncoder = mediaEncoder; // Added
            _pluginDirectory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? string.Empty;
        }

        public async Task<string> GenerateWrapperScriptAsync()
        {
            var scriptExtension = _platformService.GetScriptExtension();
            var wrapperPath = Path.Combine(_pluginDirectory, $"upscale-wrapper{scriptExtension}");
            
            // Use MediaEncoder path if available, or fallback
            var realFFmpegPath = _mediaEncoder.EncoderPath;
            if (string.IsNullOrEmpty(realFFmpegPath))
            {
                 realFFmpegPath = _platformService.IsWindows ? "C:\\ProgramData\\Jellyfin\\Server\\ffmpeg.exe" : "/usr/lib/jellyfin-ffmpeg/ffmpeg";
            }
            
            var logPath = Path.Combine(_pluginDirectory, "wrapper.log");
            var activeMarkerPath = Path.Combine(_pluginDirectory, "wrapper_active");

            string scriptContent;

            if (_platformService.IsWindows)
            {
                scriptContent = GenerateWindowsScript(realFFmpegPath, logPath, activeMarkerPath);
            }
            else
            {
                scriptContent = GenerateUnixScript(realFFmpegPath, logPath, activeMarkerPath);
            }

            await File.WriteAllTextAsync(wrapperPath, scriptContent);

            if (!_platformService.IsWindows)
            {
                try
                {
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{wrapperPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    await process.WaitForExitAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to set executable permission on wrapper script");
                }
            }

            _logger.LogInformation($"Generated FFmpeg wrapper script at: {wrapperPath}");
            return wrapperPath;
        }

        private string GenerateWindowsScript(string realFFmpegPath, string logPath, string activeMarkerPath)
        {
            return $@"@echo off
REM AI Upscaler Plugin - FFmpeg Wrapper for Windows
REM This script intercepts FFmpeg calls and injects hardware upscaling filters

set REAL_FFMPEG=""{realFFmpegPath}""
set LOG_FILE=""{logPath}""
set ACTIVE_MARKER=""{activeMarkerPath}""

REM Check if upscaler is active
if not exist %ACTIVE_MARKER% (
    REM Upscaler disabled - pass through to real FFmpeg
    %REAL_FFMPEG% %*
    exit /b %ERRORLEVEL%
)

REM Upscaler enabled - log and modify command
echo [%date% %time%] Upscaler active: Intercepting FFmpeg call >> %LOG_FILE%

REM Simple filter injection logic for demo purposes
REM In a real scenario, we would parse arguments more robustly
set ""ARGS=%*""

REM Check for video filter flag
echo %ARGS% | findstr /C:""-vf"" >nul
if %errorlevel%==0 (
    REM Inject scale_cuda if not present (simplified)
    set ""ARGS=%ARGS:-vf ""scale=-1:-1""=-vf ""hwupload_cuda,scale_cuda=-1:-1,hwdownload,format=yuv420p""""%""
)

%REAL_FFMPEG% %ARGS%
exit /b %ERRORLEVEL%
";
        }

        private string GenerateUnixScript(string realFFmpegPath, string logPath, string activeMarkerPath)
        {
            return $@"#!/bin/bash
# AI Upscaler Plugin - FFmpeg Wrapper for Linux/macOS
# This script intercepts FFmpeg calls and injects hardware upscaling filters

REAL_FFMPEG=""{realFFmpegPath}""
LOG_FILE=""{logPath}""
ACTIVE_MARKER=""{activeMarkerPath}""

# Check if upscaler is active
if [ ! -f ""$ACTIVE_MARKER"" ]; then
    # Upscaler disabled - pass through to real FFmpeg
    exec ""$REAL_FFMPEG"" ""$@""
fi

# Upscaler enabled - log and modify command
echo ""[$(date)] Upscaler active: Intercepting FFmpeg call"" >> ""$LOG_FILE""

# Argument injection logic
# Iterate over arguments to act as a proxy and inject filters
# This is a complex task in bash, simplified here for demonstration

ARGS=()
for arg in ""$@""; do
    if [[ ""$arg"" == ""-vf"" ]]; then
        ARGS+=(""-vf"")
        continue
    fi
    
    # Check if this is a filter chain that needs modification
    if [[ ""$arg"" == *""scale=""* ]]; then
        # Inject CUDA scaling (example)
        NEW_FILTER=""$(echo ""$arg"" | sed 's/scale=\([0-9]*:[0-9]*\)/hwupload_cuda,scale_cuda=\1,hwdownload,format=yuv420p/')""
        ARGS+=(""$NEW_FILTER"")
    else
        ARGS+=(""$arg"")
    fi
done

exec ""$REAL_FFMPEG"" ""${{ARGS[@]}}""
";
        }

        public async Task<bool> InstallWrapperAsync()
        {
            try
            {
                var wrapperPath = await GenerateWrapperScriptAsync();
                
                var activeMarkerPath = Path.Combine(_pluginDirectory, "wrapper_active");
                await File.WriteAllTextAsync(activeMarkerPath, DateTime.UtcNow.ToString("O"));

                _logger.LogInformation($"FFmpeg wrapper installed at: {wrapperPath}");
                _logger.LogInformation("IMPORTANT: Update Jellyfin's FFmpeg path to: " + wrapperPath);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to install FFmpeg wrapper");
                return false;
            }
        }

        public async Task<bool> UninstallWrapperAsync()
        {
            try
            {
                var activeMarkerPath = Path.Combine(_pluginDirectory, "wrapper_active");
                
                if (File.Exists(activeMarkerPath))
                {
                    File.Delete(activeMarkerPath);
                }

                _logger.LogInformation("FFmpeg wrapper uninstalled (marker file removed)");
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to uninstall FFmpeg wrapper");
                return false;
            }
        }

        public bool IsWrapperInstalled()
        {
            var activeMarkerPath = Path.Combine(_pluginDirectory, "wrapper_active");
            return File.Exists(activeMarkerPath);
        }

        public string GetWrapperPath()
        {
            var scriptExtension = _platformService.GetScriptExtension();
            return Path.Combine(_pluginDirectory, $"upscale-wrapper{scriptExtension}");
        }
    }
}
