# ❓ Frequently Asked Questions

---

## General

### What is the AI Upscaler Plugin?
An extension for Jellyfin that uses artificial intelligence to improve low-resolution videos (e.g., SD → HD → 4K) using neural network models running in a Docker container.

### Is it free?
**Yes!** The plugin is open-source under the MIT license.

### What's new in v1.5.1?
- **Docker Microservice Architecture** – AI runs in a separate container (no more DLL conflicts)
- **SSH Remote Transcoding** – Offload FFmpeg to GPU-accelerated containers
- **5 GPU architectures** – NVIDIA, AMD, Intel, Apple Silicon, CPU
- **Lightweight plugin** – Only ~1.6 MB instead of 417 MB

### Why Docker?
Jellyfin's plugin system loads ALL `.dll` files as .NET assemblies. Native C++ libraries (ONNX Runtime, CUDA) caused `BadImageFormatException` crashes. Moving AI to Docker eliminates this problem entirely.

---

## Hardware

### What hardware do I need?
- **Minimum:** CPU with 4+ cores + Docker (CPU-only image)
- **Recommended:** NVIDIA RTX 3060+ or AMD RX 6600+ for real-time 4K
- **NAS:** Use CPU image with pre-processing cache enabled

### Which Docker image for my GPU?
| GPU | Tag |
|-----|-----|
| NVIDIA GTX/RTX | `:1.5.1` |
| AMD Radeon RX | `:1.5.1-amd` |
| Intel Arc/iGPU | `:1.5.1-intel` |
| Apple M1-M4 | `:1.5.1-apple` |
| No GPU | `:1.5.1-cpu` |

### Does it work on a NAS?
**Yes!** Use the CPU image and enable pre-processing cache. The plugin will pre-calculate upscaled versions so playback is instant.

### Does remote transcoding require the Docker to be on the same machine?
**No!** The Docker container can run on any machine reachable via SSH. This means you can have Jellyfin on a NAS and the GPU container on a gaming PC.

---

## Docker

### How do I update the Docker image?
```bash
docker pull kuscheltier/jellyfin-ai-upscaler:1.5.1
docker-compose down && docker-compose up -d
```

### Can I run multiple GPU containers?
Yes, but each needs different port mappings (e.g., `-p 5001:5000 -p 2223:22`).

### The container keeps restarting?
Check logs: `docker logs jellyfin-ai-upscaler`. Common causes:
- Missing GPU drivers on host
- Port conflicts (5000 or 2222 already in use)

---

## SSH Remote Transcoding

### What is SSH Remote Transcoding?
Instead of Jellyfin running FFmpeg locally, it sends the command to the Docker container via SSH. This allows the container's GPU to handle transcoding.

### Do I need SSH for basic upscaling?
**No!** SSH is only needed for the FFmpeg wrapper (remote transcoding). Basic AI upscaling works via the HTTP API without SSH.

### Settings not saving?
Make sure you're on **v1.5.1.1** or later. Earlier versions had a bug where SSH configuration fields were not included in the save function.

---

## Troubleshooting

### Plugin shows "Not Supported"
1. Uninstall old versions (v1.4.x)
2. Delete old plugin folder from Jellyfin plugins directory
3. Restart Jellyfin
4. Install latest version from repository

### Connection refused to Docker
```bash
# Verify Docker is running
docker ps --filter name=jellyfin-ai-upscaler

# Test health endpoint
curl http://YOUR_IP:5000/health
```

### AI button missing in player
1. Enable "Show Player Button" in plugin settings
2. Clear browser cache (Ctrl+F5)
3. Check that plugin is listed as "Active" in dashboard

---

## Support

- **🐛 Bug Reports:** [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)
- **💬 Community:** [GitHub Discussions](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions)
- **📖 Wiki:** [You're already here!](Home)
