using System;
using System.Collections.Generic;
using Jellyfin.Plugin.LanguageSelector.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LanguageSelector;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Language Selector";

    public override Guid Id => Guid.Parse("d4c4a3e2-9b7a-4f5c-8e1d-2a3b4c5d6e7f");

    public override string Description => "One-click language selection for anime and media playback";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;
        
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = ns + ".Configuration.config.html"
            },
            new PluginPageInfo
            {
                Name = "LanguageSelector/language-selector.js",
                EmbeddedResourcePath = ns + ".Web.language-selector.js"
            },
            new PluginPageInfo
            {
                Name = "LanguageSelector/language-selector.css",
                EmbeddedResourcePath = ns + ".Web.language-selector.css"
            },
            new PluginPageInfo
            {
                Name = "LanguageSelector/flags/de.svg",
                EmbeddedResourcePath = ns + ".Web.flags.de.svg"
            },
            new PluginPageInfo
            {
                Name = "LanguageSelector/flags/us.svg",
                EmbeddedResourcePath = ns + ".Web.flags.us.svg"
            },
            new PluginPageInfo
            {
                Name = "LanguageSelector/flags/jp.svg",
                EmbeddedResourcePath = ns + ".Web.flags.jp.svg"
            },
            new PluginPageInfo
            {
                Name = "LanguageSelector/flags/jp-de.svg",
                EmbeddedResourcePath = ns + ".Web.flags.jp-de.svg"
            },
            new PluginPageInfo
            {
                Name = "LanguageSelector/flags/jp-en.svg",
                EmbeddedResourcePath = ns + ".Web.flags.jp-en.svg"
            }
        };
    }
}
