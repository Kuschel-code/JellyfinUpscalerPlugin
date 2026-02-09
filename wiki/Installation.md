# 📥 Installation Guide

Follow these steps to install both the Docker AI Service and the Jellyfin Plugin.

---

## Prerequisites

- **Jellyfin Server** v10.11.0 or higher
- **Docker** installed on your server (or a remote machine)
- **GPU** (recommended): NVIDIA, AMD, Intel, or Apple Silicon

---

## Step 1: Start Docker AI Service

Choose the Docker image that matches your GPU:

### NVIDIA GPU (Recommended)
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --gpus all \
  -p 5000:5000 \
  -p 2222:22 \
  -v ai-models:/app/models \
  -v /path/to/media:/media:ro \
  -v /path/to/transcode:/transcode \
  kuscheltier/jellyfin-ai-upscaler:1.5.1
```

### AMD GPU (ROCm)
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --device=/dev/kfd \
  --device=/dev/dri \
  --group-add video \
  -p 5000:5000 \
  -p 2222:22 \
  -v ai-models:/app/models \
  -v /path/to/media:/media:ro \
  -v /path/to/transcode:/transcode \
  kuscheltier/jellyfin-ai-upscaler:1.5.1-amd
```

### Intel GPU (Arc/iGPU)
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --device=/dev/dri:/dev/dri \
  -p 5000:5000 \
  -p 2222:22 \
  -v ai-models:/app/models \
  -v /path/to/media:/media:ro \
  -v /path/to/transcode:/transcode \
  kuscheltier/jellyfin-ai-upscaler:1.5.1-intel
```

### CPU Only (No GPU)
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  -p 5000:5000 \
  -p 2222:22 \
  -v ai-models:/app/models \
  -v /path/to/media:/media:ro \
  -v /path/to/transcode:/transcode \
  kuscheltier/jellyfin-ai-upscaler:1.5.1-cpu
```

### Apple Silicon (M1/M2/M3/M4)
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  -p 5000:5000 \
  -p 2222:22 \
  -v ai-models:/app/models \
  -v /path/to/media:/media:ro \
  -v /path/to/transcode:/transcode \
  kuscheltier/jellyfin-ai-upscaler:1.5.1-apple
```

### Verify Docker is Running
```bash
curl http://localhost:5000/health
# Expected: {"status": "healthy"}
```

Open the Web UI: **http://YOUR_SERVER_IP:5000**

---

## Step 2: Install Jellyfin Plugin

### Option A: Via Repository (Recommended)

1. Open Jellyfin Dashboard → **Plugins** → **Repositories**
2. Click **+** and add:
   ```
   https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/manifest.json
   ```
3. Go to **Catalog**, find **"AI Upscaler Plugin"**
4. Click **Install** → Select latest version
5. **Restart Jellyfin**

### Option B: Manual Installation

1. Download the latest `.zip` from [Releases](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases)
2. Extract to your Jellyfin plugins directory:
   - **Windows**: `C:\ProgramData\Jellyfin\Server\plugins\AIUpscaler`
   - **Linux**: `/var/lib/jellyfin/plugins/AIUpscaler`
   - **Docker Jellyfin**: Map the `/config/plugins` volume
3. **Restart Jellyfin**

---

## Step 3: Configure Plugin

1. Go to **Dashboard → Plugins → AI Upscaler Plugin → Settings**
2. Set **AI Service URL** to `http://YOUR_DOCKER_IP:5000`
3. Click **Test Connection** to verify
4. Click **Save**
5. (Optional) [Setup SSH Remote Transcoding](SSH-Remote-Transcoding)

---

## Next Steps

- [🐳 Docker Setup Details](Docker-Setup) – Advanced Docker configuration
- [🔐 SSH Remote Transcoding](SSH-Remote-Transcoding) – Offload transcoding via SSH
- [⚙️ Configuration Guide](Configuration) – Full settings reference
