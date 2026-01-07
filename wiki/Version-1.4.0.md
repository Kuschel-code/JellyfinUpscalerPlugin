# 🚀 Version 1.4.0 STABLE - Hardware Intelligence Update

## 🎉 **Release-Informationen**

- **Veröffentlichungsdatum:** 8. Januar 2026
- **Version:** 1.4.0.0 STABLE
- **Kompatibilität:** Jellyfin 10.10.x
- **Status:** Produktion (Stabil)

---

## 🔥 **Hauptverbesserungen**

### **Echte Hardware-Erkennung**
*   **Keine Simulationen mehr**: Das Plugin nutzt nun `nvidia-smi` und die ONNX Runtime API, um echte Hardware-Daten zu erfassen.
*   **CUDA & DirectML**: Native Unterstützung für NVIDIA Tensor-Kerne und Windows DirectML.
*   **Intelligente Empfehlungen**: Automatische Auswahl des besten Modells basierend auf deiner GPU-Leistung.

### **Synchronisierte Konfiguration**
*   **Fehlerbehebung**: Ein kritischer Fehler, bei dem Einstellungen nicht gespeichert wurden, wurde durch die Angleichung der Datenmodelle behoben.
*   **Dashboard-Update**: Neue Live-Hardware-Anzeige und verbesserte Vergleichsvorschau.

### **Optimierter AI-Kern**
*   **OOM-Schutz**: Intelligente Speicherverwaltung verhindert Abstürze (Out-of-Memory) bei hochauflösenden Previews.
*   **Semaphore-Steuerung**: Begrenzt gleichzeitige Frame-Verarbeitung, um die Systemstabilität zu gewährleisten.

---

## 🛠️ **Technische Änderungen**
- **UpscalerCore**: Vollständige Implementierung der ONNX-Inferenz.
- **VideoProcessor**: Stabilisierung der Pipeline durch verbesserte FFmpeg-Integration.
- **Plugin-Architektur**: Upgrade auf .NET 8 und Jellyfin 10.10 SDK-Standards.

---

**Vielen Dank an die Community für das Feedback zur ALPHA-Version! v1.4.0 markiert den Übergang zu einem professionellen Tool für jeden Jellyfin-Nutzer.**
