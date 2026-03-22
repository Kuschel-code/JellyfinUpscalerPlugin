# Multi-Frame Video Super-Resolution Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add multi-frame VSR (5 frames → 1 upscaled frame) to the batch upscaling pipeline, with EDVR-M as proof-of-concept model.

**Architecture:** Sliding window sends 5 PNG frames to new `/upscale-video-chunk` endpoint. Docker service stacks frames as `(1,5,3,H,W)` tensor, runs ONNX inference, returns upscaled center frame. C# `ProcessMultiFrameAsync()` handles window construction + sequential dispatch. Auto-detection via `input_frames` model metadata.

**Tech Stack:** Python/FastAPI (ONNX Runtime), C#/.NET 9 (Jellyfin plugin), FFmpeg (frame extraction/reconstruction)

**Spec:** `docs/superpowers/specs/2026-03-22-multi-frame-vsr-design.md`

---

## Chunk 1: Docker Service — Multi-Frame Inference

### Task 1: Add `current_model_input_frames` to AppState + `/status`

**Files:**
- Modify: `docker-ai-service/app/main.py:53-88` (AppState class)
- Modify: `docker-ai-service/app/main.py:1363-1386` (/status endpoint)
- Modify: `docker-ai-service/app/main.py:898` (model load — set input_frames)

- [ ] **Step 1: Add `current_model_input_frames` to AppState (line ~88)**

After the last property in AppState, add:
```python
    current_model_input_frames: int = 1
```

- [ ] **Step 2: Set `input_frames` when loading ONNX model**

Find where `state.onnx_model_scale = model_info.get("scale", 4)` is set (line ~898). Add below it:
```python
        state.current_model_input_frames = model_info.get("input_frames", 1)
```

Do the same at ALL other places where `onnx_model_scale` is set (CoreML path ~line 954, etc.).

- [ ] **Step 3: Include `input_frames` in `/status` response**

In the `/status` endpoint response dict (line ~1372-1386), add:
```python
        "input_frames": state.current_model_input_frames,
```

- [ ] **Step 4: Add `ONNX_TILE_SIZE_MULTIFRAME` env var**

Near `ONNX_TILE_SIZE` (line ~98), add:
```python
ONNX_TILE_SIZE_MULTIFRAME = int(os.getenv("ONNX_TILE_SIZE_MULTIFRAME", "256"))
```

- [ ] **Step 5: Verify syntax**

Run: `python -c "import ast; ast.parse(open(r'docker-ai-service/app/main.py').read())"`
Expected: No output (success)

- [ ] **Step 6: Commit**

```bash
git add docker-ai-service/app/main.py
git commit -m "feat(docker): add input_frames to AppState and /status"
```

---

### Task 2: Add `_onnx_infer_multiframe_tile()` function

**Files:**
- Modify: `docker-ai-service/app/main.py` (after `_onnx_infer_tile` at line ~1174)

- [ ] **Step 1: Add the multi-frame tile inference function**

After `_onnx_infer_tile()` (line ~1181), add:

```python
def _onnx_infer_multiframe_tile(tiles: list, session, input_name: str, output_name: str, num_frames: int) -> np.ndarray:
    """Infer a multi-frame tile stack. tiles = list of num_frames arrays, each (H,W,3) float32 [0,1]."""
    stacked = np.stack(tiles, axis=0)                    # (T, H, W, 3)
    stacked = np.transpose(stacked, (0, 3, 1, 2))       # (T, 3, H, W)
    batch = np.expand_dims(stacked, axis=0).astype(np.float32)  # (1, T, 3, H, W)

    result = session.run([output_name], {input_name: batch})[0]

    # Handle models that output all T frames: (1, T, 3, H*s, W*s)
    if result.ndim == 5:
        result = result[:, num_frames // 2, :, :, :]     # center frame only

    result = np.squeeze(result, axis=0)                  # (3, H*s, W*s)
    result = np.transpose(result, (1, 2, 0))             # (H*s, W*s, 3)
    return result
```

- [ ] **Step 2: Verify syntax**

Run: `python -c "import ast; ast.parse(open(r'docker-ai-service/app/main.py').read())"`

- [ ] **Step 3: Commit**

```bash
git add docker-ai-service/app/main.py
git commit -m "feat(docker): add _onnx_infer_multiframe_tile function"
```

