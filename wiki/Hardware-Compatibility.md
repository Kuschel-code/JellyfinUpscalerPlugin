# 🎯 Hardware Compatibility

The AI Upscaler Plugin v1.5.1 supports multiple GPU architectures through dedicated Docker images.

---

## GPU Support Matrix

| GPU | Docker Image | Acceleration | Performance |
|-----|-------------|--------------|-------------|
| **NVIDIA RTX 40xx** | `:1.5.1` | CUDA + TensorRT | ⭐⭐⭐⭐⭐ Excellent |
| **NVIDIA RTX 30xx** | `:1.5.1` | CUDA | ⭐⭐⭐⭐⭐ Excellent |
| **NVIDIA RTX 20xx** | `:1.5.1` | CUDA | ⭐⭐⭐⭐ Very Good |
| **NVIDIA GTX 16xx** | `:1.5.1` | CUDA | ⭐⭐⭐ Good |
| **NVIDIA GTX 10xx** | `:1.5.1` | CUDA | ⭐⭐⭐ Good (4GB+ VRAM) |
| **AMD RX 7000** | `:1.5.1-amd` | ROCm | ⭐⭐⭐⭐ Very Good |
| **AMD RX 6000** | `:1.5.1-amd` | ROCm | ⭐⭐⭐⭐ Very Good |
| **Intel Arc A-Series** | `:1.5.1-intel` | OpenVINO | ⭐⭐⭐⭐ Very Good |
| **Intel Iris Xe** | `:1.5.1-intel` | OpenVINO | ⭐⭐ Satisfactory |
| **Intel UHD 770** | `:1.5.1-intel` | OpenVINO | ⭐⭐ Satisfactory |
| **Apple M4** | `:1.5.1-apple` | CPU (ARM64) | ⭐⭐⭐ Good |
| **Apple M3** | `:1.5.1-apple` | CPU (ARM64) | ⭐⭐⭐ Good |
| **Apple M1/M2** | `:1.5.1-apple` | CPU (ARM64) | ⭐⭐⭐ Good |
| **Any x86 CPU** | `:1.5.1-cpu` | Multi-Thread | ⭐⭐ Satisfactory |

---

## Driver Requirements

### NVIDIA
- **Driver:** 525+ (CUDA 12.2 support)
- **Container Toolkit:** `nvidia-container-toolkit` required
- Install: `apt-get install nvidia-container-toolkit`

### AMD
- **ROCm:** 5.0+ installed on host
- **Kernel:** Linux 5.15+ recommended
- Devices: `/dev/kfd` and `/dev/dri` must be accessible

### Intel
- **Driver:** i915 kernel module loaded
- **Device:** `/dev/dri` must be accessible
- Works on Intel Gen 11+ (Ice Lake) and Arc GPUs

### Apple Silicon
- **macOS:** Docker Desktop for Mac
- **Note:** GPU acceleration requires native execution (not Docker)

---

## Memory Requirements

| Task | VRAM | System RAM |
|------|------|------------|
| 720p → 1080p (2x) | ~1 GB | 4 GB |
| 1080p → 4K (2x) | ~2 GB | 8 GB |
| 1080p → 4K (4x) | ~6 GB | 8 GB |
| 4K → 8K (2x) | ~12 GB | 16 GB |

---

## Recommended Setup by Use Case

### Home Theater (Best Quality)
- **GPU:** NVIDIA RTX 4070+ or AMD RX 7800 XT
- **Docker:** GPU image on dedicated machine
- **Settings:** 4x scale, High quality, SSH enabled

### NAS / Low Power
- **GPU:** None required
- **Docker:** CPU image
- **Settings:** 2x scale, Medium quality, Pre-processing cache ON

### Budget / Mixed
- **GPU:** Intel Arc A380 or NVIDIA GTX 1060
- **Docker:** Matching GPU image
- **Settings:** 2x scale, Medium quality

---

## Linux GPU Passthrough

### NVIDIA GPU to Docker
```bash
# Install container toolkit
sudo apt-get install nvidia-container-toolkit
sudo nvidia-ctk runtime configure --runtime=docker
sudo systemctl restart docker

# Test
docker run --rm --gpus all nvidia/cuda:12.2.2-base-ubuntu22.04 nvidia-smi
```

### AMD GPU to Docker
```bash
# Ensure ROCm is installed
# Pass devices to Docker
docker run --device=/dev/kfd --device=/dev/dri --group-add video ...
```

### Intel GPU to Docker
```bash
# Check /dev/dri exists
ls -la /dev/dri/

# Pass to Docker
docker run --device=/dev/dri:/dev/dri ...
```
