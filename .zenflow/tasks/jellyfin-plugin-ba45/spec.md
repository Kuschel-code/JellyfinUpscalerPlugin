# Technical Specification: Jellyfin One-Click Language Selection Plugin

## Complexity Assessment: **HARD**

This is a complex implementation requiring:
- Full C# server-side plugin development from scratch
- Deep integration with Jellyfin's media stream analysis
- Frontend UI modification via JavaScript injection
- Coordination between backend API and frontend controls
- Playback state management with pre-selected audio/subtitle tracks
- Multi-language support and automatic track detection

---

## 1. Technical Context

### Language & Framework
- **Backend**: C# (.NET Standard 2.1 / .NET 6+)
- **Frontend**: JavaScript (ES6+), HTML5, CSS3
- **Target Platform**: Jellyfin Server 10.8+

### Dependencies
- **Jellyfin Plugin SDK**: `MediaBrowser.Common`, `MediaBrowser.Controller`, `MediaBrowser.Model`
- **Jellyfin Web**: JavaScript injection via custom plugin or manual injection
- **NuGet Packages**: System.Linq, System.Collections.Generic

### Architecture Overview
```
┌─────────────────────────────────────────────────────────┐
│                    Jellyfin Server                       │
│  ┌───────────────────────────────────────────────────┐  │
│  │  Language Selection Plugin (C#)                   │  │
│  │  - Media Stream Analyzer                          │  │
│  │  - Language Detection Engine                      │  │
│  │  - API Controller (/LanguageOptions)              │  │
│  └───────────────────────────────────────────────────┘  │
│                         │                                │
│                         ▼ (REST API)                     │
│  ┌───────────────────────────────────────────────────┐  │
│  │  Jellyfin Web API Layer                           │  │
│  └───────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼ (HTTP/JSON)
┌─────────────────────────────────────────────────────────┐
│                  Jellyfin Web Client                     │
│  ┌───────────────────────────────────────────────────┐  │
│  │  Language Selector UI (JavaScript)                │  │
│  │  - Flag Button Renderer                           │  │
│  │  - DOM Injection Logic                            │  │
│  │  - Playback Controller (with track pre-selection)│  │
│  └───────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────┐  │
│  │  Custom CSS Styles                                │  │
│  │  - AniWorld-inspired flag buttons                 │  │
│  │  - Hover effects & selection states               │  │
│  └───────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

---

## 2. Implementation Approach

### 2.1 Server-Side Plugin (C#)

#### Plugin Structure
```
Jellyfin.Plugin.LanguageSelector/
├── Jellyfin.Plugin.LanguageSelector.csproj
├── Plugin.cs (Entry point, implements IPlugin)
├── Configuration/
│   ├── PluginConfiguration.cs
│   └── config.html (Admin UI)
├── Api/
│   └── LanguageOptionsController.cs (REST API)
├── Services/
│   ├── MediaStreamAnalyzer.cs
│   └── LanguageDetector.cs
└── Models/
    ├── LanguageOption.cs
    └── MediaStreamInfo.cs
```

#### Core Components

**A. Media Stream Analyzer** (`MediaStreamAnalyzer.cs`)
- **Purpose**: Extract and analyze audio/subtitle tracks from media files
- **Key Methods**:
  - `AnalyzeMediaStreams(BaseItem item)`: Returns list of available audio/subtitle tracks
  - `DetectLanguages(MediaStream[] streams)`: Identifies language codes (ger, jpn, eng, etc.)
  - `BuildLanguageMatrix(BaseItem item)`: Creates mapping of available language combinations

**B. Language Detection Engine** (`LanguageDetector.cs`)
- **Purpose**: Map language codes to flag icons and friendly names
- **Logic**:
  - Recognize ISO 639-2 codes: `ger`/`deu` → German, `jpn` → Japanese, `eng` → English
  - Handle subtitle detection: `ger.forced`, `eng.sdh`, etc.
  - Priority system: prefer non-forced, non-commentary tracks

**C. API Controller** (`LanguageOptionsController.cs`)
- **Endpoint**: `GET /Items/{itemId}/LanguageOptions`
- **Response Format**:
```json
{
  "itemId": "abc123",
  "options": [
    {
      "id": "ger-audio-only",
      "displayName": "German Audio",
      "flagCode": "de",
      "audioStreamIndex": 0,
      "subtitleStreamIndex": -1
    },
    {
      "id": "jpn-ger-sub",
      "displayName": "Japanese + German Subs",
      "flagCode": "jp-de",
      "audioStreamIndex": 1,
      "subtitleStreamIndex": 2
    },
    {
      "id": "jpn-eng-sub",
      "displayName": "Japanese + English Subs",
      "flagCode": "jp-us",
      "audioStreamIndex": 1,
      "subtitleStreamIndex": 3
    }
  ]
}
```

**D. Plugin Entry Point** (`Plugin.cs`)
- Implements `IPlugin` interface
- Registers API routes
- Provides plugin metadata (name, version, GUID)

---

### 2.2 Frontend Modification (JavaScript)

#### Injection Strategy
Two deployment options:

**Option A**: Use existing JavaScript injection plugin
- Install `Jellyfin-JavaScript-Injector` or `jellyfin-plugin-custom-javascript`
- Add custom script via plugin configuration

**Option B**: Create dedicated web plugin
- Package JavaScript/CSS as part of the C# plugin
- Serve static files from plugin directory

#### UI Component Structure

**A. Flag Button Renderer** (`language-selector.js`)
```javascript
class LanguageSelectorUI {
  constructor() {
    this.apiClient = null;
    this.currentItemId = null;
  }

