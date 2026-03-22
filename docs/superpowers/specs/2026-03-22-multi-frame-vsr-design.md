# Design: v1.5.4.0 — Multi-Frame Video Super-Resolution (VSR)

## Overview

Add multi-frame video super-resolution to the Jellyfin AI Upscaler Plugin. Instead of upscaling each frame independently, multi-frame models (EDVR-M, RealBasicVSR, AnimeSR) use 5 consecutive frames as input to produce one higher-quality output frame. This captures temporal information (motion, detail across frames) that single-frame models miss, resulting in significantly better video quality with fewer artifacts and better temporal consistency.

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
"edvr-m-x4": {
    "name": "EDVR-M x4 (Video SR - 5 Frame)",
    "url": "https://huggingface.co/kuscheltier/jellyfin-vsr-models/resolve/main/edvr_m_x4.onnx",
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
DetermineProcessingMethod(inputInfo, hardwareProfile, options, inputFrames):
    if EnableAIUpscaling && inputFrames > 1:
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

**Request:** Multipart form with 5 PNG files (`frame_0` through `frame_4`). PNG to avoid lossy JPEG re-encoding in the quality-critical batch path.

**Response:** Single PNG — the upscaled center frame

**State changes required:**
- `AppState` gains `current_model_input_frames: int = 1`
- When model loaded via `/models/load`: `state.current_model_input_frames = AVAILABLE_MODELS[name].get("input_frames", 1)`
- `/status` response includes `"input_frames": state.current_model_input_frames`

**ONNX Input/Output Tensor Shapes:**

All VSR models in this spec use the standard `(N, T, C, H, W)` layout:
```
Input:  (1, 5, 3, H, W)    float32   — 5 frames, RGB, height, width
Output: (1, 3, H*scale, W*scale)     — single upscaled center frame
```

Note: EDVR and AnimeSR output only the center frame. RealBasicVSR outputs all T frames `(1, T, 3, H*s, W*s)` — we slice `output[:, T//2, :, :, :]` to get the center frame.

**New inference function `_onnx_infer_multiframe_tile()`:**

```python
def _onnx_infer_multiframe_tile(tiles: list[np.ndarray], session, input_name, output_name, num_frames: int) -> np.ndarray:
    """
    tiles: list of num_frames numpy arrays, each (H, W, 3) RGB float32 [0,1]
    Returns: single upscaled tile (H*s, W*s, 3) RGB float32
    """
    # Stack: (T, H, W, 3) → transpose → (T, 3, H, W) → expand → (1, T, 3, H, W)
    stacked = np.stack(tiles, axis=0)                    # (T, H, W, 3)
    stacked = np.transpose(stacked, (0, 3, 1, 2))       # (T, 3, H, W)
    batch = np.expand_dims(stacked, axis=0)              # (1, T, 3, H, W)

    result = session.run([output_name], {input_name: batch})[0]

    # Handle models that output all T frames
    if result.ndim == 5:  # (1, T, 3, H*s, W*s)
        result = result[:, num_frames // 2, :, :, :]     # center frame

    result = np.squeeze(result, axis=0)                  # (3, H*s, W*s)
    result = np.transpose(result, (1, 2, 0))             # (H*s, W*s, 3)
    return result
```

**Tiling for Multi-Frame:**
- All 5 frames are tiled with identical grid (same positions, same overlap)
- For each tile position: extract tile from each of the 5 frames → list of 5 tiles
- Call `_onnx_infer_multiframe_tile(tiles_list, ...)` → single output tile
- Stitch output tiles with same blending/weight accumulation as single-frame
- Default tile size for multi-frame: 256 (not 512) to reduce VRAM. Configurable via `ONNX_TILE_SIZE_MULTIFRAME` env var.

**Concurrency:** Semaphore-protected (same as `/upscale`).

**Fallback within Docker:** If `state.current_model_input_frames == 1` (single-frame model loaded), extract center frame (index 2) from the multipart upload and upscale with existing `upscale_image_array()`. Return HTTP 200 with upscaled center frame. No error code — transparent fallback.

**Error handling:** Returns 503 if busy (semaphore), 400 if number of uploaded frames != expected `input_frames`, 500 on inference error with detail message.

### 2. VideoProcessor: `ProcessMultiFrameAsync()`

**File:** `Services/VideoProcessor.cs`

**New method** alongside existing `ProcessFrameByFrameAsync()`. NOT a modification of the existing method.

**CRITICAL: Sequential processing only.** Do NOT use the parallel `Task.WhenAll` / `SemaphoreSlim` pattern from `ProcessFramesAsync`. Each window must be processed in order: `await` each `/upscale-video-chunk` call before starting the next. Reason: sliding window order must be maintained, and multi-frame models use 3-5x more VRAM than single-frame (concurrent requests would OOM).

**Sliding Window Logic:**
```
Total frames: N
Window size: input_frames (e.g., 5)
Half window: input_frames / 2 (e.g., 2)

For output frame i (0 to N-1):       ← sequential, NOT parallel
    window = []
    For each position j in range(i - half_window, i + half_window + 1):
        if j < 0:         window.append(frame[0])        (boundary repeat)
        elif j >= N:       window.append(frame[N-1])      (boundary repeat)
        else:              window.append(frame[j])

    POST /upscale-video-chunk with window as multipart PNG
    Save result as output frame i
    Report progress: i / N
```

**Edge padding examples:**
- 1-frame video (N=1): window = `[0,0,0,0,0]` — all same frame, degrades to single-frame quality. Valid.
- 2-frame video (N=2): frame 0 window = `[0,0,0,0,1]`, frame 1 window = `[0,0,1,1,1]`. Valid.
- 3-frame video (N=3): frame 0 window = `[0,0,0,1,2]`. Valid.

**Multipart upload construction:**
```csharp
using var content = new MultipartFormDataContent();
for (int j = 0; j < windowFrames.Count; j++)
{
    var frameBytes = await File.ReadAllBytesAsync(windowFrames[j], ct);
    var byteContent = new ByteArrayContent(frameBytes);
    byteContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
    content.Add(byteContent, $"frame_{j}", $"frame_{j}.png");
}
```

**Error handling:** On failure for any window, copy original center frame to output directory (preserves contiguous sequence for FFmpeg reconstruction).

**Progress:** Same SignalR progress reporting as existing frame-by-frame — one update per output frame.

### 3. Jellyfin Proxy: `/api/upscaler/upscale-video-chunk`

**File:** `Controllers/UpscalerController.cs`

New proxy endpoint forwarding multipart form data to Docker service. Same pattern as existing `/upscale-frame` proxy but with multipart instead of raw body.

**Timeout:** Requires a SEPARATE static `HttpClient` with 300-second timeout:
```csharp
private static readonly HttpClient _multiFrameClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(300)
};
```
The existing `_aiServiceClient` has a 30-second timeout which is too short for multi-frame inference. Cannot change timeout on an existing `HttpClient` after first use.

### 4. UpscalerCore Integration

**File:** `Services/UpscalerCore.cs`

Flow:
1. `UpscalerCore.ProcessVideoAsync()` calls `HttpUpscalerService.GetServiceStatusAsync()`
2. `ServiceStatus` now includes `InputFrames` property (int, default 1)
3. `UpscalerCore` passes `inputFrames` to `VideoProcessor.DetermineProcessingMethod()`
4. `DetermineProcessingMethod` signature change: add `int inputFrames = 1` parameter
5. If `inputFrames > 1` → return `ProcessingMethod.MultiFrame`

**File:** `Services/HttpUpscalerService.cs`
- `ServiceStatus` class gains: `public int InputFrames { get; set; } = 1;`
- `GetServiceStatusAsync()` maps `input_frames` from JSON response

**File:** `Models/UpscalerModels.cs`
- `ProcessingMethod` enum gains: `MultiFrame` value
- `ExecuteProcessingAsync` switch gains: `ProcessingMethod.MultiFrame => ProcessMultiFrameAsync(...)`

---

## Models

### Phase 1: EDVR-M x4 (Proof of Concept)
- **Architecture:** EDVR-M — Medium variant WITHOUT Deformable Convolutions
- **Why -M:** Full EDVR requires DeformConv2d (opset 19) which is CPU-only in ONNX Runtime < 1.24. EDVR-M uses standard convolutions → full GPU acceleration guaranteed.
- **Input:** 5 frames, any resolution. Tensor shape: `(1, 5, 3, H, W)`
- **Output:** 1 center frame. Tensor shape: `(1, 3, H*4, W*4)`
- **Scale:** 4x
- **Size:** ~10MB ONNX
- **Runtime note:** When full EDVR DeformConv2d GPU support lands in ORT, upgrade path is trivial (same API, same shapes, just swap ONNX file).

### Phase 2: RealBasicVSR x4
- **Architecture:** Recurrent VSR with optical flow (CVPR 2022)
- **Input:** 5 frames. Tensor shape: `(1, 5, 3, H, W)`
- **Output:** All 5 frames upscaled. Shape: `(1, 5, 3, H*4, W*4)` — we slice center frame `[:, 2, :, :, :]`
- **Scale:** 4x
- **Key feature:** Best quality for real-world degraded video (VHS, DVD, streaming)
- **ONNX export:** Requires unrolling recurrent connections to fixed 5-frame window
- **Size:** ~50MB ONNX

### Phase 3: AnimeSR v2 x4
- **Architecture:** Anime-specialized VSR (NeurIPS 2022)
- **Input:** 5 frames. Tensor shape: `(1, 5, 3, H, W)`
- **Output:** 1 center frame. Shape: `(1, 3, H*4, W*4)`
- **Scale:** 4x
- **Key feature:** Trained on anime — preserves line art, flat colors, reduces banding
- **Size:** ~30MB ONNX

### Conversion Tool
**File:** `docker-ai-service/tools/convert_to_onnx.py`

Script to convert PyTorch checkpoints to ONNX:
- Downloads pretrained weights from original repos
- Exports with dynamic axes for H, W (variable resolution)
- Fixed T axis (always 5 for our pipeline)
- Validates output against PyTorch reference
- Uploads to HuggingFace (manual step)

---

## Fallback Chain

```
Multi-Frame model loaded? (check ServiceStatus.InputFrames > 1)
├─ Yes → ProcessMultiFrameAsync() [sequential loop]
│        └─ POST /upscale-video-chunk (5 PNG frames as multipart)
│             ├─ HTTP 200 → upscaled center frame (save to processed dir)
│             └─ HTTP 4xx/5xx/timeout → copy original center frame (fallback)
└─ No → ProcessFrameByFrameAsync() (existing, unchanged)
         └─ POST /upscale (1 frame)
              ├─ Success → upscaled frame
              └─ Error → use original frame

Docker-side fallback (transparent):
  /upscale-video-chunk called but single-frame model loaded?
  → Extract center frame → upscale with upscale_image_array() → HTTP 200
  (No error code — caller doesn't need to know)
```

---

## What Is NOT In Scope

- Real-time multi-frame upscaling (latency too high)
- Temporal consistency for real-time WebGL/Server mode
- Custom model training or fine-tuning
- New UI elements (model selection already exists)
- New config properties (auto-detection via model metadata)
- Full EDVR with DeformConv2d (waiting for ORT GPU support)

---

## Files to Create/Modify

| File | Action | Description |
|------|--------|-------------|
| `docker-ai-service/app/main.py` | Modify | `/upscale-video-chunk` endpoint, `_onnx_infer_multiframe_tile()`, `AppState.current_model_input_frames`, `/status` response update, new models in AVAILABLE_MODELS |
| `docker-ai-service/tools/convert_to_onnx.py` | Create | PyTorch → ONNX conversion script for EDVR-M/RealBasicVSR/AnimeSR |
| `Services/VideoProcessor.cs` | Modify | `ProcessMultiFrameAsync()` with sequential sliding window, multipart upload |
| `Services/UpscalerCore.cs` | Modify | Read `InputFrames` from `ServiceStatus`, pass to `DetermineProcessingMethod()` |
| `Services/HttpUpscalerService.cs` | Modify | `ServiceStatus.InputFrames` property, map from `/status` JSON |
| `Models/UpscalerModels.cs` | Modify | Add `MultiFrame` to `ProcessingMethod` enum |
| `Controllers/UpscalerController.cs` | Modify | Proxy for `/upscale-video-chunk` (multipart), new `_multiFrameClient` with 300s timeout |
| Version files (6) | Modify | Bump to 1.5.4.0 |
| `README.md` | Modify | Document multi-frame VSR feature |
| `website/i18n.js` | Modify | Changelog + badge for all 6 languages |
| Manifests (3) | Modify | New version entry with checksum |

---

## VRAM Budget

| Mode | Tile Size | Input Tensor | Approx VRAM |
|------|-----------|-------------|-------------|
| Single-frame | 512x512 | (1,3,512,512) = 3MB | ~500MB with activations |
| Multi-frame | 256x256 | (1,5,3,256,256) = 3.75MB | ~800MB with activations |
| Multi-frame | 512x512 | (1,5,3,512,512) = 15MB | ~2.5GB with activations |

Default `ONNX_TILE_SIZE_MULTIFRAME = 256` for safety on 4GB GPUs.

---

## Verification

1. `dotnet build -c Release` — 0 errors, 0 warnings
2. Python AST parse on `main.py` — syntax valid
3. Send 5 PNG frames to `/upscale-video-chunk` with multi-frame model → returns 1 upscaled PNG
4. Send 5 PNG frames with single-frame model loaded → transparent fallback, returns upscaled center frame (HTTP 200, not error)
5. `ProcessMultiFrameAsync` with 10-frame test video → outputs 10 contiguous frames in correct order
6. Edge case: 1-frame video → window `[0,0,0,0,0]` → valid output (degenerate to single-frame quality)
7. Edge case: 2-frame video → windows padded correctly, 2 output frames
8. Edge case: 3-frame video → windows padded correctly, 3 output frames
9. Frame error mid-sequence → original frame copied, no gaps in sequence
10. Model with `input_frames: 5` → `ServiceStatus.InputFrames == 5` → `ProcessingMethod.MultiFrame` auto-selected
11. Sequential processing verified: no parallel dispatch in `ProcessMultiFrameAsync`
12. VRAM: multi-frame with 256px tiles stays under 1GB on 4GB GPU
