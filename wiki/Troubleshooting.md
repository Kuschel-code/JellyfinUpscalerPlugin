# 🔍 Fehlerbehebung (Troubleshooting)

Hier findest du Lösungen für häufig auftretende Probleme mit dem AI Upscaler Plugin.

---

## ❌ Häufige Probleme

### 🚫 Plugin funktioniert nicht
**Symptome:** Keine Bildverbesserung, Button fehlt im Player.
**Lösungen:**
1. Jellyfin-Server neu starten.
2. Prüfen, ob das Plugin im Dashboard aktiviert ist.
3. Hardware-Kompatibilität verifizieren (siehe [Hardware](Hardware-Compatibility)).
4. Grafikkartentreiber auf den neuesten Stand bringen.

### 🐌 Schlechte Leistung
**Symptome:** Ruckeln, Verzögerungen, hohe CPU-Last.
**Lösungen:**
1. Qualitäts-Preset senken (High → Medium oder Low).
2. Skalierungsfaktor reduzieren (4x → 2x).
3. "Hardware-Beschleunigung" in den Einstellungen aktivieren.
4. Prüfen, ob andere rechenintensive Aufgaben auf dem Server laufen.

### 🎨 Bildfehler (Artefakte)
**Symptome:** Unschärfe, Geisterbilder, falsche Farben.
**Lösungen:**
1. Anderes AI-Modell ausprobieren (z. B. SwinIR statt Real-ESRGAN).
2. Sicherstellen, dass die Modelldateien (.onnx) nicht beschädigt sind.
3. Plugin auf die neueste Version aktualisieren.

---

## 🛠️ Fortgeschrittene Analyse

### 📊 Performance-Diagnose
Überprüfe die Jellyfin-Logs (`Dashboard -> Protokolle`) auf Einträge mit dem Schlagwort `AI Upscaler`. Dort findest du detaillierte Fehlermeldungen zur Hardware-Initialisierung.

### 🔧 Konfiguration zurücksetzen
Falls das Plugin instabil läuft:
1. Jellyfin stoppen.
2. Die Datei `JellyfinUpscalerPlugin.xml` im Konfigurationsordner löschen.
3. Jellyfin starten und neu konfigurieren.

---

## 📞 Weiterführende Hilfe
Falls dein Problem weiterhin besteht, besuche bitte unsere [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues) oder die [Community-Diskussionen](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions).
