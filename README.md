# 🎮 Jellyfin AI Upscaler Plugin v1.4.9.5

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Jellyfin Version](https://img.shields.io/badge/Jellyfin-10.11.x+-00A4DC.svg)](https://jellyfin.org)

> [!CAUTION]
> **🧪 TEST PHASE - v1.4.9.5**
> 
> Diese Version befindet sich in der Testphase! AI-Upscaling funktioniert über einen separaten Docker Container.
> Bitte melde Bugs im [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues).

---

## 🐳 Neue Architektur: Docker AI Service

### Das Problem mit v1.4.9.4

Jellyfin's Plugin-System versucht **ALLE** `.dll` Dateien als .NET Assemblies zu laden. Native C++ Libraries (ONNX Runtime, CUDA, OpenCV) verursachten:

```
System.BadImageFormatException: Bad IL format
Failed to load assembly "onnxruntime_providers_shared.dll"
```

**Resultat:** Plugin wurde deaktiviert, keine AI-Upscaling möglich.

### Die Lösung: Microservice Architektur

```
┌──────────────────────────────────────────┐
│  Jellyfin Server                         │
│  ┌────────────────────────────────────┐  │
│  │  AI Upscaler Plugin v1.4.9.5       │  │
│  │  ✅ Nur 759 KB (statt 417 MB!)     │  │
│  │  ✅ Keine nativen DLLs             │  │
│  │  ✅ Sendet Frames via HTTP         │  │
│  └──────────────┬─────────────────────┘  │
└─────────────────┼────────────────────────┘
                  │ HTTP POST /upscale
                  ▼
┌──────────────────────────────────────────┐
│  AI Upscaler Docker Container            │
│  ┌────────────────────────────────────┐  │
│  │  Python + FastAPI + ONNX Runtime   │  │
│  │  ✅ CUDA / TensorRT / DirectML     │  │
│  │  ✅ Real-ESRGAN, FSRCNN Models     │  │
│  │  ✅ Web UI für Model Management    │  │
│  └────────────────────────────────────┘  │
└──────────────────────────────────────────┘
```

### Vorteile

| Feature | Alt (v1.4.9.4) | Neu (v1.4.9.5) |
|---------|---------------|----------------|
| **ZIP Größe** | 417 MB | 759 KB |
| **Native DLLs** | Im Plugin → Crashes | Im Docker → Isoliert |
| **GPU Support** | Probleme mit Jellyfin | Voller CUDA/TensorRT |
| **Updates** | Neues Plugin bauen | Docker Image pullen |

---

## 📥 Installation (2 Schritte)

### Schritt 1: Docker AI Service starten

```bash
# Clone oder download docker-ai-service Ordner
cd docker-ai-service
docker-compose up -d --build
```

Öffne http://localhost:5000 um die Web UI zu sehen.

### Schritt 2: Jellyfin Plugin installieren

1. Öffne Jellyfin Dashboard → **Plugins** → **Repositories** → **Add**
2. URL eingeben:
   ```
   https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/manifest.json
   ```
3. Gehe zu **Catalog**, finde "AI Upscaler", installiere **v1.4.9.5**
4. Jellyfin neustarten
5. In Plugin Settings: **AI Service URL** auf `http://localhost:5000` setzen

---

## 🚀 Features

- **Real-Time Upscaling**: WebGL client-side rendering für Live-Preview
- **Hardware Acceleration**: NVIDIA (CUDA), TensorRT, DirectML, CPU Fallback
- **AI Models**: Real-ESRGAN, FSRCNN, SwinIR (via Docker)
- **Hardware Benchmarking**: Automatische Erkennung der optimalen Einstellungen
- **Dashboard**: AI Upscaler Dashboard im Sidebar mit Job-Monitoring
- **Comparison View**: Vorher/Nachher Vergleich vor dem Processing
- **FFmpeg Integration**: Automatische Filter-Injection
- **Job Control API**: Pause, Resume, Cancel via REST API

---

## ⚙️ Konfiguration

Nach der Installation findest du die Einstellungen unter **Dashboard → Plugins → AI Upscaler Plugin**.

| Setting | Beschreibung |
|---------|-------------|
| **AI Service URL** | URL zum Docker Container (z.B. `http://nas:5000`) |
| **Enable Plugin** | Globaler Schalter |
| **Scaling Factor** | 2x oder 4x |
| **Quality Level** | low / medium / high |
| **Hardware Acceleration** | Auto-detect oder manuell |

---

## 📋 Changelog

### v1.4.9.5 (TEST PHASE)
- **🐳 Docker Microservice Architecture**: AI Processing in separatem Container
- **📦 759 KB statt 417 MB**: Keine nativen DLLs mehr im Plugin
- **🔧 Neuer HttpUpscalerService**: HTTP-basierte Kommunikation mit Docker
- **🌐 Web UI**: Model Management unter http://localhost:5000
- **✅ Kein BadImageFormatException mehr**: Jellyfin lädt nur .NET DLLs

### v1.4.9.4
- Settings Page Fix
- Cross-Platform Support
- Complete DI Registration

### v1.4.9.3
- Verified Service Registration
- Settings Version Fix

---

## 🔧 Troubleshooting

### Plugin startet nicht
```bash
# Docker Container prüfen
docker ps --filter name=jellyfin-ai-upscaler

# Logs anschauen
docker logs jellyfin-ai-upscaler
```

### Upscaling funktioniert nicht
1. Prüfe ob Docker läuft: `curl http://localhost:5000/health`
2. Prüfe Plugin Settings: AI Service URL korrekt?
3. Prüfe ob Model geladen: http://localhost:5000 → Web UI

### GPU wird nicht erkannt
```bash
# NVIDIA Runtime prüfen
docker run --rm --gpus all nvidia/cuda:12.0-base nvidia-smi
```

---

## 📖 Wiki & Support

- [GitHub Wiki](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki)
- [Issues / Bug Reports](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)

---

## 📜 License

MIT License - See [LICENSE](LICENSE) for details.
