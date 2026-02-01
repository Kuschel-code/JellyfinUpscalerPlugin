# Jellyfin AI Upscaler

AI-powered video upscaling service for Jellyfin Media Server.

## Features

- **Real-ESRGAN x4** - Best quality upscaling for photos and anime
- **FSRCNN x2/x3** - Fast real-time upscaling
- **ESPCN x2/x3** - Ultra-fast lightweight upscaling
- **NVIDIA GPU Acceleration** - Full TensorRT support
- **Intel GPU Support** - OpenVINO acceleration for Intel Arc/iGPU
- **Web Dashboard** - Monitor status, run benchmarks, test upscaling

## Requirements

- Docker with GPU support (NVIDIA Container Toolkit)
- NVIDIA GPU with CUDA support (recommended)
- 4GB+ VRAM for Real-ESRGAN models
- Jellyfin AI Upscaler Plugin installed

## Configuration

| Variable | Description | Default |
|----------|-------------|---------|
| `USE_GPU` | Enable GPU acceleration | `true` |
| `MAX_CONCURRENT_REQUESTS` | Max parallel processing | `4` |
| `DEFAULT_MODEL` | Auto-load model on startup | `fsrcnn-x2` |

## Ports

| Port | Description |
|------|-------------|
| 5000 | Web UI & API |

## Volumes

| Path | Description |
|------|-------------|
| `/app/models` | AI model storage |

## Links

- [Project Website](https://jellyfin-upscale-ai.base44.app)
- [GitHub Repository](https://github.com/Kuschel-code/JellyfinUpscalerPlugin)
- [Docker Hub](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