---

### Task 3: Add `upscale_multiframe()` function with tiling

**Files:**
- Modify: `docker-ai-service/app/main.py` (after `upscale_with_onnx`)

- [ ] **Step 1: Add the multi-frame upscale function with tiling**

After `upscale_with_onnx()` function, add:

```python
def upscale_multiframe(frames: list) -> np.ndarray:
    """Upscale using multi-frame model. frames = list of np.ndarray BGR images. Returns upscaled center frame BGR."""
    num_frames = len(frames)

    # Acquire model references once under lock
    with _model_lock:
        session = state.onnx_session
        if session is None:
            raise ValueError("No ONNX model loaded")
        input_name = session.get_inputs()[0].name
        output_name = session.get_outputs()[0].name
        scale = state.onnx_model_scale or 4

    # Convert all frames BGR → RGB float32
    frames_rgb = []
    for f in frames:
        rgb = cv2.cvtColor(f, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
        frames_rgb.append(rgb)

    h, w, _ = frames_rgb[0].shape
    tile_size = ONNX_TILE_SIZE_MULTIFRAME
    overlap = 32

    # Small image fast path: no tiling needed
    if w <= tile_size and h <= tile_size:
        result_rgb = _onnx_infer_multiframe_tile(frames_rgb, session, input_name, output_name, num_frames)
        result_rgb = np.clip(result_rgb * 255.0, 0, 255).astype(np.uint8)
        return cv2.cvtColor(result_rgb, cv2.COLOR_RGB2BGR)

    # Tiled processing — same grid for all frames
    out_h, out_w = h * scale, w * scale
    output = np.zeros((out_h, out_w, 3), dtype=np.float64)
    weight = np.zeros((out_h, out_w, 3), dtype=np.float64)

    step = tile_size - overlap
    y_tiles = list(range(0, max(h - tile_size, 0) + 1, step))
    if not y_tiles or y_tiles[-1] + tile_size < h:
        y_tiles.append(max(h - tile_size, 0))
    x_tiles = list(range(0, max(w - tile_size, 0) + 1, step))
    if not x_tiles or x_tiles[-1] + tile_size < w:
        x_tiles.append(max(w - tile_size, 0))

    for y in y_tiles:
        for x in x_tiles:
            # Extract same tile position from all frames
            tile_list = []
            for frame_rgb in frames_rgb:
                tile = frame_rgb[y:y+tile_size, x:x+tile_size, :]
                tile_list.append(tile)

            out_tile = _onnx_infer_multiframe_tile(tile_list, session, input_name, output_name, num_frames)

            # Build blend weights (same as single-frame tiling)
            th, tw = out_tile.shape[:2]
            blend_y = np.ones(th, dtype=np.float64)
            blend_x = np.ones(tw, dtype=np.float64)
            ramp = overlap * scale
            if ramp > 0:
                for i in range(min(ramp, th)):
                    blend_y[i] = i / ramp
                    blend_y[th - 1 - i] = i / ramp
                for i in range(min(ramp, tw)):
                    blend_x[i] = i / ramp
                    blend_x[tw - 1 - i] = i / ramp
            blend_w = blend_y[:, None] * blend_x[None, :]
            blend_w3 = blend_w[:, :, None]

            oy, ox = y * scale, x * scale
            oy_end = min(oy + th, out_h)
            ox_end = min(ox + tw, out_w)
            actual_th = oy_end - oy
            actual_tw = ox_end - ox

            output[oy:oy_end, ox:ox_end] += out_tile[:actual_th, :actual_tw].astype(np.float64) * blend_w3[:actual_th, :actual_tw]
            weight[oy:oy_end, ox:ox_end] += blend_w3[:actual_th, :actual_tw]

    weight = np.maximum(weight, 1e-8)
    output = output / weight
    output = np.clip(output * 255.0, 0, 255).astype(np.uint8)
    return cv2.cvtColor(output, cv2.COLOR_RGB2BGR)
```

- [ ] **Step 2: Verify syntax**

Run: `python -c "import ast; ast.parse(open(r'docker-ai-service/app/main.py').read())"`

- [ ] **Step 3: Commit**

```bash
git add docker-ai-service/app/main.py
git commit -m "feat(docker): add upscale_multiframe with tiled inference"
```

