# Jellyfin AI Upscaler Plugin v1.5.5.3

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Jellyfin Version](https://img.shields.io/badge/Jellyfin-10.11.x+-00A4DC.svg)](https://jellyfin.org)
[![Docker Hub](https://img.shields.io/docker/pulls/kuscheltier/jellyfin-ai-upscaler?logo=docker&label=Docker%20Hub)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Docker Image](https://img.shields.io/docker/v/kuscheltier/jellyfin-ai-upscaler?logo=docker&label=Latest)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Project Website](https://img.shields.io/badge/Website-Visit-blueviolet)](https://transcendent-blancmange-824967.netlify.app)

AI-powered video upscaling for Jellyfin. Upscale SD content to HD/4K using neural networks, running entirely in a Docker container with GPU acceleration.

**Docker Images (docker5 / v1.5.5.3):**
*   `kuscheltier/jellyfin-ai-upscaler:docker5` (NVIDIA CUDA + cuDNN 9)
*   `kuscheltier/jellyfin-ai-upscaler:docker5-amd` (AMD ROCm)
*   `kuscheltier/jellyfin-ai-upscaler:docker5-intel` (Intel Arc/iGPU OpenVINO)
*   `kuscheltier/jellyfin-ai-upscaler:docker5-apple` (macOS Apple Silicon)
*   `kuscheltier/jellyfin-ai-upscaler:docker5-vulkan` (Vulkan/ncnn — AMD pre-RDNA2, Intel iGPU)
*   `kuscheltier/jellyfin-ai-upscaler:docker5-cpu` (CPU Only)

**Report bugs:** [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)

---

## Architecture

Jellyfin's plugin system tries to load ALL `.dll` files as .NET assemblies. Native C++ libraries (ONNX Runtime, CUDA, OpenCV) caused `BadImageFormatException` crashes in older versions. The solution: a Docker microservice architecture where the plugin (only ~1.6 MB) communicates with an external AI container via HTTP.

```
┌──────────────────────────────────────────┐
│  Jellyfin Server                         │
│  ┌────────────────────────────────────┐  │
│  │  AI Upscaler Plugin v1.5.5.3      │  │
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
│  │  EDSR, FSRCNN, ESPCN (40+ models) │  │
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
- Select from **40+ AI models** across 12 categories (Real-ESRGAN, SPAN, SwinIR, DAT2, EDVR-M, RealBasicVSR, AnimeSR, APISR, EDSR, LapSRN, FSRCNN, ESPCN, ncnn-Vulkan)
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
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:docker5
```

**Intel GPU (Arc / Iris):**
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --device=/dev/dri \
  --group-add=render \
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:docker5-intel
```

**AMD GPU (ROCm):**
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --device=/dev/kfd --device=/dev/dri \
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:docker5-amd
```

**Vulkan GPU (AMD RX 5700, Intel iGPU, etc.):**
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --device=/dev/dri \
  --group-add=render \
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:docker5-vulkan
```

**CPU Only (any platform):**
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:docker5-cpu
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
- Quick model selection across 12 categories (Real-ESRGAN, SPAN, SwinIR, DAT2, EDVR-M, RealBasicVSR, AnimeSR, APISR, EDSR, LapSRN, FSRCNN, ESPCN, ncnn-Vulkan)
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

- **40+ AI Models**: Real-ESRGAN, SPAN, SwinIR, DAT2, EDVR-M, RealBasicVSR, AnimeSR, APISR, EDSR, FSRCNN, ESPCN, LapSRN (2x–8x)
- **Multi-Frame VSR**: 5-frame sliding window for temporal consistency (EDVR-M, RealBasicVSR, AnimeSR v2)
- **Auto-Model Selection**: Picks best model per video based on genre (anime/live-action), resolution, and mode
- **Real-Time Upscaling**: Two-tier system — WebGL client-side shader + Server AI frame pipeline with auto-fallback
- **Pre-Upscaling**: Scheduled task batch-processes low-res videos overnight
- **Image Upscaling**: Scheduled task for posters, backdrops, thumbnails, logos, banners
- **Model Fallback Chain**: Comma-separated models — tries next on failure (images + videos)
- **Priority Queue**: Pause/resume, priority 1-10, optional persistence across restarts
- **Prometheus Metrics**: `/metrics` endpoint with per-model jobs, failures, frames, timing
- **Circuit Breaker**: Auto-opens after consecutive failures, resets after timeout
- **Health Monitoring**: `/health/detailed` with GPU health, circuit breaker state, model info
- **Webhook Notifications**: HTTP POST on job complete/failure
- **Model Management**: Disk usage tracking, LRU cleanup of unused models
- **Docker Microservice**: AI runs isolated in a container (no DLL conflicts, ~1.6 MB plugin)
- **6 GPU Architectures**: NVIDIA CUDA/TensorRT, AMD ROCm, Intel OpenVINO, Vulkan/ncnn, Apple Silicon, CPU
- **Player Integration**: In-player button with quick settings menu, FPS overlay, keyboard shortcuts (Alt+U, Alt+M)
- **SSH Remote Transcoding**: Offload FFmpeg to GPU containers via SSH
- **Web UI**: Model management at `http://YOUR_SERVER_IP:5000`

---

## AI Models (40+ Total)

| Category | Models | Scale | Speed | Best For |
|----------|--------|-------|-------|----------|
| **Real-ESRGAN** | realesrgan-x4, x4-256, x2-plus, animevideo-x4 | 2-4x | Slow | Best overall quality |
| **SPAN** | span-x2, span-x4 | 2-4x | Fast | Real-time video |
| **SwinIR** | swinir-x4, swinir-small-x2/x4 | 2-4x | Medium | Photos & live-action |
| **APISR** | apisr-x3, apisr-anime-x2 | 2-3x | Medium | CVPR 2024, general & anime |
| **Video Real-Time** | clearreality-x4, nomosuni-compact-x2, lsdir-compact-x4 | 2-4x | Fast | Low-latency video |
| **Video Quality** | ultrasharp-v2-x4, nomos2-dat2-x4, nomos2-realplksr-x4 | 4x | Slow | Maximum detail |
| **Film Restoration** | fsdedither-x4, nomos8k-hat-x4 | 4x | Medium | DVD/VHS cleanup |
| **Anime** | anime-compact-x4 | 4x | Fast | Lightweight anime |
| **Multi-Frame VSR** | edvr-m-x4, realbasicvsr-x4, animesr-v2-x4 | 4x | Slow | Temporal consistency (5 frames) |
| **OpenCV Classic** | edsr-x2/x3/x4, lapsrn-x2/x4/x8, fsrcnn-x2/x3/x4, espcn-x2/x3/x4 | 2-8x | Fast-Medium | CPU-only, lightweight |
| **Vulkan/ncnn** | realesrgan-x4-vulkan, realesrgan-anime-x4-vulkan, span-x4-vulkan | 4x | Fast | AMD pre-RDNA2, Intel iGPU |

---

## Configuration

After installation, find settings under **Dashboard → Plugins → AI Upscaler Plugin**.

| Setting | Description |
|---------|-------------|
| **AI Service URL** | URL to Docker container (e.g., `http://192.168.1.100:5000`) |
| **Enable Plugin** | Global on/off switch |
| **AI Model** | Choose upscaling model (`auto` = intelligent selection per content) |
| **Scale Factor** | 2x, 3x, or 4x |
| **Min Resolution** | Threshold for scheduled task (default: 1920x1080) |
| **Model Fallback Chain** | Comma-separated fallback models (e.g., `realesrgan-x4,span-x4,edsr-x4`) |
| **Preferred Anime Model** | Model for anime content when auto-selection is enabled |
| **Preferred Live-Action Model** | Model for live-action content when auto-selection is enabled |
| **Enable Processing Queue** | Priority queue with pause/resume (default: true) |
| **Max Queue Size** | Maximum items in queue (default: 100) |
| **Pause Queue During Playback** | Pause processing when user is watching (default: true) |
| **Webhook URL** | HTTP POST notifications on job complete/failure |
| **Enable Health Monitoring** | Circuit breaker + health checks (default: true) |
| **Circuit Breaker Threshold** | Consecutive failures before circuit opens (default: 5) |
| **Model Disk Quota MB** | Max disk space for cached models (default: 2048) |
| **Enable Model Auto Cleanup** | LRU cleanup of unused models (default: true) |
| **Player Button** | Show/hide AI button in video player |
| **Real-Time Upscaling** | Enable/disable real-time enhancement during playback |
| **Output Codec** | Codec for upscaled videos: H.264, H.265, or copy |
| **MaxItemsPerScan** | Limit items per scan run (default: unlimited) |
| **Remote Transcoding** | Enable SSH-based remote transcoding |

---

## Docker Image Tags

| Tag | GPU | Use Case |
|-----|-----|----------|
| `:docker5` | NVIDIA CUDA 12.8 + TensorRT | RTX 50/40/30/20, GTX 16/10 |
| `:docker5-amd` | AMD ROCm | RX 7000, RX 6000 |
| `:docker5-intel` | Intel OpenVINO | Arc A-Series, Iris Xe |
| `:docker5-apple` | ARM64 Optimized | Apple M1–M5 (Docker=CPU, native=CoreML) |
| `:docker5-vulkan` | Vulkan (ncnn) | AMD pre-RDNA2, Intel iGPU, any Vulkan GPU |
| `:docker5-cpu` | Multi-threaded CPU | Any platform |

---

## Changelog

### v1.5.5.3 (Deepscan Round 2 — Deep Security Hardening)
- **Security**: SSH command injection prevention — single-quote each FFmpeg arg before SSH
- **Security**: Model name regex validation on `/models/load` endpoint
- **Security**: CRLF injection check on AiServiceUrl at runtime
- **Security**: Import settings validation — RemoteHost, RemoteUser, SSH key path, mount paths
- **Security**: Symlink rejection for SSH key files (`FileInfo.LinkTarget`)
- **Security**: Command injection fix in chmod — `ProcessStartInfo.ArgumentList`
- **Security**: XSS fix — `addEventListener` replaces inline `onclick` in Docker Web UI
- **Security**: Private IP blocking on `/connections/register` (SSRF prevention)
- **Security**: Exception message sanitization (no internal details leaked)
- **Security**: Prometheus metric label injection prevention (`safe_name`)
- **Input Validation**: `gpu_device_id` bounds (0–99) on `/models/load` and `/config`
- **Input Validation**: `max_concurrent` bounds (1–256) on `/config`
- **Input Validation**: `max_age_days` bounds (0–36500) on `/models/cleanup`
- **Input Validation**: `ScaleFactor` whitelist (2, 3, 4, 8) on settings import
- **Input Validation**: `AiServiceUrl` scheme validation on import + runtime
- **Input Validation**: Content-Length pre-check on JSON deserialization
- **Thread Safety**: `/models/cleanup` reads `current_model` under `_model_lock`
- **Thread Safety**: Cleanup tracks `actually_freed_mb` only on successful delete
- **Manifests**: Fixed checksum mismatch for v1.5.5.0 (fixes #51)
- **Manifests**: Synced checksums across manifest.json, repository-jellyfin.json, repository-simple.json
- **Manifests**: Normalized 11+ uppercase MD5 checksums to lowercase
- **Manifests**: Fixed wrong changelog for v1.5.1.0 in repository-simple.json
- **Manifests**: Aligned `targetAbi` to 10.11.0.0 in publish_plugin/meta.json
- **Docker**: Updated docker-ai-service README (40+ models, docker5 tags)

### v1.5.5.1 (Deepscan — Security & Quality Hardening)
- **Security**: XSS prevention in Docker Web UI (escapeHtml + textContent)
- **Security**: SSRF fix — model download URL validated by hostname, not prefix
- **Security**: SSH port 2222 removed from docker-compose default
- **Security**: Model name regex validation on all 5 controller endpoints
- **Security**: Webhook SSRF prevention (scheme whitelist)
- **Security**: Path traversal whitelist (output must be sibling of input)
- **Security**: PowerShell injection sanitization on all FFmpeg paths
- **Thread Safety**: `volatile` on `_disposed` flags in HttpUpscalerService, UpscalerCore
- **Thread Safety**: `SemaphoreSlim` for async model loading (replaces `object` lock)
- **Thread Safety**: `state.providers` moved inside `_model_lock`
- **Thread Safety**: `total_frames_processed` protected with `_processing_count_lock`
- **Thread Safety**: AppState mutable defaults moved to `__init__()` (Python class-level sharing fix)
- **Fixed**: Variable shadowing (`outputDir` declared twice) in UpscalerController
- **Fixed**: Bare catch blocks replaced with typed/logged exceptions across all files
- **Fixed**: ProcessingQueue `Resume()` signal flooding (N releases → single release)
- **Fixed**: Content-Length header parsing crash on malformed values
- **Fixed**: Env var parsing with fallbacks (MAX_CONCURRENT_REQUESTS, GPU_DEVICE_ID, ONNX_TILE_SIZE)
- **Fixed**: Multi-frame endpoint capped to MAX_INPUT_FRAMES=10
- **Improved**: Exponential backoff on upscale retry (1s, 2s)
- **Improved**: Configuration — all 16 numeric properties validated with Math.Clamp/Math.Max
- **Improved**: 20 named default constants in PluginConfiguration
- **Removed**: Dead code — NativeDependencyLoader.cs, NativeLibraryLoader.cs (pre-Docker legacy)
- **Removed**: Unused dependencies — pydantic, aiofiles
- **Removed**: Redundant `import asyncio as _asyncio`
- **Updated**: Python 3.11 → 3.12 (Dockerfile.cpu, Dockerfile.vulkan)
- **Updated**: onnxruntime bounds pinned `<2.0.0` across all 6 requirements files
- **Updated**: onnxruntime-gpu upper bound raised to `<1.25.0`
- **Docker**: .dockerignore added, resource limits in compose, version key removed
- **Docker**: All 6 images tagged as docker5

### v1.5.5.0 (Critical Bug Fixes)
- **Fixed**: Circuit breaker half-open bypass — probe no longer lets ALL requests through
- **Fixed**: ncnn CHW/HWC reshape — output was garbled due to wrong memory layout
- **Fixed**: ncnn blend weight clamp — ramp limited to half-tile prevents center overlap
- **Fixed**: Audio passthrough — `-c:a copy` instead of re-encoding to AAC
- **Fixed**: max_concurrent=0 deadlock — Semaphore(0) blocked all requests forever
- **Fixed**: Health endpoint gpu_healthy — reports `null` when GPU disabled (was `true`)
- **Fixed**: Health endpoint exposes circuit breaker half_open state
- **Fixed**: requirements-intel ORT version compatible with OpenVINO 2025.4.1
- **Docker**: All 6 images rebuilt with fixes and pushed to Docker Hub

### v1.5.4.4 (Security & Quality Hardening)
- **Security**: Path traversal protection on all queue/process/preprocess endpoints
- **Security**: RequiresElevation on metrics, job control, and queue status endpoints
- **Security**: Non-root containers (USER appuser) in all 6 Docker images
- **Security**: SSH completely removed from all Docker images
- **Security**: API token required for destructive model cleanup operations
- **Security**: XSS prevention — all server data escaped in frontend HTML
- **Security**: Download URL allowlist (HuggingFace/GitHub only)
- **Fixed**: GPU detection — JsonPropertyName for snake_case deserialization
- **Fixed**: Queue worker — full background processing loop implemented
- **Fixed**: Circuit breaker thread safety with proper half-open state
- **Fixed**: Cache size tracking race condition (Interlocked.Add)
- **Fixed**: Benchmark scale factor for ncnn/Vulkan models
- **Fixed**: Float32 blend buffers — halves RAM usage for 4K upscaling
- **Fixed**: Atomic model downloads prevent corrupted partial files
- **Improved**: Structured logging across entire C# codebase (no interpolation)
- **Improved**: DateTime.UtcNow used consistently everywhere
- **Improved**: IHttpClientFactory replaces static HttpClient instances
- **Improved**: Health check start-period=60s for model download scenarios
- **Improved**: MODEL_CATALOG synced to 40+ models across 12 categories
- **Improved**: CI pipeline with test step and branch-safe manifest push
- **Updated**: FastAPI >=0.115, uvicorn >=0.32, OpenCV >=4.10, python-multipart CVE fix

### v1.5.4.4 (Docker Deep Scan — 20+ Bug Fixes)
- **Fixed**: Thread-safe model dispatch — snapshot state under lock before inference
- **Fixed**: Circuit breaker half-open probe re-opens on failure (was stranding)
- **Fixed**: Per-model async download locks with UUID temp files (no concurrent corruption)
- **Fixed**: Semaphore capture pattern — release targets same instance after `/config` recreates it
- **Fixed**: Model switch clears ALL backend refs (cv_model, onnx_session, ncnn_upscaler)
- **Fixed**: ONNX tile blend weight clamped for small tiles (division by zero)
- **Fixed**: ncnn-Vulkan tile blending with weighted overlap and dynamic reshape
- **Fixed**: ncnn bundled models skip download/file-exists check
- **Fixed**: `/models/cleanup` uses equality instead of substring match
- **Fixed**: `/health/detailed` checks all 3 backends (opencv, onnx, ncnn)
- **Fixed**: `use_gpu` rollback on model load failure
- **Fixed**: Vulkan Dockerfile — appuser added to render/video groups for GPU access
- **Fixed**: Intel Dockerfile — missing `apt-get clean` in first apt block
- **Fixed**: AMD Dockerfile — base image tag corrected, onnxruntime-rocm pinned ≤1.22.99
- **Fixed**: NVIDIA Dockerfile — removed non-existent TensorRT packages, python -m pip
- **Improved**: All 6 Dockerfiles verified against Docker Hub for correct base image tags
- **Improved**: All 6 requirements files updated with flexible version pins

### v1.5.4.3 (Bench Button Fix + Auto-Download)
- **Fixed**: Bench buttons all showing "Failed" — models now auto-download when you click Bench
- **Fixed**: Controller proxy returns actual Docker HTTP status codes (was always 200)
- **Fixed**: Fallback model list format now matches Docker's `{models: [...], total: N}` response
- **Improved**: Bench cells show "Downloading..." / "Benchmarking..." status indicators
- **Improved**: 180s timeout on model load requests for large model downloads
- **Fixed**: Live Video Benchmark now properly checks `loadResult.detail` for Docker errors

### v1.5.4.2 (CORS Fix + Live Video Benchmark)
- **Added**: Live Video Benchmark — test all 40+ models against your library content
- **Added**: 3 new CORS proxy endpoints (`models/load`, `model-benchmark`, `metrics`)
- **Fixed**: CORS errors in model catalog and performance monitor on TrueNAS multi-container setups
- **Fixed**: Frontend uses `ApiClient.getUrl()` instead of direct Docker fetch

### v1.5.4.1 (Multi-Frame Audio Fix)
- **Fixed**: Multi-frame processing dropped audio track (now uses `ReconstructVideoAsync`)
- **Fixed**: Model fallback chain for video processing
- **Fixed**: Scale dropdown crash on missing model data

### v1.5.4.0 (Multi-Frame VSR + Auto-Model + Image Upscaling + Metrics + Health)
- **Added**: Multi-Frame VSR — 5-frame sliding window for batch upscaling (EDVR-M, RealBasicVSR, AnimeSR v2)
- **Added**: `/upscale-video-chunk` endpoint for multi-frame inference
- **Added**: Auto-detection of multi-frame models via `input_frames` metadata
- **Added**: Sequential processing pipeline with boundary frame padding
- **Added**: Intelligent auto-model selection — picks best model per content (anime/live-action, resolution, batch/real-time)
- **Added**: Image upscaling for posters, backdrops, thumbnails, logos, banners
- **Added**: Scheduled task "Scan & Upscale Library Images" (weekly Sunday 4 AM)
- **Added**: `POST /upscale-images/{itemId}` endpoint for batch image upscaling
- **Added**: `GET /recommend-model` endpoint for model recommendation API
- **Added**: Priority processing queue with pause/resume, priority 1-10, optional persistence
- **Added**: Queue management endpoints (add, pause, resume, cancel, set priority)
- **Added**: Prometheus metrics endpoint (`/metrics`) — jobs, failures, frames, timing per model
- **Added**: Detailed health endpoint (`/health/detailed`) with GPU health + circuit breaker state
- **Added**: Circuit breaker pattern — auto-opens after consecutive failures, resets after timeout
- **Added**: Model auto-management — disk usage tracking (`/models/disk-usage`), LRU cleanup (`/models/cleanup`)
- **Added**: Webhook notifications — fire-and-forget HTTP POST on job complete/failure
- **Added**: Model fallback chain — comma-separated models, tries next on failure (images + videos)
- **Added**: 20+ new configuration options (queue, webhooks, health, model management, filtering)
- **Added**: ONNX conversion tool for PyTorch → ONNX model export (EDVR-M, RealBasicVSR, AnimeSR)
- **Added**: `ONNX_TILE_SIZE_MULTIFRAME` env var (default 256) for multi-frame VRAM control
- **Fixed**: Socket exhaustion — HttpClient now reused across multi-frame processing loop
- **Fixed**: Window frame count for even `inputFrames` values (count-based loop)
- **Fixed**: Edge tile ONNX shape error when one dimension < tile_size
- **Fixed**: `processing_count` going negative on semaphore timeout
- **Fixed**: Blend weight normalization using actual content size, not padded tile size
- **Fixed**: Scale parameter validation (1-8) on upscale-images endpoint
- **Fixed**: Real-time model selection inverted (2x for real-time, 4x for batch)
- **Fixed**: GPU verification uses model's actual input shape (fixes #46 — RTX A2000)
- **Fixed**: Prometheus `total_frames_processed` double-counted in metrics
- **Fixed**: Webhook `HttpClient` socket exhaustion (now uses static shared client)
- **Fixed**: `/api/upscaler/models` returned only 14 of 40+ models (now proxies Docker service)
- **Fixed**: `ProcessingQueue._paused` not volatile (thread visibility issue)
- **Added**: APISR x3 (CVPR 2024) — general 3x model for 720p→1080p video

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
4. Install the latest version fresh from repository

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
- **NVIDIA RTX 5000 (Blackwell)**: Requires CUDA 12.8+ — use latest `:docker5` image. Compute capability sm_120 auto-detected.
- **NVIDIA RTX 4000/3000/2000**: Install `nvidia-container-toolkit`, use `--gpus all`. TensorRT is skipped by default — set `SKIP_TENSORRT=false` if your GPU supports it.
- **Intel**: Use `:docker5-intel` tag with `--device=/dev/dri --group-add=render`. Check diagnostics: `curl http://YOUR_SERVER_IP:5000/gpu-verify`
- **AMD**: Use `:docker5-amd` tag with `--device=/dev/kfd --device=/dev/dri`
- **Apple M1–M5**: Docker on macOS runs CPU-only. For GPU acceleration via CoreML/Neural Engine, use the native install:
  ```bash
  cd docker-ai-service && chmod +x install-native-macos.sh && ./install-native-macos.sh
  ```
- **Windows Docker Desktop**: GPU passthrough not supported — use `:docker5-cpu`

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
