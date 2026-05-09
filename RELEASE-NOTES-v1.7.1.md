# v1.7.1 - WebGPU AI Realtime + Wishlist Models + Cleanup-Closure

**Release date:** 2026-05-09
**Type:** Feature + comprehensive cleanup release
**Tests:** 117/117 (was 115; -3 redundant methods + 5 new theory cases)
**Build:** 0 warnings, 0 errors

## Headline features

### WebGPU + ONNX Runtime Web Real-ESRGAN Realtime (NEW)

The "DLSS-for-video" tier the plugin was missing. Pure-browser AI inference via:
- `onnxruntime-web` (Microsoft, MIT) loaded from jsdelivr CDN
- Real-ESRGAN compact ONNX models (FP16, ~3-5MB each) fetched from HuggingFace mirrors
- WebGPU compute backend with WebAssembly fallback
- Hardware-accelerated on any browser with WebGPU (Chrome 113+, Edge 113+, Firefox 142+, Safari 26+, Opera 99+)

Defensive fallback chain (any layer can fail without breaking playback):
- WebGPU not available -> falls back to Lanczos
- onnxruntime-web load fails -> falls back to Lanczos
- Model fetch fails -> falls back to Lanczos
- 30 consecutive slow frames (>50ms) -> auto-stops, falls back to Lanczos with hint

The Realtime dropdown now offers **5 honest options**:

| Mode | What | Hardware |
|---|---|---|
| Auto | Smart-pick from benchmark | any |
| Lanczos + Sharpen | Classical shader (FSR1-equivalent) | any GPU |
| Anime4K | Real AI-shader for anime (v1.7.0) | any GPU with WebGL |
| **AI WebGPU** *(NEW)* | **Real-ESRGAN compact via WebGPU** | WebGPU-capable browser |
| Server AI | Docker AI service (full Real-ESRGAN/SwinIR catalog) | Docker AI service |

### 5 new wishlist models (first catalog growth in 9 releases)

| Model | Category | Why |
|---|---|---|
| **OmniSR x2/x4** | nextgen | CVPR 2023 omni-axis self-attention. Sweet spot between SwinIR quality and FSRCNN speed. ~10MB. |
| **DAT-light x2/x4** | nextgen | 2023 Dual Aggregation Transformer light variant. Production-friendly. ~15MB. |
| **RestoreFormer++** | face_restore | MM 2023, state-of-the-art for severely degraded faces. Better than GFPGAN/CodeFormer. ~50MB. |

Catalog: 48 -> **53 models**.

## Drift-Cleanup Closure (audit's three open siblings)

### NEW `Services/RealtimeModeRegistry.cs`

Single source of truth for the RealtimeMode value set. Two distinct sets:
- `UiModes` - what the UI dropdown advertises (HTML drift-lock asserts equality)
- `AcceptedAtImport` - UI modes PLUS backwards-compat aliases (`webgl` for v1.6.x configs)

Same pattern as `CodecRegistry` / `QualityLevelRegistry` / `ButtonPositionRegistry`.

### `_validFilterPresets` -> `VideoFilterService.SupportedPresets`

Moved 17-element preset array from `UpscalerController.cs:2188` (where it had 5 inline references) to `VideoFilterService.SupportedPresets` (where the preset implementations live).

### Generic `RegistryDriftLockTests.HtmlDropdown_MatchesRegistry [Theory]`

Replaces three separate `HtmlDropdown_*` test methods with one `[Theory]` covering 5 dropdowns: OutputCodec, QualityLevel, ButtonPosition, RealtimeMode, ActiveFilterPreset. Adding a new Registry: one MemberData entry, no new test method needed.

## CI Audit Workflows (NEW)

`.github/workflows/v1.7.1-audit-checks.yml` with 3 jobs that catch drift-bug-classes:

1. **`verify-fallback-sync`**: parses `docker-ai-service/app/main.py:AVAILABLE_MODELS` and `Resources/models-fallback.json:models[]`, asserts both have the same model IDs and `total` field is correct. Catches v17/v18/v19/v23 drift class.
2. **`audit-tryapply-lambdas`**: lists every multi-line `TryApply` lambda for human review. Surfaces the OutputCodec/QualityLevel/ButtonPosition bug class.
3. **`zip-version-check`**: builds publish output, extracts `meta.json`, asserts version matches `manifest.json`. Catches the v1.7.0 release-time bug.

## Smaller fixes

- `HttpUpscalerService.cs:194` LogDebug -> LogWarning - pre-load status check failures were hiding AI-service-offline cascades.

## Files touched

### New (5 files)
- `Services/RealtimeModeRegistry.cs` - single-source-of-truth registry
- `Configuration/webgpu-ai-realtime.js` (~250 LoC) - defensive ONNX inference via WebGPU
- `JellyfinUpscalerPlugin.Tests/Services/RegistryDriftLockTests.cs` - generic Theory drift-lock
- `.github/workflows/v1.7.1-audit-checks.yml` - 3-job pre-release audit
- `RELEASE-NOTES-v1.7.1.md` - this file

### Modified (~12 files)
- `Controllers/UpscalerController.cs` - RealtimeMode + ActiveFilterPreset via registries; deleted local `_validFilterPresets` array
- `Configuration/configurationpage.html` - 5th RealtimeMode option `ai-webgpu`
- `Configuration/player-integration.js` - `_startWebGPUAI` / `_loadWebGPUAIScript` / `_stopWebGPUAI` methods + dispatch
- `Plugin.cs` - new `UPSCALERWebGPUAI` page registration
- `JellyfinUpscalerPlugin.csproj` - EmbeddedResource `webgpu-ai-realtime.js` + version 1.7.0.0 -> 1.7.1.0
- `Services/VideoFilterService.cs` - new `SupportedPresets` HashSet
- `Services/HttpUpscalerService.cs:194` - LogDebug -> LogWarning
- `docker-ai-service/app/main.py` - 5 new wishlist model entries
- `Resources/models-fallback.json` - 5 entries, total 48 -> 53
- 3 existing test files - 3 redundant `HtmlDropdown_*` methods deleted (covered by RegistryDriftLockTests now)
- `meta.json`, `PluginConfiguration.cs` - version 1.7.0.0 -> 1.7.1.0
- `manifest.json`, `repository-jellyfin.json` - new v1.7.1.0 entry

## What this release deliberately defers

- **Phase 2 mocked-async tests** (ProcessingQueue debounce, IsAnyUserPlayed fail-open, frame-loop CT propagation, WaitForExitAsync linked-CTS) - require non-trivial mocking infrastructure. Defer to v1.7.2 with proper mock-IUpscalerCore scaffolding.
- **6 numeric Math.Min upper-clamps** + **RealtimeCaptureWidth lower-bound** + **ProcessingStatus.Analyzing dead-enum cleanup** + **_cleanupLock(ct) at L446** - cascade-risk without proper test coverage. Defer to v1.7.2.

## Roadmap

- **v1.7.2**: Phase 2 mocked-async test coverage + remaining polish items
- **v1.8.0**: Pipeline parallelization (frame-extract / AI-inference / encode concurrent via Channel<T>)
- **v2.0.0**: Multi-frame model integration in realtime (EDVR / RealBasicVSR temporal context)
