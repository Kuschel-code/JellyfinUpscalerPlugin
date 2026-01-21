# Installation Guide

## 🎯 Easy Method (Plugin Repository) - Recommended

Users can install the plugin with just a few clicks:

### Step 1: Add Repository to Jellyfin

1. Open **Jellyfin Admin Dashboard**
2. Go to **Plugins** → **Repositories**
3. Click **+** (Add Repository)
4. Enter the following:
   - **Repository Name**: `Language Selector Repository`
   - **Repository URL**: `https://raw.githubusercontent.com/Kuschel-code/jellyfin-plugin-languageselector/main/manifest.json`
5. Click **Save**

### Step 2: Install Plugin

1. Go to **Plugins** → **Catalog**
2. Search for **"Language Selector"**
3. Click **Install**
4. **Restart Jellyfin Server**

### Step 3: Use It!

1. Open an anime episode with multiple audio/subtitle tracks
2. You'll see the flag buttons 🎌🇩🇪🇯🇵
3. Click a flag → video starts immediately with your selected language!

---

## ⚙️ Manual Installation (Fallback)

If the repository method doesn't work:

### Step 1: Download

1. Go to [GitHub Releases](https://github.com/Kuschel-code/jellyfin-plugin-languageselector/releases)
2. Download `jellyfin-plugin-languageselector_1.0.1.0.zip`
3. Extract the ZIP file → you'll get `Jellyfin.Plugin.LanguageSelector.dll`

### Step 2: Installation

1. Find your Jellyfin data directory:
   - **Windows**: `C:\ProgramData\Jellyfin\Server\` or `%APPDATA%\Jellyfin\Server\`
   - **Linux**: `/var/lib/jellyfin/` or `~/.local/share/jellyfin/`
   - **Docker**: `/config/`

2. Navigate to `<jellyfin-data-dir>/plugins/`
3. Create a folder named `LanguageSelector`
4. Copy `Jellyfin.Plugin.LanguageSelector.dll` into this folder

   Final structure:
   ```
   <jellyfin-data-dir>/
   └── plugins/
       └── LanguageSelector/
           └── Jellyfin.Plugin.LanguageSelector.dll
   ```

5. **Restart Jellyfin Server**

### Step 3: Verification

1. Open **Admin Dashboard** → **Plugins**
2. You should see **"Language Selector"** in the list
3. Status: **Active** ✅

---

## 🚀 Getting Started

### Configure Plugin (Optional)

1. Go to **Plugins** → **Language Selector**
2. Enable **"Auto Detect Languages"** (Default: On)
3. Select preferred languages: `ger`, `jpn`, `eng`
4. **Save**

### Test Plugin

1. Open an episode with multiple languages (e.g., Anime with GerDub + GerSub + EngSub)
2. You'll see the flag bar below the episode title
3. Test each flag:
   - 🇩🇪 **DE**: German audio, no subtitles
   - 🎌🇩🇪 **JP-DE**: Japanese audio, German subtitles
   - 🎌🇺🇸 **JP-EN**: Japanese audio, English subtitles

---

## ❓ Troubleshooting

### Plugin Doesn't Appear in Catalog

**Cause**: Repository URL is incorrect or GitHub is unreachable

**Solution**:
1. Check the URL: `https://raw.githubusercontent.com/Kuschel-code/jellyfin-plugin-languageselector/main/manifest.json`
2. Open the URL in browser → should display JSON
3. If 404 error: Wait a few minutes (GitHub cache)
4. Ensure the repository is **PUBLIC** (at least for `manifest.json`)

### Flags Not Showing

**Cause**: JavaScript not loaded or no matching tracks found

**Solution**:
1. Open browser console (F12)
2. Look for error messages
3. Check if the file has multiple audio/subtitle tracks:
   - Jellyfin Dashboard → **Libraries** → Select episode → **Media Info**
   - Should have at least 1 audio track with language (ger/jpn/eng)

### Playback Doesn't Start

**Cause**: Jellyfin PlaybackManager not found

**Solution**:
1. Update Jellyfin to at least version **10.10.0**
2. Use a compatible client (web browser, not native app)
3. Check browser console for JavaScript errors

### Checksum Error During Download

**Cause**: The `manifest.json` has an incorrect MD5 hash

**Solution**:
1. Download the plugin manually (see "Manual Installation")
2. Or: Report the issue on GitHub Issues

---

## 📚 Additional Documentation

- [README.md](README.md) - Project overview and features
- [RELEASE_GUIDE.md](RELEASE_GUIDE.md) - For developers: How to create a release
- [TESTING_GUIDE.md](TESTING_GUIDE.md) - Testing strategy
- [BUG_FIXES.md](BUG_FIXES.md) - Known bugs and fixes

---

## 💡 Tip for Anime Fans

Organize your library like this:

```
/Anime
  /Sword Art Online
    /Season 01
      S01E01.mkv (Audio: ger, jpn | Subs: ger, eng)
      S01E02.mkv (Audio: ger, jpn | Subs: ger, eng)
```

The plugin automatically detects tracks and displays the appropriate flags!

Enjoy! 🎉
