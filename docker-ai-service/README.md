# Jellyfin AI Upscaler - Docker Service

🚀 **AI-powered video upscaling service for Jellyfin** with Real-ESRGAN support.

[![Docker Pulls](https://img.shields.io/docker/pulls/kuscheltier/jellyfin-ai-upscaler)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Docker Image Size](https://img.shields.io/docker/image-size/kuscheltier/jellyfin-ai-upscaler/latest)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)

## 🌟 Features

- **Real-ESRGAN** - Best quality upscaling (x4 photos & anime)
- **FSRCNN/ESPCN** - Fast real-time upscaling (CPU friendly)
- **LapSRN/EDSR** - High quality upscaling
- **NVIDIA GPU Support** - CUDA acceleration
- **Web UI** - Model management at port 5000
- **REST API** - `/upscale`, `/models`, `/benchmark`

## 📦 Available Models

| Model | Type | Best For |
|-------|------|----------|
| `realesrgan-x4plus-anime` | ONNX | Anime/Cartoons ⭐ |
| `realesrgan-x4plus` | ONNX | Real photos |
| `realesrnet-x4plus` | ONNX | Fast processing |
| `fsrcnn-x2/x3/x4` | OpenCV | Real-time |
| `espcn-x2/x3/x4` | OpenCV | Fastest |
| `edsr-x2/x3/x4` | OpenCV | High quality |

---

## 🚀 Quick Start

### GPU Version (Recommended for Real-ESRGAN)

**Requires:** NVIDIA GPU + [nvidia-container-toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html)

```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --gpus all \
  -p 5000:5000 \
  -v ai-models:/app/models \
  -e USE_GPU=true \
  -e DEFAULT_MODEL=realesrgan-x4plus-anime \
  kuscheltier/jellyfin-ai-upscaler:1.1.1
```

### CPU Version

```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  -p 5000:5000 \
  -v ai-models:/app/models \
  -e USE_GPU=false \
  -e DEFAULT_MODEL=fsrcnn-x2 \
  kuscheltier/jellyfin-ai-upscaler:1.1.1
```

### Docker Compose (GPU)

```yaml
version: "3.9"
services:
  ai-upscaler:
    image: kuscheltier/jellyfin-ai-upscaler:1.1.1
    container_name: jellyfin-ai-upscaler
    ports:
      - "5000:5000"
    volumes:
      - ai-models:/app/models
    environment:
      - USE_GPU=true
      - DEFAULT_MODEL=realesrgan-x4plus-anime
      - MAX_CONCURRENT_REQUESTS=4
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

### 1. Install NVIDIA Container Toolkit

**Ubuntu/Debian:**
```bash
curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | sudo gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg
curl -s -L https://nvidia.github.io/libnvidia-container/stable/deb/nvidia-container-toolkit.list | \
  sed 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-container-toolkit-keyring.gpg] https://#g' | \
  sudo tee /etc/apt/sources.list.d/nvidia-container-toolkit.list
sudo apt-get update
sudo apt-get install -y nvidia-container-toolkit
sudo nvidia-ctk runtime configure --runtime=docker
sudo systemctl restart docker
```

### 2. Verify GPU Access

```bash
docker run --rm --gpus all nvidia/cuda:12.2-base nvidia-smi
```

### 3. Start Container with GPU

```bash
docker run -d --gpus all -p 5000:5000 kuscheltier/jellyfin-ai-upscaler:1.1.1
```

---

## 🔧 Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `USE_GPU` | `true` | Enable/disable GPU acceleration |
| `DEFAULT_MODEL` | - | Auto-load model on startup |
| `MAX_CONCURRENT_REQUESTS` | `4` | Max parallel upscaling jobs |
| `LOG_LEVEL` | `INFO` | Logging verbosity |

---

## 🌐 API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/` | GET | Web UI |
| `/status` | GET | Service status |
| `/health` | GET | Health check |
| `/models` | GET | List available models |
| `/models/download` | POST | Download a model |
| `/models/load` | POST | Load model into memory |
| `/upscale` | POST | Upscale an image |
| `/benchmark` | GET | Run benchmark |

---

## 📱 TrueNAS SCALE

See [TRUENAS_INSTALL.md](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/blob/main/docker-ai-service/TRUENAS_INSTALL.md) for installation guide.

---

## 🔗 Links

- **GitHub:** https://github.com/Kuschel-code/JellyfinUpscalerPlugin
- **Jellyfin Plugin:** Install via repository manifest
- **Issues:** https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues

---

## 📜 License

MIT License
