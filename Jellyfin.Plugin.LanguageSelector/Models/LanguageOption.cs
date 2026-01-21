using System.Collections.Generic;

namespace Jellyfin.Plugin.LanguageSelector.Models;

public class LanguageOption
{
    public string Id { get; set; } = string.Empty;
    
    public string DisplayName { get; set; } = string.Empty;
    
    public string FlagIcon { get; set; } = string.Empty;
    
    public int AudioStreamIndex { get; set; }
    
    public int? SubtitleStreamIndex { get; set; }
    
    public string AudioLanguage { get; set; } = string.Empty;
    
    public string? SubtitleLanguage { get; set; }
    
    public bool IsDefault { get; set; }
}

public class LanguageOptionsResponse
{
    public List<LanguageOption> Options { get; set; } = new();
    
    public string ItemId { get; set; } = string.Empty;
    
    public string ItemName { get; set; } = string.Empty;
}
