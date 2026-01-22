using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellyfinUpscalerPlugin
{
    /// <summary>
    /// AI Upscaler Plugin for Jellyfin v1.4.8 - Stable Update with Modern UI & Sync
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <summary>
        /// Initializes a new instance of the Plugin class.
        /// </summary>
        /// <param name="applicationPaths">Application paths.</param>
        /// <param name="xmlSerializer">XML serializer.</param>
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
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
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configurationpage.html"
                },
                new PluginPageInfo
                {
                    Name = "AI Upscaler Dashboard",
                    EmbeddedResourcePath = GetType().Namespace + ".Pages.dashboard.html",
                    EnableInMainMenu = true,
                    MenuSection = "server",
                    MenuIcon = "trending_up",
                    DisplayName = "AI Upscaler"
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
