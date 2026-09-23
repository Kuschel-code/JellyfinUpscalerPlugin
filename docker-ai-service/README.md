# Jellyfin AI Upscaler 🚀

**AI-powered video/image upscaling service for Jellyfin** using neural networks like Real-ESRGAN, FSRCNN, EDSR, and more.

[![Docker Pulls](https://img.shields.io/docker/pulls/kuscheltier/jellyfin-ai-upscaler)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Docker Image Size](https://img.shields.io/docker/image-size/kuscheltier/jellyfin-ai-upscaler/latest)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![GitHub](https://img.shields.io/github/stars/Kuschel-code/JellyfinUpscalerPlugin?style=social)](https://github.com/Kuschel-code/JellyfinUpscalerPlugin)

---

[Download Jellyfin 12 candidate v1.8.3.32 RC1](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/tag/v1.8.3.32-rc.1) (requires Jellyfin 12.0+, five runtime DLLs plus meta.json).

## Jellyfin 12 candidate

v1.8.3.32 moves the plugin to Jellyfin 12 / .NET 10. All seven Docker candidates below are published; their tags, ten platform builds and version/revision labels were verified on 2026-09-23. Stable `docker7` and `latest` remain at v1.8.3.31. The Python HTTP service has no Jellyfin or .NET runtime dependency. [Registry digests and evidence](../docs/DOCKER-RC-v1.8.3.32.md).

## v1.8.3.31 and Docker updates

Release v1.8.3.31 updates both the plugin and AI service. The owner explicitly waived target-server acceptance on 2026-09-17. GPU, real Jellyfin playback and HDR target-hardware behavior remain unverified; see the [release plan](../docs/RELEASE-PLAN-v1.8.3.31.md).

| Backend | Stable tag (1.8.3.31) | Candidate tag (1.8.3.32) | Architectures |
|---|---|---|---|
| NVIDIA CUDA | `docker7` | `rc-v1.8.3.32` | amd64 |
| AMD ROCm | `docker7-amd` | `rc-v1.8.3.32-amd` | amd64 |
| Intel OpenVINO | `docker7-intel` | `rc-v1.8.3.32-intel` | amd64 |
| Apple Docker (CPU) | `docker7-apple` | `rc-v1.8.3.32-apple` | amd64, arm64 |
| Vulkan/ncnn | `docker7-vulkan` | `rc-v1.8.3.32-vulkan` | amd64, arm64 |
| CPU | `docker7-cpu` | `rc-v1.8.3.32-cpu` | amd64, arm64 |
| Converter (CPU + Torch/Spandrel) | `docker7-converter` | `rc-v1.8.3.32-converter` | amd64 |

All tags belong to `kuscheltier/jellyfin-ai-upscaler`. Candidate jobs also publish `rc-v1.8.3.32-2dabc6d[-backend]` for reproducible tests. A candidate does not update `docker7`, `latest` or final version pins. Check the [workflow result](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/actions/workflows/docker-publish.yml) for each backend before pulling.

## 🌟 Features

- **40+ AI Models** - Real-ESRGAN, SPAN, SwinIR, FSRCNN, ESPCN, LapSRN, EDSR, and more
- **NVIDIA GPU Support** - CUDA 12.8 by default; `SKIP_TENSORRT=true`. TensorRT needs explicit opt-in and compatible image libraries.
- **AMD GPU Support** - ROCm 6.2 base; actual acceleration depends on host drivers, device access and the loaded model.
- **Intel GPU Support** - OpenVINO 2025.4 acceleration (Arc, iGPU)
- **Apple Silicon** - Docker runs CPU inference. CoreML requires a native macOS installation.
- **Vulkan GPU Support** - ncnn for AMD pre-RDNA2, Intel iGPU, any Vulkan GPU
- **Web UI Dashboard** - Model management at port 5000
- **REST API** - Easy integration with `/upscale`, `/models`, `/benchmark`
- **Multi-scale** - 2x, 3x, 4x, 8x upscaling options

---

## ⚡ Quick Start

Create a private `.env` file containing `API_TOKEN=<your generated secret>` and use the same token in the Jellyfin plugin. Keep the file out of Git. These examples bind to localhost; for a remote Jellyfin host configure the intended private interface explicitly. Preserve `/app/config` along with models and cache during updates.

### 🟢 NVIDIA GPU

```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --gpus all \
  -p 127.0.0.1:5000:5000 \
  -v ai-models:/app/models \
  -v ai-cache:/app/cache \
  -v ai-config:/app/config \
  --env-file .env \
  kuscheltier/jellyfin-ai-upscaler:docker7
```

### 🔴 AMD GPU (ROCm)

```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --device=/dev/kfd --device=/dev/dri \
  --group-add video \
  -p 127.0.0.1:5000:5000 \
  -v ai-models:/app/models \
  -v ai-cache:/app/cache \
  -v ai-config:/app/config \
  --env-file .env \
  kuscheltier/jellyfin-ai-upscaler:docker7-amd
```

### 🔵 Intel GPU (OpenVINO)

```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --device=/dev/dri \
  -p 127.0.0.1:5000:5000 \
  -v ai-models:/app/models \
  -v ai-cache:/app/cache \
  -v ai-config:/app/config \
  --env-file .env \
  kuscheltier/jellyfin-ai-upscaler:docker7-intel
```

### 🍎 Apple Silicon (macOS)

```bash
# Docker (ARM64 optimized, CPU-mode)
docker run -d \
  --name jellyfin-ai-upscaler \
  -p 127.0.0.1:5000:5000 \
  -v ai-models:/app/models \
  -v ai-cache:/app/cache \
  -v ai-config:/app/config \
  --env-file .env \
  kuscheltier/jellyfin-ai-upscaler:docker7-apple

# Native (recommended for best performance with CoreML)
pip install -r requirements-apple.txt
python -m uvicorn app.main:app --host 0.0.0.0 --port 5000
```

### 💻 CPU Only (Any Platform)

```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  -p 127.0.0.1:5000:5000 \
  -v ai-models:/app/models \
  -v ai-cache:/app/cache \
  -v ai-config:/app/config \
  --env-file .env \
  -e USE_GPU=false \
  kuscheltier/jellyfin-ai-upscaler:docker7-cpu
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
| `SKIP_TENSORRT` | `true` | CUDA default; set false only with compatible TensorRT libraries |
| `API_TOKEN` | unset | Requests require configured authentication; use the same secret in the plugin |

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
services:
  ai-upscaler:
    image: kuscheltier/jellyfin-ai-upscaler:docker7
    container_name: jellyfin-ai-upscaler
    ports:
      - "127.0.0.1:5000:5000"
    volumes:
      - ai-models:/app/models
      - ai-cache:/app/cache
      - ai-config:/app/config
    env_file: .env
    environment:
      - SKIP_TENSORRT=true
    restart: unless-stopped
    mem_limit: 8g
    memswap_limit: 12g
    cpus: 4.0
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: 1
              capabilities: [gpu]

volumes:
  ai-models:
  ai-cache:
  ai-config:
```

---

## 🖥️ GPU Setup (NVIDIA)

**Requirements:** NVIDIA Driver + [nvidia-container-toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html)

```bash
# Verify GPU access
docker run --rm --gpus all nvidia/cuda:12.8.0-base-ubuntu24.04 nvidia-smi
```

---

## 🔷 Intel GPU Setup (OpenVINO)

Support for Intel iGPU and Arc discrete GPUs via OpenVINO 2025.4.

### Quick Start (Intel GPU)

```bash
# Build Intel version
docker build -f Dockerfile.intel --build-arg APP_VERSION=1.8.3.32 --build-arg APP_COMMIT="$(git rev-parse --short HEAD)" -t jellyfin-ai-upscaler:rc-intel .

# Run with Intel GPU access
docker run -d \
  --name jellyfin-ai-upscaler-intel \
  --device=/dev/dri \
  -p 127.0.0.1:5000:5000 \
  -v ai-models:/app/models \
  -v ai-cache:/app/cache \
  -v ai-config:/app/config \
  --env-file .env \
  jellyfin-ai-upscaler:rc-intel
```

**Requirements:**
- Intel iGPU (6th gen+) or Intel Arc GPU
- Linux host with `/dev/dri` device access
- Intel GPU drivers installed on host

---

## Controlled update and rollback

Before replacing an existing service, stop new jobs and save its Compose configuration, current image digest and recoverable copies of the model, cache and config volumes. Keep the previous image available. Select the same backend; use a published commit-specific candidate tag only on the test instance.

```bash
docker compose pull ai-upscaler
docker compose up -d --no-deps ai-upscaler
docker compose ps
```

Use the existing dashboard credentials to check `/health/detailed`, `/gpu-verify`, model loading and actual inference. Confirm the expected version and active provider. Then test the Jellyfin player for at least five minutes, retry/recovery, driver-upscaling guard and masking. A healthy container alone does not verify these player paths or GPU/HDR quality. Restore the saved image/configuration if acceptance fails.

For maintainers: dispatch `docker-publish.yml` on `update/v1.8.3.32` with version `1.8.3.32`, `push=true`, `channel=candidate`. It builds all seven variants. Use `channel=release` after server acceptance or an explicitly documented owner waiver (recorded for v1.8.3.31). Plugin ZIP publication remains manual.

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
