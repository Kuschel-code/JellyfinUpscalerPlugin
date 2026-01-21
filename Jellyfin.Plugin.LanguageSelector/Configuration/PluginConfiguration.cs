using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LanguageSelector.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool EnableDebugLogging { get; set; } = false;
    
    public bool AutoDetectLanguages { get; set; } = true;
    
    public string[] PreferredLanguages { get; set; } = new[] { "ger", "jpn", "eng" };
}
