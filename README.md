# 🎮 Jellyfin AI Upscaler Plugin v1.4.0 STABLE

[![Lizenz: MIT](https://img.shields.io/badge/Lizenz-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Jellyfin Version](https://img.shields.io/badge/Jellyfin-10.10.x-00A4DC.svg)](https://jellyfin.org)

Ein fortschrittliches, AI-gestütztes Videoverbesserungs-Plugin für Jellyfin. Verbessere deine Medien in Echtzeit oder per Vorverarbeitung mit modernsten neuronalen Netzen.

## 🚀 Hauptfunktionen

- **Echtzeit-Upscaling**: Erlebe kristallklare Bilder während der Wiedergabe.
- **Hardware-Beschleunigung**: Volle Unterstützung für NVIDIA (CUDA) und DirectML (AMD/Intel).
- **Mehrere AI-Modelle**: Unterstützung für Real-ESRGAN, SwinIR, Waifu2x und mehr.
- **Hardware-Benchmarking**: Integrierte Tools zur Erkennung und Optimierung basierend auf der Server-Leistung.
- **Nahtlose Integration**: Modernes Dashboard und Quick-Access-Menü direkt im Player.

## 🛠️ Installation

### Repository-Methode (Empfohlen)
1. Öffne dein Jellyfin-Dashboard.
2. Gehe zu **Plugins** > **Repositories**.
3. Füge ein neues Repository mit folgender URL hinzu:
   `https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/repository-jellyfin.json`
4. Gehe zum **Katalog**, suche nach "AI Upscaler Plugin" und installiere Version **1.4.0**.
5. Starte Jellyfin neu.

## ⚙️ Konfiguration

Nach der Installation findest du die Einstellungen unter **Dashboard > Plugins > AI Upscaler Plugin**.

- **Plugin aktivieren**: Globaler Schalter für den Upscaler.
- **Skalierungsfaktor**: Wähle zwischen 2x, 4x oder benutzerdefinierter Skalierung.
- **Hardware-Erkennung**: Das Plugin erkennt automatisch verfügbare GPUs und schlägt optimale Einstellungen vor.

## 📖 Wiki & Support

Detaillierte Anleitungen, Hardware-Listen und Fehlerbehebung findest du in unserem **[GitHub Wiki](wiki/Home.md)**.

- [Erste Schritte](wiki/Quick-Start.md)
- [Hardware-Kompatibilität](wiki/Hardware-Compatibility.md)
- [Performance-Benchmarks](wiki/Performance-Benchmarks.md)
- [FAQ](wiki/FAQ.md)

## 📄 Lizenz

Dieses Projekt lizenziert unter der MIT-Lizenz - siehe [LICENSE](LICENSE) für Details.
