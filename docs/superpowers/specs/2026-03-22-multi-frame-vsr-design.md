# Design: v1.5.4.0 — Multi-Frame Video Super-Resolution (VSR)

## Overview

Add multi-frame video super-resolution to the Jellyfin AI Upscaler Plugin. Instead of upscaling each frame independently, multi-frame models (EDVR, RealBasicVSR, AnimeSR) use 5 consecutive frames as input to produce one higher-quality output frame. This captures temporal information (motion, detail across frames) that single-frame models miss, resulting in significantly better video quality with fewer artifacts and better temporal consistency.

**Scope:** Batch/scheduled upscaling only. Real-time playback stays single-frame (latency constraints).

**Version:** 1.5.4.0

---

## Architecture

### Current Flow (Single-Frame)
```
FFmpeg extract → [Frame N] → POST /upscale → [Upscaled Frame N] → FFmpeg reconstruct
```

### New Flow (Multi-Frame)
```
FFmpeg extract → Sliding Window [N-2, N-1, N, N+1, N+2] → POST /upscale-video-chunk
    → Model processes 5 frames → [Upscaled Frame N] → FFmpeg reconstruct
```

### Model Detection
Models declare their input frame count via `input_frames` field in `AVAILABLE_MODELS`:

```python
"edvr-x4": {
    "name": "EDVR x4 (Video SR - 5 Frame)",
    "url": "https://huggingface.co/kuscheltier/jellyfin-vsr-models/resolve/main/edvr_x4.onnx",
    "scale": 4,
    "type": "onnx",
    "category": "video-sr",
    "model_type": "edvr",
    "input_frames": 5,
    "available": True
}
```

- `input_frames` absent or `1` → single-frame pipeline (existing behavior)
- `input_frames: 5` → multi-frame pipeline with 5-frame sliding window

### Pipeline Selection
```
DetermineProcessingMethod():
    if EnableAIUpscaling && loaded model has input_frames > 1:
        → ProcessingMethod.MultiFrame
    elif EnableAIUpscaling && normal model:
        → ProcessingMethod.FrameByFrame (existing)
    elif video < 5min && no AI:
        → ProcessingMethod.RealTime (FFmpeg filters)
```

---

## Component Design

### 1. Docker Service: `POST /upscale-video-chunk`

**File:** `docker-ai-service/app/main.py`

**Request:** Multipart form with 5 JPEG files (`frame_0` through `frame_4`)

**Response:** Single JPEG — the upscaled center frame

**ONNX Input Shape:**
```
Single-Frame:  (1, 3, H, W)           → (1, 3, H*scale, W*scale)
Multi-Frame:   (1, input_frames, 3, H, W) → (1, 3, H*scale, W*scale)
```

**Tiling for Multi-Frame:**
- Each frame in the window is tiled identically (same tile grid)
- For each tile position: stack tiles from all 5 frames → shape (1, 5, 3, tile_h, tile_w)
- Run inference per tile-stack
- Stitch output tiles with same blending as single-frame

**Concurrency:** Semaphore-protected (same as `/upscale`). Sequential processing recommended — multi-frame models use 3-5x more VRAM than single-frame.

**Fallback:** If `state.current_model_input_frames == 1` (single-frame model loaded), extract center frame and upscale with existing single-frame pipeline. Returns valid result either way.

**Error handling:** Returns 503 if busy, 400 if wrong number of frames, 500 on inference error.

### 2. VideoProcessor: `ProcessMultiFrameAsync()`

**File:** `Services/VideoProcessor.cs`

**New method** alongside existing `ProcessFrameByFrameAsync()`. NOT a modification of the existing method.

**Sliding Window Logic:**
```
Total frames: N
Window size: input_frames (e.g., 5)
Half window: input_frames / 2 (e.g., 2)

For output frame i (0 to N-1):
    window_start = i - half_window
    window_end = i + half_window

    For each position j in window:
        if j < 0:         use frame 0        (mirror padding)
        elif j >= N:       use frame N-1      (mirror padding)
        else:              use frame j

    Send 5 frames to /upscale-video-chunk
    Save result as output frame i
```

**Edge padding:** First and last frames use repeated boundary frames (not black frames). Frame 0's window: `[0, 0, 0, 1, 2]`. Frame 1's window: `[0, 0, 1, 2, 3]`.

**Error handling:** On failure, copy original frame to output directory (preserves contiguous sequence for FFmpeg).

**Progress:** Same SignalR progress reporting as existing frame-by-frame — one update per output frame.

### 3. Jellyfin Proxy: `/api/upscaler/upscale-video-chunk`

