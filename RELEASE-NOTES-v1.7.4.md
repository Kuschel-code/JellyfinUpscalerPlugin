# Release v1.7.4 — Docker-Side Fixes (FP16 + WSL2 GPU Detection)

**Release date:** 2026-05-19
**Build:** 0 warnings, 0 errors
**Tests:** 123/123 (unchanged from v1.7.3.1 — Phase E tests deferred to v1.7.5)
**Bit-compat:** v1.7.x saved configs unchanged. C# Plugin DLL is functionally identical to v1.7.3.1 — this release ships **docker-ai-service Python fixes** and an updated docker-compose recipe.

## Two real-world bugs closed

### Fix #67 — ONNX models failing with FP16/FP32 type mismatch
Reported by @eparrish64 on 2026-05-19 (same day). On NVIDIA hardware with `docker7` image, **every** ONNX model crashed at warmup:
```
Warmup failed: [ONNXRuntimeError] : 2 : INVALID_ARGUMENT :
  Unexpected input data type. Actual: (tensor(float16)), expected: (tensor(float))
```

**Root cause:** `_resolve_fp16_setting()` (main.py:1137) auto-enables FP16 mixed-precision for every NVIDIA with Compute Capability >=7.0 (Volta+). The cast paths in `_onnx_infer_tile()` and `_onnx_infer_multiframe_tile()` then blindly converted the input batch to `np.float16` regardless of whether the loaded ONNX model actually expected float16. Most models in our catalog (Real-ESRGAN, SwinIR, HAT, etc.) are exported as FP32 — they crashed at the first `session.run()`.

**Fix:** New helper `_session_input_is_fp16(session)` checks the loaded ONNX model's actual input dtype before casting. FP16 only kicks in when **both** the global flag is on AND the model expects `tensor(float16)`. Symmetric guard on the output `astype(np.float32)` cast.

**Net effect:** FP32-exported models (the catalog default) now run as float32 even with `USE_FP16=auto`. Future FP16-exported models will still benefit from the precision path automatically.

### Fix #66 — Docker image / WSL2 / Intel Arc on Windows 11
Reported by @FrRene06 on 2026-05-17. Windows 11 + Docker Desktop + WSL2 + Intel Arc A380 — dashboard stuck on "No GPU detected (CPU-only mode)" no matter which image tag (`intel`, `vulkan`) was tried.

**Root cause:** Intel-GPU-Detection in `detect_hardware()` (main.py:1307-1404) searched exclusively for `/dev/dri/renderD*` (classical Linux DRM render nodes). WSL2 exposes the GPU through `/dev/dxg` (DirectX bridge). The fallback path `elif ONNX_AVAILABLE and 'OpenVINOExecutionProvider' in ort.get_available_providers():` marked the GPU as "Intel OpenVINO (CPU inference only)" — misleading, because the GPU was actually reachable via the DXG bridge.

**Fix:**
- New helper `_parse_clinfo_intel_name()` extracts "Intel(R) Arc(TM) A380 Graphics" from `clinfo --list` output.
- New WSL2 detection branch in the Intel-GPU block: when `/dev/dxg` exists, runs `clinfo --list`, and if an Intel platform line is reported, registers the GPU with `type: intel-wsl2`.
- Falls cleanly through to the existing OpenVINO-CPU fallback when the WSL2 driver mount is missing (with a warning pointing to the `docker-compose.yml` WSL2 section).
- `/gpu-verify` endpoint exposes a new `wsl2` section: `is_wsl2_environment`, `wsl_lib_mounted`, `ld_library_path`. Users self-debug without reading server logs.

**Setup** (now also documented in `docker-compose.yml`):
```yaml
volumes:
  - /usr/lib/wsl:/usr/lib/wsl:ro
devices:
  - /dev/dxg:/dev/dxg
environment:
  - USE_GPU=true
  - OPENVINO_DEVICE=GPU
  - LD_LIBRARY_PATH=/usr/lib/wsl/lib
```

## Three issues closed as obsolete / already-implemented

- **#49** Vulkan support — already supported since v1.5.x as `:docker7-vulkan` image. Closed with hint.
- **#64** Feature: Select Library — implemented in v1.6.1.14 (Apr 2026) as `EnabledLibraryIds` + chip-picker UI. Closed with hint.
- **#62** + **#63** Plugin install failures from v1.5.x — all symptoms originate from v1.5.5.4-era checksum bugs + API_TOKEN issues fixed in v1.6.1.x. Repo-feed URL drift fixed in `e470849`. Closed as v1.5.x-era.

## Issue-Status

| # | Title | Status |
|---|---|---|
| #67 | ONNX models failing because of wrong input type | CLOSED (this release) |
| #66 | Docker image and WSL2 subsystem linux in windows 11 | CLOSED (this release) |
| #64 | Feature: Select Library | CLOSED (already implemented v1.6.1.14) |
| #49 | Vulkan support? | CLOSED (already supported docker7-vulkan) |
| #62 | Plugin ver 1.5.5.4 Unable to connect to Docker AI Service | CLOSED (v1.5.x-era, obsolete) |
| #63 | Cannot install plugin | CLOSED (v1.5.x-era, obsolete) |

Repo now has **0 open issues**.

## Polish

- README L1048: "Windows Docker Desktop: GPU passthrough not supported" replaced with WSL2 mount instructions + reference to the auto-FP16 fix.
- `docker-compose.yml`: new commented `ai-upscaler-wsl2` variant block with the full Intel Arc verified setup recipe.
- `docs/ISSUES-PLAN-2026-05-19.md`: today's triage/fix-plan covering all 6 open issues.
- `docs/ANALYSE-v1.7.3.1.md`: 14-section state-of-plugin analysis (commit backlog cleared in `bd7dacd`).

## Files touched

### Modified
- `docker-ai-service/app/main.py` — 2 new helpers + 2 cast-guards + 1 detection branch + /gpu-verify wsl2 diagnostics (+97/-13 LoC)
- `docker-ai-service/docker-compose.yml` — WSL2-variant section (+34 LoC)
- `README.md` — L1048 corrected
- `meta.json`, `manifest.json`, `repository-jellyfin.json`, `repository-simple.json`, `PluginConfiguration.cs`, `JellyfinUpscalerPlugin.csproj` — version 1.7.3.1 -> 1.7.4.0
- `site/index.html`, `site/changelog.html` — v1.7.4 entry

### New
- `docs/ISSUES-PLAN-2026-05-19.md`
- `RELEASE-NOTES-v1.7.4.md`

## Roadmap

- **v1.7.5** (deferred): complete Phase E test coverage — add `Jellyfin.Data` package-ref + PlayCount/Played-flag tests + `VideoFrameProcessorTests` (CT propagation via mocked `IUpscalerCore`) + `ProcessingMethodExecutorTests` (process-mock for linked-CTS).
- **v1.8.0**: Pipeline-Parallelization (`Channel<T>`-based concurrent extract/inference/encode).
- **v2.0.0**: Multi-Frame VSR (EDVR / RealBasicVSR temporal context) in realtime.

## Verification

- **Build:** 0 warnings, 0 errors (verified via `dotnet publish -c Release`).
- **Python syntax:** `python -c "import ast; ast.parse(open('docker-ai-service/app/main.py').read())"` -> OK.
- **Quad-MD5:** local ZIP md5 == GitHub-asset md5 == manifest.json checksum == repository-*.json checksum (verified post-release via `Scripts/verify-release.ps1`).
- **meta.json-in-ZIP:** unzip-and-check shows version matches manifest (`1.7.4.0`).
