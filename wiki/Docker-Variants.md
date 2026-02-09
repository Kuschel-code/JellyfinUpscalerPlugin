# 🖥️ Docker Image Variants

Complete list of all Docker images for the AI Upscaler Service.

---

## Image Matrix

| GPU Type | Docker Tag | Base Image | Size | SSH |
|----------|-----------|------------|------|-----|
| **NVIDIA** | `kuscheltier/jellyfin-ai-upscaler:1.5.1` | `nvidia/cuda:12.2.2-cudnn8-runtime-ubuntu22.04` | ~6 GB | ✅ |
| **AMD** | `kuscheltier/jellyfin-ai-upscaler:1.5.1-amd` | `rocm/pytorch:latest` | ~8 GB | ✅ |
| **Intel** | `kuscheltier/jellyfin-ai-upscaler:1.5.1-intel` | `openvino/ubuntu22_runtime:2024.0.0` | ~2 GB | ✅ |
| **Apple** | `kuscheltier/jellyfin-ai-upscaler:1.5.1-apple` | `python:3.11-slim` | ~500 MB | ✅ |
| **CPU** | `kuscheltier/jellyfin-ai-upscaler:1.5.1-cpu` | `python:3.11-slim-bookworm` | ~500 MB | ✅ |

---

## NVIDIA (CUDA)

**Best for:** NVIDIA GeForce GTX/RTX, Tesla, Quadro

**Requirements:**
- NVIDIA Driver 525+ installed on host
- `nvidia-container-toolkit` installed
- Docker configured with NVIDIA runtime

```bash
docker run -d --gpus all \
  -p 5000:5000 -p 2222:22 \
  kuscheltier/jellyfin-ai-upscaler:1.5.1
```

**Supported GPUs:** GTX 1050+, RTX 2060+, RTX 3060+, RTX 4060+

---

## AMD (ROCm)

**Best for:** AMD Radeon RX, Radeon Pro

**Requirements:**
- ROCm drivers installed on host
- Access to `/dev/kfd` and `/dev/dri`

```bash
docker run -d \
  --device=/dev/kfd --device=/dev/dri \
  --group-add video \
  -p 5000:5000 -p 2222:22 \
  kuscheltier/jellyfin-ai-upscaler:1.5.1-amd
```

**Supported GPUs:** RX 6600+, RX 7600+, Radeon Pro

**Environment:**
- `HSA_OVERRIDE_GFX_VERSION=11.0.0` – Override for GPU architecture detection

---

## Intel (OpenVINO)

**Best for:** Intel Arc GPUs, integrated graphics (UHD/Iris Xe)

**Requirements:**
- Access to `/dev/dri` on host
- i915 driver loaded

```bash
docker run -d \
  --device=/dev/dri:/dev/dri \
  -p 5000:5000 -p 2222:22 \
  kuscheltier/jellyfin-ai-upscaler:1.5.1-intel
```

**Supported GPUs:** Intel Arc A380+, Iris Xe, UHD 770

---

## Apple Silicon (ARM64)

**Best for:** Mac Mini, MacBook (M1/M2/M3/M4)

> **Note:** CoreML acceleration works natively on macOS, not inside Docker. This image provides CPU-optimized inference for Docker Desktop on macOS.

```bash
docker run -d \
  -p 5000:5000 -p 2222:22 \
  kuscheltier/jellyfin-ai-upscaler:1.5.1-apple
```

**For native performance on Mac:**
```bash
pip install -r requirements-apple.txt
python -m uvicorn app.main:app --host 0.0.0.0 --port 5000
```

---

## CPU Only

**Best for:** Systems without a compatible GPU, NAS devices, VPS

```bash
docker run -d \
  -p 5000:5000 -p 2222:22 \
  kuscheltier/jellyfin-ai-upscaler:1.5.1-cpu
```

**Performance:** Works well for pre-processing and 720p upscaling. Real-time 4K requires a GPU.

---

## Choosing the Right Image

```
Do you have a GPU?
├── Yes → What brand?
│   ├── NVIDIA → Use :1.5.1
│   ├── AMD → Use :1.5.1-amd
│   ├── Intel → Use :1.5.1-intel
│   └── Apple → Use :1.5.1-apple (or native)
└── No → Use :1.5.1-cpu
```

---

## Building from Source

If you need a custom build:

```bash
cd docker-ai-service

# NVIDIA
docker build -f Dockerfile -t my-upscaler:nvidia .

# AMD
docker build -f Dockerfile.amd -t my-upscaler:amd .

# Intel
docker build -f Dockerfile.intel -t my-upscaler:intel .

# Apple
docker build -f Dockerfile.apple -t my-upscaler:apple .

# CPU
docker build -f Dockerfile.cpu -t my-upscaler:cpu .
```
