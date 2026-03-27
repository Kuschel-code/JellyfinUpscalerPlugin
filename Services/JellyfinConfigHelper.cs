using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Helper service to automatically configure Jellyfin to use FFmpeg wrapper
    /// Modifies encoding.xml to inject upscaling wrapper
    /// </summary>
    public class JellyfinConfigHelper
    {
        private readonly ILogger<JellyfinConfigHelper> _logger;

        public JellyfinConfigHelper(ILogger<JellyfinConfigHelper> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Setup FFmpeg wrapper in Jellyfin configuration
        /// </summary>
        public void SetupFFmpegWrapper()
        {
            try
            {
                _logger.LogInformation("Attempting to configure FFmpeg wrapper...");

                // Locate Jellyfin config directory
                var configDir = FindJellyfinConfigDir();
                if (string.IsNullOrEmpty(configDir))
                {
                    _logger.LogWarning("Could not locate Jellyfin config directory");
                    CreateWrapperInstructionsFile();
                    return;
                }

                var encodingXmlPath = Path.Combine(configDir, "encoding.xml");
                
                // Deploy wrapper scripts
                var wrapperPath = DeployWrapperScripts(configDir);
                if (string.IsNullOrEmpty(wrapperPath))
                {
                    _logger.LogWarning("Failed to deploy wrapper scripts");
                    return;
                }

                // Update encoding.xml if it exists
                if (File.Exists(encodingXmlPath))
                {
                    UpdateEncodingXml(encodingXmlPath, wrapperPath);
                }
                else
                {
                    CreateEncodingXml(encodingXmlPath, wrapperPath);
                }

                _logger.LogInformation("FFmpeg wrapper configured at: {WrapperPath}", wrapperPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to setup FFmpeg wrapper");
                CreateWrapperInstructionsFile();
            }
        }

        /// <summary>
        /// Find Jellyfin configuration directory
        /// </summary>
        private string? FindJellyfinConfigDir()
        {
            var possiblePaths = new[]
            {
                Environment.GetEnvironmentVariable("JELLYFIN_CONFIG_DIR"),
                "/etc/jellyfin",
                "/config",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Jellyfin", "Server"),
                Path.Combine(Environment.GetEnvironmentVariable("ProgramData") ?? "C:\\ProgramData", "Jellyfin", "Server"),
                "/var/lib/jellyfin"
            };

            foreach (var path in possiblePaths)
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    _logger.LogInformation("Found Jellyfin config directory: {ConfigPath}", path);
                    return path;
                }
            }

            return null;
        }

        /// <summary>
        /// Deploy wrapper scripts to Jellyfin directory
        /// </summary>
        private string? DeployWrapperScripts(string configDir)
        {
            try
            {
                var pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(pluginDir))
                {
                    return null;
                }

                var wrapperDir = Path.Combine(configDir, "upscaler-wrapper");
                Directory.CreateDirectory(wrapperDir);

                var isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
                var sourceWrapper = Path.Combine(pluginDir, "Scripts", isWindows ? "upscale-ffmpeg.bat" : "upscale-ffmpeg.sh");
                var targetWrapper = Path.Combine(wrapperDir, isWindows ? "upscale-ffmpeg.bat" : "upscale-ffmpeg.sh");

                if (File.Exists(sourceWrapper))
                {
                    File.Copy(sourceWrapper, targetWrapper, true);

                    // Make executable on Unix
                    if (!isWindows)
                    {
                        try
                        {
                            var chmodInfo = new System.Diagnostics.ProcessStartInfo("chmod") { UseShellExecute = false };
                            chmodInfo.ArgumentList.Add("+x");
                            chmodInfo.ArgumentList.Add(targetWrapper);
                            using var chmodProc = System.Diagnostics.Process.Start(chmodInfo);
                            chmodProc?.WaitForExit(5000);
                        }
                        catch (Exception ex) { _logger.LogDebug(ex, "Failed to set executable permission on wrapper script"); }
                    }

                    _logger.LogInformation("Deployed wrapper script to: {TargetWrapper}", targetWrapper);
                    return targetWrapper;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deploy wrapper scripts");
                return null;
            }
        }

        /// <summary>
        /// Update existing encoding.xml with wrapper path
        /// </summary>
        private void UpdateEncodingXml(string xmlPath, string wrapperPath)
        {
            try
            {
                var xml = File.ReadAllText(xmlPath);

                // Simple XML replacement (not using XmlDocument to avoid dependencies)
                if (xml.Contains("<EncoderAppPath>"))
                {
                    xml = System.Text.RegularExpressions.Regex.Replace(
                        xml,
                        "<EncoderAppPath>.*?</EncoderAppPath>",
                        $"<EncoderAppPath>{wrapperPath}</EncoderAppPath>"
                    );
                }
                else
                {
                    xml = xml.Replace("</EncodingOptions>", $"  <EncoderAppPath>{wrapperPath}</EncoderAppPath>\n</EncodingOptions>");
                }

                File.WriteAllText(xmlPath, xml);
                _logger.LogInformation("Updated encoding.xml with wrapper path");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update encoding.xml");
            }
        }

        /// <summary>
        /// Create new encoding.xml with wrapper configuration
        /// </summary>
        private void CreateEncodingXml(string xmlPath, string wrapperPath)
        {
            try
            {
                var xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<EncodingOptions xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <EncoderAppPath>{wrapperPath}</EncoderAppPath>
  <TranscodingTempPath></TranscodingTempPath>
  <FallbackFontPath></FallbackFontPath>
  <EnableHardwareEncoding>true</EnableHardwareEncoding>
  <EnableSubtitleExtraction>true</EnableSubtitleExtraction>
  <HardwareAccelerationType></HardwareAccelerationType>
</EncodingOptions>";

                File.WriteAllText(xmlPath, xml);
                _logger.LogInformation("Created encoding.xml with wrapper path");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create encoding.xml");
            }
        }

        /// <summary>
        /// Create manual installation instructions file
        /// </summary>
        private void CreateWrapperInstructionsFile()
        {
            try
            {
                var pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(pluginDir))
                {
                    return;
                }

                var instructionsPath = Path.Combine(pluginDir, "FFMPEG_WRAPPER_SETUP.txt");
                var instructions = @"AI Upscaler FFmpeg Wrapper - Manual Setup Instructions
=================================================================

AUTOMATIC SETUP FAILED - Please configure manually:

1. Locate your wrapper script:
   - Windows: [PluginDir]\Scripts\upscale-ffmpeg.bat
   - Linux: [PluginDir]/Scripts/upscale-ffmpeg.sh

2. Edit the wrapper script:
   - Set REAL_FFMPEG_PATH to your actual ffmpeg location
   - Windows: Usually C:\ProgramData\Jellyfin\Server\ffmpeg.exe
   - Linux: Usually /usr/bin/ffmpeg

3. Configure Jellyfin:
   - Go to Dashboard → Playback → Transcoding
   - Set FFmpeg path to the wrapper script location
   - Restart Jellyfin

4. Verify:
   - Start a transcoding session
   - Check upscaler-wrapper.log for activity
   - Monitor Jellyfin logs for upscaling messages

For support, visit: https://github.com/Kuschel-code/JellyfinUpscalerPlugin
";

                File.WriteAllText(instructionsPath, instructions);
                _logger.LogInformation("Created setup instructions at: {InstructionsPath}", instructionsPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create instructions file");
            }
        }
    }
}
