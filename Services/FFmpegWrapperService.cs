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
        private readonly IMediaEncoder _mediaEncoder;
        private readonly string _pluginDirectory;

        public FFmpegWrapperService(
            ILogger<FFmpegWrapperService> logger,
            IPlatformDetectionService platformService,
            IServerConfigurationManager serverConfig,
            IMediaEncoder mediaEncoder)
        {
            _logger = logger;
            _platformService = platformService;
            _serverConfig = serverConfig;
            _mediaEncoder = mediaEncoder;
            _pluginDirectory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? string.Empty;
        }

        public async Task<string> GenerateWrapperScriptAsync()
        {
            var config = Plugin.Instance?.Configuration;
            var scriptExtension = _platformService.GetScriptExtension();
            var wrapperPath = Path.Combine(_pluginDirectory, $"upscale-wrapper{scriptExtension}");
            var psScriptPath = Path.Combine(_pluginDirectory, "upscale-logic.ps1");
            
            // Determine real FFmpeg path
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
                // On Windows, we define the Batch entrypoint AND the PowerShell logic script
                scriptContent = GenerateWindowsBatchScript(psScriptPath);
                
                // Generate the PowerShell logic script (handles path mapping & SSH)
                var psContent = GenerateWindowsPowerShellScript(realFFmpegPath, logPath, activeMarkerPath, config);
                await File.WriteAllTextAsync(psScriptPath, psContent);
            }
            else
            {
                // Unix - not yet fully refactored for SSH in this step (as requested focus is Windows)
                // But we keep existing logic + local fallback
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
            if (_platformService.IsWindows) _logger.LogInformation($"Generated PowerShell logic script at: {psScriptPath}");
            
            return wrapperPath;
        }

        private string GenerateWindowsBatchScript(string psScriptPath)
        {
            // Simple Batch wrapper that passes everything to PowerShell
            // We use -ExecutionPolicy Bypass to ensure it runs
            return $@"@echo off
pwsh -ExecutionPolicy Bypass -File ""{psScriptPath}"" %*
if %ERRORLEVEL% NEQ 0 exit /b %ERRORLEVEL%
";
        }

        private string GenerateWindowsPowerShellScript(string realFFmpegPath, string logPath, string activeMarkerPath, PluginConfiguration config)
        {
            bool enableRemote = config?.EnableRemoteTranscoding ?? false;
            string remoteUser = config?.RemoteUser ?? "root";
            string remoteHost = config?.RemoteHost ?? "localhost";
            int remotePort = config?.RemoteSshPort ?? 2222;
            string keyFile = config?.RemoteSshKeyFile ?? "";
            
            string localMount = config?.LocalMediaMountPoint?.Replace("\\", "\\\\") ?? "";
            string remoteMount = config?.RemoteMediaMountPoint ?? "";
            string transcodeDir = config?.RemoteTranscodePath ?? "/transcode";

            // If remote is disabled, fall back to simple local execution with logging
            if (!enableRemote)
            {
                 return $@"
$RealFFmpeg = ""{realFFmpegPath}""
$LogFile = ""{logPath}""
$ActiveMarker = ""{activeMarkerPath}""

if (-not (Test-Path $ActiveMarker)) {{
    & $RealFFmpeg @args
    exit $LASTEXITCODE
}}

Add-Content -Path $LogFile -Value ""[$(Get-Date)] Upscaler active (Local Mode)""
& $RealFFmpeg @args
exit $LASTEXITCODE
";
            }

            // Remote SSH Logic
            return $@"
$RealFFmpeg = ""{realFFmpegPath}""
$LogFile = ""{logPath}""
$ActiveMarker = ""{activeMarkerPath}""

# Configuration
$RemoteUser = ""{remoteUser}""
$RemoteHost = ""{remoteHost}""
$RemotePort = {remotePort}
$KeyFile = ""{keyFile}""
$LocalMount = ""{localMount}""
$RemoteMount = ""{remoteMount}""
$RemoteTranscodeDir = ""{transcodeDir}""

# If Upcaler is disabled via marker file, run local FFmpeg
if (-not (Test-Path $ActiveMarker)) {{
    & $RealFFmpeg @args
    exit $LASTEXITCODE
}}

try {{
    $CmdArgs = @()
    
    # Iterate arguments for Path Mapping
    for ($i = 0; $i -lt $args.Count; $i++) {{
        $arg = $args[$i]
        
        # Map Input Files (-i)
        if ($arg -eq '-i' -and ($i + 1) -lt $args.Count) {{
            $CmdArgs += '-i'
            $path = $args[$i+1]
            
            # Translate Path
            if ($path -like ""$LocalMount*"") {{
                $relPath = $path.Substring($LocalMount.Length).Replace('\', '/')
                if (-not $relPath.StartsWith('/')) {{ $relPath = '/' + $relPath }}
                $newPath = ""$RemoteMount$relPath""
                $CmdArgs += $newPath
                $i++ # Skip next arg
            }} else {{
                $CmdArgs += $path
                $i++
            }}
            continue
        }}

        # Map Transcode Directory (if present in arguments)
        # Jellyfin often passes fully qualified paths for output
        if ($arg -like ""$LocalMount*"") {{
             $relPath = $arg.Substring($LocalMount.Length).Replace('\', '/')
             if (-not $relPath.StartsWith('/')) {{ $relPath = '/' + $relPath }}
             $newPath = ""$RemoteMount$relPath""
             $CmdArgs += $newPath
             continue
        }}
        
        # Pass through other args
        $CmdArgs += $arg
    }}

    # Construct SSH Command
    # We use -o BatchMode=yes to fail fast if auth fails
    # We strictly map stderr to host stderr to keep Jellyfin informed
    
    $SshArgs = @(
        '-p', $RemotePort,
        '-o', 'BatchMode=yes',
        '-o', 'StrictHostKeyChecking=no',
        ""$RemoteUser@$RemoteHost""
    )
    
    if (-not [string]::IsNullOrWhiteSpace($KeyFile)) {{
        $SshArgs = @('-i', $KeyFile) + $SshArgs
    }}

    # The command to run inside Docker
    # We explicitly call the internal ffmpeg
    $RemoteCommand = ""ffmpeg "" + ($CmdArgs -join ' ')

    Add-Content -Path $LogFile -Value ""[$(Get-Date)] Remote Command: $RemoteCommand""

    # Execute SSH
    # 2>&1 ensures stderr is piped back
    & ssh $SshArgs $RemoteCommand
    exit $LASTEXITCODE

}} catch {{
    Add-Content -Path $LogFile -Value ""[$(Get-Date)] Error: $_""
    exit 1
}}
";
        }

        private string GenerateUnixScript(string realFFmpegPath, string logPath, string activeMarkerPath)
        {
            return $@"#!/bin/bash
# AI Upscaler Plugin - Unix Wrapper (Placeholder for future SSH update)
REAL_FFMPEG=""{realFFmpegPath}""
exec ""$REAL_FFMPEG"" ""$@""
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