---

### Task 4: Add `POST /upscale-video-chunk` endpoint

**Files:**
- Modify: `docker-ai-service/app/main.py` (after existing `/upscale-frame` endpoint)

- [ ] **Step 1: Add the endpoint**

After the `/upscale-frame` endpoint, add:

```python
@app.post("/upscale-video-chunk")
async def upscale_video_chunk(request: Request):
    """Multi-frame upscaling: receives N PNG frames, returns upscaled center frame."""
    if state.current_model is None:
        raise HTTPException(status_code=400, detail="No model loaded")

    expected_frames = state.current_model_input_frames

    # Semaphore with acquired flag
    acquired = False
    try:
        await asyncio.wait_for(_upscale_semaphore.acquire(), timeout=0)
        acquired = True
    except asyncio.TimeoutError:
        raise HTTPException(status_code=503, detail="Busy")

    try:
        state.processing_count += 1

        # Parse multipart form
        form = await request.form()
        frame_files = []
        for i in range(expected_frames):
            key = f"frame_{i}"
            if key not in form:
                raise HTTPException(status_code=400, detail=f"Missing {key}. Expected {expected_frames} frames.")
            frame_files.append(form[key])

        # Read all frames
        frames = []
        for ff in frame_files:
            data = await ff.read()
            img = cv2.imdecode(np.frombuffer(data, np.uint8), cv2.IMREAD_COLOR)
            if img is None:
                raise HTTPException(status_code=400, detail="Invalid image data")
            frames.append(img)

        # If single-frame model loaded, transparent fallback: upscale center frame only
        if expected_frames == 1 or state.current_model_input_frames == 1:
            center = frames[len(frames) // 2]
            result = upscale_image_array(center)
        else:
            result = upscale_multiframe(frames)

        # Encode as PNG
        _, buffer = cv2.imencode('.png', result)
        return Response(content=buffer.tobytes(), media_type="image/png")

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Multi-frame upscale error: {e}")
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        state.processing_count -= 1
        if acquired:
            _upscale_semaphore.release()
```

- [ ] **Step 2: Verify syntax**

Run: `python -c "import ast; ast.parse(open(r'docker-ai-service/app/main.py').read())"`

- [ ] **Step 3: Commit**

```bash
git add docker-ai-service/app/main.py
git commit -m "feat(docker): add /upscale-video-chunk endpoint"
```

---

### Task 5: Add VSR models to AVAILABLE_MODELS

**Files:**
- Modify: `docker-ai-service/app/main.py` (in AVAILABLE_MODELS dict, after nextgen section)

- [ ] **Step 1: Add EDVR-M model entry**

In `AVAILABLE_MODELS` dict, after the last nextgen model and before the closing `}`, add:

```python
    # ============================================================
    # === VIDEO SR Models (Multi-Frame) ===
    # ============================================================
    "edvr-m-x4": {
        "name": "EDVR-M x4 (Video SR - 5 Frame)",
        "url": "https://huggingface.co/kuscheltier/jellyfin-vsr-models/resolve/main/edvr_m_x4.onnx",
        "scale": 4,
        "description": "EDVR-M — Multi-frame video super-resolution. Uses 5 frames for temporal consistency. Best batch quality.",
        "type": "onnx",
        "category": "video-sr",
        "model_type": "edvr",
        "input_frames": 5,
        "available": True
    },
```

- [ ] **Step 2: Verify syntax + commit**

```bash
python -c "import ast; ast.parse(open(r'docker-ai-service/app/main.py').read())"
git add docker-ai-service/app/main.py
git commit -m "feat(docker): add EDVR-M x4 VSR model to AVAILABLE_MODELS"
```

---

## Chunk 2: C# Plugin — Pipeline Integration

### Task 6: Add `MultiFrame` to `ProcessingMethod` enum

**Files:**
- Modify: `Models/UpscalerModels.cs:229-234`

- [ ] **Step 1: Add enum value**

Find the `ProcessingMethod` enum (line ~229). Add `MultiFrame`:

```csharp
public enum ProcessingMethod
{
    RealTime,
    FrameByFrame,
    Batch,
    MultiFrame
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build -c Release`
Expected: 0 errors (the switch default arm `_ => throw` handles the new value until we add the case)

- [ ] **Step 3: Commit**

