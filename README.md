# 🎮 Jellyfin AI Upscaler Plugin v1.4.9.5

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Jellyfin Version](https://img.shields.io/badge/Jellyfin-10.11.x+-00A4DC.svg)](https://jellyfin.org)

> [!CAUTION]
> **🧪 TEST PHASE - v1.4.9.5**
> 
> This version is in testing! AI upscaling works via a separate Docker container.
> Please report bugs in [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues).

---

## 🐳 New Architecture: Docker AI Service

### The Problem with v1.4.9.4

Jellyfin's plugin system tries to load **ALL** `.dll` files as .NET assemblies. Native C++ libraries (ONNX Runtime, CUDA, OpenCV) caused:

```
System.BadImageFormatException: Bad IL format
Failed to load assembly "onnxruntime_providers_shared.dll"
```

**Result:** Plugin was disabled, no AI upscaling possible.

### The Solution: Microservice Architecture

```
┌──────────────────────────────────────────┐
│  Jellyfin Server                         │
│  ┌────────────────────────────────────┐  │
│  │  AI Upscaler Plugin v1.4.9.5       │  │
│  │  ✅ Only 759 KB (instead of 417 MB)│  │
│  │  ✅ No native DLLs                 │  │
│  │  ✅ Sends frames via HTTP          │  │
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
│  │  ✅ Web UI for Model Management    │  │
│  └────────────────────────────────────┘  │
└──────────────────────────────────────────┘
```

### Benefits

| Feature | Old (v1.4.9.4) | New (v1.4.9.5) |
|---------|---------------|----------------|
| **ZIP Size** | 417 MB | 759 KB |
| **Native DLLs** | In plugin → Crashes | In Docker → Isolated |
| **GPU Support** | Issues with Jellyfin | Full CUDA/TensorRT |
| **Updates** | Rebuild plugin | Pull Docker image |

---

## 📥 Installation (2 Steps)

### Step 1: Start Docker AI Service

```bash
# Clone or download docker-ai-service folder
cd docker-ai-service
docker-compose up -d --build
```

Open http://localhost:5000 to see the Web UI.

### Step 2: Install Jellyfin Plugin

1. Open Jellyfin Dashboard → **Plugins** → **Repositories** → **Add**
2. Enter URL:
   ```
   https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/manifest.json
   ```
3. Go to **Catalog**, find "AI Upscaler", install **v1.4.9.5**
4. Restart Jellyfin
5. In Plugin Settings: Set **AI Service URL** to `http://localhost:5000`

---

## 🚀 Features

- **Real-Time Upscaling**: WebGL client-side rendering for live preview
- **Hardware Acceleration**: NVIDIA (CUDA), TensorRT, DirectML, CPU fallback
- **AI Models**: Real-ESRGAN, FSRCNN, SwinIR (via Docker)
- **Hardware Benchmarking**: Automatic detection of optimal settings
- **Dashboard**: AI Upscaler Dashboard in sidebar with job monitoring
- **Comparison View**: Before/after comparison before processing
- **FFmpeg Integration**: Automatic filter injection
- **Job Control API**: Pause, Resume, Cancel via REST API

---

## ⚙️ Configuration

After installation, find settings under **Dashboard → Plugins → AI Upscaler Plugin**.

| Setting | Description |
|---------|-------------|
| **AI Service URL** | URL to Docker container (e.g., `http://nas:5000`) |
| **Enable Plugin** | Global switch |
| **Scaling Factor** | 2x or 4x |
| **Quality Level** | low / medium / high |
| **Hardware Acceleration** | Auto-detect or manual |

---

## 📋 Changelog

### v1.4.9.5 (TEST PHASE)
- **🐳 Docker Microservice Architecture**: AI processing in separate container
- **📦 759 KB instead of 417 MB**: No more native DLLs in plugin
- **🔧 New HttpUpscalerService**: HTTP-based communication with Docker
- **🌐 Web UI**: Model management at http://localhost:5000
- **✅ No more BadImageFormatException**: Jellyfin only loads .NET DLLs

### v1.4.9.4
- Settings Page Fix
- Cross-Platform Support
- Complete DI Registration

### v1.4.9.3
- Verified Service Registration
- Settings Version Fix

---

## 🔧 Troubleshooting

### Plugin won't start
```bash
# Check Docker container
docker ps --filter name=jellyfin-ai-upscaler

# View logs
docker logs jellyfin-ai-upscaler
```

### Upscaling not working
1. Check if Docker is running: `curl http://localhost:5000/health`
2. Check Plugin Settings: AI Service URL correct?
3. Check if model is loaded: http://localhost:5000 → Web UI

### GPU not detected
```bash
# Check NVIDIA runtime
docker run --rm --gpus all nvidia/cuda:12.0-base nvidia-smi
```

---

## 📖 Wiki & Support

- [GitHub Wiki](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki)
- [Issues / Bug Reports](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)

---

## 📜 License

MIT License - See [LICENSE](LICENSE) for details.
