# Quick Installation Guide - Language Selector Plugin

## 🚀 Quick Start (5 minutes)

### Step 1: Build the Plugin

Open PowerShell/Command Prompt and run:

```bash
cd "C:\Users\Kuscheltier\.zenflow\worktrees\jellyfin-plugin-ba45"
dotnet build Jellyfin.Plugin.LanguageSelector\Jellyfin.Plugin.LanguageSelector.csproj --configuration Release
```

**Expected output**: Build succeeds (47 warnings about XML docs are normal)

---

### Step 2: Locate the Built DLL

The plugin DLL will be at:
```
Jellyfin.Plugin.LanguageSelector\bin\Release\net6.0\Jellyfin.Plugin.LanguageSelector.dll
```

---

### Step 3: Install to Jellyfin

1. **Find your Jellyfin plugin directory**:
   - Windows: `%AppData%\Jellyfin\Server\plugins\`
   - Linux: `/var/lib/jellyfin/plugins/`
   - Docker: `/config/plugins/` (inside container)

2. **Create plugin subfolder**:
   ```
   %AppData%\Jellyfin\Server\plugins\LanguageSelector\
   ```

3. **Copy the DLL**:
   Copy `Jellyfin.Plugin.LanguageSelector.dll` to the folder you just created:
   ```
   %AppData%\Jellyfin\Server\plugins\LanguageSelector\Jellyfin.Plugin.LanguageSelector.dll
   ```

---

### Step 4: Restart Jellyfin

- **Windows Service**: Restart from Services
- **Windows App**: Close and reopen
- **Linux**: `sudo systemctl restart jellyfin`
- **Docker**: `docker restart jellyfin`

---

### Step 5: Verify Installation

1. Open Jellyfin web UI: `http://localhost:8096`
2. Go to **Dashboard** → **Plugins**
3. Look for **"Language Selector"** in the plugin list

✅ **Success**: Plugin appears in the list  
❌ **Problem**: Plugin not showing → Check Jellyfin logs at `%AppData%\Jellyfin\Server\log\`

---

### Step 6: Test the API

Get an episode/movie ID from Jellyfin:
1. Navigate to any episode in Jellyfin web UI
2. Look at the URL: `http://localhost:8096/web/index.html#!/item?id=XXXXX`
3. Copy the ID (the `XXXXX` part)

Test the API endpoint:
```bash
# Replace YOUR_API_KEY with your actual key from Dashboard → API Keys
# Replace ITEM_ID with the ID you copied

curl -H "X-Emby-Token: YOUR_API_KEY" http://localhost:8096/Items/ITEM_ID/LanguageOptions
```