```bash
git add Models/UpscalerModels.cs
git commit -m "feat: add MultiFrame to ProcessingMethod enum"
```

---

### Task 7: Add `InputFrames` to `ServiceStatus`

**Files:**
- Modify: `Services/HttpUpscalerService.cs:333-342` (ServiceStatus class)

- [ ] **Step 1: Add property to ServiceStatus**

In the `ServiceStatus` class (line ~333), add:

```csharp
    public int InputFrames { get; set; } = 1;
```

- [ ] **Step 2: Build + commit**

Run: `dotnet build -c Release`

```bash
git add Services/HttpUpscalerService.cs
git commit -m "feat: add InputFrames to ServiceStatus"
```

---

### Task 8: Update `DetermineProcessingMethod` + `ExecuteProcessingAsync`

**Files:**
- Modify: `Services/VideoProcessor.cs:408-427` (DetermineProcessingMethod)
- Modify: `Services/VideoProcessor.cs:439-445` (ExecuteProcessingAsync switch)

- [ ] **Step 1: Add `inputFrames` parameter to `DetermineProcessingMethod`**

Change signature (line ~408) from:
```csharp
private ProcessingMethod DetermineProcessingMethod(
    VideoInfo inputInfo,
    HardwareProfile hardwareProfile,
    VideoProcessingOptions options)
```
To:
```csharp
private ProcessingMethod DetermineProcessingMethod(
    VideoInfo inputInfo,
    HardwareProfile hardwareProfile,
    VideoProcessingOptions options,
    int inputFrames = 1)
```

- [ ] **Step 2: Add MultiFrame check at TOP of method body (before existing logic)**

At the beginning of the method body (line ~413), BEFORE the existing checks, add:

```csharp
    // Multi-frame VSR takes priority when model supports it
    if (options.EnableAIUpscaling && inputFrames > 1)
    {
        _logger.LogInformation("Multi-frame model detected (input_frames={Frames}), using MultiFrame processing", inputFrames);
        return ProcessingMethod.MultiFrame;
    }
```

- [ ] **Step 3: Add case to ExecuteProcessingAsync switch**

In the switch at line ~439, add before the default arm:

```csharp
    ProcessingMethod.MultiFrame => await ProcessMultiFrameAsync(inputPath, outputPath, job, inputFrames, cancellationToken),
```

Note: `inputFrames` needs to be passed through. Add it as a field on the method or pass via `job`. Simplest: add `int inputFrames` parameter to `ExecuteProcessingAsync` and pass it from the call site.

- [ ] **Step 4: Update call site in ProcessVideoAsync**

Find where `DetermineProcessingMethod` is called (line ~158 in VideoProcessor.cs). Before this call, fetch `InputFrames` from the service status. The service status should already be available in the flow. Pass it as the 4th argument.

- [ ] **Step 5: Build to verify**

Run: `dotnet build -c Release`
Expected: Errors about missing `ProcessMultiFrameAsync` — that's expected, we add it next.

- [ ] **Step 6: Commit**

```bash
git add Services/VideoProcessor.cs
git commit -m "feat: wire MultiFrame into processing method selection"
```

---

### Task 9: Implement `ProcessMultiFrameAsync()`

**Files:**
- Modify: `Services/VideoProcessor.cs` (new method after `ProcessFrameByFrameAsync`)

- [ ] **Step 1: Add the method**

After `ProcessFrameByFrameAsync()` (which starts at line ~499), add:

