# Jellyfin AI Upscaler 🚀

**AI-powered video/image upscaling service for Jellyfin** using neural networks like Real-ESRGAN, FSRCNN, EDSR, and more.

[![Docker Pulls](https://img.shields.io/docker/pulls/kuscheltier/jellyfin-ai-upscaler)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Docker Image Size](https://img.shields.io/docker/image-size/kuscheltier/jellyfin-ai-upscaler/latest)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![GitHub](https://img.shields.io/github/stars/Kuschel-code/JellyfinUpscalerPlugin?style=social)](https://github.com/Kuschel-code/JellyfinUpscalerPlugin)

---

## 🌟 Features

- **14 AI Models** - Real-ESRGAN, FSRCNN, ESPCN, LapSRN, EDSR
- **NVIDIA GPU Support** - CUDA 12.2 + cuDNN acceleration
- **Web UI Dashboard** - Model management at port 5000
- **REST API** - Easy integration with `/upscale`, `/models`, `/benchmark`
- **Multi-scale** - 2x, 3x, 4x, 8x upscaling options

---

## ⚡ Quick Start

### With GPU (NVIDIA)

```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --gpus all \
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:latest
```

### Without GPU (CPU only)

```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  -p 5000:5000 \
  -v ai-models:/app/models \
  -e USE_GPU=false \
  kuscheltier/jellyfin-ai-upscaler:latest
```

**📱 Open:** http://localhost:5000

---

## 📦 Available Models

| Model | Type | Scale | Speed | Quality |
|-------|------|-------|-------|---------|
| **Real-ESRGAN x4** | ONNX | 4x | ⭐ | ⭐⭐⭐⭐⭐ |
| **Real-ESRGAN x4-256** | ONNX | 4x | ⭐⭐ | ⭐⭐⭐⭐⭐ |
| **EDSR x2/x3/x4** | OpenCV | 2-4x | ⭐⭐ | ⭐⭐⭐⭐ |
| **LapSRN x2/x4/x8** | OpenCV | 2-8x | ⭐⭐⭐ | ⭐⭐⭐ |
| **FSRCNN x2/x3/x4** | OpenCV | 2-4x | ⭐⭐⭐⭐ | ⭐⭐ |
| **ESPCN x2/x3/x4** | OpenCV | 2-4x | ⭐⭐⭐⭐⭐ | ⭐⭐ |

---

## 🔧 Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `USE_GPU` | `true` | Enable GPU acceleration |
| `DEFAULT_MODEL` | - | Auto-load model on startup |
| `MAX_CONCURRENT_REQUESTS` | `4` | Max parallel jobs |
| `LOG_LEVEL` | `INFO` | Logging verbosity |

---

## 🌐 API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/` | GET | Web Dashboard |
| `/health` | GET | Health check |
| `/status` | GET | Service status + GPU info |
| `/hardware` | GET | GPU/CPU hardware info |
| `/models` | GET | List all models |
| `/models/download` | POST | Download a model |
| `/models/load` | POST | Load model into VRAM |
| `/upscale` | POST | Upscale an image |
| `/benchmark` | GET | Performance test |

---

## 🐳 Docker Compose

```yaml
version: "3.9"
services:
  ai-upscaler:
    image: kuscheltier/jellyfin-ai-upscaler:latest
    container_name: jellyfin-ai-upscaler
    ports:
      - "5000:5000"
    volumes:
      - ai-models:/app/models
    restart: unless-stopped
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: 1
              capabilities: [gpu]

volumes:
  ai-models:
```

---

## 🖥️ GPU Setup (NVIDIA)

**Requirements:** NVIDIA Driver + [nvidia-container-toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html)

```bash
# Verify GPU access
docker run --rm --gpus all nvidia/cuda:12.2-base nvidia-smi
```

---

## 📱 Part of Jellyfin Upscaler Plugin

This Docker service works with the **Jellyfin AI Upscaler Plugin** for automatic video transcoding with AI upscaling.

🔗 **GitHub:** https://github.com/Kuschel-code/JellyfinUpscalerPlugin

---

## 📄 License

MIT License - Free for personal and commercial use.

---

**Made with ❤️ for the Jellyfin community**
