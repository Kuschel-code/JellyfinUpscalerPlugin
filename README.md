# 🎮 Jellyfin AI Upscaler Plugin v1.5.0.2

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Jellyfin Version](https://img.shields.io/badge/Jellyfin-10.11.x+-00A4DC.svg)](https://jellyfin.org)
[![Docker Hub](https://img.shields.io/docker/pulls/kuscheltier/jellyfin-ai-upscaler?logo=docker&label=Docker%20Hub)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Docker Image](https://img.shields.io/docker/v/kuscheltier/jellyfin-ai-upscaler?logo=docker&label=Latest)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Project Website](https://img.shields.io/badge/Website-Visit-blueviolet)](https://jellyfin-upscale-ai.base44.app)

> [!CAUTION]
> **🧪 TEST PHASE - v1.5.0.2**
> 
> This is an **EXPERIMENTAL** version with the new Docker AI microservice architecture!
> AI upscaling now runs in a separate Docker container instead of directly in Jellyfin.
> 
> **🐳 Docker Image:** [kuscheltier/jellyfin-ai-upscaler](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
> 
> **Please report bugs:** [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)

---

## 🐳 New Architecture: Docker AI Service

### Why Docker? (Problem with v1.4.9.x)

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
│  │  AI Upscaler Plugin v1.5.0.0       │  │
│  │  ✅ Only ~1.6 MB (instead of 417MB)│  │
│  │  ✅ No native DLLs                 │  │
│  │  ✅ Sends frames via HTTP          │  │
│  └──────────────┬─────────────────────┘  │
└─────────────────┼────────────────────────┘
                  │ HTTP POST /upscale
                  ▼
┌──────────────────────────────────────────┐
│  AI Upscaler Docker Container            │
│  ┌────────────────────────────────────┐  │
│  │  Python + FastAPI + OpenCV DNN     │  │
│  │  ✅ CUDA / GPU Acceleration        │  │
│  │  ✅ FSRCNN, ESPCN, LapSRN, EDSR    │  │
│  │  ✅ Web UI for Model Management    │  │
│  └────────────────────────────────────┘  │
└──────────────────────────────────────────┘
```

### Benefits

| Feature | Old (v1.4.9.x) | New (v1.5.0.0) |
|---------|----------------|----------------|
| **ZIP Size** | 417 MB | ~1.6 MB |
| **Native DLLs** | In plugin → Crashes | In Docker → Isolated |
| **GPU Support** | Issues with Jellyfin | Full CUDA support |
| **Updates** | Rebuild plugin | Pull Docker image |

---

## 📥 Installation (2 Steps)

### Step 1: Start Docker AI Service

**Option A - Docker Hub (easiest):**
```bash
docker run -d --name jellyfin-ai-upscaler \
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:latest
```

**Option B - Build locally:**
```bash
cd docker-ai-service
docker-compose up -d --build
```

Open http://YOUR_SERVER_IP:5000 to see the Web UI.

### Step 2: Install Jellyfin Plugin

1. Open Jellyfin Dashboard → **Plugins** → **Repositories** → **Add**
2. Enter URL:
   ```
   https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/manifest.json
   ```
3. Go to **Catalog**, find "AI Upscaler", install **v1.5.0.0**
4. Restart Jellyfin
5. In Plugin Settings: Set **AI Service URL** to `http://YOUR_SERVER_IP:5000`

---

## 🚀 Features

- **Docker Microservice**: AI runs isolated in a container (no DLL conflicts!)
- **Multiple AI Models**: FSRCNN, ESPCN, LapSRN, EDSR (2x, 3x, 4x upscaling)
- **Web UI**: Manage models at http://YOUR_SERVER_IP:5000
- **Hardware Detection**: Automatic GPU/CPU detection
- **Dashboard**: Job monitoring in Jellyfin sidebar
- **FFmpeg Integration**: Automatic filter injection

---

## ⚙️ Configuration

After installation, find settings under **Dashboard → Plugins → AI Upscaler Plugin**.

| Setting | Description |
|---------|-------------|
| **AI Service URL** | URL to Docker container (e.g., `http://nas:5000`) |
| **Enable Plugin** | Global switch |
| **Scaling Factor** | 2x, 3x, or 4x |
| **Quality Level** | low / medium / high |

---

## 📋 Changelog

### v1.5.0.1 (Hotfix)
- **🔧 Fixed #34**: Plugin initialization error (HardwareBenchmarkService DI)
- **🔧 Fixed #33**: Checksum mismatch during installation
- **🔷 Added #32**: Intel GPU/iGPU support via OpenVINO (Dockerfile.intel)

### v1.5.0.0 (TEST PHASE)
- **🐳 Docker Microservice Architecture**: AI processing in separate container
- **📦 ~1.6 MB instead of 417 MB**: No more native DLLs in plugin
- **🔧 OpenCV DNN Models**: FSRCNN, ESPCN, LapSRN, EDSR from public sources
- **🌐 Web UI**: Model management at http://localhost:5000
- **✅ Fixed version format**: 4-part version for Jellyfin compatibility

### v1.4.9.4
- Settings Page Fix
- Cross-Platform Support
- Complete DI Registration

---

## 🔧 Troubleshooting

### Plugin shows "Not Supported"
1. Make sure you uninstalled old versions (1.4.9.x)
2. Delete old plugin folder from Jellyfin plugins directory
3. Restart Jellyfin
4. Install v1.5.0.0 fresh from repository

### Plugin won't start
```bash
# Check Docker container
docker ps --filter name=jellyfin-ai-upscaler

# View logs
docker logs jellyfin-ai-upscaler
```

### Upscaling not working
1. Check if Docker is running: `curl http://YOUR_IP:5000/status`
2. Check Plugin Settings: AI Service URL correct?
3. Check if model is loaded: http://YOUR_IP:5000 → Web UI

---

## 📖 Support

- [Project Website](https://jellyfin-upscale-ai.base44.app)
- [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)
- [GitHub Wiki](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki)

---

## 📜 License

MIT License - See [LICENSE](LICENSE) for details.