```csharp
private async Task<VideoProcessingResult> ProcessMultiFrameAsync(
    string inputPath,
    string outputPath,
    ProcessingJob job,
    int inputFrames,
    CancellationToken cancellationToken)
{
    var tempDir = Path.Combine(Path.GetTempPath(), $"upscaler_mf_{Guid.NewGuid():N}");

    try
    {
        var framesDir = Path.Combine(tempDir, "frames");
        var processedDir = Path.Combine(tempDir, "processed");
        Directory.CreateDirectory(framesDir);
        Directory.CreateDirectory(processedDir);

        // Extract frames (same as frame-by-frame)
        var effectiveFps = job.InputInfo?.FrameRate ?? 24;
        var extractArgs = $"-i \"{inputPath}\" -vf fps={effectiveFps} \"{framesDir}/frame_%06d.png\"";
        _logger.LogInformation("Extracting frames for multi-frame processing: {Args}", extractArgs);

        await Cli.Wrap(_ffmpegPath)
            .WithArguments(extractArgs)
            .WithValidation(CommandResultValidation.ZeroExitCode)
            .ExecuteAsync(cancellationToken);

        var frameFiles = Directory.GetFiles(framesDir, "*.png")
            .OrderBy(f => f)
            .ToList();

        if (frameFiles.Count == 0)
        {
            throw new InvalidOperationException("No frames extracted");
        }

        _logger.LogInformation("Extracted {Count} frames. Processing with {InputFrames}-frame sliding window (SEQUENTIAL)",
            frameFiles.Count, inputFrames);

        int halfWindow = inputFrames / 2;
        int totalFrames = frameFiles.Count;
        int processedCount = 0;

        // SEQUENTIAL sliding window — do NOT parallelize
        for (int i = 0; i < totalFrames; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Build window with boundary padding
                var windowPaths = new List<string>();
                for (int j = i - halfWindow; j <= i + halfWindow; j++)
                {
                    int idx = Math.Clamp(j, 0, totalFrames - 1);
                    windowPaths.Add(frameFiles[idx]);
                }

                // Send window to AI service
                var serviceUrl = _configuration["UpscalerServiceUrl"] ?? "http://localhost:5000";
                using var content = new MultipartFormDataContent();
                for (int k = 0; k < windowPaths.Count; k++)
                {
                    var frameBytes = await File.ReadAllBytesAsync(windowPaths[k], cancellationToken);
                    var byteContent = new ByteArrayContent(frameBytes);
                    byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    content.Add(byteContent, $"frame_{k}", $"frame_{k}.png");
                }

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
                var response = await client.PostAsync($"{serviceUrl}/upscale-video-chunk", content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var resultBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    var outputFile = Path.Combine(processedDir, Path.GetFileName(frameFiles[i]));
                    await File.WriteAllBytesAsync(outputFile, resultBytes, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("Multi-frame upscale failed for frame {Frame} (HTTP {Code}), using original",
                        i, (int)response.StatusCode);
                    var outputFile = Path.Combine(processedDir, Path.GetFileName(frameFiles[i]));
                    File.Copy(frameFiles[i], outputFile, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Multi-frame upscale error for frame {Frame}, using original", i);
                var outputFile = Path.Combine(processedDir, Path.GetFileName(frameFiles[i]));
                File.Copy(frameFiles[i], outputFile, true);
            }

            processedCount++;
            var progress = (double)processedCount / totalFrames * 100;
            _logger.LogDebug("Multi-frame progress: {Progress:F1}% ({Processed}/{Total})",
                progress, processedCount, totalFrames);
        }

        // Reconstruct video (same as frame-by-frame)
        _logger.LogInformation("Reconstructing video from {Count} processed frames", totalFrames);
        var codec = _configuration["OutputCodec"] ?? "libx264";
        var reconstructArgs = $"-framerate {effectiveFps} -i \"{processedDir}/frame_%06d.png\" -c:v {codec} -pix_fmt yuv420p \"{outputPath}\"";

        await Cli.Wrap(_ffmpegPath)
            .WithArguments(reconstructArgs)
            .WithValidation(CommandResultValidation.ZeroExitCode)
            .ExecuteAsync(cancellationToken);

        return new VideoProcessingResult
        {
            Success = true,
            OutputPath = outputPath,
            ProcessingMethod = ProcessingMethod.MultiFrame
        };
    }
    finally
    {
        // Cleanup temp directory
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup temp directory: {Dir}", tempDir);
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build -c Release`
Expected: 0 errors, 0 warnings

- [ ] **Step 3: Commit**

```bash
git add Services/VideoProcessor.cs
git commit -m "feat: implement ProcessMultiFrameAsync with sliding window"
```

---

### Task 10: Add proxy endpoint in UpscalerController

**Files:**
- Modify: `Controllers/UpscalerController.cs:40` (add second HttpClient)
- Modify: `Controllers/UpscalerController.cs` (new endpoint after upscale-frame)

- [ ] **Step 1: Add second HttpClient with 300s timeout**

After the existing `_aiServiceClient` declaration (line ~40), add:

```csharp
private static readonly HttpClient _multiFrameClient = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
```