**Expected response** (example):
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
  "itemId": "...",
  "itemName": "Episode Name"
}
```

---

### Step 7: Test the UI

**Important**: The JavaScript needs to be injected into the web UI. This can be done in two ways:

#### Option A: Manual Injection (For Testing)
1. Open Jellyfin web UI in Chrome/Firefox
2. Navigate to any episode detail page
3. Open browser console (F12)
4. Manually load the JavaScript:
   ```javascript
   // Load the script
   var script = document.createElement('script');
   script.src = '/web/configurationpage?name=LanguageSelector/language-selector.js';
   document.head.appendChild(script);
   
   // Load the CSS
   var css = document.createElement('link');
   css.rel = 'stylesheet';
   css.href = '/web/configurationpage?name=LanguageSelector/language-selector.css';
   document.head.appendChild(css);
   ```

#### Option B: Use Custom JS Injection Plugin
1. Install a Jellyfin custom JavaScript plugin (search for "jellyfin-web-injector" or similar)
2. Add this to the custom JavaScript configuration:
   ```html
   <script src="/web/configurationpage?name=LanguageSelector/language-selector.js"></script>
   <link rel="stylesheet" href="/web/configurationpage?name=LanguageSelector/language-selector.css">
   ```

---

### Step 8: Verify Flag Buttons Appear

1. Navigate to an episode with multiple audio/subtitle tracks
2. Flag buttons should appear near the play button
3. Hover over flags to see tooltips
4. Click a flag to start playback

**Expected behavior**:
- ✅ Flag buttons render with correct icons
- ✅ Clicking flag starts video immediately
- ✅ Audio/subtitles are set correctly without manual selection

---

## 🐛 Troubleshooting

### Problem: Plugin doesn't appear in Dashboard

**Check**:
1. DLL is in correct location: `%AppData%\Jellyfin\Server\plugins\LanguageSelector\`
2. DLL filename is exact: `Jellyfin.Plugin.LanguageSelector.dll`
3. Jellyfin was restarted after copying DLL
4. Check Jellyfin logs: `%AppData%\Jellyfin\Server\log\log_*.txt`

**Look for**:
- "Plugin loaded: Language Selector" (success)
- Any errors mentioning "LanguageSelector" (failure)

---

### Problem: API returns 404 Not Found

**Possible causes**:
1. Plugin not loaded (check Dashboard → Plugins)
2. Wrong item ID (verify ID exists in Jellyfin)
3. Wrong API key (check Dashboard → API Keys)

**Fix**:
- Verify plugin is loaded in Dashboard
- Use a valid episode/movie ID
- Create a new API key if needed

---

### Problem: Flag buttons don't appear

**Check**:
1. JavaScript loaded (check browser console for errors)
2. Episode has multiple audio/subtitle tracks (API returns options)
3. Correct page (must be on episode detail page, not library view)

**Debug**:
Open browser console (F12) and check:
```javascript
// Check if script loaded
window.languageSelector

// Check if API client available
ApiClient

// Manually fetch options
fetch('/Items/YOUR_ITEM_ID/LanguageOptions', {
  headers: { 'X-Emby-Token': ApiClient.accessToken() }
}).then(r => r.json()).then(console.log)
```

---

### Problem: Wrong language selected on playback

**Possible causes**:
1. Stream indices mismatch with file metadata
2. File has incorrect language tags

**Debug**:
1. Use MediaInfo or ffprobe to check actual file streams:
   ```bash
   ffprobe -show_streams yourfile.mkv
   ```
2. Compare with API response stream indices
3. Check Jellyfin logs during playback for warnings

---

### Problem: Icons show as broken images

**Check**:
1. Icons are embedded in plugin (should be automatic)
2. Browser can access: `/web/configurationpage?name=LanguageSelector/flags/de.svg`
3. Check browser Network tab (F12) for 404 errors

**Fix**:
- Rebuild plugin with `--configuration Release`
- Verify all SVG files exist in `Web/flags/` folder before build
- Clear browser cache (Ctrl+F5)

---

## 📋 Next Steps

After successful installation and basic testing:

1. **Follow the comprehensive testing guide**: `TESTING_GUIDE.md`
2. **Review bug fixes applied**: `BUG_FIXES.md`
3. **Test with different media files** (anime with various language combinations)
4. **Test in different browsers** (Chrome, Firefox, Edge)
5. **Report any issues you encounter**

---

## 💡 Tips

- **Test files**: Use anime episodes with Japanese/German/English audio and subtitles for best results
- **Clear cache**: Always clear browser cache (Ctrl+F5) after updating plugin
- **Check logs**: Jellyfin logs are your friend - check them first when troubleshooting
- **Console is helpful**: Browser console (F12) shows JavaScript errors and debug messages

---

## 🔗 File Locations Reference

### Windows
- **Plugin**: `%AppData%\Jellyfin\Server\plugins\LanguageSelector\`
- **Logs**: `%AppData%\Jellyfin\Server\log\`
- **Config**: `%AppData%\Jellyfin\Server\config\`

### Linux
- **Plugin**: `/var/lib/jellyfin/plugins/LanguageSelector/`
- **Logs**: `/var/log/jellyfin/`
- **Config**: `/etc/jellyfin/`

### Docker
- **Plugin**: `/config/plugins/LanguageSelector/` (inside container)
- **Logs**: `/config/log/` (inside container)
- Map to host: `-v /path/on/host/config:/config`
