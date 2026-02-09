# 🎮 Jellyfin AI Upscaler Plugin

Welcome to the official documentation for the **Jellyfin AI Upscaler Plugin** – an advanced AI-powered video upscaling plugin that uses a Docker microservice architecture to enhance your media library.

> **Current Version: v1.5.1.1** (SSH Remote Transcoding Edition)

---

## 🚀 What is the AI Upscaler?

The AI Upscaler Plugin enhances low-resolution video content (SD/HD → 4K) using neural network models. Unlike traditional scaling, AI upscaling reconstructs real detail, textures, and sharpness.

### Architecture Overview

```
┌────────────────────────┐         HTTP          ┌──────────────────────────┐
│   Jellyfin Server      │ ──────────────────────►│  Docker AI Container     │
│                        │      Port 5000        │                          │
│  ┌──────────────────┐  │                        │  ┌────────────────────┐  │
│  │  AI Upscaler     │  │         SSH            │  │  Python + FastAPI  │  │
│  │  Plugin (1.6 MB) │──│──────────────────────►│  │  OpenCV DNN / ONNX │  │
│  │                  │  │      Port 2222         │  │  CUDA / ROCm       │  │
│  └──────────────────┘  │                        │  └────────────────────┘  │
└────────────────────────┘                        └──────────────────────────┘
```

**Two communication paths:**
- **HTTP** (Port 5000) → AI upscaling requests (frames)
- **SSH** (Port 2222) → Remote FFmpeg execution for transcoding

---

## 🐳 Supported GPU Architectures

| GPU | Docker Image | Tag |
|-----|-------------|-----|
| **NVIDIA** (RTX/GTX) | `kuscheltier/jellyfin-ai-upscaler` | `:1.5.1` |
| **AMD** (ROCm) | `kuscheltier/jellyfin-ai-upscaler` | `:1.5.1-amd` |
| **Intel** (Arc/iGPU) | `kuscheltier/jellyfin-ai-upscaler` | `:1.5.1-intel` |
| **Apple Silicon** (M1-M4) | `kuscheltier/jellyfin-ai-upscaler` | `:1.5.1-apple` |
| **CPU Only** | `kuscheltier/jellyfin-ai-upscaler` | `:1.5.1-cpu` |

---

## 🏁 Quick Start

1. **[🐳 Start Docker Container](Docker-Setup)** – Pull and run the right image for your GPU
2. **[📥 Install Jellyfin Plugin](Installation)** – Add the repository and install the plugin
3. **[⚙️ Configure](Configuration)** – Set the AI Service URL and run the benchmark
4. **[🔐 Setup SSH (optional)](SSH-Remote-Transcoding)** – Enable remote transcoding for maximum performance
5. **🎬 Enjoy** – Play media and watch AI enhance your content!

---

## ✨ Key Features

- **🐳 Docker Microservice** – AI runs isolated, no DLL conflicts with Jellyfin
- **🚀 Remote Transcoding** – Offload FFmpeg to GPU-accelerated Docker containers via SSH
- **📦 Lightweight Plugin** – Only ~1.6 MB (vs. 417 MB in v1.4.x)
- **🎨 Multiple AI Models** – FSRCNN, ESPCN, LapSRN, EDSR (2x/3x/4x)
- **🖥️ 5 GPU Architectures** – NVIDIA, AMD, Intel, Apple Silicon, CPU
- **🔧 Web UI** – Model management at `http://your-server:5000`
- **📊 Smart Benchmarking** – Auto-detects optimal settings for your hardware
- **🎮 Player Integration** – AI button directly in Jellyfin player controls

---

## 📖 Documentation

| Page | Description |
|------|-------------|
| [Installation](Installation) | Plugin + Docker installation guide |
| [Docker Setup](Docker-Setup) | Detailed Docker configuration for all GPUs |
| [SSH Remote Transcoding](SSH-Remote-Transcoding) | Setup SSH-based FFmpeg offloading |
| [Configuration](Configuration) | Plugin settings reference |
| [Features](Features) | Complete feature list |
| [Hardware Compatibility](Hardware-Compatibility) | GPU/CPU compatibility matrix |
| [AI Models](AI-Models) | Available neural network models |
| [Troubleshooting](Troubleshooting) | Fix common issues |
| [FAQ](FAQ) | Frequently asked questions |

---

## 📞 Support & Community

- **🐛 Bug Reports**: [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)
- **💬 Discussions**: [GitHub Discussions](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions)
- **🌐 Website**: [jellyfin-upscale-ai.base44.app](https://jellyfin-upscale-ai.base44.app)

---

*Developed for the Jellyfin community with ❤️ by [Kuschel-code](https://github.com/Kuschel-code)*