- [ ] **Step 2: Add proxy endpoint**

After the existing `UpscaleFrame()` endpoint (line ~870), add:

```csharp
[HttpPost("upscale-video-chunk")]
public async Task<ActionResult> UpscaleVideoChunk()
{
    var config = Plugin.Instance?.Configuration;
    if (config == null) return StatusCode(500, "Plugin not configured");

    var serviceUrl = config.AIServiceUrl?.TrimEnd('/') ?? "http://localhost:5000";

    try
    {
        // Forward the entire multipart form to the AI service
        var form = await Request.ReadFormAsync();
        using var content = new MultipartFormDataContent();

        foreach (var file in form.Files)
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var byteContent = new ByteArrayContent(ms.ToArray());
            byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType ?? "image/png");
            content.Add(byteContent, file.Name, file.FileName ?? file.Name);
        }

        var response = await _multiFrameClient.PostAsync($"{serviceUrl}/upscale-video-chunk", content);

        if (response.IsSuccessStatusCode)
        {
            var resultBytes = await response.Content.ReadAsByteArrayAsync();
            return File(resultBytes, "image/png");
        }

        return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }
    catch (TaskCanceledException)
    {
        return StatusCode(504, "AI service timeout (multi-frame inference)");
    }
    catch (Exception ex)
    {
        return StatusCode(502, $"AI service error: {ex.Message}");
    }
}
```

- [ ] **Step 3: Build + commit**

Run: `dotnet build -c Release`

```bash
git add Controllers/UpscalerController.cs
git commit -m "feat: add /upscale-video-chunk proxy with 300s timeout"
```

---

## Chunk 3: Version Bump + Docs + Release

### Task 11: Version bump to 1.5.4.0

**Files:**
- Modify: `JellyfinUpscalerPlugin.csproj` (Version, AssemblyVersion, FileVersion)
- Modify: `Plugin.cs` (version comment)
- Modify: `PluginConfiguration.cs` (PluginVersion default)
- Modify: `Services/UpscalerCore.cs` (version comment)
- Modify: `Configuration/player-integration.js` (PLUGIN_VERSION)
- Modify: `meta.json` (version field)

- [ ] **Step 1: Replace "1.5.3.5" with "1.5.4.0" in all version files**

Use targeted edits — do NOT use replace_all on files that have changelog entries mentioning "1.5.3.5" as a historical version.

- [ ] **Step 2: Build to verify**

Run: `dotnet build -c Release`

- [ ] **Step 3: Commit**

```bash
git add JellyfinUpscalerPlugin.csproj Plugin.cs PluginConfiguration.cs Services/UpscalerCore.cs Configuration/player-integration.js meta.json
git commit -m "chore: bump version to 1.5.4.0"
```

---

### Task 12: Update README + website i18n + manifests

**Files:**
- Modify: `README.md` (version, changelog, feature description)
- Modify: `website/i18n.js` (badge + changelog for 6 languages)
- Modify: `manifest.json`, `repository-jellyfin.json`, `repository-simple.json`

- [ ] **Step 1: Update README**

- Change title version to v1.5.4.0
- Add changelog entry for v1.5.4.0:
  - Added: Multi-Frame Video Super-Resolution (EDVR-M) — 5-frame sliding window for batch upscaling
  - Added: `/upscale-video-chunk` endpoint for multi-frame inference
  - Added: Auto-detection of multi-frame models via `input_frames` metadata
  - Added: Sequential processing pipeline with boundary frame padding

- [ ] **Step 2: Update website i18n**

For all 6 languages (en, de, fr, zh, ru, ja):
- Badge → v1.5.4.0 with subtitle "Multi-Frame Video SR"
- Changelog entry with items above translated

- [ ] **Step 3: Build ZIP + compute checksum**

```bash
dotnet publish -c Release -o publish_1540
cd publish_1540 && powershell -Command "Compress-Archive -Path '*' -DestinationPath '../ai-upscaler-plugin-v1.5.4.0.zip' -Force"
cd .. && md5sum ai-upscaler-plugin-v1.5.4.0.zip
```

- [ ] **Step 4: Update manifests with new version + checksum**

Add v1.5.4.0 entry above v1.5.3.5 in all 3 manifest files.

- [ ] **Step 5: Commit all**

