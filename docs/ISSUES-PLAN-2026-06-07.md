# Issues & v1.7.10 Plan — 2026-06-07

**Context:** v1.7.9 is shipped (Docker + plugin: Setup Doctor `GET /doctor`, embedded Anime4K tier, GPU-state fix). Two fixes are **already committed to `main` but not yet released** — they ride along in the next build:
- `1c1b2bb` — `/doctor` `onnx_provider_pkg` no longer false-fails on CPU images.
- `f3a7450` — `configurationpage.html` version strings synced to 1.7.9.

Four open support issues (**#69 Arc A310**, **#70 Arc A380/WSL2**, **#71 RTX 4060/ZimaOS**, **#72 Mac M1**) cluster on exactly **two recurring frictions**: *GPU-setup* and *"job stuck at 95%"*. This plan converts those into code/docs/bot fixes and bundles them into **v1.7.10**.

> **Rev. 3 (FINAL) — code-level cross-review verified against `main`; all 4 verification points resolved.** Adjustments folded in: **WS4 P3 -> P2** (the stale `PLUGIN_VERSION` is shown in the System-Diagnostics panel users screenshot); **WS3 split** into the verified time-estimate cap (`ProcessingStrategySelector.cs:19/21/213-216`) and a pinpointed fail-fast (`VideoProcessor.cs:261` does *"proceeding with default"* on no-model instead of aborting = the #70 root cause); **WS5 = dual release** (plugin DLL + Docker).
> **Verification:** **V1** #72 confirmed from screenshots (Mac M1, model loaded, `/upscale` 200 OK but CPU-slow on `docker7-apple` — slow-CPU/expectation class, *not* a new bug). **V2** batch path checks the model (`VideoProcessor.cs:242`) but proceeds on failure (`:261`) instead of aborting → fail-fast = one `return Failed`. **V3** real frame-progress plumbing already exists (`UpscalerProgressHub.SendFrameProgress`, `ProcessingJob.TotalFrames`, `VideoFrameProcessor` logs `n/total @ fps`) → WS3-H1 is low-effort wiring. **V4** issue tracker: **4 open (#69-#72)**, no new replies; #69/#70 are *fixed in code but still administratively open* (close after the next image re-pull confirms); closed #62-#67 (May) map onto the WS1 bot-KB topics (install / docker-connect / WSL2 / ONNX-input / "no newer version").

---

## Recurring themes -> root cause -> fix

| Theme (issues) | Root cause | Fix |
|---|---|---|
| **"Job stuck at 95%"** (#70, #72) | Two distinct cases both surface as 95%: (a) **no model loaded** -> every `/upscale` returns **400** -> task grinds to 95% and never finishes; (b) **model loaded but slow on CPU** -> `/upscale` **200 OK** but ~4 fps -> progress *pinned* at 95% while still crunching. | **WS3**: fail-fast + honest progress. |
| **GPU "not recognized" / "CPU Mode"** (#69, #71) | GPU passthrough OK + CUDA EP *available*, but the ONNX **session falls back to CPU** (e.g. #71: host NVIDIA driver too old for the image's CUDA 12/cuDNN 9 -> `model_smoke` 3624 ms). `/doctor` mislabels this as `backend=cpu` / "no GPU image", which is contradictory. | **WS2**: doctor detects "GPU available but inactive". |
| **OpenCV vs ONNX model** (#70) | `espcn/fsrcnn/edsr/lapsrn` are OpenCV `.pb` -> **CPU only**; only **ONNX** models use OpenVINO/CUDA. Users pick a `.pb` model and wonder why the GPU is idle / CPU pegs. | **WS1** KB + a UI hint. |
| **Benchmark fps != real-time** (#70) | Models-tab fps is small-tile inference (e.g. 66 fps); real-time pushes full 1080p frames through capture->JPEG->HTTP->inference->render (his realtime bench: **0.3 fps**) -> drops to WebGL. | **WS1** KB; steer live use to **Anime4K** (client GPU). |
| **Apple Silicon / Docker** (#72) | Docker on macOS runs a Linux VM with **no Metal/CoreML passthrough** -> the M1 GPU is invisible -> `docker7-apple` in Docker is CPU-only. | **WS1** KB; document native-macOS path for GPU. |
| **"Can't pull image / authentication required"** (seen in local test) | A stale/invalid Docker Hub login is sent even for the public image. | **WS1** KB: `docker logout`. |

---

## Workstreams

### WS1 - Support-bot KB build-out · P1 · Pages only (no release) · effort: low, risk: none
Encode every case above into `site/assets/support-kb.json` so users self-serve (and we stop hand-answering the same things). New/expanded step-by-step topics:
- **gpu-not-recognized** - image is `docker7-<backend>`; pass the device (`--gpus all` / `/dev/dri` / `/dev/dxg`); `docker exec ... nvidia-smi`; `/doctor`; recreate the app. **Plus** the "CUDA available but `model_smoke` is seconds (CPU) -> host driver too old, update it" case (#71).
- **opencv-vs-onnx** - `espcn/fsrcnn/edsr/lapsrn` = CPU; pick an **ONNX** model (e.g. `realesrgan-x4`, `realesr-general-x4v3`) for the GPU. (#70)
- **stuck-at-95** - (a) `current_model: null` -> load a model first; (b) model loaded + low fps -> it's *progressing on CPU, not hung*; watch Frames Total climb; full-episode CPU upscales are slow. (#70, #72)
- **apple-silicon-docker** - Docker can't use the M1 GPU; run natively for CoreML, or accept CPU. (#72)
- **cant-pull-image** - `docker logout` clears a stale login that blocks the public pull.
- **benchmark-vs-realtime** - Models-tab fps = test tile; real-time = full-frame round-trip; use Anime4K for live. (#70)
- Bump `support-kb.json` version + `updated`.

### WS2 - `/doctor`: detect "GPU available but inactive" · P1 · Docker (`main.py`)
Today on #71 the doctor says `backend=cpu` + `gpu_provider_active: warn (no GPU image)` while `onnx_provider_pkg` lists CUDA/TensorRT as *available* - contradictory.
- New logic: if a GPU device is present **and** GPU EPs are in `ort.get_available_providers()` **but** `gpu_is_active()` is false -> emit an explicit **fail/warn**: *"GPU + CUDA available but inference is running on CPU - the CUDA/OpenVINO session failed to initialise (most often a host driver/runtime mismatch). Update the host NVIDIA driver / check `docker logs` for the onnxruntime error."*
- `model_smoke`: also report the session's **active** providers and flag when it's CPU-slow (e.g. >1 s) while a GPU EP is available.
- `_detect_backend()`: label as `"<gpu> (CPU fallback)"` instead of `"cpu"` when GPU EPs are available but inactive.
- Verify: `dotnet`/pytest unaffected; add a pytest asserting the new branch with a stubbed provider set.

### WS3 - Batch "95%" honesty + fail-fast · P1 · **plugin only** (C#) · effort: med · **highest-value code fix**
Kills the #1 recurring complaint. Two sub-fixes, **both code-verified against `main`**:

1. **Honest progress (verified, fix is clear).** `Services/ProcessingStrategySelector.cs` computes job progress as a *pure time estimate*, capped at 95%:
   ```csharp
   // L19  ProgressMaxPercent = 95.0 ;  L21  EstimatedProcessingSpeedRatio = 0.5
   // L213-216 (CalculateJobProgress):
   var elapsed = (DateTime.UtcNow - job.StartTime).TotalSeconds;
   var estimatedTotal = job.InputInfo.Duration.TotalSeconds * EstimatedProcessingSpeedRatio; // assumes 0.5x runtime
   return Math.Min(ProgressMaxPercent, (elapsed / estimatedTotal) * 100);   // -> sticks at 95 once elapsed exceeds the guess
   ```
   On slow hardware (real processing = 3-5x runtime) `elapsed` blows past the estimate immediately, so it **pins at 95%** for the whole remaining (long) run. *Fix:* derive progress from **actual frames** (the frame loop already knows current/total) and show *"upscaling 4,300/8,640 @ 4 fps, ~18 min"* instead of the time-estimate cap. (#72)

2. **Fail-fast on no-model (V2 RESOLVED — sharper than the review).** The batch path **does** check the model: `Services/VideoProcessor.cs:242` calls `EnsureModelLoadedAsync` over the fallback chain. **But on failure it does not abort** — `VideoProcessor.cs:261-263`:
   ```csharp
   if (!modelLoaded) {
       _logger.LogWarning("No model in fallback chain could be loaded, proceeding with default");
       // ...continues -> extracts frames -> every /upscale returns 400 -> grinds to 95% and hangs
   }
   ```
   That "proceeding with default" is the exact #70 root cause. *Fix:* turn L261 into a hard abort — `return new VideoProcessingResult { Success = false, Error = "No AI model could be loaded — load a model first (Dashboard -> Models)." }` and mark the job `Failed`. So fail-fast = **make the existing check abort**, not "add a check".

- **V3 RESOLVED — the plumbing already exists.** Real frame progress is already wired elsewhere: `Services/UpscalerProgressHub.cs:99` `SendFrameProgress(jobId, file, currentFrame, totalFrames, fps)`, `Models/UpscalerModels.cs:175` `ProcessingJob.TotalFrames`, and `Services/VideoFrameProcessor.cs:297` already logs *"Processed {n}/{total} ({fps} FPS)"*. WS3-H1 = add a `ProcessedFrames` counter on `ProcessingJob` (updated in the frame loop) and make `CalculateJobProgress` return `ProcessedFrames/TotalFrames*100` instead of the time estimate. Low effort — the data is there.

### WS4 - Version-string hygiene · **P2 (user-visible, not cosmetic)** · low risk
Stale hardcoded `PLUGIN_VERSION` constants missed in the v1.7.9 sweep — and they are **shown to users**, not just source comments:
- `Configuration/player-integration.js:10` -> `1.6.1.21`  (rendered in the in-player menu, L1174)
- `Configuration/quick-menu.js:9` -> `1.5.5.7`  (**System-Diagnostics panel "Plugin Version: 1.5.5.7", L218** — the exact panel users screenshot for support tickets)
- `Configuration/sidebar-upscaler.js:6` -> `1.6.1.21`
So a v1.7.9 plugin tells a user (and us, in their screenshot) it's "1.5.5.7" — active misinformation. Sync all three constants + their header comments to `1.7.10`, and extend the version-sync script to cover these JS constants so they never drift again. (`configurationpage.html` already fixed in `f3a7450`.)

### WS5 - Ship v1.7.10 · **DUAL release (plugin DLL + Docker image)**
v1.7.10 touches **both** artifacts — the release mechanics must cover both paths (like v1.7.9):
- **Plugin DLL:** WS3 (`ProcessingStrategySelector.cs` + `VideoProcessor.cs`) + WS4 (3 JS `PLUGIN_VERSION`) + `f3a7450` (configurationpage.html). → 6-file ZIP, manifest+2-feeds entry (`1.7.10.0`, MD5, targetAbi 10.11.8.0), quad-MD5 `verify-release.ps1`.
- **Docker image:** WS2 (`main.py`) + `1c1b2bb` (doctor). → `docker-publish.yml -f version=1.7.10`, AMD `ROCMExecutionProvider present` assert.
- **Tag discipline (v1.7.7 lesson):** everything to `main` first, then `gh release create v1.7.10 --target main`, then Docker rebuild, then `verify-release`.
Side benefit: the NAS `/doctor` red `onnx_provider_pkg` clears on the rebuilt image.

### WS6 - Trivy AMD scan timeout · P4 · CI
`docker-publish.yml` "Build amd" goes red on a Trivy scan timeout (20 GB ROCm image) though the image **does publish**. Add `timeout` + `skip-dirs` for `root/.triton/**` and `opt/conda/pkgs/cache/**` (or `--scanners vuln`) so the job goes green. (Not a release blocker - see memory note.)

### WS7 - Optional / deferred
Site "use what you already have" section on features.html; CAIN-NCNN interpolation (WS5 of the v1.7.9 plan); ESC/ATD catalog adds. Unchanged priority - only if there's appetite.

---

## v1.7.10 release mechanics (the version-bump checklist)
- [ ] `meta.json` -> `1.7.10` · `csproj` Version/Assembly/File -> `1.7.10.0`
- [ ] `manifest.json` + `repository-jellyfin.json` + `repository-simple.json` -> new `1.7.10.0` entry (sourceUrl v1.7.10, **MD5**, targetAbi 10.11.8.0, timestamp)
- [ ] `PLUGIN_VERSION` in player-integration/quick-menu/sidebar (WS4)
- [ ] README title/box/tag examples + `Scripts/sync-site-topbar-versions.ps1` + site footers + changelog entry
- [ ] docker `APP_VERSION=1.7.10`
- [ ] Plugin ZIP = the curated **6 files**; write MD5 into the 3 feeds **before** uploading
- [ ] Tag discipline: commit -> `gh release create v1.7.10 <zip> --target main` -> `gh workflow run docker-publish.yml -f version=1.7.10 -f push=true` -> `verify-release.ps1 -Tag v1.7.10` (must print PASSED)

## Suggested order
1. **WS1** (bot KB) - ship today, no release, immediate deflection of #69-#72 repeats.
2. **WS3** (95% fail-fast + honest progress) - the real code fix for the #1 theme.
3. **WS2** (doctor GPU-fallback) - turns the next GPU case into self-diagnosis.
4. **WS4** + **WS5** - version hygiene, then cut **v1.7.10**.
5. **WS6** (Trivy) when convenient; **WS7** optional.

## Verification gates (before "done")
- `dotnet build` 0/0 · `dotnet test` (+ new doctor pytest) · `main.py` AST · `node --check` on touched JS · `support-kb.json` valid JSON.
- Quad-MD5 `verify-release.ps1` PASS · AMD assert `ROCMExecutionProvider present`.

## Out of scope
Chasing Topaz quality; server-side real-time arms race; bumping the manifest before the asset exists; any change that re-introduces a false `/doctor` red on a clean CPU box.

*Author: maintainer session 2026-06-07. Grounded in live triage of #69-#72 (with screenshots) + the v1.7.9 deploy/test on the NAS (TrueNAS intel-wsl2) and local (RTX 5070 Ti) - both confirmed GPU recognition works; the open issues are setup/driver/expectation problems, addressed above by docs + doctor + progress-honesty rather than by the upscaler core.*
