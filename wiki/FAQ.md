# ❓ Häufig gestellte Fragen (FAQ)

Fragen und Antworten zum Jellyfin AI Upscaler Plugin v1.4.0 STABLE.

---

## 🔥 Allgemeine Fragen

### **Was ist das AI Upscaler Plugin?**
Es ist eine Erweiterung für Jellyfin, die künstliche Intelligenz nutzt, um Videos mit niedriger Auflösung in Echtzeit oder per Vorverarbeitung zu verbessern (z. B. von SD auf 4K).

### **Ist das Plugin kostenlos?**
**Ja!** Das Plugin ist quelloffen und unter der MIT-Lizenz absolut kostenlos.

### **Was ist neu in v1.4.0 STABLE?**
Im Gegensatz zu früheren Versionen nutzt v1.4.0 **echte Hardware-Erkennung** (ONNX Runtime, nvidia-smi), um sicherzustellen, dass die Einstellungen perfekt auf die tatsächlichen Fähigkeiten deines Servers abgestimmt sind.

---

## 🖥️ Hardware & Leistung

### **Welche Hardware benötige ich?**
*   **Minimum**: Eine CPU mit mindestens 4 Kernen oder eine Einsteiger-GPU (GTX 1050).
*   **Empfohlen**: NVIDIA RTX 3060 oder besser für 4K-Echtzeit-Upscaling.
*   **NAS**: Funktioniert am besten mit Pre-Processing (Vorab-Berechnung).

### **Unterstützt es integrierte Grafikkarten?**
**Ja!** Dank **DirectML** läuft das Plugin auch auf Intel UHD/Iris, AMD APUs und Apple Silicon (M1/M2/M3).

### **Wird mein Server dadurch langsam?**
Upscaling ist rechenintensiv. Bei Verwendung von Hardware-Beschleunigung (GPU) bleibt die CPU jedoch frei für andere Aufgaben wie Transkodierung.

---

## 🎮 Bedienung

### **Warum sehe ich keinen AI-Button im Player?**
1. Stelle sicher, dass "Player-Button anzeigen" in den Plugin-Einstellungen aktiviert ist.
2. Leere den Browser-Cache (Strg+F5).
3. Prüfe, ob das Plugin im Jellyfin-Dashboard als "Aktiv" gelistet ist.

### **Speichert das Plugin meine Einstellungen nicht?**
Bitte stelle sicher, dass du Version **1.4.0** installiert hast. In älteren Versionen gab es einen Fehler bei der Speicherung, der in der Stable-Version behoben wurde.

---

## 📞 Support
Falls du weitere Fragen hast, nutze unsere [GitHub Diskussionen](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions).