  async init() {
    // Detect when user navigates to episode page
    // Inject flag buttons into DOM
    // Attach event listeners
  }

  async fetchLanguageOptions(itemId) {
    // Call /Items/{itemId}/LanguageOptions
    // Return available options
  }

  renderFlagButtons(options) {
    // Create HTML structure with flag icons
    // Apply CSS classes
    // Insert into DOM (below or replacing default play button)
  }

  async startPlaybackWithTracks(itemId, audioIndex, subtitleIndex) {
    // Use Jellyfin playback API to start with specific tracks
    // PlaybackManager.play({ items: [...], audioStreamIndex, subtitleStreamIndex })
  }
}
```

**B. DOM Injection Logic**
- **Target Element**: Episode detail page play button (`.detailButton`, `.itemDetailPage`)
- **Injection Point**: Insert flag bar directly above or below the main play button
- **Mutation Observer**: Watch for page changes to re-inject if needed

**C. Playback Control**
- Use Jellyfin's `PlaybackManager` API:
```javascript
window.PlaybackManager.play({
  items: [{ Id: itemId }],
  audioStreamIndex: 1,    // Japanese audio
  subtitleStreamIndex: 2   // German subtitles
});
```

---

### 2.3 Frontend Styling (CSS)

**Design Reference**: AniWorld-inspired flag buttons
- Rounded corners (`border-radius: 8px`)
- Blue background bar (`background: #5B7FBF`)
- Flag icons with border on hover
- Black border for active/selected language
- Smooth transitions on hover

**CSS Structure** (`language-selector.css`)
```css
.language-selector-bar {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 15px;
  background: #5B7FBF;
  border-radius: 8px;
  margin: 15px 0;
}

.language-flag-btn {
  cursor: pointer;
  border: 2px solid transparent;
  border-radius: 6px;
  transition: all 0.2s ease;
}

.language-flag-btn:hover {
  border-color: rgba(255, 255, 255, 0.5);
  transform: scale(1.05);
}

.language-flag-btn.active {
  border-color: #000;
  box-shadow: 0 0 8px rgba(0, 0, 0, 0.3);
}
```

---

## 3. Source Code Structure

### Files to Create

#### Backend (C# Plugin)
1. `Jellyfin.Plugin.LanguageSelector/Jellyfin.Plugin.LanguageSelector.csproj`
2. `Jellyfin.Plugin.LanguageSelector/Plugin.cs`
3. `Jellyfin.Plugin.LanguageSelector/Configuration/PluginConfiguration.cs`
4. `Jellyfin.Plugin.LanguageSelector/Api/LanguageOptionsController.cs`
5. `Jellyfin.Plugin.LanguageSelector/Services/MediaStreamAnalyzer.cs`
6. `Jellyfin.Plugin.LanguageSelector/Services/LanguageDetector.cs`
7. `Jellyfin.Plugin.LanguageSelector/Models/LanguageOption.cs`
8. `Jellyfin.Plugin.LanguageSelector/Models/MediaStreamInfo.cs`
9. `build.yaml` (JPRM build configuration)

#### Frontend (JavaScript/CSS)
10. `Jellyfin.Plugin.LanguageSelector/Web/language-selector.js`
11. `Jellyfin.Plugin.LanguageSelector/Web/language-selector.css`
12. `Jellyfin.Plugin.LanguageSelector/Web/flags/` (SVG flag icons: de.svg, jp.svg, us.svg, jp-de.svg, jp-us.svg)

#### Documentation & Build
13. `.gitignore`
14. `README.md` (build/installation instructions)

---

## 4. Data Model / API Changes

### New API Endpoint

**Endpoint**: `GET /Items/{itemId}/LanguageOptions`

**Parameters**:
- `itemId` (required): The ID of the media item (episode/movie)

**Response Model** (`LanguageOption.cs`):
```csharp
public class LanguageOption
{
    public string Id { get; set; }
    public string DisplayName { get; set; }
    public string FlagCode { get; set; }
    public int AudioStreamIndex { get; set; }
    public int SubtitleStreamIndex { get; set; } // -1 for no subtitles
    public string AudioLanguage { get; set; }
    public string SubtitleLanguage { get; set; }
}
```

### Language Detection Logic Matrix

| Available Tracks | Generated Option | Flag Code | Audio Index | Subtitle Index |
|------------------|------------------|-----------|-------------|----------------|
| Audio: ger | German Audio | de | 0 | -1 |
| Audio: jpn, Sub: ger | Japanese + German Sub | jp-de | N | M |
| Audio: jpn, Sub: eng | Japanese + English Sub | jp-us | N | M |
| Audio: eng | English Audio | us | 0 | -1 |
| Audio: jpn, Sub: none | Japanese Audio Only | jp | 0 | -1 |

