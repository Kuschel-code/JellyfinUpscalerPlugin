using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using JellyfinUpscalerPlugin.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin
{
    /// <summary>
    /// AI Upscaler Plugin for Jellyfin v1.5.5.8
    /// v1.5.5.8 - Deep scan fixes: targeted library refresh, FFmpeg injection fix, concurrency hardening
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
        /// v1.8.3.35 - when the file cannot be written, <see cref="PlayerScriptMiddleware"/>
        /// adds the tag to the page as Jellyfin serves it, so a read-only web folder is no
        /// longer a problem and not reported as one.
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
                    _logger.LogDebug("AI Upscaler: Cannot write to {WebPath} (read-only): {Message}", webPath, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("AI Upscaler: Failed to inject at {WebPath}: {Message}", webPath, ex.Message);
                }
            }

            _logger.LogInformation(
                "AI Upscaler: index.html is not writable ({Paths}); the player script is added to the page as Jellyfin serves it",
                string.Join(", ", pathsToTry));
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
            string injected;
            try
            {
                if (!PlayerScriptTag.TryInject(contents, PlayerScriptTag.For(version), out injected))
                {
                    _logger.LogWarning("AI Upscaler: No </head> in {Path}, skipping injection", indexPath);
                    return false;
                }
            }
            catch (RegexMatchTimeoutException ex)
            {
                _logger.LogWarning(ex, "AI Upscaler: Regex timeout injecting into {Path}, skipping injection", indexPath);
                return false;
            }

            // Already injected with current version?
            if (ReferenceEquals(injected, contents))
            {
                _logger.LogDebug("AI Upscaler: Player script already injected at {Path}", indexPath);
                return true;
            }

            File.WriteAllText(indexPath, injected);
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
                },
                // v1.7.1 - WebGPU + ONNX Runtime Web realtime AI upscaler page.
                // Loaded lazily by player-integration.js when RealtimeMode === 'ai-webgpu'.
                new PluginPageInfo
                {
                    Name = "UPSCALERWebGPUAI",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.webgpu-ai-realtime.js"
                },
                // v1.7.9 - vendored, tree-shaken Anime4K.js (WebGL) for the anime tier.
                // Loaded lazily by player-integration.js when RealtimeMode is anime4k.
                new PluginPageInfo
                {
                    Name = "UPSCALERAnime4K",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.anime4k.js"
                },
                // v1.8.3.20 - i18n groundwork. English is the SOURCE language here: the
                // catalogue is where these strings live, not a translation of them.
                // Adding a locale means shipping a second json and pointing the loader
                // at it; no UI code changes.
                new PluginPageInfo
                {
                    Name = "UPSCALERI18n",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.i18n.js"
                },
                new PluginPageInfo
                {
                    Name = "UPSCALERStringsEn",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.strings.en.json"
                }
            };
        }
    }
}
