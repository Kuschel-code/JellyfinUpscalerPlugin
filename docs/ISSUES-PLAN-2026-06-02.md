# Issues Triage + Code-Fix Plan — 2026-06-02

**Scan date:** 2026-06-02
**Repo state:** `main` @ latest (v1.7.7)
**Open issues:** #70, #71 (both user-setup, not code)
**Method:** all 43 images attached to issues #45/#62/#63/#69 were reviewed visually (not just the issue text). Breakdown: #69 = 18 imgs (the only active/recent report), #45 = 11 imgs (FrRene06 history), #62 = 5 imgs, #63 = 9 imgs. The one oversized image (`i62_00`, 1800px) was read by splitting it into top/bottom crops — it is the v1.5.5.4 "service Unavailable" state, identical to `i62_02`/`i63_02`, nothing new.

---

## What the screenshots revealed (that the text did not)

The decisive find is in **#69** (Intel Arc A380, WSL2, Docker Desktop). The user's own container log (image `i69_13`) and `/gpu-verify` (image `i69_02`) prove the **current v1.7.7 `docker7-intel` image works end-to-end on WSL2 Intel Arc**:

```
[entrypoint] Version: 1.7.7   Backend: intel-wsl2   USE_GPU: true
Detected Intel GPU via WSL2 /dev/dxg: Intel(R) OpenCL Graphics
ONNX Runtime providers: ['OpenVINOExecutionProvider', 'CPUExecutionProvider']
GPU inference verification passed (OpenVINOExecutionProvider) input_shape=[1, 3, 64, 64]
GPU acceleration active: OpenVINOExecutionProvider
ONNX model realsrgan-x4 loaded successfully with: ['OpenVINOExecutionProvider', 'CPUExecutionProvider']
```

This confirms **all four prior fixes are live and working** on the published image:
- v1.7.4 — WSL2 `/dev/dxg` detection (`Detected Intel GPU via WSL2 /dev/dxg`)
- v1.7.4 — FP16 guard (`FP16 Mixed Precision: False` on a non-CUDA GPU)
- v1.7.6 — `onnxruntime-openvino` (`OpenVINOExecutionProvider` present)
- v1.7.7 — entrypoint WSL2 awareness (`Backend: intel-wsl2`, not `cpu`)

Conclusion: **there is no functional GPU bug left** on the current image. #70 (Laurent) is simply running a stale cached image (his providers show `AzureExecutionProvider` = plain onnxruntime). His revised answer now contains the proven working compose + `docker compose pull`.

---

## Open issues — status

| # | Reporter | Type | Action |
|---|---|---|---|
| #70 | FrRene06 (Laurent) | **User-setup** (stale image) | Answered: force-pull + proven compose. Awaiting his new `/gpu-verify`. No code change. |
| #71 | OutsetIG | **User-setup** (API_TOKEN) | Answered: `API_TOKEN=disable` or matching token. No code change. |

Neither open issue requires a code change — both are configuration/caching on the user side.

---

## What the closed-issue screenshots (#62/#63) showed

Both are historical and already-fixed, but the images confirm nothing regressed:

- **#63** (`i63_00`–`i63_08`) — the old **install checksum-mismatch** error on v1.5.x. `i63_03` is the smoking gun: `The checksums didn't match while installing "AI Upscaler Plugin", expected: cc54d960… got: 8ED32E…` → `System.IO.InvalidDataException: The checksum of the received data doesn't match`. This is exactly the manifest↔asset MD5 drift that the current **Quad-MD5 release process** now prevents. No code change.
- **#62** (`i62_01`–`i62_04`) — the old **install 500 + service-unavailable** state on v1.5.6.0. `i62_04` shows `POST /Packages/Installed/…version=1.5.6.0&repositoryUrl=…repository-jellyfin.json` → **500 Internal Server Error** (served through the user's own `miaoufilm.duckdns.org` reverse proxy / openresty). `i62_03` proves the container itself was running. `i62_01` is the only mildly-current signal: the :5000 dashboard's "Test Upscaling" panel **spams** `API_TOKEN not configured. Set API_TOKEN env var to secure this service.` on a working NVIDIA T600 (CUDA + TensorRT active) — this ties to **#71** (already answered with `API_TOKEN=disable`). The repetition is cosmetic log/UI noise, not a failure.

---

## Code fixes actually needed (small)

The image review surfaced **four** genuine code items. None blocks any user today; all are quality/honesty/robustness fixes.