**File:** `Controllers/UpscalerController.cs`

New proxy endpoint forwarding multipart form data to Docker service. Same pattern as existing `/upscale-frame` proxy but with multipart instead of raw body.

**Timeout:** 300 seconds (multi-frame inference is slower than single-frame).

### 4. UpscalerCore Integration

**File:** `Services/UpscalerCore.cs`

- Query loaded model info via `/models/status` endpoint
- Read `input_frames` from model metadata
- Pass to `VideoProcessor.DetermineProcessingMethod()`
- No new config properties needed — fully automatic based on loaded model

---

## Models

### Phase 1: EDVR x4 (Proof of Concept)
- **Architecture:** Enhanced Deformable Video Restoration (CVPRW 2019)
- **Input:** 5 frames, any resolution
- **Scale:** 4x
- **Key feature:** Deformable Convolutions align frames without explicit optical flow
- **ONNX export:** Requires opset 19+ for DeformConv2d
- **Fallback:** EDVR-M (no deformable conv) if export fails
- **Size:** ~20MB ONNX

### Phase 2: RealBasicVSR x4
- **Architecture:** Recurrent VSR with optical flow (CVPR 2022)
- **Input:** 5-15 frames (we use 5 for consistency)
- **Scale:** 4x
- **Key feature:** Best quality for real-world degraded video (VHS, DVD, streaming)
- **ONNX export:** Requires unrolling recurrent connections
- **Size:** ~50MB ONNX

### Phase 3: AnimeSR v2 x4
- **Architecture:** Anime-specialized VSR (NeurIPS 2022)
- **Input:** 5 frames
- **Scale:** 4x
- **Key feature:** Trained on anime — preserves line art, flat colors, reduces banding
- **Size:** ~30MB ONNX

### Conversion Tool
**File:** `docker-ai-service/tools/convert_to_onnx.py`

Script to convert PyTorch checkpoints to ONNX:
- Downloads pretrained weights from original repos
- Exports with dynamic axes (variable resolution)
- Validates output against PyTorch reference
- Uploads to HuggingFace (manual step)

---

## Fallback Chain

```
Multi-Frame model loaded?
├─ Yes → ProcessMultiFrameAsync()
│        └─ POST /upscale-video-chunk (5 frames)
│             ├─ Success → upscaled center frame
│             └─ Error → fallback: upscale center frame with single-frame pipeline
│                  └─ Error → use original frame
└─ No → ProcessFrameByFrameAsync() (existing, unchanged)
         └─ POST /upscale (1 frame)
              ├─ Success → upscaled frame
              └─ Error → use original frame
```

---

## What Is NOT In Scope

- Real-time multi-frame upscaling (latency too high)
- Temporal consistency for real-time WebGL/Server mode
- Custom model training or fine-tuning
- New UI elements (model selection already exists)
- New config properties (auto-detection via model metadata)

---

## Files to Create/Modify

| File | Action | Description |
|------|--------|-------------|
| `docker-ai-service/app/main.py` | Modify | `/upscale-video-chunk` endpoint, multi-frame ONNX inference, new models in AVAILABLE_MODELS, `input_frames` support |
| `docker-ai-service/tools/convert_to_onnx.py` | Create | PyTorch → ONNX conversion script for EDVR/RealBasicVSR/AnimeSR |
| `Services/VideoProcessor.cs` | Modify | `ProcessMultiFrameAsync()`, sliding window, multipart upload, new `ProcessingMethod.MultiFrame` enum value |
| `Services/UpscalerCore.cs` | Modify | Read `input_frames` from model info, pass to processing method selection |
| `Controllers/UpscalerController.cs` | Modify | Proxy endpoint for `/upscale-video-chunk` (multipart) |
| Version files (6) | Modify | Bump to 1.5.4.0 |
| `README.md` | Modify | Document multi-frame VSR feature |
| `website/i18n.js` | Modify | Changelog + badge for all 6 languages |
| Manifests (3) | Modify | New version entry with checksum |

---

## Verification

1. `dotnet build -c Release` — 0 errors, 0 warnings
2. Python AST parse on `main.py` — syntax valid
3. Send 5 test frames to `/upscale-video-chunk` — returns 1 upscaled frame
4. Send 5 frames with single-frame model loaded — falls back to center frame upscale
5. `ProcessMultiFrameAsync` with 10-frame test video — outputs 10 contiguous frames
6. Edge case: 3-frame video (shorter than window) — padding works correctly
7. Frame error mid-sequence — original frame used, no gaps
8. Model with `input_frames: 5` auto-selects `ProcessingMethod.MultiFrame`
