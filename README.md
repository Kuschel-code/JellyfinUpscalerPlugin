# Jellyfin AI Upscaler Plugin v1.8.3.1

[![Built with Claude Opus](https://img.shields.io/badge/Built%20with-Claude%20Opus%204.8-D97757?logo=anthropic&logoColor=white&style=for-the-badge)](https://www.anthropic.com/claude/opus)

> **Built with Claude Opus 4.8** — this plugin is developed and maintained entirely with [Anthropic's Claude Opus](https://www.anthropic.com/claude/opus). Code contributions, Dockerfiles, CI workflows and documentation are produced in a pair-programming style with the model; the maintainer ([Kuschel-code](https://github.com/Kuschel-code)) reviews, tests and publishes every change. Release commits carry the `Co-Authored-By: Claude` trailer as disclosure.

---

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Jellyfin Version](https://img.shields.io/badge/Jellyfin-10.11.x+-00A4DC.svg)](https://jellyfin.org)
[![Docker Hub](https://img.shields.io/docker/pulls/kuscheltier/jellyfin-ai-upscaler?logo=docker&label=Docker%20Hub)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Docker Image](https://img.shields.io/docker/v/kuscheltier/jellyfin-ai-upscaler?logo=docker&label=Latest)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Documentation](https://img.shields.io/badge/Docs-kuschel--code.github.io-blueviolet)](https://kuschel-code.github.io/JellyfinUpscalerPlugin/)

AI-powered video upscaling for Jellyfin. Upscale SD content to HD/4K using neural networks, running entirely in a Docker container with GPU acceleration.

**Docker Images (docker7 base — plugin is independently versioned at v1.8.3.1):**
*   `kuscheltier/jellyfin-ai-upscaler:docker7` (NVIDIA CUDA + cuDNN 9)
*   `kuscheltier/jellyfin-ai-upscaler:docker7-amd` (AMD ROCm)
*   `kuscheltier/jellyfin-ai-upscaler:docker7-intel` (Intel Arc/iGPU OpenVINO)
*   `kuscheltier/jellyfin-ai-upscaler:docker7-apple` (macOS Apple Silicon — multi-arch amd64/arm64)
*   `kuscheltier/jellyfin-ai-upscaler:docker7-vulkan` (Vulkan/ncnn — AMD pre-RDNA2, Intel iGPU)
*   `kuscheltier/jellyfin-ai-upscaler:docker7-cpu` (CPU Only — multi-threaded ONNXRuntime, multi-arch)

**Report bugs:** [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)

---

## Architecture

Jellyfin's plugin system tries to load ALL `.dll` files as .NET assemblies. Native C++ libraries (ONNX Runtime, CUDA, OpenCV) caused `BadImageFormatException` crashes in older versions. The solution: a Docker microservice architecture where the plugin (only ~1.6 MB) communicates with an external AI container via HTTP.

```
┌──────────────────────────────────────────┐
│  Jellyfin Server                         │
│  ┌────────────────────────────────────┐  │
│  │  AI Upscaler Plugin v1.8.3.1   │  │
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
│  │  EDSR, FSRCNN, ESPCN (70+ models) │  │
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

### Real-Time Upscaling During Playback
When you press play, the plugin enhances the video in real-time. It offers several **honest tiers** (pick one, or let *Auto* choose) — each labelled for what it actually is, not marketing:

- **WebGL (sharpen)** — a Lanczos2 + CAS sharpening shader on your browser's GPU. Zero latency, works on any WebGL device. *Not AI* — the always-available baseline.
- **Anime4K (anime shader)** — the Anime4K 4.0.1 GLSL filter (a *shader*, not a neural net), embedded in the plugin and run client-side via WebGL. Best for anime; auto-falls back to WebGL if the client lacks WebGL2 float textures.
- **WebGPU AI (client GPU)** — a real Real-ESRGAN compact model via onnxruntime-web on **WebGPU**, running on *your* GPU in the browser. Real neural-net AI, no server needed.
- **Server AI** — frames are sent to the Docker AI service, upscaled with the selected model (Real-ESRGAN / SwinIR / DAT2 / …), and rendered back. Highest live quality; needs a capable server GPU.
- **Batch (scheduled task)** — for guaranteed best quality, upscale the whole file once on the server and just play the `_upscaled` result — and reach the TVs/phones nothing else can.

**Pair it with what you already have:** on a desktop browser you can also use your GPU's own VSR (NVIDIA RTX Video Super Resolution / Intel VSR); for mpv there's mpv-shim + Anime4K. This plugin is the *hub* that brings anime/AI upscaling to every web/TV/mobile client **and** batch-upscales your whole library.

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
  kuscheltier/jellyfin-ai-upscaler:docker7
```

**Intel GPU (Arc / Iris):**
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --device=/dev/dri \
  --group-add=render \
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:docker7-intel
```

**AMD GPU (ROCm):**
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --device=/dev/kfd --device=/dev/dri \
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:docker7-amd
```

**Vulkan GPU (AMD RX 5700, Intel iGPU, etc.):**
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  --device=/dev/dri \
  --group-add=render \
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:docker7-vulkan
```

**CPU Only (any platform):**
```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  -p 5000:5000 \
  -v ai-models:/app/models \
  kuscheltier/jellyfin-ai-upscaler:docker7-cpu
```

Verify the container is running: `curl http://YOUR_SERVER_IP:5000/health`

### Step 2: Install the Jellyfin Plugin

1. Open Jellyfin Dashboard → **Plugins** → **Repositories** → **Add**
2. Enter this repository URL:
   ```
   https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/repository-jellyfin.json
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
- **Real-Time Upscaling**: Honest tiers — WebGL (sharpen) · Anime4K (anime shader) · WebGPU AI (client GPU) · Server AI · Batch (best) — with auto-fallback
- **Pre-Upscaling**: Scheduled task batch-processes low-res videos overnight
- **Image Upscaling**: Scheduled task for posters, backdrops, thumbnails, logos, banners
- **Quality Metrics (PSNR/SSIM)**: Compare bicubic vs AI upscale quality with Gaussian-based SSIM (NEW in v1.5.5.4)
- **Face Enhancement (GFPGAN)**: Haar cascade detection → ONNX inference or bilateral filter fallback, soft elliptical blending (NEW in v1.5.5.4)
- **Film Grain Management**: NL-means denoising for removal, Gaussian noise for re-grain, configurable "both" mode (NEW in v1.5.5.4)
- **Camera-Style Video Filters**: 7 presets (Cinematic, Vintage, Vivid, Noir, Warm, Cool, HDR Pop) + full custom mode with brightness, contrast, saturation, gamma, sharpness, color temperature, vignette, film grain, denoise, and LUT color grading (NEW in v1.6.1)
- **Custom ONNX Model Upload**: Upload and validate custom ONNX models at runtime with NCHW shape validation
- **OpenAPI/Swagger Docs**: Togglable `/docs` and `/redoc` endpoints for API exploration
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
| **Enable Quality Metrics** | Compute PSNR/SSIM scores after upscaling (default: true) |
| **Enable Face Enhancement** | Detect and enhance faces via GFPGAN ONNX or fallback (default: true) |
| **Face Enhance Strength** | Blend ratio 0.0–1.0 for face enhancement (default: 0.7) |
| **Enable Grain Management** | Film grain removal/re-addition pipeline (default: true) |
| **Grain Denoise Strength** | NL-means filter strength 1–30 (default: 5) |
| **Grain Re-add Intensity** | Gaussian noise sigma 0–50 for re-grain (default: 0) |
| **Enable Custom Model Upload** | Allow uploading custom ONNX models at runtime (default: true) |
| **Enable API Docs** | Toggle /docs and /redoc Swagger endpoints (default: true) |
| **Player Button** | Show/hide AI button in video player |
| **Real-Time Upscaling** | Enable/disable real-time enhancement during playback |
| **Output Codec** | Codec for upscaled videos: H.264, H.265, or copy |
| **MaxItemsPerScan** | Limit items per scan run (default: unlimited) |
| **Remote Transcoding** | Enable SSH-based remote transcoding |

---

## Docker Image Tags

| Tag | GPU | Use Case |
|-----|-----|----------|
| `:docker7` | NVIDIA CUDA 12.8 + TensorRT | RTX 50/40/30/20, GTX 16/10 |
| `:docker7-amd` | AMD ROCm 6.2 | RX 7000, RX 6000 |
| `:docker7-intel` | Intel OpenVINO 2025.4 | Arc A-Series, Iris Xe, iGPU |
| `:docker7-apple` | ARM64 Optimized (multi-arch) | Apple M1–M5 (Docker=CPU, native=CoreML) |
| `:docker7-vulkan` | Vulkan (ncnn) | AMD pre-RDNA2, Intel iGPU, any Vulkan GPU |
| `:docker7-cpu` | Multi-threaded CPU (multi-arch) | Any platform (amd64/arm64) |

Each tag is published three ways so you can pin precisely:
- `:docker7` — rolling tag family (Watchtower auto-updates)
- `:docker7-v1.8.2` — pinned to a specific plugin release
- `:v1.8.2-<backend>` — full semver (e.g. `:v1.8.2-cpu`)

---

## Changelog

### v1.8.3.1 (Cancel-fix)

**Plugin-only release.**

- **`Cancel` now actually stops a job.** `jobs/{id}/cancel` reported success but never halted the running job: `ProcessVideoAsync` created a per-job `CancellationTokenSource` (the one `CancelJob` cancels) yet passed the bare caller token to the pipeline instead of the job's linked `cts.Token`, so cancellation never reached extraction/upscaling. The semaphore wait, model load and the method executor now all receive `cts.Token` (still linked to the caller token). Pre-existing across all processing methods.

### v1.8.3 (Opt-in pipeline parallelism)

**Plugin-only release** (works with the existing docker7 AI-service images, no rebuild needed).

- **Pipeline parallelism (experimental, default OFF).** A new setting overlaps frame extraction with upscaling instead of running them strictly back-to-back: while ffmpeg is still extracting later frames, already-extracted frames are upscaled concurrently. Built on a thread-safe `FrameStreamCoordinator` that hands a frame to the upscaler only once a successor frame proves it was fully written, and drops the unproven highest frame if extraction fails, so output frame-count and ordering are identical to the sequential path. Default behaviour is unchanged; the overlap runs only when you tick the box.
- **Tests.** `FrameStreamCoordinator` state-machine coverage (COMPLETE-vs-FAILED asymmetry, terminal-first-wins); xUnit 164, dotnet build clean.

### v1.8.2 (Quality + hardening pass)

**Plugin + service release.**

- **Denoise-before-encode prefilter.** Dedicated source-clean pass (`hqdn3d` / `nlmeans`) run before upscaling/encoding, independent of the camera-style filters — cleaner input → better SR + smaller re-encode.
- **VMAF quality scoring.** New admin-only `POST /Upscaler/vmaf` scores an upscaled file against a reference via ffmpeg+libvmaf (501 if the ffmpeg build lacks libvmaf).
- **Second interpolation architecture.** The interpolation engine is now input-signature-adaptive (RIFE / IFRNet 3-input / CAIN 2-input); IFRNet + CAIN added to the catalog as experimental self-host.
- **MultiFrame extraction progress.** The multi-frame path now reports live extraction progress like FrameByFrame/Batch.
- **Decoupled model download.** `POST /models/download-async` + `GET /models/download-status/{id}` run big downloads in the background, so they no longer trip client/proxy timeouts.
- **Supply-chain hardening.** All 6 Docker base images pinned by `@sha256` digest + a pip-audit CVE sweep across the requirement sets (0 known).
- **Docs + tests.** New "client-VSR vs server-side" hardware chapter, a verified `FrameStreamCoordinator`, and new tests across detection, denoise, VMAF, interpolation, async download and multi-frame VSR.

### v1.8.1 (Live filter fix + hardware-aware recommendation)

**Plugin + service release.**

- **Live video filters fixed.** In-player presets/sliders had no visible effect while realtime client-side upscaling was active — the upscaler renders to a canvas overlay that covered the filtered `<video>`, so the CSS filter sat on a hidden element. It is now applied to the visible canvas too.
- **Hardware-aware model recommendation.** New `/recommend` (service) and `/Upscaler/recommend` (plugin, admin) pick a model + scale the detected hardware can actually run — weak CPU → `fsrcnn-x2` @2x, dedicated GPU → `realesrgan-x4` — instead of letting you pick a heavy model that hits the seconds-per-frame wall.

### v1.7.13 (Settings-page redesign + API-token guide)

**Plugin release.**

- **Redesigned settings page.** The plugin config page now uses the same clean "Operator Console" look as the Docker service UI (consistent dark theme, cards, KPI tiles, chips, blue accent, Inter typography). CSS-only re-theme — every setting and behaviour is unchanged.
- **API-token setup made clear.** New guide for securing the AI service: `API_TOKEN` is a **container environment variable** — set it in your compose/run config and put the **same** value in the plugin's "AI Service API Token" field; or set `API_TOKEN=disable` for a trusted LAN. Full walkthrough on the website's API page.

### v1.7.12 (Timeout-wall + error-message fixes)

**Plugin release** (no Docker functional change).

- **First-time downloads no longer fail with "Load failed".** Loading a model that has to download (face-restore GFPGAN/CodeFormer/GPEN ~280-377MB, or large ONNX models) used to hit a 120s proxy timeout while the download was still running. A dedicated 570s download client fixes it (kept just under the 600s UI wait so an over-long download shows a real error, not a blank browser timeout); the `/models/load` UI timeout was raised to match, and benchmarks went 120s -> 300s.
- **Real error messages.** The UI error parsers now read `detail || error || message`, so the actual cause is shown instead of a generic "Load failed". A DI test guards the named-client timeouts so a typo can never silently fall back to the 100s default.

### v1.7.11 (Honest extraction progress + version-display guard)

**Plugin release** (no Docker functional change).

- **"Stuck at 95%" - now fixed for the extraction phase too.** Batch progress shows real frame-extraction + upscale progress (frames done/total) instead of a time estimate that pinned at 95% during the long ffmpeg extraction on slow CPUs (#72); the dashboard shows the live phase (Extracting / Upscaling / Encoding) instead of "Idle". The pipe-encode path that could also pin at 95% is fixed too.
- **Honest version display, guarded.** `configurationpage.html` now shows the real plugin version (v1.7.10 shipped still rendering v1.7.9), and the release script asserts every user-visible version string matches the tag so it can never drift again.

### v1.7.10 (Fixes "stuck at 95%" + sharper GPU diagnostics)

**Docker + Plugin release.** Pull the refreshed `docker7` / `docker7-<backend>` images **and** update the plugin.

- **"Stuck at 95%" fixed at the root.** Batch progress now reflects **real frames processed** (frames done/total + fps + ETA) instead of a time estimate that pinned at 95% on slow hardware; and a job with **no AI model loaded fails fast** ("load a model first") instead of grinding to 95% and hanging (#70 / #72).
- **Smarter Setup Doctor.** `/doctor` now detects **"GPU + CUDA/ROCm available but inference on CPU"** (a host driver/toolkit mismatch) and says to update the host driver / check logs, instead of mislabeling it a CPU image (#71).
- **Honest version display.** The in-player menu + System-Diagnostics panel now show the real plugin version.

### v1.7.9 (Setup Doctor + Anime4K Embedded Tier + GPU-State Fix)

**Docker + Plugin release.** Pull the refreshed `docker7` / `docker7-<backend>` images **and** update the plugin to v1.7.9.

- **Setup Doctor** — new `GET /doctor` one-shot self-diagnostic (backend · GPU provider active · device passthrough · onnxruntime build · API token · model smoke), each row with a copy-paste fix, plus a "Setup Check" panel on the Docker dashboard's Hardware tab. Turns the #66/#69/#70 setup-friction saga into a single `curl`.
- **GPU-state fix** — `run_benchmark()`, `/models/load` and `/benchmark-frame` reported the *requested* GPU intent instead of the *active* provider truth; all three now use `gpu_is_active()`.
- **Anime4K, for real** — the "Anime4K (anime shader)" real-time tier previously loaded a dead CDN URL and silently fell back to Lanczos. It now ships a **vendored, tree-shaken Anime4K.js** (npm 1.1.2, Anime4K 4.0.1, WebGL, MIT, ~225 KB) **embedded in the plugin DLL** — offline, no CDN — with a WebGL2 float-texture support-gate and automatic Lanczos fallback. Honest label: an anime *shader*, not a neural net.
- Third-party licenses recorded in `THIRD-PARTY-NOTICES.md`; website AI support bot gained a Setup Doctor topic.

### v1.7.8 (Docker AI Service — Model Catalog +12 & GPU/Benchmark/AMD Fixes)

**Docker-image-only release** — pull the refreshed `docker7` / `docker7-<backend>` tags (or pin `:v1.7.8-<backend>`). **The Jellyfin plugin is bumped to v1.7.8 (embedded offline model-fallback refreshed 59 to 71)** — all changes are in `docker-ai-service/`, so update the plugin AND pull a fresh Docker image to get the functional fixes.

- **Model catalog 59 → 71 (+12).** New ONNX models from the curated `notaneimu/onnx-image-models` source, focused on the real Jellyfin case (h264/h265-compressed sources): `realesr-general-x4v3` / `-wdn` (tiny modern general default), `realwebphoto-v4-dat2-x4` + `nomoswebphoto-realplksr-x4` (trained on degraded web/compressed images), `dejpg-realplksr-1x` + `denoise-realplksr-1x` (1x artifact-cleanup pre-passes), `foolhardy-remacri-x4`, `nmkd-siax-x4`, `nomos8k-hat-l-x4` (full HAT-L), `textures-rgt-s-x4` (RGT-S, new architecture), `lsdir-compact-v2-x4`, `spanx2-ch48`.
- **FIX — 256px-model benchmark crash (#70).** `run_benchmark()` now reads the loaded ONNX session's real input shape; fixed-shape models (`realesrgan-x4-256`) benchmark at 256×256 instead of a 64px tile that raised a Reshape error during warmup — the "Reshape" failure Gemini misattributed to the GPU.
- **FIX — GPU-active reporting (#69/#70).** New `gpu_is_active()` derives "is the GPU really in use?" from the live execution-provider list (now counts OpenVINO/CoreML, not only CUDA/ROCm). `/health`, `/status`, `/hardware`, `/gpu-verify` report the honest value, so the dashboard and the System tab no longer disagree and `/gpu-verify` no longer shows `using_gpu:false` while OpenVINO is active.
- **FIX — AMD image ran on CPU.** The build log proved the `onnxruntime-rocm` wheel installed fine, but plain `onnxruntime` (pulled by `requirements-amd.txt`) shadowed it — both ship the same `onnxruntime` module and the plain build won (reporting `AzureExecutionProvider`/CPU). Removed plain onnxruntime from the AMD requirements and force-install `onnxruntime-rocm` as the sole provider in `Dockerfile.amd`.
- **FIX — stale service version.** The startup banner / `/status` reported a hardcoded `1.6.1.21`; `VERSION` now reads the `APP_VERSION` build arg (single source of truth).

### v1.7.7 (Docker Self-Verification Hardening)

Structural completion of the Intel-Arc saga (#45/#66/#67/#69). A dedicated docker-ai-service deep-analysis found no acute bugs left, but **a missing verification layer**: no Dockerfile checked at build time whether its expected ONNX provider was actually installed. That gap is exactly what let the v1.7.5 Intel bug slip through (plain `onnxruntime` built fine but had no OpenVINO EP). v1.7.7 structurally closes the "GPU image silently runs on CPU" class. **C# Plugin DLL bit-identical to v1.7.6** — all changes are in `docker-ai-service/`. Tests 123/123. v1.7.x configs bit-compatible.

- **Build-time provider asserts (3 images):** `Dockerfile` (NVIDIA) asserts `CUDAExecutionProvider`, `Dockerfile.intel` asserts `OpenVINOExecutionProvider` — both **hard-fail** (exit 1) if the provider is missing. The build goes red instead of publishing a working-looking CPU-only image. Would have caught the v1.7.5 Intel bug before release.
- **AMD provider visibility:** `Dockerfile.amd` had a silent `|| pip install onnxruntime` CPU fallback. New: a **warn-only** assert (exit 0, because CPU is a valid fallback for AMD when ROCm wheels are yanked) — the CPU mode is now visible in the build log instead of slipping through unnoticed.
- **entrypoint.sh WSL2 awareness:** `detect_backend()` only checked `/dev/dri/renderD128` and falsely printed "Backend: cpu" (plus a false GPU-missing warning) on WSL2 setups, even though `main.py` correctly detects the GPU via `/dev/dxg` since v1.7.4. New: a `/dev/dxg` branch → banner is consistent with the real detection.
- **Verification:** `dotnet build` — 0/0. Build asserts now run on every Docker rebuild. Quad-MD5 verified post-release.

### v1.7.6 (Intel OpenVINO Provider Fix — Issue #69 Point 1)

Hotfix release for issue #69 point 1 (Intel Arc GPU detected but inference ran on CPU). Empirically verified via Laurent's `/gpu-verify`: `onnx_providers` contained **only `AzureExecutionProvider` + `CPUExecutionProvider`** — no `OpenVINOExecutionProvider`. Root cause: `requirements-intel.txt` had a wrong comment + wrong package: `onnxruntime` (plain) instead of `onnxruntime-openvino`. Plain `onnxruntime` from PyPI **does NOT bundle the OpenVINOExecutionProvider** — even when the base image `openvino/ubuntu22_runtime:2025.4.1` provides the system libs. v1.7.6 switches to `onnxruntime-openvino>=1.20.0,<2.0.0`, which bundles the provider. After a Docker image pull, `/gpu-verify` should show `OpenVINOExecutionProvider` as the first provider. **C# Plugin DLL bit-identical to v1.7.5** — fix is docker-ai-service only. Tests 123/123 unchanged. v1.7.x configs bit-compatible.

### v1.7.5 (Non-Admin User Support + 4:3 Aspect-Ratio Fix)

Issue #69-driven release closing two bugs that **blocked majority-of-users access** to the plugin. C# Plugin DLL gets minimal surgical changes (31 authorization-policy downgrades, no logic change). Tests 123/123 unchanged. Build 0/0. v1.7.x configs bit-compatible. Repo now has **0 open issues**.

- **Fix #69 (Auth) — Non-admin users can now use the plugin.** Audit found **47 of 52 endpoints were admin-only** — including all playback/upscale/queue endpoints. Normal Jellyfin users could see models but not load or use them, making the plugin effectively admin-only. v1.7.5 **downgrades 31 endpoints to `[Authorize]` (authenticated-user OK)** while keeping 16 admin-only (server-config, destructive ops, security-sensitive, global queue control). Class-level `[Authorize]` on `UpscalerController` ensures all endpoints still require authentication — only the elevation-requirement is removed where inappropriate. **Security note:** GPU-intensive ops are now reachable by non-admin users — existing `MaxConcurrentStreams` + `MaxQueueSize` clamps (v1.7.2) provide global DoS-cap; per-user quota deferred to v1.7.6.
- **Fix #69 (Aspect-Ratio) — 4:3 movies no longer stretched on the upscaler overlay.** Both real-time renderers (`Configuration/webgl-upscaler.js` + `Configuration/webgpu-ai-realtime.js`) set the canvas-overlay CSS to `width:100%; height:100%` without `object-fit`, blindly stretching 4:3 source to 16:9 player container. Added `object-fit: contain` to both — the canvas drawbuffer already carries the correct `videoWidth × videoHeight` aspect, so `contain` lets the browser letterbox correctly without extra geometry code.
- **#45 closed** as obsolete — same reporter (FrRene06) as #66, original Feb-2026 thread predating the v1.7.4 WSL2 fixes. Cross-referenced.
- **Verification:** `dotnet build` — 0/0. Quad-MD5 verified post-release. meta.json-in-ZIP matches tag.

### v1.7.4 (Docker-Side Fixes: FP16 Type Detection + WSL2 GPU Detection)

Two real-world bug reports closed in a single release. **C# Plugin DLL is functionally identical to v1.7.3.1** — this release ships docker-ai-service Python fixes, version bumps, manifest/feeds/site updates, and a new commented `docker-compose.yml` WSL2 variant. Tests 123/123 unchanged. Build 0/0. v1.7.x saved configs bit-compatible. Repo now has **0 open issues**.

- **Fix #67 — ONNX FP16/FP32 type-mismatch** (reported by @eparrish64 on 2026-05-19, same day). NVIDIA users hit `ONNXRuntimeError INVALID_ARGUMENT: Unexpected input data type. Actual: (tensor(float16)), expected: (tensor(float))` at every model warmup. Root cause: `_resolve_fp16_setting()` auto-enables FP16 on any NVIDIA with Compute Capability ≥7.0 (Volta+), but most catalog ONNX models (Real-ESRGAN, SwinIR, HAT, …) are FP32-exported. New helper `_session_input_is_fp16(session)` checks the loaded model's input dtype before the cast in `_onnx_infer_tile()` and `_onnx_infer_multiframe_tile()` — FP16 only kicks in when **both** the global flag AND the model agree. Symmetric guard on the output `astype(np.float32)` cast.
- **Fix #66 — Docker / WSL2 / Intel Arc on Windows 11** (reported by @FrRene06 on 2026-05-17). Dashboard stuck on "No GPU detected (CPU-only mode)" no matter which image tag was tried. Root cause: Intel-detection in `detect_hardware()` only searched `/dev/dri/renderD*`; WSL2 exposes the GPU through `/dev/dxg` (DirectX bridge). New WSL2 detection branch via `clinfo --list` + new `_parse_clinfo_intel_name()` helper. New commented `ai-upscaler-wsl2` section in `docker-compose.yml` with the verified Intel Arc A380 mount/devices/env recipe. `/gpu-verify` endpoint exposes a new `wsl2` diagnostics section (`is_wsl2_environment`, `wsl_lib_mounted`, `ld_library_path`) — users self-debug without server logs.
- **Stale issues closed:** #49 Vulkan (already supported as `docker7-vulkan` image), #64 Select Library (implemented v1.6.1.14), #62 + #63 v1.5.x install bugs (obsolete after repo-feed sync in `e470849`).
- **Polish:** `README.md` "Windows Docker Desktop: GPU passthrough not supported" replaced with WSL2 mount instructions. `docs/ISSUES-PLAN-2026-05-19.md` documents the full triage/fix plan for all 6 issues.
- **Verification:** `dotnet build` — 0/0. `dotnet test` — 123/123 unchanged from v1.7.3.1. Python syntax check on `main.py` passed. Quad-MD5 verified post-release (`Scripts/verify-release.ps1 -Tag v1.7.4` PASSED).

### v1.7.3.1 (Hotfix + Interface-Extraction)

Audit-caught release-vs-code inconsistency: v1.7.3 release notes announced deletion of `GET /Upscaler/js/{name}` endpoint, but a batch-edit interrupt during release left it intact. v1.7.3.1 actually deletes it (0 callers, user-impact: zero). Plus Phase D of the audit roadmap: two test-seam interfaces (`IUpscalerCore`, `IUserManagerAdapter`) extracted with DI-factory pattern (`sp.GetRequiredService<UpscalerCore>()` → single instance shared). New `UserManagerAdapterTests` covers the fail-open guard (DB exception → returns false → treat as unwatched). Tests grew 121 → 123. Saved v1.7.x configs are bit-for-bit compatible.

### v1.7.3 (meta.json-in-ZIP Verify + Dead-Code Purge + Site Sync)

External audit caught the v1.7.0 ZIP-version-mismatch class (meta.json said 1.6.1.23, manifest said 1.7.0). New CI gate `zip-version-check` parses meta.json inside the ZIP and asserts version-match. Plus `POST /Upscaler/cache/config` endpoint deleted (0 callers, dead since v22 UI-cleanup); `UpscalerSettings` class purged (`CPUInfo` + `MemoryInfo` kept — transitively reachable via `BenchmarkResults`); `site/models.html` extended with 11 missing entries to match the Python catalog (48 → 59); new `Scripts/sync-site-topbar-versions.ps1` mirrors meta.json version into all 14 site files. Build 0/0, tests 121/121.

### v1.7.2 (Math.Clamp DoS-Hardening + 6 New Models + ProcessingStatus Cleanup)

Hardening release. **18 numeric Property setters** in `PluginConfiguration` upgraded from `Math.Max(value, lower)` to `Math.Clamp(value, lower, upper)` — int.MaxValue payloads via Settings-Import or REST PUT can no longer corrupt saved configs. 3 lower-bound drift fixes en passant (`RealtimeCaptureWidth`, `HealthCheckIntervalSeconds`, `CircuitBreakerResetSeconds`). Catalog grew 53 → 59 (MAN x2/x4, CRAFT x2/x4, GPEN-512, NAFNet-denoise). `ProcessingStatus.Analyzing` enum value removed (never emitted). New `ProcessingQueueTests` (4 tests) covers debounced persist. Tests 117 → 121.

### v1.7.1 (RealtimeModeRegistry + WebGPU AI Mode + Drift-Lock-Tests)

Drift-prevention closure. New `RealtimeModeRegistry` with `UiModes` (5) + `BackwardsCompatAliases` ({webgl}) + `AcceptedAtImport` union — old saved configs with `webgl` still load and silently migrate to `lanczos`. New `Configuration/webgpu-ai-realtime.js` (~258 LoC) loads onnxruntime-web@1.20.1 + Real-ESRGAN compact ONNX via 4-stage CDN fallback for client-side AI realtime. UI gets 5th option "AI WebGPU". New `RegistryDriftLockTests` generic `[Theory]` parses embedded `configurationpage.html`, asserts set-equality across all 5 dropdowns (Codec / Quality / ButtonPosition / RealtimeMode / FilterPreset). Adding a UI option without registry update fails the build. Tests 102 → 117.

### v1.7.0 (OutputCodec Save-Validation Fix)

Hotfix for a P0 user-impact bug surfaced in the v22 deep-audit: the Settings `#OutputCodec` dropdown offered **12 codec choices** but four code paths each had their own inline allowlist with sizes 3 / 6 / 7 / 12. The Save endpoint accepted only 3 (libx264, libx265, copy) — picking AV1, NVENC, or QSV in the dropdown silently fell back to libx264.

- **User-impact:** on NVIDIA RTX 40 hardware, picking `av1_nvenc` and clicking Save resulted in libx264 (CPU) — a 5-20× encoding-speed regression with no error, no toast, no log.
- **Fix:** new `Services/CodecRegistry.cs` with two HashSets:
  - `OutputCodecs` (all 12) — for save validation, frame-reconstruction, and batch FFmpeg
  - `RealtimeOutputCodecs` (HW-encoders + libx264/265) — for the realtime pipe path; excludes "copy" (meaningless when re-encoding) and software AV1/VP9 (too slow for frame-by-frame)
- **All 4 sites now reference CodecRegistry** — `UpscalerController.cs:1437`, `VideoFrameProcessor.cs:400`, `ProcessingMethodExecutor.cs:477`, `ProcessingMethodExecutor.cs:803`. No more inline allowlists.
- **Drift-lock test:** new `CodecRegistryTests.cs` parses embedded `configurationpage.html`, extracts every `<option value="X">` inside `#OutputCodec`, asserts set-equality against `CodecRegistry.OutputCodecs`. Adding a UI codec without bumping the registry (or vice versa) fails the build.
- **+17 new tests** — 12 codec InlineData + 5 facts. Tests grew 85 → 102.
- **Verification:** `dotnet build` 0/0, `dotnet test` 102/102. Zero deletions, only consolidation.

### v1.6.1.22 (UI Honesty Cleanup)

Follow-through release on the v1.6.1.21 honest-disclosure principle: **30 dead-backend config controls** are removed from the settings page. Discovered via a triple-pass over all **88 properties** in `PluginConfiguration.cs` (v21's scan only enumerated 54 because the regex missed property-bodies like `{ get => _x; set => _x = Math.Clamp(...); }`).

- **5 entirely-dead `<details>` sections removed** — Quality Metrics, Face Enhancement, Film Grain Management, Health & Circuit Breaker, Model Management. None of their toggles or numerics had any consumer in C#/JS/Python.
- **18 individual fields removed from mixed-live sections** — `PlayerButton`, `Notifications`, `AutoRetryButton`, `EnableProcessingQueue`, `EnableProgressNotifications`, `EnableModelPreloading`, `EnableModelAutoCleanup`, `EnableHealthMonitoring`, `EnableGpuFallbackToCpu`, `EnableComparisonView`, `EnableCustomModelUpload`, `EnableApiDocs`, `EnablePreProcessingCache`, `MaxVRAMUsage`, `CpuThreads`, `MaxUpscaledFileSizeMB`, `RealtimeTargetFps`, plus 2 dead Face-Restore sliders.
- **JS load/save arrays pruned in lockstep** — `fields`, `nums`, `floats`, `checks`, `sliderMap`, `longs`. 3 orphan slider event listeners removed.
- **Properties + Controller `TryApply` kept** — saved user configs continue to load without crash. Backwards-compatible.
- **Deliberately preserved** — `ButtonPosition`, `EnableRealtimeUpscaling`, `RealtimeMode`, `RealtimeCaptureWidth` (player-integration.js consumers, false-positive in algo's first pass), `EnableFaceRestore` + `FaceRestoreModel` (UI-direct-to-`/face-restore/load` REST consumers).
- **Verification:** `dotnet build` — 0 warnings, 0 errors. `dotnet test` — 85/85 passing (unchanged). `configurationpage.html` 2807 → 2680 lines. Zero behavior change, zero new code paths.

### v1.6.1.21 (Adoption v2 + Compute-Waste Fixes + Honest Dead-Config)

Follow-up patch closing 8 findings of the v1.6.1.20 external audit.

- **`RestrictToUnwatchedContent` + `SkipUpscaledOnRescan` finally wired** — both toggles existed since v1.6.1.14 with 0 consumers. `LibraryUpscaleScanTask` now consults a new `IsAnyUserPlayed(BaseItem)` helper (DI extended with `IUserManager` + `IUserDataManager`). User compute-waste-protection finally honored.
- **6 remaining HttpClient calls now use `HttpContext.RequestAborted`** — 16/16 coverage in UpscalerController, including the hot-path `/upscale-frame` and `/upscale-video-chunk` (no more 120s server hangs when client disconnects mid-playback).
- **`ProcessingStrategySelector` substring-matcher tightened** — the v1.6.1.18 `compact` and `realplksr` substrings were too broad: `anime-compact-x4` (anime-category) and `nomos2-realplksr-x4` (video-quality, 30 MB DAT2) were falsely accepted for RealTime, causing frame drops. Removed those substrings; only unambiguous `fsrcnn`/`espcn`/`span` prefixes remain.
- **4 frame-loop `File.Copy` → async streaming** — `VideoFrameProcessor` (2 sites) + `ProcessingMethodExecutor` (2 sites). Pattern from v1.6.1.20 `CacheManager:307` extended to all hot-paths. NAS-mount block-IO eliminated.
- **FaceRestore backend allowlist symmetric to frontend** — backend `FaceRestoreLoad` no longer hardcodes `{gfpgan-v1.4, codeformer}`; reads `category="face_restore"` from the same embedded JSON the frontend dropdown uses. New face-restore models (e.g. RestoreFormer++) won't get rejected with HTTP 400 anymore.
- **Filter-preset list deduplicated 4× → 1×** — new `_validFilterPresets` static readonly. Single-source.
- **Honest XML-doc disclosure of 6 dead-config toggles** — `EnableModelPreloading` / `EnableHealthMonitoring` / `EnableModelAutoCleanup` / `EnableQualityMetrics` / `EnableFaceEnhancement` / `EnableGrainManagement` flagged as `currently no-op pending v1.7.0 pipeline implementation`. No silent UI-lying.
- **+13 new regression tests** — `ProcessingStrategySelectorTests.cs` covers HashSet acceptance, prefix acceptance, and the 2 substring-matcher rejections. Tests grew 72 → 85 passing.

**Verification:** `dotnet build -c Release` — 0 warnings, 0 errors. `dotnet test` — 85/85 passing. No new models, no schema changes — v1.6.1.20 saved configs are bit-for-bit compatible.

### v1.6.1.20 (Adoption Completion + Cancellation + Async-IO)

Follow-up patch closing the gaps the v1.6.1.19 post-release self-audit found. The v1.6.1.19 refactor introduced the `ModelAvailability` source-of-truth class but didn't adopt it everywhere. Plus 3 new bug classes that surfaced during the deep-scan.

- **Adoption: `HardwareBenchmarkService.cs:123`** — `status.CurrentModel ?? "realesrgan-x4"` was bypassing `EnsureModelAvailable`. Now wrapped — if the Docker service ever reports a self-host model as `current_model`, it falls back to plugin default instead of being silently propagated.
- **Adoption: `UpscalerCore.cs` Single-Frame returns** — 7 hardcoded returns (anime+batch, anime+realtime, low-res realtime, HD realtime, very-low-res batch, low-res batch, default batch) now route through `PickAvailable`. Today no behavior change — regression-guard for future `KnownUnavailable` additions.
- **NEW: 9× `HttpContext.RequestAborted`** added to `UpscalerController` HttpClient calls (`/gpus`, `/models/load`, `/benchmark`, `/face-restore/{load,status,unload}`, `/metrics`, `/gpu-verify`, `/health/detailed`). Prevents 120s server-side hangs when client disconnects.
- **NEW: `CacheManager.cs:307`** — synchronous `File.Copy` replaced with async streaming (`FileStream + CopyToAsync` with `useAsync:true`). Was blocking thread-pool thread 5-30s on NAS-mounted disks per cached frame.
- **NEW: csproj-Comments** — removed explicit `v1.6.1.19` version-strings from comments. Pauschal version-bump regex would have falsely re-attributed v1.6.1.19 features to v1.6.1.20 otherwise.
- **+7 new tests** — `SingleFramePaths_AlwaysRouteThroughPickAvailable [Theory]` with 7 InlineData cases. Tests grew 65 → 72 passing.

**Verification:** `dotnet build -c Release` — 0 warnings, 0 errors. `dotnet test` — 72/72 passing. No new models, no schema changes — v1.6.1.19 saved configs are bit-for-bit compatible.

### v1.6.1.19 (Single-Source-of-Truth for Model Availability)

Structural fix release that closes the drift class v1.6.1.17 and v1.6.1.18 patched point-by-point. New `Services/ModelAvailability` static class centralises the `KnownUnavailable` set across all C# resolvers — `UpscalerCore` and `HardwareBenchmarkService` now consult one source of truth.

- **`Services/ModelAvailability.cs` extracted** — 5-entry HashSet + `IsKnownUnavailable()` + `PickAvailable()`. Marked `internal`; `<InternalsVisibleTo>` grant added so test assembly can directly assert on contract.
- **`UpscalerCore` refactored** — removed private `_knownUnavailable` HashSet and bespoke `PickAvailable()` logic, replaced with a thin wrapper that adds `_logger` telemetry on top of the static class. Public behavior unchanged.
- **`HardwareBenchmarkService` hardened** — 7 hardcoded `RecommendedModel`/`FallbackModel` assignments now route through new `EnsureModelAvailable()` helper. Today both `realesrgan-x4` and `fsrcnn-x2` are always-available so this is a regression-guard, not a behavior fix. If either is ever flipped to self-host, the helper warns + falls back to plugin default.
- **Face-Restore dropdown auto-populated** — `#FaceRestoreModel` was hardcoded HTML `<option>`s for `gfpgan-v1.4` / `codeformer`. Now `loadModels()` populates from `category="face_restore"` on Settings page open. Symmetric to the v1.6.1.18 anime/live-action dropdown auto-populate.
- **Homepage card content fix** — `site/index.html` v1.6.1.18 card was incorrectly showing the v1.6.1.16 FFmpeg-fix description because the pauschal version-bump regex bumped the `<h3>` title without rewriting the body. Card now shows actual v1.6.1.19 content.
- **+28 new tests** — `JellyfinUpscalerPlugin.Tests/Services/ModelAvailabilityTests.cs` covers contract assertions, case-insensitivity, fallback-chain semantics, and explicit drift-locks (`HaveCount(5)`, `NotContain("realesrgan-x4")`). Tests grew 37 → 65 passing.

**Verification:** `dotnet build -c Release` — 0 warnings, 0 errors. `dotnet test` — 65/65 passing. No new models, no schema changes — v1.6.1.18 saved configs are bit-for-bit compatible.

### v1.6.1.18 (Live-Action Resolver + RealTime-AI Whitelist + Docs Sync)

Follow-up patch to v1.6.1.17 — fixes 3 sibling bugs an external audit caught after release. The v1.6.1.17 review fixed the anime-side of these bugs but missed the symmetric live-action twin and the compact-family RealTime-AI rejection. No new models, no schema changes.

- **`PreferredLiveActionModel` was Dead-Config** — exact symmetric twin of the v1.6.1.17 PreferredAnimeModel fix. Field was defined, UI dropdown rendered + populated + persisted by Controller — but `ResolveModelForVideo()` never read it. Symmetric hook added: if user sets "Preferred Live-Action Model" to e.g. `drct-l-x4`, the resolver now actually uses it (with `PickAvailable(override → ultrasharp-v2-x4 → nomos2-realplksr-x4 → realesrgan-x4)` fallback chain).
- **RealTime-AI rejected the v1.6.1.17 "Speed Champion"** — `Services/ProcessingStrategySelector.IsRealTimeAIFeasible()` had a hardcoded 9-entry HashSet that drifted from the 14-entry registry. `bhi-realplksr-x4` (advertised as 2× DAT2 throughput), `nomosuni-compact-x2`, `lsdir-compact-x4`, `swinir-small-x2/x4` were all silently rejected with `RealTimeAI skipped: not in fast model list`. HashSet expanded to 14 + new substring safety-net (`compact`, `realplksr`).
- **`docs/MODEL-HOSTING.md` was 5 releases out of date** — still said "v1.6.1.12 catalog". Now points to v1.6.1.17's `drct-l-x4` as a HAT alternative and `real-cugan-x4` as APISR alternative for users who don't want to self-host.
- **+1 new drift-protection [Theory]** in `UpscalerCoreAutoModelTests` — locks down the live-action path so a future contributor can't re-introduce the same class of bug.

**Verification:** `dotnet build -c Release` — 0 warnings, 0 errors. `dotnet test` — 37/37 passing (was 33). No config breaking changes — v1.6.1.17 saved configs are bit-for-bit compatible.

### v1.6.1.17 (Auto-Mode Drift Fix + 5 New SOTA Models + UI Polish)

The big v1.6.1.17 release fixes a critical Auto-Mode bug, eliminates 4 separate cases of model-catalog drift, and adds 5 new SOTA upscaler models.

**Critical fixes:**

- **Auto-Mode multi-frame VSR was silently broken.** `ResolveModelForVideo()` returned `animesr-v2-x4` / `realbasicvsr-x4` / `edvr-m-x4` unconditionally for multi-frame batch jobs — but all three are `available: False` upstream (no public ONNX mirror). Users with `EnableAutoModelSelection=true` got 500 errors with no clear "self-host required" hint.
- **Fix:** New `PickAvailable()` helper in `UpscalerCore.cs` consults a `_knownUnavailable` HashSet and walks a fallback chain. Anime+multi-frame now resolves to `realesrgan-animevideo-x4`. VeryLowRes+multi-frame falls back to `ultrasharp-v2-x4` (DAT2). General multi-frame falls through `ultrasharp-v2-x4` → `nomos2-realplksr-x4` → `realesrgan-x4`.
- **PreferredAnimeModel was Dead-Config.** The default was `""`, and `ResolveModelForVideo` never read the field. Two-part fix: (a) default changed to `anime-compact-x4`, (b) the resolver now actually reads `Config.PreferredAnimeModel` and routes through `PickAvailable`.

**Drift cleanups:**

- **`UpscalerController.cs` fallback list** went from a hardcoded 12 models (used when Docker is briefly unreachable) to all 48 via embedded `Resources/models-fallback.json`. Auto-generated from `app/main.py:AVAILABLE_MODELS` via new `Scripts/sync-fallback-models.ps1`.
- **`site/models.html` listed 8 fictional models** (`waifu2x-cunet-x2`, `hat-l-x4`, `swinir-l-x4`, `realesrnet-x4plus`, `realesrgan-x4plus`, `realesrgan-x2plus`, `realesrgan-anime-x4`, `waifu2x-upconv-x2`) that never existed in the registry. Page now auto-generated from the registry, lists all 48 models in 12 categories.
- **`Services/ModelManager.cs` removed.** 200 LoC dead code — registered as DI singleton in `PluginServiceRegistrator` but never consumed. Header still claimed v1.5.5.4. DLL size dropped 200 KB (1.60 MB → 1.41 MB).
- **Docker service VERSION bumped 1.6.1.15 → 1.6.1.17** — the bump was missed during the v1.6.1.16 release (caught while syncing).

**5 new SOTA models (catalog 43 → 48):**

| ID | Use-Case | Source |
|---|---|---|
| `real-cugan-x2` / `real-cugan-x4` | Anime — cleaner linework than Real-ESRGAN-anime, sharper than waifu2x | `mayhug/Real-CUGAN` |
| `drct-l-x4` | SOTA photo — sharper than DAT2/UltraSharp on real-world photo content | `aaronespasa/drct-super-resolution` |
| `bhi-realplksr-x4` | Speed champion — 2× throughput vs DAT2 at comparable quality | `Phhofm/models` |
| `rife-v4.25` | Frame interpolation — current SOTA, better scene-bleeding handling than v4.7-4.9 | `yuvraj108c/rife-onnx` |

**UI polish:**

- **Upscale-Model dropdown** now filters out `category=interpolation` (RIFE) and `category=face_restore` (GFPGAN/CodeFormer). RIFE takes 2 frames as input — selecting it via the upscale path returned 500. Frame-interpolation has a separate `/interpolate` endpoint, face-restoration runs as a post-processing step, neither belongs in the main "pick a model" picker.
- **`PreferredAnimeModel` + `PreferredLiveActionModel`** are now proper dropdowns (was free-text, prone to typos like `realcugan-x4` vs `real-cugan-x4`). Anime dropdown lists only `category=anime` entries; live-action lists everything else upscale-eligible. Self-host models are filtered out.

**Drift-protection (new):**

- New `JellyfinUpscalerPlugin.Tests/Services/UpscalerCoreAutoModelTests.cs` — 11 unit tests that lock down all multi-frame fallback chains AND a `[Theory]` that asserts `ResolveModelForVideo` never returns a known-unavailable model across 6 input combinations. Future contributors who flip `realbasicvsr-x4` to `available: True` without updating `_knownUnavailable` will get a red CI build instead of a silent regression.
- New `Scripts/sync-fallback-models.ps1` — regenerates `Resources/models-fallback.json` from `app/main.py:AVAILABLE_MODELS`. Recommended CI step: run + `git diff --exit-code` to block PRs that drift the C# fallback from the Python source-of-truth.

**Verification:** `dotnet build -c Release` — 0 warnings, 0 errors. `dotnet test` — 33/33 passing (was 22). DLL embedded resource verified: `JellyfinUpscalerPlugin.Resources.models-fallback.json` present, contains 48-model JSON with all 5 new models.

### v1.6.1.16 (Hotfix: FFprobe Late-Resolution)

Fixes issue [#64](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/64) where the nightly `Scan & Upscale Library` task failed on every item with `AI Upscaler: Failed to upscale <name>: File not found: ffprobe`, even though `ffprobe` was present at the standard path (`/usr/lib/jellyfin-ffmpeg/ffprobe`).

**Root cause:** `VideoProcessor` was constructed as a Jellyfin plugin singleton, and inside its constructor `VideoAnalyzer` was instantiated with the captured `_mediaEncoder.ProbePath`. When `MediaEncoder` had not finished resolving its encoder/probe paths by the time the plugin loaded (cold-start race), `_ffprobePath` was captured as an empty string — permanently. All subsequent `FFprobe.AnalyseAsync` / `Cli.Wrap(_ffprobePath)` calls then failed because `FFMpegCore`'s `GlobalFFOptions.BinaryFolder` was also empty, so FFMpegCore tried to find `ffprobe` in the current directory.

**Fix (three singletons, not one):**
- New `VideoProcessor.EnsureFFmpegReady()` — idempotent: re-queries `MediaEncoder.EncoderPath`/`ProbePath` when either is missing, reconfigures `FFMpegCore`'s `GlobalFFOptions`, and pushes the resolved paths into **all three** sub-services that cache them at construction.
- `VideoAnalyzer` gets the fresh ffprobe path via new `UpdateFFprobePath()`.
- `VideoFrameProcessor` gets the fresh ffmpeg path via new `UpdateFFmpegPath()` — this closed a second-stage error that surfaced after the first fix: `Cannot start process because a file name has not been provided` during `ExtractFramesAsync`, caused by `Cli.Wrap(_ffmpegPath)` with an empty string.
- `ProcessingMethodExecutor` gets the same treatment — another `_ffmpegPath` capture at construction that breaks batch / frame-by-frame routing.
- Called once at construction and again at the top of every entry point that touches ffmpeg or ffprobe: `ProcessVideoAsync`, `ExtractSingleFrameAsync`, `ExtractSingleFrameWithFiltersAsync`. All three affected fields dropped `readonly` so they can be updated late.

**Also:** README header was still labeled v1.6.1.14 after the v1.6.1.15 release — bumped title, docker-image-family banner, architecture diagram, and docker-tag examples to v1.6.1.16 in one pass.

**Verification:** live-tested on Jellyfin 10.11.8 (ABI `10.11.8.0`) — scan task now logs `FFmpeg ready: ffmpeg=/usr/lib/jellyfin-ffmpeg/ffmpeg, ffprobe=/usr/lib/jellyfin-ffmpeg/ffprobe`, `Video analysis: 1280x720 @ 30.0fps` succeeds, and frame extraction actively progresses through the library (31% at verification) where the pre-fix run failed 70/70 items instantly.

### v1.6.1.15 (In-Player Panel Redesign + Auto-Mode)

The quick-menu that pops up from the Upscaler button during playback has been completely redesigned and a new Auto-Mode lets the plugin pick model + filter per video.

**Redesigned in-player panel (non-AI style):**
- Replaces the purple/neon "AI aesthetic" with a clean operator dashboard inspired by Docker Dashboard, Grafana, and Linear.
- Palette: `#0b0d12` background, `#11141b`/`#161a23` surfaces, `#1f2430` borders, `#3b82f6` accent. No gradients, no glow shadows, no `backdrop-filter: blur`.
- Tabs switched from filled-pill to underline-style (2px `#3b82f6` bottom-border on the active tab — same pattern as the config page).
- Border-radius reduced from 18px to 6px (outer) / 3px (inner controls). Square thumb on the main toggle instead of a circle.
- Monospace numerics (`ui-monospace, SFMono-Regular, Menlo, Consolas`) for scale picker, version, FPS, model ID — consistent with the Docker console.
- HTML structure unchanged — all existing event handlers and actions keep working.

**Live status row at the top of the menu:**
- Shows at a glance whether upscaling is running: `● ACTIVE · Server · 24 fps · Real-ESRGAN x4` (or `● IDLE · — · -- fps · <configured model>` when no video is playing).
- Four states, each color-coded via the leading dot: **ACTIVE** (green, upscaling live) / **STANDBY** (amber, video playing but RT not started) / **DISABLED** (gray, user turned upscaling off) / **IDLE** (gray, no playback).
- Polls `RealtimeUpscaler.getStatus()` every 500ms while the menu is open; tears down cleanly on menu close.
- Answers the "is my video actually being upscaled?" question the in-OSD FPS HUD can only hint at.

**Auto-Mode (default OFF):**
- New opt-in toggle under Settings → Auto Model Selection & Fallback: when enabled, the plugin picks **both** the AI model and a color-filter preset for each video on playback start.
- Heuristic runs on the server (`ResolveModelForVideo` + new `ResolveFilterForVideo`) and uses real signals — genres, source resolution, multi-frame capability — not a placeholder:
  - Anime/Animation/Cartoon → `animesr-v2-x4` (batch) / `anime-compact-x4` (realtime) + `vivid` filter
  - Horror/Thriller → genre-appropriate model + `drama` filter
  - Sci-Fi/Cyberpunk → model by resolution + `cyberpunk` filter
  - Documentary/News → `sharp-hd` filter (clarity over aesthetic)
  - Very low-res batch → `realbasicvsr-x4` / `ultrasharp-v2-x4`
  - HD realtime without strong genre signal → `nomosuni-compact-x2` + no filter (conservative default)
- Frontend wiring: `PlayerIntegration._autoSelectForVideo()` calls `GET /Upscaler/recommend-model?width=W&height=H&isBatch=false&genres=…` when `EnableAutoModelSelection` is true. The picked model + filter are applied to the current session only — your saved preferences remain unchanged.
- `ResolveModelForVideo` gained a `forceAuto` flag so the endpoint bypasses the "respect user's explicit model" early-return — otherwise the endpoint would just echo back the configured model.
- Batch scan task (`LibraryUpscaleScanTask`) continues to consult the flag for scheduled library scans.

**Verification:** live-tested on Jellyfin 10.11.8 — 0 console errors on home + video pages; `GET /Upscaler/recommend-model` returns correct picks for anime 480p (`anime-compact-x4` + `vivid`), documentary 1080p (`nomosuni-compact-x2` + `sharp-hd`), horror 480p (`span-x2` + `drama`), and 4K without genre (`nomosuni-compact-x2` + `none`).

### v1.6.1.14 (Select Library + Docker UI Redesign)

Implements a long-standing feature request and fixes three version-display bugs surfaced during full button-level verification.

**Select Library (issue [#64](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/64)):**
- Scheduled scan ("Scan & Upscale Library") can now target specific libraries instead of always scanning every virtual folder. Empty selection preserves the legacy "scan everything" behavior — no change needed for existing installs.
- New UI on the Settings tab: chip-based picker with individual per-library checkboxes, Refresh button, and live chip styling (selected chips turn blue). Persisted via the new `EnabledLibraryIds` config field (comma-separated Jellyfin virtual-folder IDs).
- Backend: `GET /Upscaler/libraries` enumerates virtual folders with ID/name/collectionType/paths. `LibraryUpscaleScanTask` resolves enabled IDs to physical paths once at scan start and post-filters fetched items by path prefix (handles mixed-library item queries correctly, since `InternalItemsQuery.ParentId` is single-GUID).

**Bugfixes found during live-test:**
- `saveConfig` in `configurationpage.html` now actually writes `EnabledLibraryIds` back to the plugin config. The field was added to the load path but missing from the save payload, so any chip selection silently reset to empty on every save click.
- `config.PluginVersion` hardcoded string + header-meta version text + sidebar-upscaler.js `PLUGIN_VERSION` const all synced 1.6.1.13 → 1.6.1.14 (previously save always wrote the old version string back, making the plugin look permanently one version behind).
- `JellyfinUpscalerPlugin.csproj` `Version` / `AssemblyVersion` / `FileVersion` synced, so Jellyfin's plugin manager shows the actual release version instead of the one baked in at source.

**Docker AI service UI (localhost:5000) redesign:**
- API_TOKEN-not-configured state now shows a single persistent dismissible banner instead of repeat-spamming toasts on every `/status` poll. Dismissal is remembered for the session via `sessionStorage`.
- Toast stack now dedupes identical messages within a 10-second window, caps at max 3 visible, includes a close button, and click-to-dismiss with fade-out animation.
- AI Models card gains a name-search box and a status filter (Available / Downloaded / Loaded / All) for quicker navigation through the 29-model catalog.
- FastAPI `/status` endpoint now returns `api_token_configured` and `auth_enabled` so the UI can distinguish "auth intentionally disabled" (`API_TOKEN=disable`) from "operator forgot to set the secret".

**Verification:** live-tested on Jellyfin 10.11.8 — all 26 config-page buttons clicked across 6 tabs with 0 console errors; library-picker chips render correctly from `/Upscaler/libraries`; EnabledLibraryIds persists through save + reload.

### v1.6.1.13 (In-Player Quick-Menu Tabs + Live Filter Controls)

The overlay that pops up from the Upscaler button during playback is now a tabbed interface [Models | Filters | Realtime] instead of a flat scroll. New Filters tab lets you tune video look WITHOUT leaving playback: pick any of 15 preset looks (Cinematic, Vintage, Vivid, Sepia, Cyberpunk, Teal&Orange, ...) or drag 3 live sliders (Brightness / Contrast / Saturation). The `<video>` element's CSS filter property updates at ~60fps for instant feedback — no re-transcode. Advanced collapsible exposes server-side params (Gamma, Sharpness, Color Temperature, Vignette, Film Grain, Denoise) that persist via `POST /Upscaler/filter-config` and kick in via FFmpeg on next seek.

### v1.6.1.12 (Face-Restore Model Fix + RIFE Upgrade)

gfpgan-v1.4 + codeformer repointed from the never-published `kuscheltier/jellyfin-vsr-models` to the public `facefusion/models-3.0.0` mirror — both now `available: true`. RIFE frame-interpolation replaced: old `rife-v4.6` / `rife-v4.6-lite` (pointed at a 404 v0.0.0 placeholder tag) replaced with `rife-v4.9` / `rife-v4.8` / `rife-v4.7` from the `yuvraj108c/rife-onnx` export. `MODEL_ALIASES` keeps old config keys working transparently. Five models without public ONNX mirrors marked `[self-host required]` with `docs/MODEL-HOSTING.md` recipe. New `Scripts/verify-release.ps1` validates release assets (SHA + forbidden-pattern check) after `gh release create`.

### v1.6.1.11 (Auth-header + Elevation Hardening, Model Catalog Cleanup)

Bugfix release — addresses auth regressions and broken model catalog entries found during end-to-end deep-scan of v1.6.1.10.

**Model-name validation:**
- Regex widened to `^[a-zA-Z0-9_-]+(?:\.[a-zA-Z0-9_-]+)*$` — model IDs with dots (rife-v4.6, gfpgan-v1.4, rife-v4.6-lite) no longer return HTTP 400. Path traversal (`..`, leading/trailing dots) still blocked.

**Docker model catalog:**
- 9 models with unreachable upstream URLs flipped to `available: false` so the UI filters them instead of fast-failing: `nomos8k-hat-x4` (CPU EP can't run HAT ops), `apisr-x3` (HF 401), `edvr-m-x4`, `realbasicvsr-x4`, `animesr-v2-x4` (pending HF upload), `rife-v4.6`, `rife-v4.6-lite` (GitHub placeholder tag), `gfpgan-v1.4`, `codeformer` (pending HF upload).

**Auth-header regressions in player-integration.js:**
- Three raw `fetch()` calls that were silently 401-ing switched to `ApiClient.accessToken()` bearer header: `POST /Upscaler/models/load` (primary quick-menu action), `POST /Upscaler/upscale-frame` (realtime frame), `GET /Upscaler/benchmark-frame` (realtime benchmark). Same root cause as v1.6.1.10's `_fetchModelStates` fix.

**Hardening:**
- `GET /Upscaler/jobs` now requires `RequiresElevation` (was exposing active-job file paths to any authenticated user).
- `Jellyfin.Controller` SDK pin bumped 10.11.6 → 10.11.8 to match `targetAbi`.
- `docker-ai-service/app/main.py` `VERSION`: 1.6.1.7 → 1.6.1.11.
- `Dockerfile.amd`, `Dockerfile.apple` `LABEL version`: 1.6.1.10 → 1.6.1.11.

**Verification:** 22/22 unit tests pass; bulk-load live-tested on Jellyfin 10.11.8 — 30 of 30 available models load successfully.

### v1.6.1.10 (Quick-menu Model States Auth Fix)

Hotfix — `_fetchModelStates` in player-integration.js was returning 401 because `ApiClient.getRequestHeader()` (singular method name) does not exist in current apiclient builds. Switched to `ApiClient.ajax` which auto-injects `X-Emby-Token`. Menu summary now shows accurate "N of 42 models ready" counter.

### v1.6.1.9 (Redesigned Player Quick-Menu + API Token Plumbing)

Glassmorphic 380px player menu with filter chips (All/Downloaded/Recommended), live summary strip, per-model download-state icons, in-place spinner during load, smarter failure messaging. New `AiServiceApiToken` plugin config + `X-Api-Token` auth handler auto-injects the shared secret on every AI service call (fixes 403 errors on `/models/download` and `/models/load`).

### v1.6.1.8 (Quick-menu Model Picker Actually Loads)

Quick-menu now calls `POST /Upscaler/models/load` to warm the AI service, not just persist the config. Error toasts now surface real HTTP/service errors instead of a generic message.

### v1.6.1.7 (AI Face Restoration + 8 New Filter Presets + Auto-Preview)

**AI Face Restoration (NEW):**
- **GFPGAN v1.4** and **CodeFormer** ONNX models for restoring faces in low-quality / upscaled video frames
- **OpenCV Haar cascade** face detection — no additional dependencies, uses the cascade bundled with `opencv-python`
- **Feathered alpha-blend mask** (feather_ratio=0.12) for seamless paste-back — no visible seams on restored faces
- **CodeFormer fidelity** (`w` parameter) handled via dynamic ONNX input enumeration (auto-fills secondary inputs)
- **Configurable guards**: `FaceRestoreMaxPerFrame` (1–20, default 6) and `FaceRestoreMaxWidth` (0 = always, else only frames ≤ N px wide)
- **4 new Docker endpoints**: `POST /face-restore/load`, `/face-restore/unload`, `GET /face-restore/status`, `POST /face-restore/frame`
- **3 Jellyfin proxy endpoints** (all require elevation): `Upscaler/face-restore/load|unload|status`
- **New Face Restore card** on Filters tab: model select, sliders, Load/Unload/Preview buttons, before/after comparison

**8 New Camera-Style Filter Presets:**
- **Sepia** — classic monochrome warm tone via curves
- **Pastel** — soft washed look (low contrast/sat, warm WB)
- **Cyberpunk** — high-sat neon push with cool WB and unsharp edge
- **Drama** — punched contrast with vignette and darker curves
- **Soft Glow** — gaussian bloom + mild sharpen (portrait-friendly)
- **Sharp HD** — aggressive unsharp mask (7×7 radius) + mild contrast boost
- **Retro Game** — saturation boost + gamma + subtle film noise
- **Teal & Orange** — Hollywood grade (blues in shadows, oranges in skin tones)

**Live Filter Preview Auto-Update:**
- Changing the preset dropdown on the Filters tab now auto-triggers preview with 300ms debounce
- In-flight cancellation pattern prevents race conditions — only the latest selection renders
- Extracted into `runFilterPreviewCurrent(page, opts)` shared function

**Diagnostic Hints in Model Catalog:**
- 9 single-frame-incompatible models now show friendly N/A hints instead of generic "Failed":
  - EDVR / RealBasicVSR / AnimeSR / APISR-x3 → "Multi-frame — needs 5 frames"
  - RIFE → "Interpolation — needs 2 frames"
  - Vulkan/NCNN variants → "Vulkan/NCNN runtime not bundled"
  - Face restore models → "Use Face tab"

**Docker Service 1.6.1.7:**
- Version sync `main.py` → 1.6.1.7
- New `AppState` fields: `face_restore_session`, `face_restore_model_name`, `face_restore_loaded`, `face_restore_input_size`, `face_detector`
- 2 new model entries in `AVAILABLE_MODELS` (`gfpgan-v1.4`, `codeformer`) hosted on HuggingFace
- Module-level `_face_restore_lock` + `_face_cascade_path_cache`

### v1.6.1.0 (Camera-Style Video Filters)

**Video Filters (Camera-Style):**
- **7 built-in presets**: Cinematic, Vintage, Vivid, Noir, Warm, Cool, HDR Pop — one-click color grading
- **Custom mode**: Full manual control over brightness, contrast, saturation, gamma, sharpness, color temperature, vignette, film grain, denoise
- **LUT color grading**: Support for .cube LUT files for professional color grading
- **New Filters tab** in plugin settings with interactive sliders and live FFmpeg filter chain preview
- **Filter-preview API endpoint** (`POST /api/upscaler/filter-preview`) — returns FFmpeg filter chain for any preset
- **VideoFilterService**: New service that builds FFmpeg `-vf` filter chains, integrated into both `ProcessingMethodExecutor` (full video) and `VideoFrameProcessor` (frame extraction)
- Filters applied as post-processing after upscaling for optimal quality

### v1.5.6.0 (Authorization, Rate Limiting, Lanczos Shader, Security Review Fixes)

**Authorization & Rate Limiting:**
- **RequiresElevation** — 8 sensitive admin endpoints (`/hardware-info`, `/recommendations`, `/recommend-model`, `/compare/{itemId}`, `/cache/stats`, `/hardware`, `/fallback`, `/service-health`) now require Jellyfin admin role
- **Per-user rate limiting** — sliding-window limiter (10 requests/minute) on all upscale endpoints with automatic stale entry pruning

**WebGL Upscaler — Lanczos2 Resampling:**
- **Real Lanczos2 kernel** — replaced FSR-style sharpening with a true 4x4-tap (16 sample) Lanczos2 resampling shader that reconstructs sub-pixel detail from the source texture
- **CAS (Contrast Adaptive Sharpening)** — AMD-style post-processing pass that adapts sharpening strength to local contrast
- **Fixed u_resolution uniform** — shader now receives source video dimensions (not canvas size), fixing incorrect sample coordinates

**Dashboard:**
- **Test Upscale button** — sends a tiny test image through the full pipeline to verify Docker AI service is working end-to-end
- **Namespace cleanup** — `window._benchModel` moved to `window.AiUpscaler._benchModel`
- **Dashboard version** updated to v1.5.6.0

**Docker:**
- **Healthcheck** — `docker-compose.yml` now includes `curl -sf http://localhost:5000/health` healthcheck (30s interval, 3 retries, 30s start period)

**Security Fixes (from code review):**
- **SSRF bypass via IPv4-mapped IPv6** — `::ffff:192.168.1.1` (16-byte address) now correctly detected as private via `IPAddress.MapToIPv4()` normalization; applies to both static IP check and DNS rebinding protection
- **Multipart filename injection** — `UpscaleVideoChunk` now sanitizes `file.Name`/`file.FileName` before forwarding to Docker AI service (prevents CRLF header injection)
- **Dead path traversal check removed** — `Contains("..")` after `Path.GetFullPath()` was unreachable; directory allowlist is the actual protection

**Concurrency Fixes (from code review):**
- **Semaphore signal loss** — `DequeueAsync` now restores the semaphore permit when queue is empty (prevents eventual deadlock under concurrent dequeue workers)

### v1.5.5.9 (Security Hardening, Concurrency Fixes, CI Supply Chain Protection)

**Security — SSRF & DNS Rebinding:**
- **Webhook DNS rebinding protection** — hostname is now DNS-resolved before dispatch; resolved IPs are re-checked against private ranges (prevents TOCTOU attacks where DNS changes between validation and request)
- **CVE-2024-53981 + CVE-2026-28356** — python-multipart pinned to `>=1.2.2,<2.0` across all 7 requirement files
- **CVE-2026-25990** — Pillow pinned to `>=12.1.1,<13.0` across all requirement files
- **Docker API token** — `_require_api_token` now rejects requests when `API_TOKEN` env var is not set (secure-by-default; use `API_TOKEN=disable` to explicitly opt out)

**Concurrency & Stability:**
- **ProcessingQueue semaphore race** — pause/resume no longer leaks semaphore signals; pause is checked before consuming the signal, with a double-check after WaitAsync
- **Timer-after-Dispose** — CacheManager and HardwareBenchmarkService timer callbacks now check `_disposed` flag; timers are stopped (`Change(Infinite, 0)`) before `Dispose()` to prevent callbacks firing during teardown
- **CacheManager unlimited mode** — `CacheSizeMB=0` (unlimited) no longer triggers delete-everything cleanup

**CI/CD Supply Chain:**
- **GitHub Actions SHA pinning** — all 10 action references across both workflows pinned to full commit SHAs (protects against tag-swap supply chain attacks like tj-actions/changed-files 2025)
- **SHA-256 checksums** — release workflow uses sha256sum instead of md5sum
- **Shell injection fix** — `${{ env.* }}` replaced with `${VAR}` in `run:` blocks

**WebGL Performance:**
- **Uniform location caching** — `getUniformLocation` calls cached once at shader link time (eliminates ~120 GPU roundtrips/sec at 60fps)
- **Context restore handler** — `webglcontextrestored` event rebuilds shaders, geometry, and textures automatically (previously required manual page reload)

**Code Quality:**
- **Namespace cleanup** — quick-menu.js globals moved from `window.loadDefaults` etc. to `window.AiUpscaler.*` namespace (backwards compat shims retained)
- **HttpUpscalerService** — `GetServiceUrl()` documented as intentionally allowing localhost/private IPs (AI service runs locally, unlike webhooks)
- **HttpResponseMessage disposal** — all 5 HTTP response objects now use `using var` (fixes potential socket exhaustion)
- **Dashboard version** — config page updated to v1.5.5.9

**Repository Cleanup:**
- Removed 147 tracked files that shouldn't have been in git (old backups, publish dirs, batch scripts, debug logs, agent artifacts)
- Updated `.gitignore` to prevent re-addition

### v1.5.5.8 (Deep Scan Fixes — Security, Performance, Concurrency)

**Critical Fixes:**
- **Library scan performance** — replaced full `ValidateMediaLibrary()` with targeted `RefreshMetadata()` per file (no more scanning entire library after every upscale)
- **FFmpeg argument injection** — `ProcessRealTimeAIAsync` now uses `Process.ArgumentList` instead of raw string interpolation
- **HDR upscaling timeout** — registered `"UpscalerHDR"` named HttpClient with 5-minute timeout (was falling back to 100s default)
- **Temp audio file leak** — `tempAudioPath` now cleaned up in `finally` block on cancellation/failure
- **Content-Type injection** — hardcoded `image/png` in video chunk proxy (was forwarding user-controlled header)

**Security Hardening:**
- Path traversal protection upgraded from blocklist to library folder allowlist (all endpoints)
- Settings import now catches JSON type mismatches gracefully (no more 500 errors)
- Queue error messages sanitized (no more raw `ex.Message` in API responses)
- Frame failure threshold: job aborts if >50% frames fail (prevents false-success output)

**Docker AI Service:**
- `/connections/register` — added `_connections_lock` to prevent concurrent list corruption
- `/upscale-stream` — concurrency semaphore + header validation before acquire (prevents semaphore leak)
- `/upscale-stream` — buffer cap (10 frames max) to prevent OOM on slow GPU
- `/enhance-faces` — added missing API token enforcement
- `/models/cleanup` — fixed inverted auth logic (was blocking when no token set)
- CUDA `device_id` consistently passed as `int` (was `str` in CUDA chain and TRT reload)
- Added `Pillow>=10.0.0` to NVIDIA, CPU, AMD, and Intel requirements (was only in Vulkan)

### v1.5.5.7 (Code Quality, OOM Fix, VideoProcessor Refactor, CI Tests)

**Bug Fixes & Stability:**
- **OOM Fix**: `RealtimeStats` replaced unbounded `int`/`float` accumulators with `deque(maxlen=500)` — prevents memory growth in long-running deployments
- **Deadlock Fix**: Removed ABBA lock pattern in `delete_custom_model` — `_models_registry_lock` nested inside `_model_lock` eliminated
- **Python 3.12**: `asyncio.get_event_loop()` → `asyncio.get_running_loop()` (deprecated in 3.10+)
- **Python 3.12**: `asyncio.wait_for(timeout=0)` bug workaround — semaphore non-blocking check uses `sem._value` instead
- **Thread Pool**: Bounded `ThreadPoolExecutor(max_workers=cpu_count)` replaces unbounded default pool
- **Cache Invalidation**: `upload_custom_model` now calls `_invalidate_models_cache()` — `GET /models` reflects uploads immediately
- **Env Vars**: `MODELS_DIR`/`CACHE_DIR`/`STATIC_DIR` read from env vars — no more hardcoded `/app/models` in upload/delete endpoints

**Performance:**
- **`/models` Cache**: 30s response cache with mutex-protected snapshot — 40x reduction in `Path.exists()` calls

**Code Quality:**
- **VideoProcessor.cs**: 1930-line god class refactored into 5 focused classes: `VideoAnalyzer`, `ProcessingStrategySelector`, `VideoFrameProcessor`, `ProcessingMethodExecutor`, `VideoJobManager`
- **Retry Logic**: `DownloadModelAsync` and `LoadModelAsync` in `HttpUpscalerService` retry once with 2s back-off (no retry on 4xx)

**Testing & CI:**
- **C# Unit Tests**: New `JellyfinUpscalerPlugin.Tests` project — 22 tests (xUnit + Moq + FluentAssertions)
- **Python Test Suite**: 25 pytest tests covering health, auth, validation, semaphore behavior
- **CI**: `python-tests` job (pytest + pip-audit), `docker-smoke-test` job (live container health check)
- **pytest-asyncio**: `asyncio_mode = "auto"` in `pyproject.toml` for Python 3.12 compatibility

### v1.5.5.4 (5 New Features + Deep Security Hardening)

**New Features (all configurable via Dashboard):**
- **Quality Metrics (PSNR/SSIM)**: `/quality-metrics` endpoint — compare bicubic vs AI upscale with Gaussian-based SSIM matching scikit-image
- **Face Enhancement (GFPGAN)**: `/enhance-faces` endpoint — Haar cascade detection, GFPGAN ONNX inference (512x512) or bilateral filter fallback, soft elliptical mask blending
- **Film Grain Management**: `/process-grain` endpoint — NL-means denoising for removal, Gaussian noise for re-grain, "both" mode chains: remove → upscale → re-add
- **Custom ONNX Model Upload**: `/models/upload` + `/models/upload/{name}` endpoints — validate ONNX shape (4D NCHW), register at runtime, path traversal protection
- **OpenAPI/Swagger Docs**: Togglable `/docs` and `/redoc` via `ENABLE_API_DOCS` env var
- **Dashboard UI**: New settings sections for all 5 features with full load/save support
- **Settings Export/Import**: All 30+ new config properties included in export/import with validation

**Security hardening (deep scan rounds 1–4):**
- Thread safety: `/features` endpoint reads shared state under locks, `enhance_face_region` lock granularity improved
- Image dimension validation: `MAX_IMAGE_PIXELS` (256 MP) on all new endpoints to prevent OOM
- Resource leaks: temp file cleanup on `shutil.move` failure in both upload endpoints
- Path traversal: defense-in-depth `resolve()+startswith()` checks in upload and delete endpoints
- Error sanitization: ONNX validation errors split into safe `ValueError` vs generic `Exception`
- Input validation: `MaxUpscaledFileSizeMB` Math.Max(0), `QualityLevel`/`ButtonPosition` allowlists on import
- Removed unused `scale` parameter from `/quality-metrics`

**Round 3 — 13 HIGH fixes (C# + Docker):**
- **SSRF**: Centralized proxy URL validation via `GetValidatedServiceUrl()` — all 13 proxy endpoints secured
- **SSRF**: Webhook DNS rebinding prevention — block `0.0.0.0`, `169.254.x.x` (link-local), `100.64-127.x.x` (CGNAT)
- **SSRF**: DNS resolution via `socket.getaddrinfo` in `/connections/register` — resolved IPs checked against private ranges
- **Path Traversal**: Expanded blocklist (`/root`, `/home`, `/var`, `/tmp`, `/run`, `/opt`, `C:\Users`)
- **Command Injection**: Unix FFmpeg wrapper path regex validation + `/usr/bin/ffmpeg` fallback
- **Info Disclosure**: Generic error messages in 20+ catch blocks — no more `ex.Message` in HTTP responses
- **Auth**: API token (`X-Api-Token` + `hmac.compare_digest`) required on `/models/load`, `/models/download`, `/config`, `/benchmark`, `/benchmark-frame`
- **DoS**: `asyncio.Lock` on benchmark endpoints — 429 "already in progress" rate limit
- **DoS**: 2GB disk space check before multi-frame extraction in `ProcessMultiFrameAsync`
- **XSS**: `innerHTML` replaced with `createElement`/`replaceChildren` in Docker Web UI
- **Path Safety**: MODELS_DIR prefix validation in TensorRT subprocess probe
- **Resource Disclosure**: JS resource allowlist in `GetJavaScript` endpoint (4 permitted files)
- **Race Condition**: `SemaphoreSlim` for cache cleanup/store coordination + size recalculation

**Round 2 — Security hardening:**
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
- **Docker**: Updated docker-ai-service README (40+ models, docker6.1 tags)

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
- **Docker**: All 6 images tagged as docker6.1

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
- **NVIDIA RTX 5000 (Blackwell)**: Requires CUDA 12.8+ — use latest `:docker7` image. Compute capability sm_120 auto-detected.
- **NVIDIA RTX 4000/3000/2000**: Install `nvidia-container-toolkit`, use `--gpus all`. TensorRT is skipped by default — set `SKIP_TENSORRT=false` if your GPU supports it.
- **Intel**: Use `:docker7-intel` tag with `--device=/dev/dri --group-add=render`. Check diagnostics: `curl http://YOUR_SERVER_IP:5000/gpu-verify`
- **AMD**: Use `:docker7-amd` tag with `--device=/dev/kfd --device=/dev/dri`
- **Apple M1–M5**: Docker on macOS runs CPU-only. For GPU acceleration via CoreML/Neural Engine, use the native install:
  ```bash
  cd docker-ai-service && chmod +x install-native-macos.sh && ./install-native-macos.sh
  ```
- **Windows Docker Desktop (WSL2)**: Intel/AMD GPUs accessible via `/dev/dxg` + WSL2-driver mount — see `docker-ai-service/docker-compose.yml` WSL2 section. NVIDIA: use NVIDIA Container Toolkit. FP16 mismatch (Issue #67) is auto-detected in v1.7.4+ based on the loaded ONNX model's input type.

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

- 💬 **[Support Assistant](https://kuschel-code.github.io/JellyfinUpscalerPlugin/)** — an in-browser bot on the docs site (button, bottom-right) that answers from **every issue we've ever handled**: install errors, GPU not used, Docker/NAS setup, API token, model choice, and more. No login, runs entirely in your browser.
- [Project Website](https://transcendent-blancmange-824967.netlify.app)
- [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)
- [GitHub Wiki](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki)

---

## License

MIT License - See [LICENSE](LICENSE) for details.
