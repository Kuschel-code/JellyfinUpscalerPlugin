# ⚙️ Configuration Guide

Complete reference for all AI Upscaler Plugin settings.

---

## Accessing Settings

**Dashboard → Plugins → AI Upscaler Plugin → Settings**

---

## Basic Settings

| Setting | Description | Default |
|---------|-------------|---------|
| **Enable Plugin** | Global on/off switch for all AI features | ☑ Enabled |
| **AI Service URL** | URL of the Docker container | `http://localhost:5000` |
| **AI Model** | Neural network model for upscaling | `realesrgan` |
| **Scale Factor** | Upscaling multiplier (2x, 3x, 4x) | `2` |
| **Quality Level** | Processing precision (low/medium/high) | `medium` |

---

## Hardware Settings

| Setting | Description | Default |
|---------|-------------|---------|
| **Hardware Acceleration** | Use GPU if available | ☑ Enabled |
| **Max VRAM Usage** | Memory limit for GPU processing (MB) | `2048` |
| **CPU Threads** | Number of CPU threads for processing | `4` |

---

## Remote Transcoding (SSH)

| Setting | Description | Default |
|---------|-------------|---------|
| **Enable Remote Transcoding** | Offload FFmpeg to Docker via SSH | ☐ Disabled |
| **Remote Host** | IP/hostname of Docker host | `localhost` |
| **SSH Port** | SSH port (mapped from Docker) | `2222` |
| **SSH User** | Container login user | `root` |
| **SSH Key File** | Path to private SSH key | *(empty)* |
| **Local Media Path** | Media path on Jellyfin server | *(empty)* |
| **Remote Media Path** | Media mount in container | *(empty)* |
| **Transcode Path** | Transcode directory in container | `/transcode` |

See [SSH Remote Transcoding](SSH-Remote-Transcoding) for full setup guide.

---

## UI Settings

| Setting | Description | Default |
|---------|-------------|---------|
| **Show Player Button** | AI button in video player | ☑ Enabled |
| **Button Position** | Left or right in player bar | `right` |
| **Notifications** | Show status popups during playback | ☑ Enabled |
| **Auto Retry** | Automatically retry failed upscaling | ☐ Disabled |

---

## Advanced Features

| Setting | Description | Default |
|---------|-------------|---------|
| **Comparison View** | Enable before/after preview | ☑ Enabled |
| **Performance Metrics** | Show FPS and processing stats | ☑ Enabled |
| **Pre-Processing Cache** | Cache upscaled frames | ☑ Enabled |
| **Cache Size (MB)** | Maximum cache size | `5120` |
| **Cache Age (Days)** | Auto-delete old cache entries | `30` |

---

## Testing Connections

### Test AI Service Connection
Click **"Test Connection"** to verify the Docker container is reachable on the configured URL.

### Test SSH Connection
Click **"Test SSH Connection"** to verify SSH access to the Docker container. This sends a test command via SSH and reports success or failure.

---

## Settings Tips

1. **Start with defaults** – The default settings work for most configurations
2. **Test Connection first** – Always verify Docker connectivity before enabling features
3. **Lower Scale Factor** – If experiencing slow playback, reduce from 4x to 2x
4. **Enable Pre-Processing** – For NAS/CPU users, pre-process your library for instant playback
5. **Path mapping matters** – For SSH transcoding, paths must match exactly
