# 🐳 Docker Setup Guide

Complete guide for setting up the AI Upscaler Docker container.

---

## Architecture

The AI Upscaler uses a **microservice architecture**: the plugin runs inside Jellyfin (lightweight, ~1.6 MB), while all heavy AI processing happens in a separate Docker container.

```
Jellyfin Plugin (~1.6 MB)     Docker Container (~500 MB - 6 GB)
┌──────────────────────┐      ┌──────────────────────────┐
│  • Sends HTTP frames │─────►│  • Python + FastAPI      │
│  • SSH FFmpeg calls  │─────►│  • OpenCV DNN / ONNX     │
│  • UI Configuration  │      │  • GPU Acceleration      │
│  • Player Controls   │      │  • SSH Server (Port 22)  │
└──────────────────────┘      └──────────────────────────┘
```

---

## Docker Compose (Recommended)

Create a `docker-compose.yml`:

### NVIDIA GPU
```yaml
version: '3.8'
services:
  ai-upscaler:
    image: kuscheltier/jellyfin-ai-upscaler:1.5.1
    container_name: jellyfin-ai-upscaler
    restart: unless-stopped
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: all
              capabilities: [gpu]
    ports:
      - "5000:5000"   # AI Service API
      - "2222:22"     # SSH for Remote Transcoding
    volumes:
      - ai-models:/app/models
      - /path/to/media:/media:ro        # Your media library (read-only)
      - /path/to/transcode:/transcode   # Shared transcode directory

volumes:
  ai-models:
```

### AMD GPU
```yaml
version: '3.8'
services:
  ai-upscaler:
    image: kuscheltier/jellyfin-ai-upscaler:1.5.1-amd
    container_name: jellyfin-ai-upscaler
    restart: unless-stopped
    devices:
      - /dev/kfd:/dev/kfd
      - /dev/dri:/dev/dri
    group_add:
      - video
    ports:
      - "5000:5000"
      - "2222:22"
    volumes:
      - ai-models:/app/models
      - /path/to/media:/media:ro
      - /path/to/transcode:/transcode

volumes:
  ai-models:
```

### Intel GPU
```yaml
version: '3.8'
services:
  ai-upscaler:
    image: kuscheltier/jellyfin-ai-upscaler:1.5.1-intel
    container_name: jellyfin-ai-upscaler
    restart: unless-stopped
    devices:
      - /dev/dri:/dev/dri
    ports:
      - "5000:5000"
      - "2222:22"
    volumes:
      - ai-models:/app/models
      - /path/to/media:/media:ro
      - /path/to/transcode:/transcode

volumes:
  ai-models:
```

### CPU Only
```yaml
version: '3.8'
services:
  ai-upscaler:
    image: kuscheltier/jellyfin-ai-upscaler:1.5.1-cpu
    container_name: jellyfin-ai-upscaler
    restart: unless-stopped
    ports:
      - "5000:5000"
      - "2222:22"
    volumes:
      - ai-models:/app/models
      - /path/to/media:/media:ro
      - /path/to/transcode:/transcode

volumes:
  ai-models:
```

Start with:
```bash
docker-compose up -d
```

---

## Volume Mounts

| Volume | Container Path | Purpose |
|--------|---------------|---------|
| Media Library | `/media` | Your videos/movies (read-only) |
| Transcode Dir | `/transcode` | Shared transcoding output |
| AI Models | `/app/models` | Persistent model storage |

> **Important**: The media volume must match your Jellyfin media paths for SSH transcoding to work correctly. See [SSH Remote Transcoding](SSH-Remote-Transcoding) for path mapping details.

---

## Port Mapping

| Host Port | Container Port | Service |
|-----------|---------------|---------|
| `5000` | `5000` | AI Service API & Web UI |
| `2222` | `22` | SSH (Remote Transcoding) |

---

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `USE_GPU` | `true` | Enable GPU acceleration |
| `NVIDIA_VISIBLE_DEVICES` | `all` | NVIDIA GPU selection |
| `HSA_OVERRIDE_GFX_VERSION` | `11.0.0` | AMD GPU arch override |
| `OPENVINO_DEVICE` | `GPU` | Intel OpenVINO target |

---

## Updating

```bash
# Pull latest image
docker pull kuscheltier/jellyfin-ai-upscaler:1.5.1

# Restart container
docker-compose down
docker-compose up -d
```

---

## Health Check

The container includes an automatic health check:
```bash
# Manual health check
curl http://localhost:5000/health

# Docker health status
docker inspect --format='{{.State.Health.Status}}' jellyfin-ai-upscaler
```

---

## Logs & Debugging

```bash
# View logs
docker logs jellyfin-ai-upscaler

# Follow logs in real-time
docker logs -f jellyfin-ai-upscaler

# Check GPU detection
docker exec jellyfin-ai-upscaler python -c "import torch; print(torch.cuda.is_available())"
```

---

## Next Steps

- [🔐 Setup SSH Remote Transcoding](SSH-Remote-Transcoding)
- [⚙️ Configure Plugin](Configuration)
