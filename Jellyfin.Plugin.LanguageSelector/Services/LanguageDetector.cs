using System.Collections.Generic;

namespace Jellyfin.Plugin.LanguageSelector.Services;

public class LanguageDetector
{
    private static readonly Dictionary<string, string> LanguageCodeMap = new()
    {
        { "ger", "de" },
        { "deu", "de" },
        { "de", "de" },
        { "jpn", "jp" },
        { "ja", "jp" },
        { "jp", "jp" },
        { "eng", "us" },
        { "en", "us" },
        { "us", "us" }
    };
    
    private static readonly Dictionary<string, string> LanguageNames = new()
    {
        { "de", "German" },
        { "jp", "Japanese" },
        { "us", "English" }
    };
    
    public string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "unknown";
        }
        
        var normalized = languageCode.ToLowerInvariant().Trim();
        return LanguageCodeMap.TryGetValue(normalized, out var code) ? code : normalized;
    }
    
    public string GetLanguageName(string languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        return LanguageNames.TryGetValue(normalized, out var name) ? name : normalized;
    }
    
    public string GetFlagIcon(string? audioLanguage, string? subtitleLanguage)
    {
        var audioLang = NormalizeLanguageCode(audioLanguage);
        var subLang = NormalizeLanguageCode(subtitleLanguage);
        
        if (string.IsNullOrEmpty(subtitleLanguage))
        {
            return audioLang;
        }
        
        if (audioLang == "jp" && subLang == "de")
        {
            return "jp-de";
        }
        
        if (audioLang == "jp" && subLang == "us")
        {
            return "jp-us";
        }
        
        return $"{audioLang}-{subLang}";
    }
    
    public string GetDisplayName(string? audioLanguage, string? subtitleLanguage)
    {
        var audioName = GetLanguageName(audioLanguage ?? "unknown");
        
        if (string.IsNullOrWhiteSpace(subtitleLanguage))
        {
            return audioName;
        }
        
        var subName = GetLanguageName(subtitleLanguage);
        return $"{audioName} + {subName} Sub";
    }
    
    public string GetOptionId(string? audioLanguage, string? subtitleLanguage)
    {
        var audioLang = NormalizeLanguageCode(audioLanguage);
        var subLang = NormalizeLanguageCode(subtitleLanguage);
        
        if (string.IsNullOrWhiteSpace(subtitleLanguage))
        {
            return audioLang;
        }
        
        return $"{audioLang}-{subLang}";
    }
}
