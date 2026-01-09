# 🛠️ Installation

Folge diesen Schritten, um das Jellyfin AI Upscaler Plugin auf deinem Server zu installieren.

## 📦 Option 1: Über das Repository (Empfohlen)

1.  Öffne dein Jellyfin-Dashboard.
2.  Gehe zu **Plugins** -> **Katalog**.
3.  Klicke auf das Zahnrad-Symbol oben rechts (Repositories).
4.  Füge die Repository-URL hinzu: `https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/repository.json`
5.  Suche das **AI Upscaler Plugin** im Katalog und klicke auf **Installieren**.
6.  Starte deinen Jellyfin-Server neu.

## 📂 Option 2: Manuelle Installation

1.  Lade die neueste `.zip`-Datei von der [Releases-Seite](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases) herunter.
2.  Extrahiere den Inhalt in dein Jellyfin `plugins` Verzeichnis:
    *   **Windows**: `C:\ProgramData\Jellyfin\Server\plugins\AIUpscaler`
    *   **Linux**: `/var/lib/jellyfin/plugins/AIUpscaler`
    *   **Docker**: Mappe das `/plugins` Volume entsprechend.
3.  Starte deinen Jellyfin-Server neu.

## 📦 AI-Modelle hinzufügen

Das Plugin benötigt ONNX-Modelle, um zu funktionieren.

1.  Erstelle einen Ordner namens `models` in deinem Plugin-Konfigurationsverzeichnis:
    *   **Windows**: `%AppData%\Jellyfin-Server\plugins\configurations\JellyfinUpscalerPlugin\models`
    *   **Linux**: `/etc/jellyfin/plugins/configurations/JellyfinUpscalerPlugin/models`
2.  Lade kompatible `.onnx`-Modelle herunter (z.B. Real-ESRGAN) und platziere sie in diesem Ordner.
3.  Die Modelle werden beim nächsten Start automatisch erkannt.

## ⚙️ Voraussetzungen

*   **Jellyfin Server v10.10.0** oder höher.
*   **Grafikkarte (Optional, aber empfohlen)**: NVIDIA GPU für CUDA oder eine DirectML-kompatible GPU für beste Leistung.
*   **RAM**: Mindestens 4GB (8GB+ empfohlen für 4K Upscaling).
