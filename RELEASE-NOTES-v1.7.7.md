# Release v1.7.7 — Docker Self-Verification Hardening

**Release date:** 2026-05-27
**Build:** 0 warnings, 0 errors
**Tests:** 123/123 (unchanged)
**Bit-compat:** v1.7.x saved configs unchanged. **C# Plugin DLL bit-identical to v1.7.6** — all changes are in `docker-ai-service/`.

## What this is

The structural completion of the Intel-Arc saga (#45/#66/#67/#69). A dedicated deep-analysis of the entire `docker-ai-service` layer found **no acute bugs left** — but it found the **missing verification layer** that allowed the whole saga's bug class to slip through: no Dockerfile checked at build time whether its expected ONNX provider was actually installed.

That gap is exactly what shipped the v1.7.5 Intel bug: plain `onnxruntime` built fine, but had no OpenVINO EP, so the image ran on CPU while the dashboard showed the Intel GPU. Nobody noticed until a user (Laurent) posted his `/gpu-verify`.

v1.7.7 closes the entire "GPU image silently runs on CPU" class with build-time asserts.

## Changes (all in docker-ai-service/)

### 1. Build-time provider asserts — NVIDIA + Intel (hard-fail)

`Dockerfile` and `Dockerfile.intel` now run a provider assert immediately after pip install:

```dockerfile
# Dockerfile (NVIDIA)
RUN python -c "import onnxruntime as ort; p = ort.get_available_providers(); \
    print('ONNX providers:', p); \
    assert 'CUDAExecutionProvider' in p, 'BUILD FAIL: CUDAExecutionProvider missing'"

# Dockerfile.intel
RUN python3 -c "import onnxruntime as ort; p = ort.get_available_providers(); \
    print('ONNX providers:', p); \
    assert 'OpenVINOExecutionProvider' in p, 'BUILD FAIL: OpenVINOExecutionProvider missing'"
```

If the provider isn't present, the build goes **red** (exit 1) instead of publishing a working-looking CPU-only image. This single measure would have caught the v1.7.5 Intel bug before release.

### 2. AMD provider visibility (warn-only)

`Dockerfile.amd` has a deliberate silent `|| pip install onnxruntime` CPU fallback (ROCm wheels are frequently yanked, so CPU is a *valid* degraded mode for AMD - unlike NVIDIA/Intel). The new assert there is **warn-only** (exit 0):

```dockerfile
RUN python3 -c "import onnxruntime as ort; p = ort.get_available_providers(); \
    print('ONNX providers:', p); \
    print('OK: ROCMExecutionProvider present') if 'ROCMExecutionProvider' in p \
    else print('WARNING: ROCMExecutionProvider MISSING - this AMD image will run inference on CPU...')"
```

The CPU fallback is now **visible in the build log** instead of slipping through unnoticed.

### 3. entrypoint.sh WSL2 awareness

`detect_backend()` only checked `/dev/dri/renderD128`, so on WSL2 setups (Docker Desktop on Windows) it printed "Backend: cpu" and fired a false "GPU not detected" warning - even though `main.py` correctly detects the GPU via `/dev/dxg` since v1.7.4. Added a `/dev/dxg` branch so the startup banner is consistent with the real detection:

```bash
if [ -e /dev/dxg ]; then
    echo "intel-wsl2"
    return
fi
```

## Why hard-fail for NVIDIA/Intel but warn-only for AMD

This is the key design decision. NVIDIA's `onnxruntime-gpu` and Intel's `onnxruntime-openvino` are reliable PyPI packages - if their provider is missing, it's a genuine build bug and the image should not ship. AMD's `onnxruntime-rocm` is deprecated after v1.23 and its wheels are frequently unavailable - a CPU fallback is the correct degraded behavior there, so the AMD assert informs rather than blocks.

## Files touched

### Modified
- `docker-ai-service/Dockerfile` - CUDA provider assert (hard-fail)
- `docker-ai-service/Dockerfile.intel` - OpenVINO provider assert (hard-fail)
- `docker-ai-service/Dockerfile.amd` - ROCm provider visibility (warn-only)
- `docker-ai-service/entrypoint.sh` - `/dev/dxg` WSL2 detection branch
- `meta.json`, `PluginConfiguration.cs`, `JellyfinUpscalerPlugin.csproj` - version 1.7.6 to 1.7.7
- `manifest.json`, `repository-jellyfin.json`, `repository-simple.json` - prepended v1.7.7 entry
- `README.md` - title + tags + new changelog section
- `site/index.html`, `site/changelog.html` - v1.7.7 entry
- `site/*.html` (14 files) - topbar brand-version synced

### New
- `RELEASE-NOTES-v1.7.7.md`

## Verification

- **Build:** 0/0
- **Quad-MD5:** local ZIP == GitHub asset == manifest checksum == repo-feed checksum
- **meta.json-in-ZIP:** matches tag (1.7.7)
- **Docker build asserts:** will now run on the next docker-publish rebuild - any provider regression fails the build loudly.

## Roadmap

- **v1.8.0**: Pipeline-Parallelization (`Channel<T>`-based concurrent extract/inference/encode)
- **v2.0.0**: Multi-Frame VSR (EDVR / RealBasicVSR temporal context) in realtime
- **Backlog (P3):** `test_detection.py` mock-based detection-path coverage; base-image SHA256 digest pinning; OS-package CVE sweep (apt upgrade in Dockerfiles -> closes ~193 Trivy alerts)
