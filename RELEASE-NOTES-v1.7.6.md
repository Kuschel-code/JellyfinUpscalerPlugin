# Release v1.7.6 — Intel OpenVINO Provider Hotfix (Issue #69 Point 1)

**Release date:** 2026-05-25
**Build:** 0 warnings, 0 errors
**Tests:** 123/123 (unchanged)
**Bit-compat:** v1.7.x saved configs unchanged. **C# Plugin DLL bit-identical to v1.7.5** — fix is docker-ai-service-only.

## What this fixes

Issue [#69](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/69) Point 1: User reported Intel Arc GPU was detected (dashboard showed "Intel Arc A310") but inference still ran on CPU. v1.7.5 closed Points 2 (aspect-ratio) and 3 (non-admin auth) but missed the deeper cause of Point 1.

User Laurent (@FrRene06) posted his `/gpu-verify` output with confirmation: setup is otherwise correct (`group_add: render`, WSL2 driver mount, latest image), but the providers list shows:

```json
"onnx_providers": ["AzureExecutionProvider", "CPUExecutionProvider"]
```

**No `OpenVINOExecutionProvider` at all.** Gemini's diagnosis (Reshape-node bug in realesrgan-x4) was symptom-only — the real root cause is that OpenVINO EP isn't even loaded into the runtime.

## Root cause

`docker-ai-service/requirements-intel.txt` had:

```python
# The separate onnxruntime-openvino package is deprecated; use onnxruntime + system OpenVINO
# Pin to match OpenVINO 2025.4.1 base image - 1.24.x includes built-in OpenVINO EP for 2025.x
onnxruntime>=1.20.0,<2.0.0
```

The hypothesis in the comment is wrong: plain `onnxruntime` from PyPI **does not bundle the `OpenVINOExecutionProvider`** - even when the base image (`openvino/ubuntu22_runtime:2025.4.1`) provides the system OpenVINO libraries. The provider is only built into the dedicated `onnxruntime-openvino` PyPI variant (or a from-source build with `--use_openvino`).

This was committed under an incorrect assumption about onnxruntime build flavors. Laurent's empirical `/gpu-verify` output is the proof.

## Fix

Single-line change in `requirements-intel.txt`:

```diff
-# The separate onnxruntime-openvino package is deprecated; use onnxruntime + system OpenVINO
-# Pin to match OpenVINO 2025.4.1 base image - 1.24.x includes built-in OpenVINO EP for 2025.x
-onnxruntime>=1.20.0,<2.0.0
+# Use onnxruntime-openvino which BUNDLES the OpenVINOExecutionProvider.
+# Plain onnxruntime from PyPI does NOT include OpenVINO EP even with system OpenVINO
+# present in the base image - verified empirically via issue #69.
+onnxruntime-openvino>=1.20.0,<2.0.0
```

After Docker Hub rebuild and container restart, Laurent's `/gpu-verify` should show:

```json
"onnx_providers": ["OpenVINOExecutionProvider", "AzureExecutionProvider", "CPUExecutionProvider"],
"active_providers": ["OpenVINOExecutionProvider", "CPUExecutionProvider"]
```

And inference will actually use the Intel GPU.

## Plugin DLL unchanged

C# Plugin code is bit-identical to v1.7.5. This release is purely:
- One-line dependency swap in `requirements-intel.txt`
- Version bumps in 5 places (meta.json, PluginConfiguration.cs, csproj x 3)
- Manifest + repo-feeds + sites + README updates
- Re-built Docker images (intel + the 5 others for consistency)

Users on `:docker7-intel` rolling tag get the fix automatically after pull. Users on pinned `:docker7-v1.7.5-intel` need to switch to `:docker7-v1.7.6-intel` or rolling.

## Side effect - AMD build retry

The v1.7.5 docker-publish run had Trivy timeout on AMD ROCm scan (Triton kernel-dumps). v1.7.6 build re-triggers all 6 backends - AMD should succeed this time (transient infra issue).

## Files touched

### Modified
- `docker-ai-service/requirements-intel.txt` - switched `onnxruntime` to `onnxruntime-openvino`
- `meta.json`, `PluginConfiguration.cs`, `JellyfinUpscalerPlugin.csproj` - version 1.7.5 to 1.7.6
- `manifest.json`, `repository-jellyfin.json`, `repository-simple.json` - prepended v1.7.6 entry
- `README.md` - title + tags + new changelog section
- `site/index.html`, `site/changelog.html` - v1.7.6 entry
- `site/*.html` (14 files) - topbar brand-version synced

### New
- `RELEASE-NOTES-v1.7.6.md`

## Verification

- **Build:** 0/0
- **Quad-MD5:** local ZIP == GitHub asset == manifest checksum == repo-feed checksum
- **meta.json-in-ZIP:** matches tag (1.7.6)

## Roadmap

- **v1.7.7** (if needed): per-user GPU-quota (deferred from v1.7.5 Auth-downgrade)
- **v1.8.0**: Pipeline-Parallelization
- **v2.0.0**: Multi-Frame VSR
