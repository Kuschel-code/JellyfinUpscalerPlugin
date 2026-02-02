# Jellyfin AI Upscaler 🚀

**AI-powered video/image upscaling service for Jellyfin** using neural networks like Real-ESRGAN, FSRCNN, EDSR, and more.

[![Docker Pulls](https://img.shields.io/docker/pulls/kuscheltier/jellyfin-ai-upscaler)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Docker Image Size](https://img.shields.io/docker/image-size/kuscheltier/jellyfin-ai-upscaler/latest)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![GitHub](https://img.shields.io/github/stars/Kuschel-code/JellyfinUpscalerPlugin?style=social)](https://github.com/Kuschel-code/JellyfinUpscalerPlugin)

---

## 🌟 Features

- **14 AI Models** - Real-ESRGAN, FSRCNN, ESPCN, LapSRN, EDSR
- **NVIDIA GPU Support** - CUDA 12.2 + TensorRT acceleration
- **AMD GPU Support** - ROCm 6.0 acceleration (RX 6000/7000, MI series)
- **Intel GPU Support** - OpenVINO acceleration (Arc, iGPU)
- **Apple Silicon Support** - Native ARM64 + CoreML (M1/M2/M3/M4)
- **Web UI Dashboard** - Model management at port 5000
- **REST API** - Easy integration with `/upscale`, `/models`, `/benchmark`
- **Multi-scale** - 2x, 3x, 4x, 8x upscaling options

---

## ⚡ Quick Start

### 🟢 NVIDIA GPU

```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --gpus all \
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:latest
```

### 🔴 AMD GPU (ROCm)

```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --device=/dev/kfd --device=/dev/dri \
  --group-add video \
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:amd
```

### 🔵 Intel GPU (OpenVINO)

```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --device=/dev/dri \
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:intel
```

### 🍎 Apple Silicon (macOS)

```bash
# Docker (ARM64 optimized, CPU-mode)
docker run -d \
  --name jellyfin-ai-upscaler \
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:apple

# Native (recommended for best performance with CoreML)
pip install -r requirements-apple.txt
python -m uvicorn app.main:app --host 0.0.0.0 --port 5000
```

### 💻 CPU Only (Any Platform)

```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  -p 5000:5000 \
  -v ai-models:/app/models \
  -e USE_GPU=false \
  kuscheltier/jellyfin-ai-upscaler:cpu
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

## 🔷 Intel GPU Setup (OpenVINO)

**NEW in v1.1.4:** Support for Intel iGPU and Arc discrete GPUs via OpenVINO!

### Quick Start (Intel GPU)

```bash
# Build Intel version
docker build -f Dockerfile.intel -t jellyfin-ai-upscaler:intel .

# Run with Intel GPU access
docker run -d \
  --name jellyfin-ai-upscaler-intel \
  --device=/dev/dri \
  -p 5000:5000 \
  -v ai-models:/app/models \
  jellyfin-ai-upscaler:intel
```

**Requirements:**
- Intel iGPU (6th gen+) or Intel Arc GPU
- Linux host with `/dev/dri` device access
- Intel GPU drivers installed on host

---

## 🔄 Automatic Updates (Watchtower)

Keep your AI Upscaler container automatically updated when a new version is pushed to Docker Hub:

```bash
# Run Watchtower to monitor and update containers
docker run -d \
  --name watchtower \
  -v /var/run/docker.sock:/var/run/docker.sock \
  containrrr/watchtower \
  --cleanup \
  --interval 21600 \
  jellyfin-ai-upscaler
```

**What it does:**
- Checks Docker Hub every 6 hours for new images
- Automatically pulls and restarts the container with the new version
- Cleans up old images to save disk space

**Via docker-compose:** See the Watchtower section in `docker-compose.yml`.

## 📱 Part of Jellyfin Upscaler Plugin

This Docker service works with the **Jellyfin AI Upscaler Plugin** for automatic video transcoding with AI upscaling.

🔗 **GitHub:** https://github.com/Kuschel-code/JellyfinUpscalerPlugin

---

## 📄 License

MIT License - Free for personal and commercial use.

---

**Made with ❤️ for the Jellyfin community**