> **Status (2026-06-03):** FIX-1, FIX-3, FIX-4 are **implemented** in `docker-ai-service/app/main.py` (working tree, not yet released). FIX-2 (AMD ROCm) remains a separate investigation — see its note. Issue answers also completed: #71/#70/#69 all answered in English (#70 got a follow-up covering the `using_gpu:false`, WebGL-fallback, and 95%-stuck questions that were missed earlier).
>
> - ✅ **FIX-1** — `VERSION = os.getenv("APP_VERSION", "1.7.7")` (drift-proof).
> - ✅ **FIX-3** — `run_benchmark()` now reads the loaded ONNX session's input shape; fixed-shape models (realesrgan-x4-256) benchmark at their real dim instead of a 64px tile that crashed warmup.
> - ✅ **FIX-4** — added `gpu_is_active()` (derives GPU-active from the live provider list, counts OpenVINO/CoreML); `/health`, `/status`, `/hardware`, `/gpu-verify` now report the honest value (+ `gpu_requested` on /gpu-verify). `state.use_gpu` control flow unchanged.
> - ✅ **FIX-2** — **root cause corrected by the build log**: the `onnxruntime-rocm` wheel installed fine (1.22.2.post1); plain `onnxruntime` (pulled by `requirements-amd.txt`) shadowed it, so the runtime reported `AzureExecutionProvider`/CPU. Fixed by removing `onnxruntime` from `requirements-amd.txt` and uninstalling any pre-baked copy + force-installing `onnxruntime-rocm` as the sole provider in `Dockerfile.amd`. Needs an AMD rebuild to confirm the build-time assert now prints `ROCMExecutionProvider present`.

### FIX-1 (P2) — stale hardcoded service version

`docker-ai-service/app/main.py:119`
```python
VERSION = "1.6.1.21"
```
The container reports `1.6.1.21` in the startup banner's "Starting AI Upscaler Service v..." line and in `/status` / `/health` payloads, even though the entrypoint banner (from the `APP_VERSION` build arg) correctly says `1.7.7`. The image `i69_13` shows both side by side: `[entrypoint] Version: 1.7.7` then `Starting AI Upscaler Service v1.6.1.21...`.

**Fix:** make `VERSION` read from the `APP_VERSION` env (set at build time) with a sane fallback, so it can never drift again:
```python
VERSION = os.getenv("APP_VERSION", "1.7.7")
```
Or, at minimum, bump the literal to `1.7.7`. Reading from `APP_VERSION` is the drift-proof option (single source of truth = the build arg the Dockerfiles already pass).

**Risk:** none. Cosmetic/diagnostic only.

### FIX-2 (P2) — AMD image ran on CPU ✅ FIXED (2026-06-03)

The AMD backend showed at runtime:
```
ONNX providers: ['AzureExecutionProvider', 'CPUExecutionProvider']
WARNING: ROCMExecutionProvider MISSING ...
```

**Root cause (corrected by the actual build log, run `26640760795` job `Build amd`):** the original assumption — that the `onnxruntime-rocm` wheel failed to install and the `|| pip install onnxruntime` fallback fired — was **wrong**. The log proves the wheel installed fine:
```
#12 29.55 Successfully installed onnxruntime-rocm-1.22.2.post1
#13 0.268 ONNX providers: ['AzureExecutionProvider', 'CPUExecutionProvider']   ← plain onnxruntime won
```
`onnxruntime-rocm` and plain `onnxruntime` both ship the same `onnxruntime` Python module. `requirements-amd.txt` listed `onnxruntime>=1.20.0`, so plain onnxruntime was installed first and **shadowed** the ROCm build that was installed afterwards — hence `AzureExecutionProvider` (the plain-build fingerprint) and no `ROCMExecutionProvider`. The `||` fallback never even fired.

**Fix applied:**
1. `requirements-amd.txt` — removed the `onnxruntime>=1.20.0,<2.0.0` line (only `numpy` stays in that slot).
2. `Dockerfile.amd` — after `pip install -r requirements.txt`, `pip uninstall -y onnxruntime onnxruntime-gpu` (defensive, in case the base image pre-bakes one) then `pip install --force-reinstall "onnxruntime-rocm<=1.22.99"` so the ROCm build is the **sole** provider of the module. The `|| pip install onnxruntime` tail stays only as a genuine wheel-unavailable safety net; the warn-only assert stays.

**Verification:** needs an AMD rebuild. The existing build-time provider check will print `OK: ROCMExecutionProvider present` if the fix worked (instead of the WARNING). No AMD hardware needed to confirm — the assert runs in CI.

**Note:** `docker7-vulkan` (ncnn-Vulkan) remains the alternative AMD-acceleration path for cards/setups where ROCm isn't viable.

**Risk:** medium — touches the AMD Dockerfile / requirements; needs a real AMD test or at least a clean build-log verification.

### FIX-3 (P1) — "Real-ESRGAN x4 (256px optimized)" benchmark warmup crashes

