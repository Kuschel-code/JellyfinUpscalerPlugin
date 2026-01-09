# 🎨 AI-Modelle

Das AI Upscaler Plugin unterstützt verschiedene neuronale Netze, die jeweils für unterschiedliche Inhalte und Hardware-Leistung optimiert sind.

## 🌟 Hauptmodelle

### **Real-ESRGAN**
*   **Bestens geeignet für**: Realfilme, Naturaufnahmen, Fotos.
*   **Vorteile**: Exzellente Texturwiederherstellung, sehr realistisch.
*   **Anforderung**: Hoch (NVIDIA RTX 30/40 empfohlen).

### **ESRGAN Pro**
*   **Bestens geeignet für**: Kinofilme, TV-Serien.
*   **Vorteile**: Guter Kompromiss zwischen Schärfe und Natürlichkeit.
*   **Anforderung**: Mittel.

### **SwinIR**
*   **Bestens geeignet für**: Komplexe Szenen, Bildrauschen.
*   **Vorteile**: Nutzt Transformer-Technologie für präzise Details.
*   **Anforderung**: Hoch.

### **Waifu2x**
*   **Bestens geeignet für**: Anime, Cartoons, gezeichnete Kunst.
*   **Vorteile**: Reduziert Kompressionsartefakte in flächigen Farben extrem gut.
*   **Anforderung**: Gering bis Mittel.

## ⚡ Leichtgewichtige Modelle

### **FSRCNN / SRCNN**
*   **Bestens geeignet für**: Schwächere Hardware (NAS, ältere Laptops).
*   **Vorteile**: Sehr schnell, deutlich besser als herkömmliche Skalierung.
*   **Anforderung**: Gering.

## 📂 Installation von Modellen
1.  Lade die `.onnx`-Version deines gewünschten Modells herunter.
2.  Navigiere zum Plugin-Datenordner:
    *   **Windows**: `%AppData%\Jellyfin-Server\plugins\configurations\JellyfinUpscalerPlugin\models`
    *   **Linux**: `/etc/jellyfin/plugins/configurations/JellyfinUpscalerPlugin/models`
3.  Platziere die Datei im `models`-Ordner.
4.  Starte Jellyfin neu, damit das Modell in den Einstellungen erscheint.
