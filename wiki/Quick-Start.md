# ⚡ Schnellstart (Quick Start)

Befolge diese Schritte, um dein System in weniger als 5 Minuten startklar zu machen.

## 1. Installation
Installiere das Plugin über den Jellyfin-Katalog (siehe [Installation](Installation)). Starte den Server neu.

## 2. Modelle bereitstellen
Das Plugin wird ohne Modelle ausgeliefert. Lade mindestens ein `.onnx` Modell (z. B. `realesrgan.onnx`) in den Ordner `plugins/configurations/JellyfinUpscalerPlugin/models/`.

## 3. Hardware prüfen
Gehe zu **Dashboard -> Plugins -> AI Upscaler Plugin**.
Klicke auf **"Hardware Benchmark"**. Das Plugin analysiert nun deine CPU und GPU und stellt automatisch die empfohlenen Werte ein.

## 4. Konfiguration speichern
Scrolle nach unten und klicke auf **"💾 Save Configuration"**.

## 5. Film ab!
Öffne einen Film in deinem Browser. In der Steuerleiste unten rechts findest du nun den **🎮 AI** Button. Klicke darauf, um das Upscaling zu aktivieren.

---
**Tipp:** Wenn das Video ruckelt, wähle in den Plugin-Einstellungen einen niedrigeren Skalierungsfaktor (2x statt 4x) oder ein schnelleres Modell wie `FSRCNN`.
