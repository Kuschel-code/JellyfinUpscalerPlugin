# 🚀 AI Upscaler Service

A standalone AI upscaling microservice for the Jellyfin AI Upscaler Plugin.

## Why a Separate Service?

Jellyfin's plugin system tries to load ALL `.dll` files as .NET assemblies. Native C++ libraries (ONNX Runtime, CUDA, OpenCV) cause `BadImageFormatException`. This microservice architecture solves that problem by running AI processing in an isolated container.

## Architecture

```
┌─────────────────────────────────────────┐
│  Jellyfin Server (Port 8096)            │
│  ┌───────────────────────────────────┐  │
│  │  AI Upscaler Plugin (.NET)        │  │
│  │  • Extracts frames via FFmpeg     │  │
│  │  • Sends images via HTTP POST     │  │
│  │  • No native DLLs needed          │  │
│  └─────────────┬─────────────────────┘  │
└────────────────┼────────────────────────┘
                 │ HTTP POST /upscale
                 ▼
┌─────────────────────────────────────────┐
│  AI Upscaler Service (Port 5000)        │
│  ┌───────────────────────────────────┐  │
│  │  Python 3.11 + FastAPI            │  │
│  │  • ONNX Runtime 1.18 (GPU/CPU)    │  │
│  │  • OpenCV for image processing    │  │
│  │  • CUDA / TensorRT / DirectML     │  │
│  └───────────────────────────────────┘  │
└─────────────────────────────────────────┘
```

## Quick Start

### 1. Build and Run

```bash
# Navigate to this directory
cd docker-ai-service

# Build and start (with GPU)
docker-compose up -d --build

# Or without GPU
docker-compose up -d --build ai-upscaler-cpu
```

### 2. Access Web UI

Open http://localhost:5000 in your browser.

### 3. Download and Load a Model

1. In the Web UI, click **Download** next to a model
2. Once downloaded, click **Load**
3. The service is now ready to upscale images

### 4. Configure Jellyfin Plugin

In the Jellyfin plugin settings:
- **AI Service URL**: `http://localhost:5000` (or your server IP)
- **Enable AI Upscaling**: ✓

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/` | GET | Web UI |
| `/health` | GET | Health check |
| `/status` | GET | Service status (model, GPU, etc.) |
| `/models` | GET | List available models |
| `/models/download` | POST | Download a model |
| `/models/load` | POST | Load a model into memory |
| `/upscale` | POST | Upscale an image |

### Example: Upscale an Image

```bash
curl -X POST http://localhost:5000/upscale \
  -F "file=@input.jpg" \
  -F "scale=2" \
  -o output.png
```

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `USE_GPU` | `true` | Enable GPU acceleration |
| `MAX_CONCURRENT_REQUESTS` | `4` | Max parallel upscale requests |
| `DEFAULT_MODEL` | - | Auto-load this model on startup |
| `LOG_LEVEL` | `INFO` | Logging verbosity |

### Docker Volumes

| Path | Purpose |
|------|---------|
| `./models:/app/models` | Persistent model storage |
| `./cache:/app/cache` | Download cache |

## GPU Support

### NVIDIA (CUDA)

The `docker-compose.yml` includes NVIDIA GPU configuration:

```yaml
deploy:
  resources:
    reservations:
      devices:
        - driver: nvidia
          count: all
          capabilities: [gpu]
```

**Requirements:**
- NVIDIA GPU with CUDA support
- nvidia-docker2 installed
- NVIDIA Container Toolkit

### AMD / Intel (DirectML)

For Windows with AMD/Intel GPUs, ONNX Runtime automatically uses DirectML.

## Troubleshooting

### Service won't start
```bash
docker-compose logs ai-upscaler
```

### GPU not detected
```bash
# Check if NVIDIA runtime is available
docker run --rm --gpus all nvidia/cuda:12.0-base nvidia-smi
```

### Model download fails
- Check your internet connection
- Verify the model URL is accessible
- Check disk space in the models volume

## License

MIT License - See main plugin repository for details.
