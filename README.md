# 🎮 Jellyfin AI Upscaler Plugin v1.5.2.2

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Jellyfin Version](https://img.shields.io/badge/Jellyfin-10.11.x+-00A4DC.svg)](https://jellyfin.org)
[![Docker Hub](https://img.shields.io/docker/pulls/kuscheltier/jellyfin-ai-upscaler?logo=docker&label=Docker%20Hub)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Docker Image](https://img.shields.io/docker/v/kuscheltier/jellyfin-ai-upscaler?logo=docker&label=Latest)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Project Website](https://img.shields.io/badge/Website-Visit-blueviolet)](https://transcendent-blancmange-824967.netlify.app)

> [!CAUTION]
> **🧪 TEST PHASE - v1.5.2.2 (Player Button Fix + Intel GPU Fix)**
>
> This release fixes the **player button not showing** (global script injection via index.html) and **Intel OpenVINO GPU running on CPU** (updated compute runtime + explicit GPU_FP32 device targeting).
>
> **🐳 Docker Images (v1.5.4):**
> *   `kuscheltier/jellyfin-ai-upscaler:1.5.4` (NVIDIA CUDA + cuDNN 9)
> *   `kuscheltier/jellyfin-ai-upscaler:1.5.4-amd` (AMD ROCm)
> *   `kuscheltier/jellyfin-ai-upscaler:1.5.4-intel` (Intel Arc/iGPU OpenVINO)
> *   `kuscheltier/jellyfin-ai-upscaler:1.5.4-apple` (macOS Apple Silicon)
> *   `kuscheltier/jellyfin-ai-upscaler:1.5.4-cpu` (CPU Only)
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
3. Go to **Catalog**, find "AI Upscaler", install the latest version
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

### v1.5.2.2 (Player Button Fix + Intel GPU Fix) — Docker v1.5.4
> **🎮 Fixes Issue #45: Player button not showing + Intel OpenVINO GPU running on CPU.**

- **🎮 Fixed**: Player upscale button not showing — global script injection into `index.html` (same approach as Intro Skipper plugin)
- **🔧 Fixed**: `player-integration.js` rewritten for Jellyfin 10.11+ SPA navigation using `viewshow` events
- **🔧 Fixed**: Intel OpenVINO GPU falling back to CPU — updated Intel compute-runtime from official PPA + explicit `GPU_FP32` device targeting
- **🐳 Docker 1.5.4**: Intel Dockerfile updated with latest NEO compute-runtime + Level-Zero for Arc A310/A380/A770

### v1.5.2.1 (Security & Bug Fix) — Docker v1.5.4
> **🔒 Security hardening + bug fixes across plugin and Docker.**

- **🔒 Security**: SSH command injection prevention — regex validation + `ProcessStartInfo.ArgumentList`
- **🔒 Security**: Path traversal protection — blocks system directory prefixes
- **🔒 Security**: SSH hardening — key-only auth (`PasswordAuthentication no`), conditional sshd start
- **🔧 Fixed**: Progress tracking was stuck at 0%/50% — now uses time-based estimation
- **🔧 Fixed**: Pause/resume job ID resolution (`Path.GetFileNameWithoutExtension` bug)
- **🔄 Synced**: Model list aligned (14 models) between C# plugin and Python backend
- **🐳 Docker 1.5.4**: Scale parameter validation, async file I/O, pinned AMD ROCm base image
- **⚙️ CI/CD**: Rewritten build pipeline for .NET 9.0 with auto checksum & GitHub release

### v1.5.2.0 (GPU Fix) - **Fixes Issue #44**
> **🔧 Fixes NVIDIA GPU falling back to CPU-only processing.**

- **🔧 Fixed**: cuDNN version mismatch — upgraded base image to `nvidia/cuda:12.6.3-cudnn-runtime-ubuntu22.04` (cuDNN 9)
- **🔧 Fixed**: ONNX Runtime provider detection now intelligent — only requests available providers
- **🔧 Fixed**: Removed spurious `OpenVINOExecutionProvider` warning on NVIDIA images
- **✅ Result**: NVIDIA GPUs now correctly use CUDA/TensorRT acceleration instead of CPU fallback

### v1.5.1.0 (Remote Transcoding / SSH) - **TEST VERSION**
> **⚠️ This is a TEST version!**
> Introduces a new architecture for remote transcoding via SSH. Please report any connection issues!

- **🚀 Remote Transcoding**: Connects to Docker via SSH to execute FFmpeg.
- **☁️ Multi-Architecture**: Dedicated Docker images for NVIDIA, Intel, Apple Silicon, and CPU.
- **📂 Path Mapping**: Map local media paths to remote Docker paths for direct file access.
- **🔒 SSH Authentication**: Support for SSH Keys and Password auth.
- **✨ Enhanced UI**: New configuration section for Remote Transcoding.

### v1.5.1.1 (Hotfix)
- **🔧 Fixed**: SSH configuration was not being saved/loaded correctly.
- **✨ Added**: "Test SSH Connection" button now functional.
- **🔌 Added**: Backend API endpoint `/api/upscaler/ssh/test` for connection testing.

### v1.5.0.9
- **Fixed**: 'selectedModelId is undefined' error preventing models from loading.

### v1.5.0.8
- **Fixed**: Localization issues with 'Settings saved' message.

### v1.5.0.7
- **Fixed**: 'require is not defined' error in settings page.

### v1.5.0.6
- **Fixed**: Dynamic URL resolution for AI Service.

### v1.5.0.5
- **Fixed**: Loading spinner compatibility for Jellyfin <10.9.
- **Improved**: Dashboard hardware status & connection checks.

### v1.5.0.3 - v1.5.0.4
- **Fixed**: Save Configuration button issues.
- **Added**: Test Connection button.

### v1.5.0.2
- **Fixed**: Settings not saving (#36) - AiServiceUrl now persists correctly.


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

- [Project Website](https://transcendent-blancmange-824967.netlify.app)
- [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)
- [GitHub Wiki](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki)

---

## 📜 License

MIT License - See [LICENSE](LICENSE) for details.
