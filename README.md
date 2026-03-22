# Jellyfin AI Upscaler Plugin v1.5.4.0

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Jellyfin Version](https://img.shields.io/badge/Jellyfin-10.11.x+-00A4DC.svg)](https://jellyfin.org)
[![Docker Hub](https://img.shields.io/docker/pulls/kuscheltier/jellyfin-ai-upscaler?logo=docker&label=Docker%20Hub)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Docker Image](https://img.shields.io/docker/v/kuscheltier/jellyfin-ai-upscaler?logo=docker&label=Latest)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Project Website](https://img.shields.io/badge/Website-Visit-blueviolet)](https://transcendent-blancmange-824967.netlify.app)

AI-powered video upscaling for Jellyfin. Upscale SD content to HD/4K using neural networks, running entirely in a Docker container with GPU acceleration.

**Docker Images (docker4 / v1.5.5):**
*   `kuscheltier/jellyfin-ai-upscaler:docker4` (NVIDIA CUDA + cuDNN 9)
*   `kuscheltier/jellyfin-ai-upscaler:docker4-amd` (AMD ROCm)
*   `kuscheltier/jellyfin-ai-upscaler:docker4-intel` (Intel Arc/iGPU OpenVINO)
*   `kuscheltier/jellyfin-ai-upscaler:docker4-apple` (macOS Apple Silicon)
*   `kuscheltier/jellyfin-ai-upscaler:docker4-cpu` (CPU Only)

**Report bugs:** [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)

---

## Architecture

Jellyfin's plugin system tries to load ALL `.dll` files as .NET assemblies. Native C++ libraries (ONNX Runtime, CUDA, OpenCV) caused `BadImageFormatException` crashes in older versions. The solution: a Docker microservice architecture where the plugin (only ~1.6 MB) communicates with an external AI container via HTTP.

```
┌──────────────────────────────────────────┐
│  Jellyfin Server                         │
│  ┌────────────────────────────────────┐  │
│  │  AI Upscaler Plugin v1.5.4.0      │  │
│  │  ~1.6 MB — No native DLLs         │  │
│  │  Sends frames via HTTP             │  │
│  └──────────────┬─────────────────────┘  │
└─────────────────┼────────────────────────┘
                  │ HTTP POST /upscale
                  ▼
┌──────────────────────────────────────────┐
│  AI Upscaler Docker Container            │
│  ┌────────────────────────────────────┐  │
│  │  Python + FastAPI + OpenCV DNN     │  │
│  │  CUDA / ROCm / OpenVINO / CPU     │  │
│  │  Real-ESRGAN, SPAN, SwinIR, DAT2 │  │
│  │  EDVR-M, RealBasicVSR, AnimeSR  │  │
│  │  EDSR, FSRCNN, ESPCN (33 models) │  │
│  │  Web UI for Model Management      │  │
│  └────────────────────────────────────┘  │
└──────────────────────────────────────────┘
```

---

## How It Works

The plugin supports four upscaling modes:

### Pre-Upscaling (Batch Processing)
The **Scheduled Task** ("Scan & Upscale Library") runs daily at 3 AM and:
1. Scans your library for videos below the configured resolution (default: 1080p)
2. Skips already upscaled files (files with `_upscaled` suffix)
3. **Auto-selects the best model** per video based on genre (anime vs live-action), resolution, and available multi-frame models
4. For each low-res video: extracts frames via FFmpeg → upscales through AI → reassembles into a new video
5. With multi-frame VSR models (EDVR-M, RealBasicVSR, AnimeSR): uses 5-frame sliding window for temporal consistency
6. Saves the upscaled version alongside the original (e.g., `Movie_upscaled.mkv`)

This is ideal for users **without powerful servers** — upscaling happens overnight.

### Image Upscaling (NEW in v1.5.4.0)
The **Scheduled Task** ("Scan & Upscale Library Images") runs weekly on Sunday at 4 AM and:
1. Scans all library items for low-resolution posters, backdrops, thumbnails, logos, and banners
2. Uses different thresholds: posters < 600x900, backdrops < 1280x720
3. Auto-scales: 4x for very low-res images, 2x otherwise
4. Also available on-demand via `POST /api/upscaler/upscale-images/{itemId}`

### Real-Time Upscaling During Playback (NEW in v1.5.3.3)
When you press play, the plugin automatically enhances the video in real-time using a **two-tier system**:

- **Tier 1 — WebGL (Client-Side):** An FSR-inspired shader runs on your browser's GPU. Zero latency, always available. Works on any device with WebGL support.
- **Tier 2 — Server AI:** Video frames are captured, sent to the Docker AI service, upscaled with the selected model, and rendered back. Requires a powerful server.

**How it decides:** At playback start, a benchmark runs against the Docker service. If the server can process frames fast enough (≥80% of video FPS), it uses Server AI mode. Otherwise, it falls back to WebGL. If server performance drops during playback, it auto-switches to WebGL.

**Visual indicators:**
- FPS overlay (top-left corner): Shows current FPS, mode, and model
- Button dot: Green = Server AI, Blue = WebGL
- Menu section: Toggle on/off, switch modes manually

### Player Integration
The in-player button lets you:
- Select from **33 AI models** across 9 categories (Real-ESRGAN, SPAN, SwinIR, DAT2, EDVR-M, RealBasicVSR, AnimeSR, EDSR, LapSRN, FSRCNN, ESPCN)
- Choose scale factor (2x, 3x, 4x, 8x)
- Toggle real-time upscaling and switch modes
- Quick access via keyboard shortcuts (Alt+U, Alt+M)

---

## Installation

### Step 1: Start the Docker AI Service

Choose the command that matches your GPU:

**NVIDIA GPU (recommended):**
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --gpus all \
  -p 5000:5000 -p 2222:22 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:docker4
```

**Intel GPU (Arc / Iris):**
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --device=/dev/dri \
  --group-add=render \
  -p 5000:5000 -p 2222:22 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:docker4-intel
```

**AMD GPU (ROCm):**
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --device=/dev/kfd --device=/dev/dri \
  -p 5000:5000 -p 2222:22 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:docker4-amd
```

**CPU Only (any platform):**
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  -p 5000:5000 -p 2222:22 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:docker4-cpu
```

Verify the container is running: `curl http://YOUR_SERVER_IP:5000/health`

### Step 2: Install the Jellyfin Plugin

1. Open Jellyfin Dashboard → **Plugins** → **Repositories** → **Add**
2. Enter this repository URL:
   ```
   https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/manifest.json
   ```
3. Go to **Catalog** → find **AI Upscaler** → click **Install**
4. **Restart Jellyfin** (required after any plugin install)
5. Go to **Dashboard → Plugins → AI Upscaler Plugin** → set **AI Service URL** to `http://YOUR_SERVER_IP:5000`

### Step 3: Use the Player Button

After installation, play any video in a **web browser** (Chrome, Edge, Firefox). You will see an AI upscaler button (sparkle icon) in the player controls. Click it to access:
- Quick model selection (Real-ESRGAN, SPAN, SwinIR, EDSR, FSRCNN, ESPCN, LapSRN)
- Scale factor (2x, 3x, 4x)
- Toggle upscaling on/off

> **Note:** The player button only works in web browsers. It does NOT appear in native Jellyfin apps (Windows, Android, iOS, TV).

> **Note (Docker users):** If the button doesn't appear immediately, visit the plugin config page once — this activates the player script for the current session.

### Step 4: Pre-Upscaling (Optional)

To batch-upscale your low-resolution content:
1. Go to **Dashboard → Scheduled Tasks → AI Upscaler**
2. "Scan & Upscale Library" runs automatically at 3 AM daily
3. Or click **Run** to start immediately
4. Configure resolution threshold in plugin settings (default: 1920x1080)

---

## Features

- **Real-Time Upscaling**: Two-tier system — WebGL client-side shader + Server AI frame pipeline with auto-fallback
- **Pre-Upscaling**: Scheduled task batch-processes low-res videos overnight
- **Docker Microservice**: AI runs isolated in a container (no DLL conflicts)
- **18 AI Models**: Real-ESRGAN, SPAN, SwinIR, EDSR, FSRCNN, ESPCN, LapSRN (2x–8x)
- **5 GPU Architectures**: NVIDIA CUDA/TensorRT, AMD ROCm, Intel OpenVINO, Apple Silicon, CPU
- **Player Integration**: In-player button with quick settings menu, FPS overlay, and keyboard shortcuts (Alt+U, Alt+M)
- **Benchmark-Driven**: Automatic tier selection based on real-time server benchmarks
- **GPU Diagnostics**: `/gpu-verify` endpoint with troubleshooting tips
- **SSH Remote Transcoding**: Offload FFmpeg to GPU containers via SSH
- **Web UI**: Model management at `http://YOUR_SERVER_IP:5000`
- **Dashboard**: Job monitoring in Jellyfin sidebar

---

## AI Models

| Category | Model | Scale | Speed | Quality |
|----------|-------|-------|-------|---------|
| Real-ESRGAN | realesrgan-x4 | 4x | Slow | Best |
| Real-ESRGAN | realesrgan-x4-256 | 4x | Slow | Best (Low VRAM) |
| Real-ESRGAN | realesrgan-x2plus | 2x | Slow | Best (Photos) |
| Real-ESRGAN | realesrgan-animevideo-x4 | 4x | Slow | Best (Anime) |
| SPAN | span-x2/x4 | 2-4x | Fast | High |
| SwinIR | swinir-x4 | 4x | Slow | Best (Photos) |
| EDSR | edsr-x2/x3/x4 | 2-4x | Medium | High |
| LapSRN | lapsrn-x2/x4/x8 | 2-8x | Medium | Good |
| FSRCNN | fsrcnn-x2/x3/x4 | 2-4x | Fast | Good |
| ESPCN | espcn-x2/x3/x4 | 2-4x | Fastest | Fair |

---

## Configuration

After installation, find settings under **Dashboard → Plugins → AI Upscaler Plugin**.

| Setting | Description |
|---------|-------------|
| **AI Service URL** | URL to Docker container (e.g., `http://192.168.1.100:5000`) |
| **Enable Plugin** | Global on/off switch |
| **AI Model** | Choose upscaling model (FSRCNN fastest, Real-ESRGAN best quality) |
| **Scale Factor** | 2x, 3x, or 4x |
| **Min Resolution** | Threshold for scheduled task (default: 1920x1080) |
| **Player Button** | Show/hide AI button in video player |
| **Button Position** | Left, Center, or Right |
| **Real-Time Upscaling** | Enable/disable real-time enhancement during playback |
| **Real-Time Mode** | Auto (benchmark decides), WebGL only, or Server AI only |
| **Capture Width** | Resolution for server frame capture (default: 480px) |
| **Output Codec** | Codec for upscaled videos: H.264, H.265, or copy (default: H.264) |
| **MaxItemsPerScan** | Limit number of items processed per scan run (default: unlimited) |
| **Remote Transcoding** | Enable SSH-based remote transcoding |

---

## Docker Image Tags

| Tag | GPU | Use Case |
|-----|-----|----------|
| `:docker4` | NVIDIA CUDA 12.8 + TensorRT | RTX 50/40/30/20, GTX 16/10 |
| `:docker4-amd` | AMD ROCm | RX 7000, RX 6000 |
| `:docker4-intel` | Intel OpenVINO | Arc A-Series, Iris Xe |
| `:docker4-apple` | ARM64 Optimized | Apple M1–M5 (Docker=CPU, native=CoreML) |
| `:docker4-cpu` | Multi-threaded CPU | Any platform |

---

## Changelog

### v1.5.4.0 (Multi-Frame VSR + Auto-Model + Image Upscaling)
- **Added**: Multi-Frame VSR — 5-frame sliding window for batch upscaling (EDVR-M, RealBasicVSR, AnimeSR v2)
- **Added**: `/upscale-video-chunk` endpoint for multi-frame inference
- **Added**: Auto-detection of multi-frame models via `input_frames` metadata
- **Added**: Sequential processing pipeline with boundary frame padding
- **Added**: Intelligent auto-model selection — picks best model per content (anime/live-action, resolution, batch/real-time)
- **Added**: Image upscaling for posters, backdrops, thumbnails, logos, banners
- **Added**: Scheduled task "Scan & Upscale Library Images" (weekly Sunday 4 AM)
- **Added**: `POST /upscale-images/{itemId}` endpoint for batch image upscaling
- **Added**: `GET /recommend-model` endpoint for model recommendation API
- **Added**: `EnableAutoModelSelection` config option (default: true)
- **Added**: ONNX conversion tool for PyTorch → ONNX model export (EDVR-M, RealBasicVSR, AnimeSR)
- **Added**: `ONNX_TILE_SIZE_MULTIFRAME` env var (default 256) for multi-frame VRAM control
- **Fixed**: Socket exhaustion — HttpClient now reused across multi-frame processing loop
- **Fixed**: Window frame count for even `inputFrames` values (count-based loop)
- **Fixed**: Edge tile ONNX shape error when one dimension < tile_size

### v1.5.3.6 (30 Models — Video-Optimized Community Models)
- **Added**: 12 new community ONNX models with verified download URLs
- **Added**: Video Real-Time category: ClearReality x4 (1.7MB SPAN), NomosUni Compact x2, LSDIR Compact x4, SwinIR-S x2/x4
- **Added**: Video Quality category: UltraSharp V2 x4 (DAT2), Nomos2 DAT2 x4, Nomos2 RealPLKSR x4
- **Added**: Film Restoration category: FSDedither x4 (DVD/VHS), Nomos8k HAT-S x4
- **Added**: Anime category: Real-ESRGAN Anime Compact x4 (5MB), APISR x2 (CVPR 2024)
- Now 30 models total across 7 categories

### v1.5.3.5 (RTX 5000 + Apple M5 + Bugfixes)
- **Added**: NVIDIA RTX 5000 series support (Blackwell, sm_120) via CUDA 12.8 + ONNX Runtime 1.20+
- **Added**: Apple M5 Neural Engine support via CoreML execution provider
- **Added**: Native macOS install script (`install-native-macos.sh`) for GPU-accelerated upscaling without Docker
- **Added**: `ONNX_PROVIDERS` env var for custom execution provider chains
- **Added**: Blackwell architecture auto-detection with compute capability logging
- **Fixed**: Server-mode real-time upscaling crash (ReferenceError in `_startServer`)
- **Fixed**: ONNX model-swap race condition during inference (single-lock pattern)
- **Fixed**: Library scan resolution filter (OR instead of AND — catches all low-res content)
- **Fixed**: Frame-by-frame processing drops frames on error (now uses original as fallback)
- **Fixed**: Model download buffers entire file in RAM (now streams to disk)
- **Fixed**: WebGL buffer leak on repeated enable/disable cycles
- **Fixed**: `/config` endpoint semaphore not updated on `max_concurrent` change
- **Fixed**: Semaphore crash on concurrent 503 responses
- **Fixed**: Browser memory leak from Object URLs in real-time upscaler

### v1.5.3.4 (New Models + Critical Bugfixes)
- **Added**: SPAN x2/x4 — fastest quality model (NTIRE 2023 winner), ideal for real-time
- **Added**: Real-ESRGAN x2 Plus — high quality 2x for photos and live-action
- **Added**: Real-ESRGAN AnimeVideo x4 — optimized for anime temporal consistency
- **Added**: SwinIR x4 — Swin Transformer, best quality for photos
- **Added**: Output codec selection (H.264, H.265, copy) — preserve audio quality
- **Added**: MaxItemsPerScan config — limit batch processing per scan run
- **Fixed**: ONNX tile-based upscaling prevents OOM on GPUs with <8GB VRAM
- **Fixed**: Socket exhaustion from per-request HttpClient in frame proxy
- **Fixed**: Short videos (<5min) now get AI upscaling via API (not just Lanczos)
- **Fixed**: Correct aspect ratio in real-time server capture (was hardcoded 16:9)
- **Fixed**: Skip real-time upscaling on 1080p+ source (no needless 4K→8K)
- **Fixed**: Library scan threshold uses AND (both dimensions must be low-res)
- **Fixed**: Thread-safe model loading prevents data race during inference
- **Fixed**: Disk space check before frame extraction

### v1.5.3.3 (Real-Time Upscaling)
- **Added**: Real-time upscaling during playback with two-tier system
- **Added**: WebGL FSR-inspired client-side shader (Tier 1 — zero latency)
- **Added**: Server AI frame pipeline via `/upscale-frame` (Tier 2 — best quality)
- **Added**: Benchmark-driven tier selection at playback start
- **Added**: FPS overlay during playback (color-coded green/yellow/red)
- **Added**: Auto-fallback: server → WebGL if FPS drops or server unresponsive
- **Added**: Player menu section for real-time controls (toggle, mode switch)
- **Added**: Button indicator dot (green = Server AI, blue = WebGL)
- **Added**: Docker `/upscale-frame` (fast JPEG) and `/benchmark-frame` endpoints
- **Added**: Jellyfin proxy endpoints for CORS-free browser communication

### v1.5.3.2 (Model Selection Fix)
- **Fixed**: Model selection pipeline — configured model now downloads, loads, and is used for upscaling
- **Added**: `EnsureModelLoadedAsync()` called before every upscale operation

### v1.5.3.1 (Pre-Upscaling + GPU Fixes)
- **Added**: Scheduled task now actually processes videos (pre-upscaling pipeline)
- **Added**: Skips already upscaled files and existing `_upscaled` versions
- **Fixed**: NVIDIA TensorRT crashes — `SKIP_TENSORRT=true` now default
- **Fixed**: Intel OpenVINO GPU detection — added `intel-compute-runtime` to Docker image
- **Improved**: `/gpu-verify` endpoint with /dev/dri diagnostics, troubleshooting tips
- **Improved**: Player quick menu redesigned with all 14 models in 5 categories
- **Improved**: Menu respects ButtonPosition config (left/center/right)

### v1.5.3.0 (Player Button Fix + Scheduled Tasks)
- **Fixed**: Player button injection for Docker environments (multi-path fallback)
- **Added**: Config page auto-bootstrap — visiting plugin settings activates the player script
- **Added**: Scheduled Task "Scan Library for Upscaling" (Dashboard → Scheduled Tasks → AI Upscaler)
- **Added**: Configurable resolution threshold (MinResolutionWidth/Height)
- **Improved**: Detailed logging for script injection diagnostics

### v1.5.2.9 (Route Fix + Benchmark Fix)
- **Fixed**: FFmpeg wrapper 404 (dual route fix)
- **Fixed**: Benchmark showing 0.0s when Docker AI service unreachable
- **Fixed**: Test Connection cache issue
- **Improved**: Player Integration section in config page

### v1.5.2.8 (API Route Fix + Config Rebuild)
- **Fixed**: API route mismatch causing 503 errors on all buttons
- **Rebuilt**: Config page for Jellyfin 10.11+ compatibility
- **Added**: Horizontal tabs + collapsible sections, dark theme hardening

### v1.5.2.7 (Docker Fixes + Premium Dashboard)
- **Fixed**: TensorRT poisoning CUDA fallback
- **Fixed**: Intel OpenVINO GPU not using GPU
- **Added**: Multi-GPU selection, premium sidebar dashboard
- **Added**: Player button MutationObserver for reliable injection

### v1.5.2.4 (Major Bug Fix Release)
- 30+ bug fixes, security hardening, dashboard redesign with tabs

### v1.5.2.0–v1.5.2.3
- GPU acceleration fixes (CUDA, OpenVINO)
- Player button fix (Issue #45)
- Security hardening (SSH injection, path traversal)
- NotSupported status fix (targetAbi lowered)

### v1.5.1.0–v1.5.1.1
- SSH Remote Transcoding support
- Multi-architecture Docker images
- SSH config hotfix

### v1.5.0.0–v1.5.0.1
- Docker Microservice Architecture (plugin size 417 MB → 1.6 MB)
- Intel GPU/OpenVINO support
- DI error fix, checksum fix

---

## Troubleshooting

### Plugin shows "Not Supported"
1. Uninstall old versions (v1.4.x)
2. Delete old plugin folder from Jellyfin plugins directory
3. Restart Jellyfin
4. Install v1.5.3.5 fresh from repository

### Player button not showing
1. The button only works in **web browsers** (Chrome, Edge, Firefox)
2. It does NOT work in native apps (Windows app, mobile, TV)
3. Open Jellyfin via `http://YOUR_IP:8096` in a browser
4. Visit the plugin config page once to activate the bootstrap
5. Hard refresh: `Ctrl+Shift+R`

### Docker container not starting
```bash
# Check logs
docker logs jellyfin-ai-upscaler --tail 50

# Health check
curl http://YOUR_SERVER_IP:5000/health

# GPU diagnostics
curl http://YOUR_SERVER_IP:5000/gpu-verify

# Check GPU (NVIDIA)
docker run --rm --gpus all nvidia/cuda:12.2.2-base-ubuntu22.04 nvidia-smi
```

### GPU not detected
- **NVIDIA RTX 5000 (Blackwell)**: Requires CUDA 12.8+ — use latest `:docker4` image. Compute capability sm_120 auto-detected.
- **NVIDIA RTX 4000/3000/2000**: Install `nvidia-container-toolkit`, use `--gpus all`. TensorRT is skipped by default — set `SKIP_TENSORRT=false` if your GPU supports it.
- **Intel**: Use `:docker4-intel` tag with `--device=/dev/dri --group-add=render`. Check diagnostics: `curl http://YOUR_SERVER_IP:5000/gpu-verify`
- **AMD**: Use `:docker4-amd` tag with `--device=/dev/kfd --device=/dev/dri`
- **Apple M1–M5**: Docker on macOS runs CPU-only. For GPU acceleration via CoreML/Neural Engine, use the native install:
  ```bash
  cd docker-ai-service && chmod +x install-native-macos.sh && ./install-native-macos.sh
  ```
- **Windows Docker Desktop**: GPU passthrough not supported — use `:docker4-cpu`

### Proxmox LXC GPU Passthrough
```bash
# Add to LXC config (/etc/pve/lxc/<id>.conf):
lxc.cgroup2.devices.allow: c 226:* rwm
lxc.mount.entry: /dev/dri dev/dri none bind,optional,create=dir

# Inside LXC, use Docker with:
--device=/dev/dri --group-add=render

# Verify GPU visibility:
docker exec jellyfin-ai-upscaler curl http://localhost:5000/gpu-verify
```

### Upscaling not working
1. Verify Docker container is running: `docker ps --filter name=jellyfin-ai-upscaler`
2. Test connection: `curl http://YOUR_SERVER_IP:5000/health`
3. Check GPU diagnostics: `curl http://YOUR_SERVER_IP:5000/gpu-verify`
4. Check AI Service URL in plugin settings
5. Check Docker logs for errors

### Checksum mismatch on install
The plugin repository auto-updates checksums via CI. If you see a mismatch:
1. Wait 5 minutes for GitHub CDN to refresh
2. Remove and re-add the repository URL
3. Try installing again

---

## Support

- [Project Website](https://transcendent-blancmange-824967.netlify.app)
- [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)
- [GitHub Wiki](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki)

---

## License

MIT License - See [LICENSE](LICENSE) for details.
