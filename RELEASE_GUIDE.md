# GitHub Release Anleitung

## Schritt 1: Release-Paket erstellen

Erstelle ein ZIP-Archiv mit der DLL:

```powershell
# Navigiere zum Projektverzeichnis
cd C:\Users\Kuscheltier\.zenflow\worktrees\jellyfin-plugin-ba45

# Erstelle das Release-Verzeichnis
New-Item -ItemType Directory -Force -Path "release"

# Kopiere die DLL
Copy-Item "Jellyfin.Plugin.LanguageSelector\bin\Release\net8.0\Jellyfin.Plugin.LanguageSelector.dll" "release\"

# Erstelle das ZIP-Archiv
Compress-Archive -Path "release\Jellyfin.Plugin.LanguageSelector.dll" -DestinationPath "jellyfin-plugin-languageselector_1.0.0.0.zip" -Force
```

Oder manuell:
1. Öffne den Ordner `Jellyfin.Plugin.LanguageSelector\bin\Release\net8.0\`
2. Kopiere die Datei `Jellyfin.Plugin.LanguageSelector.dll`
3. Erstelle einen neuen Ordner und benenne ihn: `jellyfin-plugin-languageselector_1.0.0.0`
4. Füge die DLL in diesen Ordner ein
5. Rechtsklick auf den Ordner → "Send to" → "Compressed (zipped) folder"
6. Benenne die ZIP-Datei: `jellyfin-plugin-languageselector_1.0.0.0.zip`

## Schritt 2: MD5 Checksum berechnen

Berechne den MD5-Hash des ZIP-Archivs:

```powershell
(Get-FileHash -Algorithm MD5 "jellyfin-plugin-languageselector_1.0.0.0.zip").Hash
```

Notiere dir den Hash (z.B. `A1B2C3D4E5F6...`)

## Schritt 3: GitHub Release erstellen

1. Gehe zu: https://github.com/Kuschel-code/jellyfin-plugin-languageselector/releases/new
2. **Tag version**: `v1.0.0`
3. **Release title**: `Language Selector v1.0.0`
4. **Beschreibung**:
   ```markdown
   ## Language Selector - Initial Release

   One-click language selection for Jellyfin with AniWorld-style flag buttons.

   ### Features
   - 🎌 One-click playback with selected audio/subtitle combinations
   - 🇩🇪 German audio support
   - 🇯🇵 Japanese audio with German subtitles (GerSub)
   - 🇺🇸 Japanese audio with English subtitles (EngSub)
   - 🎨 AniWorld-inspired UI design
   - 🔧 Automatic language detection from media files

   ### Installation
   See the [README](https://github.com/Kuschel-code/jellyfin-plugin-languageselector#installation) for installation instructions.

   ### Requirements
   - Jellyfin 10.10.0 or higher
   - .NET 8.0

   ### Known Issues
   - None reported yet

   ### Support
   If you encounter any issues, please open an issue on GitHub.
   ```
5. **Datei hochladen**: Ziehe `jellyfin-plugin-languageselector_1.0.0.0.zip` in den "Attach binaries" Bereich
6. Klicke **Publish release**

## Schritt 4: Manifest aktualisieren

Nach dem Release:

1. Öffne `manifest.json`
2. Ersetze `PLACEHOLDER_MD5_CHECKSUM_HERE` mit dem berechneten MD5-Hash
3. Speichern und committen:
   ```bash
   git add manifest.json
   git commit -m "Update manifest with v1.0.0 checksum"
   git push
   ```

## Schritt 5: Plugin in Jellyfin installieren

Benutzer können jetzt das Plugin installieren:

### Methode 1: Über Plugin-Repository (empfohlen)

1. Jellyfin Admin Dashboard öffnen
2. **Plugins** → **Repositories**
3. **+** klicken
4. **Repository Name**: `Language Selector Repository`
5. **Repository URL**: `https://raw.githubusercontent.com/Kuschel-code/jellyfin-plugin-languageselector/main/manifest.json`
6. **Save**
7. Zu **Catalog** gehen
8. "Language Selector" suchen und installieren
9. Jellyfin Server neustarten

### Methode 2: Manuelle Installation

1. ZIP-Datei vom Release herunterladen
2. DLL entpacken nach: `<jellyfin-data-dir>/plugins/LanguageSelector/Jellyfin.Plugin.LanguageSelector.dll`
3. Jellyfin Server neustarten

## Schritt 6: Testen

1. Installiere das Plugin
2. Öffne eine Episode mit mehreren Audio/Subtitle-Tracks
3. Prüfe, ob die Flaggen-Buttons angezeigt werden
4. Teste die One-Click-Playback-Funktion

## Troubleshooting

### "Plugin not found in catalog"
- Prüfe, ob die `manifest.json` auf GitHub verfügbar ist: `https://raw.githubusercontent.com/Kuschel-code/jellyfin-plugin-languageselector/main/manifest.json`
- Stelle sicher, dass das Repository auf "Public" gestellt ist (für das Manifest)

### "Checksum verification failed"
- Der MD5-Hash in `manifest.json` muss mit dem ZIP-Archiv übereinstimmen
- Berechne den Hash erneut und aktualisiere die `manifest.json`

### Plugin lädt nicht
- Prüfe die Jellyfin-Logs: `<jellyfin-data-dir>/log/`
- Stelle sicher, dass die DLL für .NET 8.0 kompiliert wurde
- Prüfe die Jellyfin-Version (min. 10.10.0 erforderlich)
