# Installation - So installierst du das Plugin

## 🎯 Die einfache Methode (Plugin-Repository)

So können Benutzer dein Plugin mit nur wenigen Klicks installieren:

### Schritt 1: Repository in Jellyfin hinzufügen

1. Öffne **Jellyfin Admin Dashboard**
2. Gehe zu **Plugins** → **Repositories**
3. Klicke auf **+** (Add Repository)
4. Gib folgende Daten ein:
   - **Repository Name**: `Language Selector Repository`
   - **Repository URL**: `https://raw.githubusercontent.com/Kuschel-code/jellyfin-plugin-languageselector/main/manifest.json`
5. Klicke **Save**

### Schritt 2: Plugin installieren

1. Gehe zu **Plugins** → **Catalog**
2. Suche nach **"Language Selector"**
3. Klicke **Install**
4. **Jellyfin Server neustarten**

### Schritt 3: Nutzen!

1. Öffne eine Anime-Episode mit mehreren Audio/Untertitel-Spuren
2. Du siehst jetzt die Flaggen-Buttons 🎌🇩🇪🇯🇵
3. Klicke auf eine Flagge → Video startet sofort mit der gewählten Sprache!

---

## ⚙️ Manuelle Installation (Fallback)

Falls die Repository-Methode nicht funktioniert:

### Schritt 1: Download

1. Gehe zu [GitHub Releases](https://github.com/Kuschel-code/jellyfin-plugin-languageselector/releases)
2. Lade `jellyfin-plugin-languageselector_1.0.0.0.zip` herunter
3. Entpacke die ZIP-Datei → du erhältst `Jellyfin.Plugin.LanguageSelector.dll`

### Schritt 2: Installation

1. Finde dein Jellyfin-Datenverzeichnis:
   - **Windows**: `C:\ProgramData\Jellyfin\Server\` oder `%APPDATA%\Jellyfin\Server\`
   - **Linux**: `/var/lib/jellyfin/` oder `~/.local/share/jellyfin/`
   - **Docker**: `/config/`

2. Navigiere zu `<jellyfin-data-dir>/plugins/`
3. Erstelle einen Ordner namens `LanguageSelector`
4. Kopiere `Jellyfin.Plugin.LanguageSelector.dll` in diesen Ordner

   Finale Struktur:
   ```
   <jellyfin-data-dir>/
   └── plugins/
       └── LanguageSelector/
           └── Jellyfin.Plugin.LanguageSelector.dll
   ```

5. **Jellyfin Server neustarten**

### Schritt 3: Verifizierung

1. Öffne **Admin Dashboard** → **Plugins**
2. Du solltest **"Language Selector"** in der Liste sehen
3. Status: **Active** ✅

---

## 🚀 Erste Schritte

### Plugin konfigurieren (optional)

1. Gehe zu **Plugins** → **Language Selector**
2. Aktiviere **"Auto Detect Languages"** (Standard: An)
3. Wähle bevorzugte Sprachen: `ger`, `jpn`, `eng`
4. **Save**

### Plugin testen

1. Öffne eine Episode mit mehreren Sprachen (z.B. Anime mit GerDub + GerSub + EngSub)
2. Du siehst die Flaggen-Leiste unter dem Episoden-Titel
3. Teste jede Flagge:
   - 🇩🇪 **DE**: Deutsches Audio, keine Untertitel
   - 🎌🇩🇪 **JP-DE**: Japanisches Audio, deutsche Untertitel
   - 🎌🇺🇸 **JP-EN**: Japanisches Audio, englische Untertitel

---

## ❓ Troubleshooting

### Plugin erscheint nicht im Katalog

**Ursache**: Repository-URL ist falsch oder GitHub ist nicht erreichbar

**Lösung**:
1. Prüfe die URL: `https://raw.githubusercontent.com/Kuschel-code/jellyfin-plugin-languageselector/main/manifest.json`
2. Öffne die URL im Browser → sollte JSON anzeigen
3. Falls 404-Fehler: Warte einige Minuten (GitHub-Cache)
4. Stelle sicher, dass das Repository **PUBLIC** ist (zumindest für die `manifest.json`)

### Flaggen werden nicht angezeigt

**Ursache**: JavaScript wurde nicht geladen oder keine passenden Spuren gefunden

**Lösung**:
1. Öffne Browser-Konsole (F12)
2. Suche nach Fehlermeldungen
3. Prüfe, ob die Datei mehrere Audio/Subtitle-Tracks hat:
   - Jellyfin Dashboard → **Libraries** → Episode auswählen → **Media Info**
   - Sollte mindestens 1 Audio-Track mit Sprache (ger/jpn/eng) haben

### Playback startet nicht

**Ursache**: Jellyfin PlaybackManager nicht gefunden

**Lösung**:
1. Aktualisiere Jellyfin auf mindestens Version **10.10.0**
2. Verwende einen kompatiblen Client (Web-Browser, keine native App)
3. Prüfe Browser-Konsole für JavaScript-Fehler

### Checksum-Fehler beim Download

**Ursache**: Die `manifest.json` hat einen falschen MD5-Hash

**Lösung**:
1. Lade das Plugin manuell herunter (siehe "Manuelle Installation")
2. Oder: Melde das Problem auf GitHub Issues

---

## 📚 Weitere Dokumentation

- [README.md](README.md) - Projekt-Übersicht und Features
- [RELEASE_GUIDE.md](RELEASE_GUIDE.md) - Für Entwickler: Wie man ein Release erstellt
- [TESTING_GUIDE.md](TESTING_GUIDE.md) - Testing-Strategie
- [BUG_FIXES.md](BUG_FIXES.md) - Bekannte Bugs und Fixes

---

## 💡 Tipp für Anime-Fans

Organisiere deine Bibliothek so:

```
/Anime
  /Sword Art Online
    /Season 01
      S01E01.mkv (Audio: ger, jpn | Subs: ger, eng)
      S01E02.mkv (Audio: ger, jpn | Subs: ger, eng)
```

Das Plugin erkennt automatisch die Spuren und zeigt die passenden Flaggen!

Viel Spaß! 🎉
