# v1.7.0 - Anime4K Realtime + Adoption Completion + Settings-Import Fix

**Release date:** 2026-05-09
**Type:** Minor feature release (new realtime tier) + comprehensive bug-fix bundle
**Tests:** 115/115 (was 102, +13 new)
**Build:** 0 warnings, 0 errors

## TL;DR

- **NEW:** "Anime4K" realtime tier - real AI-shader upscaling for anime via the [Anime4K.js](https://github.com/monyone/Anime4K.js) library (MIT, loaded from jsdelivr CDN, fallback to Lanczos if unreachable).
- **HONESTY:** the existing "WebGL" mode is renamed "Lanczos + Sharpen" (it was always classical, never AI).
- **BUG-FIXES:** all P0/P1 findings from the v23-Audit + 2-agent comprehensive scan: Frame-Loop cancellation token leak, sync `WaitForExit`, sync `PersistQueue` inside lock, 5 missing `HttpContext.RequestAborted`, 18 missing `TryApply` lambdas, QualityLevel/ButtonPosition save-validation drift.
- **DRIFT-LOCKS:** new `QualityLevelRegistry` + `ButtonPositionRegistry` with HTML-parse drift-lock tests, same pattern as v23's `CodecRegistry`.

## Realtime modes — what's available now

| Mode | What it does | Hardware |
|---|---|---|
| **Auto** | Picks Server-AI if benchmark sustains video FPS, else Lanczos | any |
| **Lanczos + Sharpen** | Classical WebGL shader (Lanczos-2 resample + edge-aware sharpen). NOT AI. Comparable to FSR1. | any GPU |
| **Anime4K** *(NEW)* | Real AI-trained GLSL shader for anime, multi-pass restore + upscale. Quality comparable to mpv-shim's Anime4K shader pack. | any GPU with WebGL |
| **Server AI** | Capture frames -> POST to Docker AI service -> render upscaled overlay. Full Real-ESRGAN/SwinIR/EDVR catalog. Best quality but high latency. | Docker AI service |

Pre-processing pipeline (`Library -> AI -> _upscaled.mp4`) stays the headline batch feature. Anime4K covers the live-playback gap that was previously misrepresented as "WebGL = AI".

## Bug fixes - P0

| # | Bug | Fix |
|---|---|---|
| 1 | **QualityLevel save-drift** - UI {low,medium,high}, Save accepts {fast,medium,high}; "low" silently dropped on Settings-Import; `low -> fast` mapping in ProcessingStrategySelector.cs:51-54 was dead code on import path | `QualityLevelRegistry.Levels = {low,medium,high}`, drift-lock test parses HTML and asserts set-equality |
| 2 | **ButtonPosition save-drift** - UI {left,right,center}, Save accepts {left,right}; "center" silently rejected despite full `.ai-menu--center` CSS + keyframes already implemented | `ButtonPositionRegistry.Positions = {left,right,center}`, drift-lock test |
| 3 | **Frame-Loop cancellation leak** at `VideoFrameProcessor.cs:252` - `UpscaleImageAsync` called without `cancellationToken`. v20/v21 fixed 16 inner HttpClient sites - but the outer loop never propagated the token, so user-cancel waited per-frame for HTTP-timeout instead of bailing immediately. | Token propagated. Outer-Loop and inner HTTP both cancel together. |
| 4 | **Process WaitForExit timeout-vs-cancel race** - `Task.Run(() => proc.WaitForExit(60000), ct)` only gated scheduling, not the WaitForExit itself. Mid-cancel waited up to 60s. | `WaitForExitAsync(linkedCts.Token)` with linked CTS combining user-cancel and per-process timeout. Hung processes get killed cleanly. |

## Bug fixes - P1

- **3 controller endpoints + 2 IO calls** without `HttpContext.RequestAborted` (`UpscalerController.cs:595, 673, 674, 1150, 1158`)
- **Queue-worker `IsServiceAvailableAsync()`** without ct (`UpscalerService.cs:119`)
- **`PersistQueue()` sync `File.WriteAllText`** on every Enqueue/Dequeue/Complete/Cancel/SetPriority, with one site (Dequeue) running the disk-write **inside** `lock(_queueLock)`. Replaced with `RequestPersist()` - debounced (500ms quiet window) + non-blocking timer, runs outside any lock.

## Bug fixes - Settings-Import

`/Upscaler/settings/import` had `TryApply` lambdas for 67 properties but missed 18: `AiServiceApiToken`, `EnabledLibraryIds`, `EnableFaceRestore`, `FaceRestoreModel`, `FaceRestoreMaxPerFrame`, `FaceRestoreMaxWidth`, `EnableVideoFilters`, `ActiveFilterPreset`, `FilterLutPath`, plus 9 filter properties (`FilterBrightness/Contrast/Saturation/Gamma/Sharpness/Vignette/Denoise/ColorTemperature/FilmGrain`). Backup-restore would silently revert these to defaults. Now imported with appropriate validation per type.

## What this release deliberately does NOT do

- **No WebGPU + ONNX-Runtime-Web Real-ESRGAN realtime path.** That would embed ~10MB of `onnxruntime-web` + 5-15MB of compact ONNX models, plus require a WebCodecs-API texture pipeline. Worth its own release with a dedicated test cycle - deferred to **v1.7.1** as roadmap. Anime4K covers the anime use-case today; live-action users on RTX 30/40 already have NVIDIA RTX VSR built into Chrome/Edge/Firefox/Opera which is mainstream "DLSS for video".
- **No new Realtime models from the wishlist** (OmniSR, DAT-light, RestoreFormer++). Catalog stable at 48 models for the 9th release running. v1.7.x can start adding new models now that the structural-drift complex is closed.
- **No silent property deletions.** All 30 dead-backend properties from the v22 UI cleanup remain in `PluginConfiguration.cs` for backwards-compat. Saved configs from v1.6.x continue to load without crash.

## External dependencies (new)

| Library | License | Loading | Fallback |
|---|---|---|---|
| [Anime4K.js](https://github.com/monyone/Anime4K.js) (npm: `anime4k`) | MIT | jsdelivr CDN at runtime, only when user picks "anime4k" mode | Lanczos+Sharpen if CDN unreachable |

No bundled binaries. Plugin ZIP size unchanged.

## Files touched

### New (5 files)
- `Services/QualityLevelRegistry.cs` - single-source-of-truth for QualityLevel allowlist
- `Services/ButtonPositionRegistry.cs` - single-source-of-truth for ButtonPosition allowlist
- `JellyfinUpscalerPlugin.Tests/Services/QualityLevelRegistryTests.cs` - 7 tests, drift-lock incl. HTML-parse
- `JellyfinUpscalerPlugin.Tests/Services/ButtonPositionRegistryTests.cs` - 6 tests, drift-lock
- `RELEASE-NOTES-v1.7.0.md` - this file

### Modified (10 files)
- `Controllers/UpscalerController.cs` - Save lambdas via Registries, 18 new TryApply, 5 cancellation tokens
- `Services/VideoFrameProcessor.cs:252` - cancellationToken propagated
- `Services/ProcessingMethodExecutor.cs:619-621` - `WaitForExitAsync(linkedCts.Token)`
- `Services/ProcessingQueue.cs` - `RequestPersist()` debounced + async
- `Services/UpscalerService.cs:119` - `IsServiceAvailableAsync(ct)`
- `Configuration/player-integration.js` - new Anime4K mode + `webgl` aliased to `lanczos`
- `Configuration/configurationpage.html` - RealtimeMode dropdown 4 honest options
- `JellyfinUpscalerPlugin.csproj`, `meta.json`, `PluginConfiguration.cs` - version 1.6.1.23 -> 1.7.0.0
- `manifest.json`, `repository-jellyfin.json` - new v1.7.0.0 entry
- `site/index.html`, `site/changelog.html`, `README.md` - v1.7.0 surfaced

## Roadmap

- **v1.7.1**: WebGPU + ONNX Runtime Web Real-ESRGAN compact realtime (closes the live-action AI gap for browsers without RTX VSR)
- **v1.8.0**: Wishlist models (OmniSR, DAT-light, RestoreFormer++)
- **v2.0.0**: Pipeline parallelization (frame-extract / AI-inference / encode running concurrently)