**Priority Rules**:
1. Exclude forced subtitles from main options (create separate button if needed)
2. Exclude commentary audio tracks
3. Prefer first matching track if multiple with same language
4. Only show combinations that exist in the file

---

## 5. Verification Approach

### Development Testing

**Phase 1: Backend Testing**
1. Build plugin: `dotnet build`
2. Check for compilation errors
3. Deploy to Jellyfin plugin directory
4. Restart Jellyfin server
5. Verify plugin appears in Admin Dashboard
6. Test API endpoint with curl/Postman:
   ```bash
   curl http://localhost:8096/Items/{itemId}/LanguageOptions
   ```
7. Verify correct language detection for test files (MKV with multiple audio/subtitle tracks)

**Phase 2: Frontend Testing**
1. Load Jellyfin web UI in browser
2. Inject JavaScript manually via browser console (for quick testing)
3. Navigate to episode detail page
4. Verify flag buttons appear
5. Click each flag and verify:
   - Correct audio/subtitle tracks are selected
   - Playback starts immediately
   - No need to manually change tracks
6. Test with different media files (anime with ger/jpn/eng combinations)

**Phase 3: Integration Testing**
1. Package full plugin with both backend and frontend
2. Install on clean Jellyfin instance
3. Test end-to-end workflow:
   - Add media file with multiple tracks
   - Scan library
   - Open episode page
   - Click flag button
   - Verify one-click playback with correct settings
4. Test edge cases:
   - Files with only one audio track
   - Files with no subtitles
   - Files with >3 language combinations

### Test Media Files Needed
- **File 1**: Anime episode with German audio only
- **File 2**: Anime episode with Japanese audio + German/English subtitles
- **File 3**: Anime episode with Japanese/German audio + multiple subtitle options
- **File 4**: Regular movie/show (non-anime) to test general compatibility

### Acceptance Criteria
✅ Plugin compiles without errors  
✅ API endpoint returns correct language options for all test files  
✅ Flag buttons appear on episode detail page  
✅ Clicking flag starts playback with correct audio/subtitle  
✅ No manual track selection needed after clicking flag  
✅ UI matches AniWorld design reference (rounded buttons, blue bar, hover effects)  
✅ Works on both Chrome and Firefox  
✅ Plugin can be installed via repository manifest

---

## 6. Implementation Challenges & Mitigations

### Challenge 1: Language Code Variations
**Problem**: Different media files use inconsistent language codes (`ger` vs `deu`, `jpn` vs `ja`)  
**Solution**: Create comprehensive mapping in `LanguageDetector.cs` with all ISO 639-1/2/3 variants

### Challenge 2: Jellyfin Web UI Changes
**Problem**: Jellyfin web UI may change structure in updates, breaking DOM injection  
**Solution**: 
- Use flexible selectors (multiple fallback options)
- Implement mutation observer to re-inject if DOM changes
- Version compatibility checks

### Challenge 3: Playback API Limitations
**Problem**: Jellyfin's playback API may not support pre-selecting tracks in all clients  
**Solution**: 
- Focus on web client first (highest compatibility)
- Document limitations for other clients
- Implement fallback: start playback, then switch tracks immediately

### Challenge 4: Plugin Distribution
**Problem**: Users need easy installation method  
**Solution**: 
- Create JPRM manifest for official plugin repository submission
- Provide manual installation instructions
- Include pre-built DLL in GitHub releases

---

## 7. Future Enhancements (Out of Scope for v1.0)

- **Multi-client support**: Extend to Android/iOS apps
- **User preferences**: Remember last selected language per user
- **Advanced filtering**: Show/hide specific languages via plugin settings
- **Automatic language selection**: Auto-detect user's preferred language from browser/profile
- **Keyboard shortcuts**: Press 1/2/3 to select language options
- **Preview tooltips**: Show track details on hover (codec, channels, bitrate)

---

## 8. Next Steps

After approval of this specification, create a detailed implementation plan in `plan.md` with the following breakdown:

1. **Setup Development Environment** (1-2 hours)
   - Clone Jellyfin plugin template
   - Setup .NET SDK and build tools
   - Configure IDE (VS Code/Visual Studio)

2. **Implement Backend Plugin** (6-8 hours)
   - Create project structure
   - Implement MediaStreamAnalyzer
   - Implement LanguageDetector
   - Create API controller
   - Add plugin registration

3. **Implement Frontend UI** (4-6 hours)
   - Create JavaScript language selector component
   - Implement DOM injection logic
   - Add CSS styling
   - Integrate with Jellyfin playback API

4. **Testing & Refinement** (3-4 hours)
   - Test with various media files
   - Fix bugs and edge cases
   - Optimize performance
   - Refine UI/UX

5. **Documentation & Packaging** (2-3 hours)
   - Write README and installation guide
   - Create build scripts
   - Package for distribution
   - Test installation process

**Estimated Total Time**: 16-23 hours of development work
