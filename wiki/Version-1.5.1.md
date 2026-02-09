# 🚀 Version 1.5.1 – SSH Remote Transcoding Edition

> **Release Date:** January-February 2026
> **Status:** ⚠️ TEST VERSION

---

## What's New

Version 1.5.1 introduces **SSH Remote Transcoding** – the ability to offload FFmpeg execution to GPU-accelerated Docker containers via SSH.

### Key Changes
- **🚀 Remote Transcoding via SSH** – Execute FFmpeg on remote Docker containers
- **☁️ 5 GPU Architectures** – NVIDIA, AMD (NEW!), Intel, Apple Silicon, CPU
- **🔒 SSH Key Authentication** – Secure connection to Docker containers
- **📂 Path Mapping** – Translate local paths to container paths
- **🔧 Test SSH Button** – Built-in connectivity testing
- **🔌 API Endpoint** – `/api/upscaler/ssh/test` for programmatic testing

---

## Docker Images

| GPU | Tag | Docker Hub |
|-----|-----|------------|
| NVIDIA | `kuscheltier/jellyfin-ai-upscaler:1.5.1` | [Pull](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler) |
| AMD | `kuscheltier/jellyfin-ai-upscaler:1.5.1-amd` | [Pull](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler) |
| Intel | `kuscheltier/jellyfin-ai-upscaler:1.5.1-intel` | [Pull](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler) |
| Apple | `kuscheltier/jellyfin-ai-upscaler:1.5.1-apple` | [Pull](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler) |
| CPU | `kuscheltier/jellyfin-ai-upscaler:1.5.1-cpu` | [Pull](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler) |

---

## Quick Upgrade

```bash
# Pull latest image
docker pull kuscheltier/jellyfin-ai-upscaler:1.5.1

# Restart container
docker-compose down && docker-compose up -d
```

Then update the plugin via Jellyfin's plugin catalog.

---

## Breaking Changes
- v1.4.x plugins are **not compatible** with v1.5.x Docker images
- SSH port (22) must be exposed for remote transcoding
- Path mapping is required when media paths differ between Jellyfin and Docker

---

## Known Issues
- `CS8604` nullable warning in `FFmpegWrapperService.cs` (non-critical)
- Apple Silicon Docker image runs CPU-only (no CoreML in containers)
- `GenerateUnixScript` in FFmpegWrapperService is still a placeholder

---

## Setup Guide
- [📥 Installation](Installation)
- [🐳 Docker Setup](Docker-Setup)
- [🔐 SSH Remote Transcoding](SSH-Remote-Transcoding)
