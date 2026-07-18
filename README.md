# Jellyfin AI Upscaler Plugin v1.8.3.12

[![Built with Claude](https://img.shields.io/badge/Built%20with-Claude%20Opus%204.8%20%26%20Fable%205-D97757?logo=anthropic&logoColor=white&style=for-the-badge)](https://www.anthropic.com/claude)

> **Built with Claude Opus 4.8 & Claude Fable 5** — this plugin is developed and maintained entirely with [Anthropic's Claude models](https://www.anthropic.com/claude) (Opus 4.8 through v1.8.3.4, [Fable 5](https://www.anthropic.com/news/claude-fable-5-mythos-5) since v1.8.3.5). Code contributions, Dockerfiles, CI workflows and documentation are produced in a pair-programming style with the model; the maintainer ([Kuschel-code](https://github.com/Kuschel-code)) reviews, tests and publishes every change. Release commits carry the `Co-Authored-By: Claude` trailer as disclosure.

---

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Jellyfin Version](https://img.shields.io/badge/Jellyfin-10.11.x+-00A4DC.svg)](https://jellyfin.org)
[![Docker Hub](https://img.shields.io/docker/pulls/kuscheltier/jellyfin-ai-upscaler?logo=docker&label=Docker%20Hub)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Docker Image](https://img.shields.io/docker/v/kuscheltier/jellyfin-ai-upscaler?logo=docker&label=Latest)](https://hub.docker.com/r/kuscheltier/jellyfin-ai-upscaler)
[![Documentation](https://img.shields.io/badge/Docs-kuschel--code.github.io-blueviolet)](https://kuschel-code.github.io/JellyfinUpscalerPlugin/)

AI-powered video upscaling for Jellyfin. Upscale SD content to HD/4K using neural networks, running entirely in a Docker container with GPU acceleration.

**Docker Images (docker7 base — released in lockstep with the plugin, both at v1.8.3.12):**
*   `kuscheltier/jellyfin-ai-upscaler:docker7` (NVIDIA CUDA + cuDNN 9)
*   `kuscheltier/jellyfin-ai-upscaler:docker7-amd` (AMD ROCm)
*   `kuscheltier/jellyfin-ai-upscaler:docker7-intel` (Intel Arc/iGPU OpenVINO)
*   `kuscheltier/jellyfin-ai-upscaler:docker7-apple` (macOS Apple Silicon — multi-arch amd64/arm64)
*   `kuscheltier/jellyfin-ai-upscaler:docker7-vulkan` (Vulkan/ncnn — AMD pre-RDNA2, Intel iGPU)
*   `kuscheltier/jellyfin-ai-upscaler:docker7-cpu` (CPU Only — multi-threaded ONNXRuntime, multi-arch)
*   `kuscheltier/jellyfin-ai-upscaler:docker7-converter` (CPU + pth→ONNX converter for OpenModelDB community models — opt-in, ~2 GB)

**Report bugs:** [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)

**Contents:** [Architecture](#architecture) · [How It Works](#how-it-works) · [Installation](#installation) · [Features](#features) · [AI Models](#ai-models-76-curated--660-importable) · [Configuration](#configuration) · [Docker Tags](#docker-image-tags) · [Changelog](#changelog) · [Troubleshooting](#troubleshooting) · [Support](#support)

---

## Architecture

Jellyfin's plugin system tries to load ALL `.dll` files as .NET assemblies. Native C++ libraries (ONNX Runtime, CUDA, OpenCV) caused `BadImageFormatException` crashes in older versions. The solution: a Docker microservice architecture where the plugin (only ~1.6 MB) communicates with an external AI container via HTTP.

```
┌──────────────────────────────────────────┐
│  Jellyfin Server                         │
│  ┌────────────────────────────────────┐  │
│  │  AI Upscaler Plugin v1.8.3.12   │  │
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
│  │  EDSR, FSRCNN, ESPCN (76+ models) │  │
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
- Select from **76 curated models** (+ hundreds importable from OpenModelDB) across 12 categories (Real-ESRGAN, SPAN, SwinIR, DAT2, EDVR-M, RealBasicVSR, AnimeSR, APISR, EDSR, LapSRN, FSRCNN, ESPCN, ncnn-Vulkan)
- Choose scale factor (2x, 3x, 4x, 8x)
- Toggle real-time upscaling and switch modes
- Quick access via keyboard shortcuts (Alt+U, Alt+M)

---

## Jellyfin 12.0 (RC) compatibility

Jellyfin 12.0 is in release-candidate phase. Static analysis against `Jellyfin.Controller 12.0.0-rc2` found **exactly one** API break (`IUserManager.Users` removed) — fixed in v1.8.3.4 with a version-adaptive lookup, so one DLL targets 10.11.x and is expected to load on 12.0. The plugin's web code already uses only the modern `Authorization: MediaBrowser` scheme, so 12.0's default rejection of legacy auth does not affect it. Runtime verification on an RC box is pending — see [docs/JELLYFIN-12-READINESS.md](docs/JELLYFIN-12-READINESS.md) for the full break list and test plan. Do not upgrade your production server to an RC just for this.

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

**CPU + Model Converter (adds .pth community-model conversion, ~2 GB):**
```bash
docker run -d \r
  --name jellyfin-ai-upscaler \r
  -p 5000:5000 \r
  -v ai-models:/app/models \r
  kuscheltier/jellyfin-ai-upscaler:docker7-converter
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

- **76 Curated AI Models**: Real-ESRGAN, SPAN, SwinIR, DAT2, EDVR-M, RealBasicVSR, AnimeSR, APISR, EDSR, FSRCNN, ESPCN, LapSRN (2x–8x)
- **OpenModelDB Importer (v1.8.3.8+)**: one-click import of 660+ community models from the config page or the `:5000` dashboard — sha256-pinned, ZIP-aware; the opt-in `docker7-converter` image converts `.pth` models to ONNX with output verification; installs land in the ★ Favorites card
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
- **7 Image Variants**: NVIDIA CUDA/TensorRT, AMD ROCm, Intel OpenVINO, Vulkan/ncnn, Apple Silicon, CPU, CPU+Converter
- **Player Integration**: In-player button with quick settings menu, FPS overlay, keyboard shortcuts (Alt+U, Alt+M)
- **Web UI**: Model management at `http://YOUR_SERVER_IP:5000`

---

## AI Models (76 curated + 660+ importable)

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

Beyond the curated catalog, the [Importable models](https://kuschel-code.github.io/JellyfinUpscalerPlugin/models-import.html) page lists 660+ OpenModelDB community models — import them one-click from the plugin config page or the `:5000` dashboard (`.pth` models convert automatically with the `docker7-converter` image).

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

The full version history lives on the website and the release pages — this README no longer duplicates it:

- **[Changelog (website)](https://kuschel-code.github.io/JellyfinUpscalerPlugin/changelog.html)** — every release in detail
- **[GitHub Releases](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases)** — release notes and downloadable ZIPs

Latest: **v1.8.3.12** — dashboard Auto/Custom mode, face-model re-pins, UI polish · **v1.8.3.11** — Models tab, settings redesign with toggles, async imports, player favorites · **v1.8.3.10** — import-UX hotfix

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
