using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LanguageSelector.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.LanguageSelector.Services;

public class MediaStreamAnalyzer
{
    private readonly LanguageDetector _languageDetector;
    
    public MediaStreamAnalyzer(LanguageDetector languageDetector)
    {
        _languageDetector = languageDetector;
    }
    
    public List<MediaStreamInfo> ExtractAudioStreams(BaseItem item)
    {
        var streams = new List<MediaStreamInfo>();
        
        if (item.GetMediaStreams() == null)
        {
            return streams;
        }
        
        var audioStreams = item.GetMediaStreams()
            .Where(s => s.Type == MediaStreamType.Audio)
            .OrderBy(s => s.Index);
        
        foreach (var stream in audioStreams)
        {
            streams.Add(new MediaStreamInfo
            {
                Index = stream.Index,
                Type = "Audio",
                Language = stream.Language,
                Title = stream.Title,
                Codec = stream.Codec,
                IsDefault = stream.IsDefault
            });
        }
        
        return streams;
    }
    
    public List<MediaStreamInfo> ExtractSubtitleStreams(BaseItem item)
    {
        var streams = new List<MediaStreamInfo>();
        
        if (item.GetMediaStreams() == null)
        {
            return streams;
        }
        
        var subtitleStreams = item.GetMediaStreams()
            .Where(s => s.Type == MediaStreamType.Subtitle)
            .OrderBy(s => s.Index);
        
        foreach (var stream in subtitleStreams)
        {
            streams.Add(new MediaStreamInfo
            {
                Index = stream.Index,
                Type = "Subtitle",
                Language = stream.Language,
                Title = stream.Title,
                Codec = stream.Codec,
                IsDefault = stream.IsDefault,
                IsForced = stream.IsForced
            });
        }
        
        return streams;
    }
    
    public List<LanguageOption> GenerateLanguageOptions(BaseItem item)
    {
        var options = new List<LanguageOption>();
        var audioStreams = ExtractAudioStreams(item);
        var subtitleStreams = ExtractSubtitleStreams(item);
        
        if (!audioStreams.Any())
        {
            return options;
        }
        
        foreach (var audioStream in audioStreams)
        {
            var audioLang = audioStream.Language;
            
            var optionWithoutSub = new LanguageOption
            {
                Id = _languageDetector.GetOptionId(audioLang, null),
                DisplayName = _languageDetector.GetDisplayName(audioLang, null),
                FlagIcon = _languageDetector.GetFlagIcon(audioLang, null),
                AudioStreamIndex = audioStream.Index,
                SubtitleStreamIndex = null,
                AudioLanguage = audioLang ?? "unknown",
                SubtitleLanguage = null,
                IsDefault = audioStream.IsDefault && !subtitleStreams.Any(s => s.IsDefault)
            };
            
            options.Add(optionWithoutSub);
            
            foreach (var subtitleStream in subtitleStreams.Where(s => !s.IsForced))
            {
                var subLang = subtitleStream.Language;
                
                var optionWithSub = new LanguageOption
                {
                    Id = _languageDetector.GetOptionId(audioLang, subLang),
                    DisplayName = _languageDetector.GetDisplayName(audioLang, subLang),
                    FlagIcon = _languageDetector.GetFlagIcon(audioLang, subLang),
                    AudioStreamIndex = audioStream.Index,
                    SubtitleStreamIndex = subtitleStream.Index,
                    AudioLanguage = audioLang ?? "unknown",
                    SubtitleLanguage = subLang,
                    IsDefault = audioStream.IsDefault && subtitleStream.IsDefault
                };
                
                options.Add(optionWithSub);
            }
        }
        
        var uniqueOptions = options
            .GroupBy(o => o.Id)
            .Select(g => g.First())
            .ToList();
        
        return uniqueOptions;
    }
    
    public LanguageOptionsResponse GetLanguageOptionsForItem(BaseItem item)
    {
        var options = GenerateLanguageOptions(item);
        
        return new LanguageOptionsResponse
        {
            Options = options,
            ItemId = item.Id.ToString(),
            ItemName = item.Name ?? "Unknown"
        };
    }
}
