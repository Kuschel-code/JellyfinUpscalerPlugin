# ⚙️ Konfigurations-Anleitung

Das AI Upscaler Plugin bietet umfangreiche Einstellungen, um die Bildqualität und die Systemleistung optimal auszubalancieren.

## 🛠️ Basis-Einstellungen
- **Plugin aktivieren**: Der Hauptschalter. Wenn deaktiviert, wird die gesamte Upscaling-Logik übersprungen.
- **Standard AI-Modell**: Das neuronale Netzwerk, das für die Verbesserung verwendet wird (z. B. Real-ESRGAN).
- **Skalierungsfaktor**: Wähle zwischen 2x, 3x oder 4x Upscaling. Höhere Faktoren benötigen deutlich mehr Rechenleistung.
- **Qualitätsstufe**: Passt die interne Präzision der Modelle an (Low, Medium, High).

## 🔧 Hardware-Einstellungen
- **Hardware-Beschleunigung**: Dringend empfohlen, wenn du eine GPU (NVIDIA, AMD oder Intel) besitzt.
- **Max VRAM Nutzung**: Begrenzt den Grafikspeicher, den das Plugin verbrauchen darf.
- **CPU Threads**: Anzahl der gleichzeitigen Threads für die Bildverarbeitung. Empfehlung: Die Hälfte deiner physischen Kerne für beste Stabilität.

## 📊 Live Hardware Status
Dieser Bereich zeigt Echtzeitdaten deines Servers an:
- **CPU Status**: Zeigt den erkannten Prozessor und die aktuelle Kern-Auslastung.
- **GPU Status**: Zeigt die erkannte GPU (z. B. NVIDIA RTX 3080) und den Beschleunigungs-Provider (CUDA/DirectML) an.

## 🔍 AI Vergleichsvorschau (Comparison Preview)
Nutze dieses Tool, um deine Einstellungen zu prüfen:
1.  **Element wählen**: Suche einen Film oder eine Episode aus dem Dropdown-Menü aus.
2.  **Generieren**: Klicke auf "✨ Generate Preview".
3.  **Vergleichen**: Betrachte die Bilder nebeneinander. Die AI-verbesserte Version befindet sich rechts.

## 🎬 Video Player Integration
- **Player-Button anzeigen**: Schaltet die Sichtbarkeit des "🎮 AI"-Buttons in der Player-Steuerung um.
- **Button-Position**: Wähle, wo der Button in der Player-Leiste erscheinen soll.
- **Benachrichtigungen**: Aktiviert oder deaktiviert Status-Popups während der Wiedergabe.
