# Model Evaluation — 2026-07 (v1.8.3.4)

Candidates from HF `huggingworld/onnx-image-models` and OpenModelDB, plus a full URL-health sweep of the existing catalog. **Gate:** license must permit redistribution-by-link and home use without surprises (NC = flagged or rejected); file must be a direct-download ONNX; ONNX sanity (ORT CPU load, dynamic H/W, correct output scale, finite fp32 output); and the candidate must beat its nearest incumbent in at least one cell (quality-proxy, speed, or size). CPU numbers measured on this dev machine (ORT 1.27, single 64px tile, median of 5) — relative comparisons, not absolute promises.

## 1. Adopted (4)

| id | Arch | License | Size | CPU ms/64px tile | Verdict |
|---|---|---|---|---|---|
| `clearreality-x4` (URL+pin updated) | SPAN | **Apache-2.0** (Kim2091 relicensed; OpenModelDB entry is stale) | 1.7 MB | **4 ms** | **32× faster than `realesrgan-x4` (129 ms) at 1/39 the size.** Existing catalog entry pointed at a mirror without license/hash metadata; now pinned (sha256) to the verified opset17 build. |
| `purephoto-realplksr-x4` *(new)* | RealPLKSR | CC-BY-SA-4.0 (asterixcool) | 30 MB | 67 ms | Photo/portrait specialist; still ~2× faster than realesrgan-x4. URL keeps the author's original "RealPLSKR" spelling — do not "fix" it. |
| `nomos8kdat-x4` *(new)* | DAT | CC-BY-4.0 (Philip Hofmann/Helaman) | 86 MB | 486 ms | JPEG-restoration specialist for old rips — a niche no incumbent covers. GPU recommended; description says so. |
| `fallin-soft-x2` *(new)* | Real-CUGAN | CC-BY-4.0 (renarchi) | 5.7 MB | 17 ms | Real-time 1080p anime tier. Permissive replacement for the NC-licensed Adore 2x (same author, same purpose). Replaces the dead `real-cugan-*` entries functionally. |

All four: download-verified (size matches upstream API), sha256-pinned in the catalog, ORT-CPU load + inference OK, dynamic H/W inputs, correct output scale. The service now **verifies the sha256 before activating any downloaded model** (new in v1.8.3.4).

VMAF scoring on real clips (via `/Upscaler/vmaf`) is still pending — needs the reference-clip setup on real hardware; these adoptions ride on license + architecture reputation + measured speed. Numbers above are the honest current basis.

## 2. Rejected candidates (documented so nobody re-evaluates them)

| Candidate | Reason |
|---|---|
| 4x UltraSharp V2 (Kim2091) | **CC-BY-NC-SA-4.0** (confirmed in author's repo + OpenModelDB). Already in the catalog since earlier — now **flagged** `[non-commercial license]` + license field instead of silently unlicensed. Not removed: home use is non-commercial. V1 has the same NC license. |
| Adore 2x (renarchi) | CC-BY-NC-SA-4.0 → rejected; `fallin-soft-x2` (CC-BY-4.0, same author/purpose) adopted instead. |
| Archivist/"Archiver" suite (loganavter, 5 models) | MIT, but **no ONNX exists** (only .pth) — on OpenModelDB *and* in the source GitHub release. Backlog: exportable via our own PyTorch→ONNX pipeline (license permits); see §4. |
| 2x ModernSpanimation V1 (TNTwise) | MIT, SPAN — but ONNX ships only inside a **ZIP** release asset and `download_model` has (deliberately) no archive extraction. Revisit if a direct .onnx mirror appears. |
| 2x StarSample V1.0 | CC0 but trained on MLP/cartoon material — too niche for a curated catalog. |

## 3. Catalog URL-health sweep (2026-07-03)

HEAD-checked all 73 catalog URLs: **47 OK, 16 dead** (8 HF repos deleted/gated → 401, plus file-level 404s). Final per-entry outcome:

- **Re-pointed to verified mirrors and live again (6):** `omnisr-x2` (Phhofm 2xHFA2kOmniSR, load+infer verified), `omnisr-x4` (official epoch895 weights), `gpen-512` + `restoreformer-plus-plus` (FaceFusion assets mirror), `nafnet-denoise` (deepghs SIDD-width64 export; size honesty-corrected 17MB→446MB), `rife-v4.25` (TAS op21-slim, load verified). All carry `license`/`attribution`; the GitHub-hosted ones are sha256-pinned.
- **Set `[self-host required]` (10):** `craft-x2/x4`, `bhi-realplksr-x4`, `dat-light-x2/x4`, `man-x2/x4` (exhaustive hunt: **no public ONNX exists anywhere**); `real-cugan-x2/x4` (the only public ONNX exports declare opset 18 but keep the pre-18 ReduceMean `axes` attribute — onnxruntime rejects them with INVALID_GRAPH, verified locally; they target TensorRT. `fallin-soft-x2` covers the cugan-arch anime tier); `drct-l-x4` (only export has a **fixed 1×3×64×64 input** — incompatible with the dynamic tiler, verified locally).

Net catalog after this pass: **76 registered, 59 available, 14 self-host/unavailable** (was: 73/66 with 16 of those 66 actually dead — honest availability went *up*, from 50 working to 59). Lesson: HF community repos disappear and third-party exports can be broken in ways HEAD checks cannot see; that is why new entries carry `sha256` + `license` + `attribution`, the downloader verifies hashes, and adoption requires a local ORT load.

## 4. Backlog

**IFRNet ONNX export (v1.9)** — no trustworthy public IFRNet ONNX exists. Plan: reproducible export from ltkong218/IFRNet (verify MIT license, ship license text next to the asset), IFRNet-S first; fixed torch version, `torch.onnx.export`, opset ≥ 17, dynamic H/W, 3-input signature incl. timestep (the adaptive dispatcher from v1.8.2 already expects it); verify ORT-CPU output vs PyTorch (PSNR > 40 dB) + latency; host as a GitHub release asset of this repo (stable URL) + sha256; flip `ifrnet` to `available: true` in both catalog carriers.

**Archivist restoration pre-pass (v1.9 candidate)** — MIT allows our own .pth→ONNX export (same pipeline as IFRNet). Fits as a `restoration`/1x pre-pass next to the existing `dejpg-realplksr-1x`/`denoise-realplksr-1x`; only worth it if wiring stays ≤ ~20 lines in the pipeline (before upscale, like denoise).

**NPU backend (v2.x, watch)** — AMD ships RealESRGAN NPU tiles for Ryzen AI (`amd/realesrgan-256x256-tiles-amdnpu`); onnxruntime has the VitisAI EP; Microsoft's VSR API targets Copilot+ NPUs. A seventh backend "NPU (Ryzen AI)" fits the weak-CPU mini-PC audience exactly. Trigger to act: first user with a Ryzen-AI box, or VitisAI EP becoming stable in ORT release images.

**numpy 2.x migration (service, deliberate deferral)** — requirements cap `numpy<2.0` keeps us on the EOL 1.26 line. ORT ≥ 1.19 and OpenCV ≥ 4.10 support numpy 2; needs a test pass across all six images before relaxing the cap.

**Base images (deliberate deferral)** — rocm6.2 (2024-08) and openvino 2025.4.1 have newer major lines (rocm 6.4+, openvino 2026.2.1). Both are behavior-changing jumps that need real-hardware smoke tests; patch-level refreshes (cuda 12.8.1, python-slim digest re-pins) shipped in v1.8.3.4.
