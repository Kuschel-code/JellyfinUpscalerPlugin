namespace Jellyfin.Plugin.LanguageSelector.Models;

public class MediaStreamInfo
{
    public int Index { get; set; }
    
    public string Type { get; set; } = string.Empty;
    
    public string? Language { get; set; }
    
    public string? Title { get; set; }
    
    public string? Codec { get; set; }
    
    public bool IsDefault { get; set; }
    
    public bool IsForced { get; set; }
}
