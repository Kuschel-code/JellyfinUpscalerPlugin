# Jellyfin Language Selector Plugin

**One-click language selection for anime and media playback** - Inspired by AniWorld's intuitive flag-based interface.

![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)
![Jellyfin](https://img.shields.io/badge/Jellyfin-10.10.3-blueviolet.svg)

---

## 🎯 Overview

The Language Selector plugin transforms Jellyfin's language selection experience by replacing the traditional audio/subtitle configuration with intuitive flag buttons. Click a flag to instantly start playback with the correct audio and subtitle combination—no more manual track selection!

### Key Features

✅ **One-Click Playback**: Click a flag icon to start video with pre-configured audio/subtitle settings  
✅ **Automatic Detection**: Automatically detects available languages from media file metadata  
✅ **AniWorld-Inspired UI**: Beautiful flag buttons with hover effects and visual feedback  
✅ **Smart Language Mapping**: Supports German, Japanese, and English audio/subtitle combinations  
✅ **No Manual Configuration**: Plugin analyzes media streams automatically  
✅ **Seamless Integration**: Works with existing Jellyfin web interface

---

## 📸 How It Works

Instead of this traditional workflow:
1. Click "Play"
2. Click settings gear ⚙️
3. Select audio track
4. Select subtitle track
5. Hope you got it right

You get this:
1. Click flag 🇩🇪 → German audio, no subtitles
2. Click flag 🇯🇵🇩🇪 → Japanese audio + German subtitles
3. Click flag 🇯🇵🇺🇸 → Japanese audio + English subtitles

---

## 🚀 Quick Start

### Prerequisites

- **Jellyfin Server**: Version 10.8.0 or higher
- **.NET SDK**: Version 8.0 or higher (for building from source)
- **Browser**: Chrome, Firefox, or Edge (for web UI)

### Installation

#### Option 1: Plugin Repository (Recommended) ⭐

The easiest way to install - no manual downloads needed!

1. **Open** Jellyfin Admin Dashboard
2. Go to **Plugins** → **Repositories**
3. Click **+** (Add Repository)
4. Enter:
   - **Repository Name**: `Language Selector Repository`
   - **Repository URL**: `https://raw.githubusercontent.com/Kuschel-code/jellyfin-plugin-languageselector/main/manifest.json`
5. Click **Save**
6. Go to **Plugins** → **Catalog**
7. Find **"Language Selector"** and click **Install**
8. **Restart** Jellyfin server
9. Done! 🎉

#### Option 2: Manual DLL Installation

1. **Download** the latest release ZIP from the [Releases](../../releases) page
2. **Extract** the DLL from the ZIP file
3. **Locate** your Jellyfin plugin directory:
   - Windows: `%AppData%\Jellyfin\Server\plugins\`
   - Linux: `/var/lib/jellyfin/plugins/`
   - Docker: `/config/plugins/` (inside container)
4. **Create** a subfolder: `LanguageSelector`
5. **Copy** the DLL into the folder:
   ```
   %AppData%\Jellyfin\Server\plugins\LanguageSelector\Jellyfin.Plugin.LanguageSelector.dll
   ```
6. **Restart** Jellyfin server
7. **Verify** in Dashboard → Plugins

#### Option 3: Build From Source

```bash
# Clone or download this repository
git clone https://github.com/Kuschel-code/jellyfin-plugin-languageselector.git
cd jellyfin-plugin-languageselector

# Build the plugin
dotnet build Jellyfin.Plugin.LanguageSelector\Jellyfin.Plugin.LanguageSelector.csproj --configuration Release

# The DLL will be at:
# Jellyfin.Plugin.LanguageSelector\bin\Release\net8.0\Jellyfin.Plugin.LanguageSelector.dll

# Copy to Jellyfin plugins directory (see Option 2 for paths)
```

**📚 For detailed installation steps, see [INSTALLATION.md](INSTALLATION.md)**

---

## 💡 Usage

### Automatic Mode (Recommended)

The plugin automatically detects available audio and subtitle tracks from your media files. Simply navigate to an episode detail page and click the flag buttons that appear.

### Manual Testing

If flag buttons don't appear automatically, you can manually inject the JavaScript for testing:

1. Open Jellyfin web UI
2. Navigate to an episode detail page
3. Open browser console (F12)
4. Run:
   ```javascript
   var script = document.createElement('script');
   script.src = '/web/configurationpage?name=LanguageSelector/language-selector.js';
   document.head.appendChild(script);
   
   var css = document.createElement('link');
   css.rel = 'stylesheet';
   css.href = '/web/configurationpage?name=LanguageSelector/language-selector.css';
   document.head.appendChild(css);
   ```

### Flag Types

| Flag Icon | Meaning | Audio | Subtitles |
|-----------|---------|-------|-----------|
| 🇩🇪 | German | German | None |
| 🇯🇵🇩🇪 | Japanese + German Sub | Japanese | German |
| 🇯🇵🇺🇸 | Japanese + English Sub | Japanese | English |
| 🇯🇵 | Japanese | Japanese | None |
| 🇺🇸 | English | English | None |

---

## 🔧 Configuration

### Plugin Settings

Access plugin settings via **Dashboard → Plugins → Language Selector**

Available settings:
- **Enable Debug Logging**: Enable verbose logging for troubleshooting
- **Auto-Detect Languages**: Automatically detect languages from file metadata (enabled by default)
- **Preferred Languages**: Set preferred language priority

### API Endpoint

The plugin exposes a REST API endpoint for retrieving language options:

```
GET /Items/{itemId}/LanguageOptions
```

**Example Response:**
```json
{
  "options": [
    {
      "id": "de",
      "displayName": "German",
      "flagIcon": "de",
      "audioStreamIndex": 0,
      "subtitleStreamIndex": null,
      "audioLanguage": "ger",
      "subtitleLanguage": null,
      "isDefault": true
    },
    {
      "id": "jp-de",
      "displayName": "Japanese + German Sub",
      "flagIcon": "jp-de",
      "audioStreamIndex": 1,
      "subtitleStreamIndex": 2,
      "audioLanguage": "jpn",
      "subtitleLanguage": "ger",
      "isDefault": false
    }
  ],
  "itemId": "abc123...",
  "itemName": "Episode Name"
}
```

---

## 🛠️ Development

### Build Requirements

- .NET SDK 8.0+
- Visual Studio 2022 / VS Code / Rider (optional)
- Git

### Build Instructions

```bash
# Clone repository
git clone https://github.com/Kuschel-code/jellyfin-plugin-languageselector.git
cd jellyfin-plugin-languageselector

# Restore dependencies
dotnet restore Jellyfin.Plugin.LanguageSelector

# Build (Debug)
dotnet build Jellyfin.Plugin.LanguageSelector

# Build (Release)
dotnet build Jellyfin.Plugin.LanguageSelector --configuration Release
```

### Project Structure

```
Jellyfin.Plugin.LanguageSelector/
├── Api/
│   └── LanguageOptionsController.cs    # REST API endpoint
├── Configuration/
│   ├── PluginConfiguration.cs          # Plugin settings
│   └── config.html                     # Admin UI
├── Models/
│   ├── LanguageOption.cs               # Data models
│   └── MediaStreamInfo.cs
├── Services/
│   ├── MediaStreamAnalyzer.cs          # Stream analysis logic
│   └── LanguageDetector.cs             # Language detection
├── Web/
│   ├── language-selector.js            # Frontend UI logic
│   ├── language-selector.css           # UI styling
│   └── flags/                          # Flag SVG icons
├── Plugin.cs                           # Plugin entry point
└── build.yaml                          # JPRM build config
```

### Testing

**📚 For comprehensive testing procedures, see [TESTING_GUIDE.md](TESTING_GUIDE.md)**

Quick test checklist:
- [ ] Plugin loads in Dashboard
- [ ] API endpoint returns language options
- [ ] Flag buttons appear on episode page
- [ ] Clicking flags starts playback with correct tracks
- [ ] Works with different media files (various language combinations)

### Debugging

1. **Enable Debug Logging**: Dashboard → Plugins → Language Selector → Enable Debug Logging
2. **Check Logs**: 
   - Windows: `%AppData%\Jellyfin\Server\log\`
   - Linux: `/var/log/jellyfin/`
3. **Browser Console**: Press F12 to view JavaScript errors
4. **Network Tab**: Monitor API calls in browser DevTools

---

## 🐛 Troubleshooting

### Plugin doesn't appear in Dashboard

**Check:**
- DLL is in correct folder: `plugins/LanguageSelector/`
- Filename is exact: `Jellyfin.Plugin.LanguageSelector.dll`
- Jellyfin was restarted after installation
- Check Jellyfin logs for loading errors

### Flag buttons don't appear

**Check:**
- Media file has multiple audio/subtitle tracks
- You're on episode detail page (not library view)
- JavaScript loaded without errors (check browser console)
- API returns language options (test endpoint manually)

### Wrong language selected

**Check:**
- File metadata has correct language tags (use MediaInfo/ffprobe)
- Stream indices match between API response and actual file
- Check Jellyfin logs for playback warnings

**📚 For more troubleshooting tips, see [QUICK_INSTALL.md](QUICK_INSTALL.md#-troubleshooting)**

---

## 📦 Packaging

### Using JPRM (Jellyfin Plugin Repository Manager)

The plugin includes a `build.yaml` for JPRM compatibility:

```bash
# Install JPRM
pip install jprm

# Build plugin package
jprm --verbosity=debug repo add . /path/to/output/repo
```

### Manual Packaging

1. Build in Release configuration
2. Copy DLL from `bin/Release/net8.0/`
3. Create ZIP archive with DLL
4. Distribute via GitHub Releases

---

## 🤝 Contributing

Contributions are welcome! Here's how:

1. **Fork** the repository
2. **Create** a feature branch: `git checkout -b feature/amazing-feature`
3. **Commit** changes: `git commit -m 'Add amazing feature'`
4. **Push** to branch: `git push origin feature/amazing-feature`
5. **Open** a Pull Request

### Development Guidelines

- Follow C# coding conventions
- Add XML documentation comments for public APIs
- Test with various media files (different language combinations)
- Update documentation for new features

---

## 📋 Known Limitations

- **UI Injection**: Requires manual JavaScript injection or custom CSS/JS plugin for automatic loading
- **Language Support**: Currently supports German, Japanese, and English (easily extensible)
- **Web Only**: Currently only works in web browser interface (not mobile apps)
- **Stream Detection**: Relies on accurate language tags in media file metadata

### Future Improvements

- [ ] Auto-injection support (no manual script loading)
- [ ] Mobile app support
- [ ] Additional language support (French, Spanish, etc.)
- [ ] Custom flag icon uploads
- [ ] Remember user language preference per series
- [ ] Quick-switch between languages during playback

---

## 📄 License

This project is provided as-is for use with Jellyfin. See [Jellyfin's licensing](https://github.com/jellyfin/jellyfin) for more information.

---

## 🙏 Acknowledgments

- **Jellyfin Team**: For the excellent media server platform
- **AniWorld**: For the UI/UX inspiration
- **Community**: For testing and feedback

---

## 📞 Support

- **Issues**: [GitHub Issues](../../issues)
- **Discussions**: [GitHub Discussions](../../discussions)
- **Jellyfin Forum**: [Official Forum](https://forum.jellyfin.org/)

---

## 📚 Additional Documentation

- [**INSTALLATION.md**](INSTALLATION.md) - Complete installation guide with troubleshooting
- [**RELEASE_GUIDE.md**](RELEASE_GUIDE.md) - How to create a GitHub release (for developers)
- [**TESTING_GUIDE.md**](TESTING_GUIDE.md) - Comprehensive testing procedures
- [**BUG_FIXES.md**](BUG_FIXES.md) - Known bugs and fixes applied
- [**QUICK_INSTALL.md**](QUICK_INSTALL.md) - Quick start guide

---

**Made with ❤️ for the Jellyfin community**