This is the **only image-found bug with real user impact** (it surfaced as the "Reshape" error Gemini blamed in #70, visible in `i69_17`).

The model catalog has two Real-ESRGAN x4 ONNX models ([`main.py:435`](docker-ai-service/app/main.py#L435)):
- `realesrgan-x4` — **dynamic** input shape (accepts any tile size)
- `realesrgan-x4-256` — *"Optimized for 256px tiles"* = **static 256×256** input shape baked into the ONNX graph

The benchmark forces a 64×64 test image for *anything* whose model id contains `realesrgan` ([`main.py:3114`](docker-ai-service/app/main.py#L3114)):
```python
if state.current_model_type == "onnx" and "realesrgan" in state.current_model:
    test_size = 64
```
For `realesrgan-x4-256` that 64×64 input violates the model's fixed 256×256 shape → ONNX Runtime raises a Reshape/dimension error → `run_benchmark()` returns `{"error": "Warmup failed: …"}`. The substring test `"realesrgan" in state.current_model` is too broad: it can't tell the dynamic model from the static-256 one.

**Fix (robust, drift-proof):** stop guessing from the model name — read the real input shape from the loaded ONNX session and use it:
```python
test_size = 64  # default for dynamic-shape models
if state.current_model_type == "onnx" and state.onnx_session is not None:
    ishape = state.onnx_session.get_inputs()[0].shape  # e.g. [1,3,256,256] or [1,3,'h','w']
    h = ishape[2]
    if isinstance(h, int) and h > 0:   # static spatial dim → must match exactly
        test_size = h
```
(Quick alternative if reading the session shape is awkward: special-case `elif state.current_model == "realesrgan-x4-256": test_size = 256`. The session-shape version is preferred — it also fixes any future fixed-shape model added to the catalog.)

**Risk:** low. Only changes the benchmark's synthetic input size; the inference path is unchanged. Should be paired with a quick test that benchmarking `realesrgan-x4-256` no longer errors.

### FIX-4 (P2) — dashboard "GPU: Active" vs System tab "GPU: CPU only"

On the working Intel-Arc/WSL2 setup (#69), the main dashboard shows GPU **Active** (OpenVINO) while the **System** tab shows **GPU: CPU only** (`i69_09`/`i69_17`). The two views read different signals: every status endpoint returns `"using_gpu": state.use_gpu` (e.g. [`main.py:3153`](docker-ai-service/app/main.py#L3153), [`:3230`](docker-ai-service/app/main.py#L3230)), but the System-tab badge is computed from a narrower notion of "GPU" that appears to count only CUDA/ROCm as a real GPU and treats `OpenVINOExecutionProvider` as CPU.

**Investigation (not a blind fix):** find the field the System tab renders from (frontend `configurationpage.html` / the system-info endpoint payload) and make "is GPU active?" mean the same thing in both places — i.e. *any* non-CPU execution provider (`CUDA`/`Tensorrt`/`OpenVINO`/`ROCm`/`CoreML`) counts as GPU. Single source of truth: derive the badge from the active provider list, not from a backend-name string match.

**Risk:** low — display-only; no inference behavior changes. Purely an honesty/consistency fix so users stop thinking GPU isn't working when it is.

---

## Backlog (unchanged, still valid — P3)

- `docker-ai-service/tests/test_detection.py` — mock-based coverage for `detect_hardware()` (the function where the whole Intel saga lived; still untested).
- OS-package CVE sweep — `apt upgrade` in the Dockerfiles closes ~193 Trivy alerts incl. the 2 critical `libgnutls30` CVEs (see prior code-scan).
- Base-image SHA256 digest pinning (supply-chain).

---

## Recommendation

**Ship order for the next release:**

1. **FIX-3 (P1)** — the 256px-model benchmark crash is the only image-found bug a user actually hits, and it's what made #70/#69 look like a real "Reshape" GPU bug. Small, low-risk, high-clarity win. Do this first.
2. **FIX-1 (P2)** — trivial, drift-proof version string; bundle it in regardless.
3. **FIX-4 (P2)** — display-only consistency; cheap, removes a recurring "is my GPU even working?" support question.
4. **FIX-2 (AMD, P2)** ✅ — investigated via the actual build log and fixed: the issue was a plain-`onnxruntime` / `onnxruntime-rocm` package collision, not a missing wheel. Ships with the next AMD rebuild; the build-time assert validates it in CI (no AMD hardware needed).

The two open issues (#70, #71) stay open pending user response — both are configuration/caching on the user side, no code change. The GPU path itself is proven working on the current v1.7.7 `docker7-intel` image (#69 logs).

**Author:** maintainer session 2026-06-02 (after full 43-image issue review)
