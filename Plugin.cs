using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin
{
    /// <summary>
    /// AI Upscaler Plugin for Jellyfin v1.5.4.3
    /// v1.5.4.3 - Multi-Frame Video Super-Resolution (EDVR-M x4)
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly IApplicationPaths _applicationPaths;
        private readonly ILogger<Plugin> _logger;

        /// <summary>
        /// Initializes a new instance of the Plugin class.
        /// </summary>
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _applicationPaths = applicationPaths;
            _logger = logger;

            // Inject player script into Jellyfin's index.html (like Intro Skipper plugin)
            InjectPlayerScriptWithFallback();
        }

        /// <summary>
        /// Gets the plugin name.
        /// </summary>
        public override string Name => "AI Upscaler Plugin";

        /// <summary>
        /// Gets the plugin description.
        /// </summary>
        public override string Description => "AI-powered video upscaling with multiple models and Player Integration";

        /// <summary>
        /// Gets the plugin GUID.
        /// </summary>
        public override Guid Id => Guid.Parse("f87f700e-679d-43e6-9c7c-b3a410dc3f22");

        /// <summary>
        /// Gets the static plugin instance.
        /// </summary>
        public static Plugin? Instance { get; private set; }



        /// <summary>
        /// Attempts to inject the player script into Jellyfin's index.html.
        /// Tries the primary web path first, then known Docker container paths.
        /// </summary>
        private void InjectPlayerScriptWithFallback()
        {
            var version = GetType().Assembly.GetName().Version;

            // Try primary path first, then known Docker paths
            var pathsToTry = new List<string>();

            if (!string.IsNullOrEmpty(_applicationPaths.WebPath))
            {
                pathsToTry.Add(_applicationPaths.WebPath);
            }

            // Known Docker container web paths
            pathsToTry.Add("/jellyfin/jellyfin-web");
            pathsToTry.Add("/usr/share/jellyfin/web");
            pathsToTry.Add("/usr/lib/jellyfin/bin/jellyfin-web");

            foreach (var webPath in pathsToTry)
            {
                try
                {
                    if (InjectPlayerScript(webPath, version))
                    {
                        _logger.LogInformation("AI Upscaler: Player script injected via {WebPath}", webPath);
                        return;
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning("AI Upscaler: Cannot write to {WebPath} (read-only): {Message}", webPath, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("AI Upscaler: Failed to inject at {WebPath}: {Message}", webPath, ex.Message);
                }
            }

            _logger.LogWarning(
                "AI Upscaler: Could not inject player script into index.html. " +
                "The player button will be activated when you visit the plugin config page. " +
                "Tried paths: {Paths}", string.Join(", ", pathsToTry));
        }

        /// <summary>
        /// Injects the AI Upscaler player script into a specific index.html.
        /// Returns true if injection succeeded or script already present.
        /// </summary>
        private bool InjectPlayerScript(string webPath, Version? version)
        {
            if (string.IsNullOrEmpty(webPath))
            {
                return false;
            }

            var indexPath = Path.Join(webPath, "index.html");
            if (!File.Exists(indexPath))
            {
                _logger.LogDebug("AI Upscaler: index.html not found at {Path}", indexPath);
                return false;
            }

            var contents = File.ReadAllText(indexPath);
            var scriptTag = $"<script src=\"configurationpage?name=UPSCALERPlayerIntegration&release={version}\"></script>";

            // Already injected with current version?
            if (contents.Contains(scriptTag, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("AI Upscaler: Player script already injected at {Path}", indexPath);
                return true;
            }

            // Remove old versions of our script tag (with regex timeout protection)
            var pattern = @"<script src=""configurationpage\?name=UPSCALERPlayerIntegration[^""]*""></script>";
            try
            {
                contents = Regex.Replace(contents, pattern, string.Empty, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            }
            catch (RegexMatchTimeoutException ex)
            {
                _logger.LogWarning(ex, "AI Upscaler: Regex timeout removing old script tag, proceeding");
            }

            // Inject before </head>
            try
            {
                var headEndRegex = new Regex(@"</head>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                contents = headEndRegex.Replace(contents, scriptTag + "</head>", 1);
            }
            catch (RegexMatchTimeoutException ex)
            {
                _logger.LogWarning(ex, "AI Upscaler: Regex timeout finding </head>, skipping injection");
                return false;
            }

            File.WriteAllText(indexPath, contents);
            return true;
        }

        /// <summary>
        /// Gets the plugin web pages for configuration.
        /// </summary>
        /// <returns>Collection of plugin pages.</returns>
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = this.Name,
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configurationpage.html",
                    EnableInMainMenu = true, // Ensure it appears in sidebar as well
                    DisplayName = "AI Upscaler Settings"
                },
                new PluginPageInfo
                {
                    Name = "UPSCALERPlayerIntegration",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.player-integration.js"
                },
                new PluginPageInfo
                {
                    Name = "UPSCALERQuickMenu",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.quick-menu.js"
                },
                new PluginPageInfo
                {
                    Name = "UPSCALERSidebarIntegration",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.sidebar-upscaler.js"
                },
                new PluginPageInfo
                {
                    Name = "UPSCALERWebGLShader",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.webgl-upscaler.js"
                }
            };
        }
    }
}
