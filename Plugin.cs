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
    /// AI Upscaler Plugin for Jellyfin v1.5.2.7 - Docker Microservice Architecture
    /// v1.5.2.7 - Fixed notification spam, English UI, dark theme, XSS fixes
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly IApplicationPaths _applicationPaths;

        /// <summary>
        /// Initializes a new instance of the Plugin class.
        /// </summary>
        /// <param name="applicationPaths">Application paths.</param>
        /// <param name="xmlSerializer">XML serializer.</param>
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _applicationPaths = applicationPaths;

            // Inject player script into Jellyfin's index.html (like Intro Skipper plugin)
            try
            {
                InjectPlayerScript(applicationPaths.WebPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AI Upscaler: Failed to inject player script: {ex.Message}");
            }
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
        /// Injects the AI Upscaler player script into Jellyfin's index.html.
        /// This ensures the script loads on every page, not just the config page.
        /// Same approach as the Intro Skipper plugin.
        /// </summary>
        private void InjectPlayerScript(string webPath)
        {
            if (string.IsNullOrEmpty(webPath))
            {
                return;
            }

            var indexPath = Path.Join(webPath, "index.html");
            if (!File.Exists(indexPath))
            {
                return;
            }

            var contents = File.ReadAllText(indexPath);
            var version = GetType().Assembly.GetName().Version;

            // Script tag that loads our player integration JS globally
            var scriptTag = $"<script src=\"configurationpage?name=UPSCALERPlayerIntegration&release={version}\"></script>";

            // Already injected?
            if (contents.Contains(scriptTag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Remove old versions of our script tag (if version changed) - Singleline so .*? matches across newlines
            var pattern = @"<script src=""configurationpage\?name=UPSCALERPlayerIntegration.*?</script>";
            contents = Regex.Replace(contents, pattern, string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Inject before </head>
            var headEndRegex = new Regex(@"</head>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            contents = headEndRegex.Replace(contents, scriptTag + "</head>", 1);

            File.WriteAllText(indexPath, contents);
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