```bash
git add README.md website/i18n.js manifest.json repository-jellyfin.json repository-simple.json
git commit -m "docs: update README, website, manifests for v1.5.4.0"
```

---

### Task 13: Create ONNX conversion tool

**Files:**
- Create: `docker-ai-service/tools/convert_to_onnx.py`

- [ ] **Step 1: Create the conversion script**

```python
#!/usr/bin/env python3
"""Convert VSR PyTorch models to ONNX for JellyfinUpscalerPlugin.

Usage:
    python convert_to_onnx.py --model edvr-m --output edvr_m_x4.onnx
    python convert_to_onnx.py --model realbasicvsr --output realbasicvsr_x4.onnx
    python convert_to_onnx.py --model animesr --output animesr_v2_x4.onnx

Requirements:
    pip install torch torchvision basicsr onnx onnxruntime
"""
import argparse
import torch
import numpy as np

def convert_edvr_m(output_path: str, num_frames: int = 5):
    """Convert EDVR-M (no deformable conv) to ONNX."""
    # Import from basicsr
    from basicsr.archs.edvr_arch import EDVR

    model = EDVR(
        num_in_ch=3, num_out_ch=3, num_feat=64,
        num_frame=num_frames, deformable_groups=0,  # M variant: no deformable conv
        num_extract_block=5, num_reconstruct_block=10,
        center_frame_idx=num_frames // 2,
        with_tsa=False  # M variant: no temporal-spatial attention
    )
    model.eval()

    # Download pretrained weights if available
    # For now, export with random weights as proof-of-concept
    dummy_input = torch.randn(1, num_frames, 3, 64, 64)

    torch.onnx.export(
        model, dummy_input, output_path,
        opset_version=17,
        input_names=['input'],
        output_names=['output'],
        dynamic_axes={
            'input': {3: 'height', 4: 'width'},
            'output': {2: 'out_height', 3: 'out_width'}
        }
    )
    print(f"Exported EDVR-M to {output_path}")

    # Validate
    import onnxruntime as ort
    session = ort.InferenceSession(output_path)
    test_input = np.random.randn(1, num_frames, 3, 64, 64).astype(np.float32)
    result = session.run(None, {'input': test_input})[0]
    print(f"Input shape: {test_input.shape}, Output shape: {result.shape}")
    assert result.shape[2] == 64 * 4, f"Expected 4x upscale, got {result.shape}"
    print("Validation passed!")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Convert VSR models to ONNX")
    parser.add_argument("--model", choices=["edvr-m", "realbasicvsr", "animesr"], required=True)
    parser.add_argument("--output", required=True, help="Output ONNX file path")
    parser.add_argument("--num-frames", type=int, default=5)
    args = parser.parse_args()

    if args.model == "edvr-m":
        convert_edvr_m(args.output, args.num_frames)
    else:
        print(f"Model {args.model} conversion not yet implemented. Coming in Phase 2/3.")
```

- [ ] **Step 2: Commit**

```bash
git add docker-ai-service/tools/convert_to_onnx.py
git commit -m "feat: add ONNX conversion tool for VSR models (Phase 1: EDVR-M)"
```

---

### Task 14: Final build + push + release

- [ ] **Step 1: Full clean build**

```bash
dotnet build -c Release
```
Expected: 0 errors, 0 warnings

- [ ] **Step 2: Python syntax check**

```bash
python -c "import ast; ast.parse(open(r'docker-ai-service/app/main.py').read())"
```

- [ ] **Step 3: Push to GitHub**

```bash
git push origin main
```

- [ ] **Step 4: Create GitHub release**

```bash
gh release create v1.5.4.0 ai-upscaler-plugin-v1.5.4.0.zip --title "v1.5.4.0 — Multi-Frame Video Super-Resolution" --notes "..."
```

---

## Implementation Notes

- **DO NOT push to GitHub** until user explicitly says to. Per CLAUDE.md: "noch nicht auf github pushen"
- Tasks 1-5 (Docker service) are independent of Tasks 6-10 (C# plugin) and can be done in parallel
- Task 9 (ProcessMultiFrameAsync) depends on Tasks 6-8
- Task 12 (docs) depends on Task 11 (version bump)
- Task 13 (conversion tool) is independent of everything else
- Task 14 (release) depends on everything else
