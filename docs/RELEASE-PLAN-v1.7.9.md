# Release Plan — v1.7.9 (FINAL, rev. 3)

**Date:** 2026-06-04 · **Theme:** *Support-friction killer + client-tier honesty + anime broadening.*
**Two artifacts, same version:** **Docker v1.7.9** (AI service) and **Plugin v1.7.9** (C# DLL + site), built/released by different paths (see §Release mechanics).
**Rev. 2** folds in a verified cross-review: corrected the sibling-bug line label (A.1), added third-party licensing + source-vetting for Anime4K (A.2/A.3), clarified `/doctor model_smoke` (A.4), sharpened WS5/GMFSS (ONNX is the real blocker, B.1) + added CAIN as the tractable alternative (B.2), and the notaneimu-source synergy (B.3) + open SISR backlog (B.4).

Priority (cross-review-weighted): **Setup Doctor first** (it condenses the entire recent support saga into self-service), then Anime4K, docs, visibility, optional anime extras.

---

## WS1 — Setup Doctor + sibling-bug fix · P1 · Docker v1.7.9 · effort: medium, risk: low

#66/#69/#70/Laurent/Daniel were all **setup friction**. A one-shot diagnostic turns days of back-and-forth into one `curl`. Builds on `gpu_is_active()` (v1.7.8); pairs with the support bot (bot answers questions, doctor diagnoses the **running** instance). The Doctor checks map 1:1 onto the saga's root causes.

### NEW
- **`GET /doctor`** in `docker-ai-service/app/main.py` — checklist, each item `{check, status: ok|warn|fail, detail, fix}`:
  | check | pass condition | fix-hint on fail |
  |---|---|---|
  | `backend` | detected backend (intel-wsl2/nvidia/amd/…) | info only |
  | `gpu_provider_active` | `gpu_is_active()` true | "on CPU — pull `docker7-<backend>` + pass the device (see `device_passthrough`)" |
  | `device_passthrough` | `/dev/dri` or `/dev/dxg` present, or `nvidia-smi` works | exact line for the backend (`--gpus all` / `/dev/dri` / `/dev/dxg` + `group_add: render`) |
  | `onnx_provider_pkg` | not only `Azure/CPU` on a GPU image | "wrong image — plain `onnxruntime` shadows the vendor build; pull `docker7-<backend>`" |
  | `api_token` | `API_TOKEN` set or `=disable` | "add `API_TOKEN=disable` (LAN) or matching token both sides" |
  | `model_smoke` | short inference on the **already-loaded** model succeeds | "model load/warmup failed: <error>" |
  - Reuses `gpu_is_active()`, `state.providers`, the `/gpu-verify` probes.
  - **`model_smoke` is the one non-read-only check** (it runs a tiny inference). Therefore: if **no model is loaded → `warn`/`info`, never `fail`** (a freshly started instance must not show red), and **wrap it in a short timeout** so `GET /doctor` never blocks. Everything else is strictly read-only.
- **"Setup Check" panel** in `docker-ai-service/static/index.html` — calls `/doctor`, renders green/red rows + copy-paste fixes.

### CHANGED — fix the v1.7.8 sibling-bug (verified by enclosing function, not nearest route)
Three spots still report raw `state.use_gpu` instead of `gpu_is_active()` → change all three (one-liners; same value in the normal case, strictly more correct in edge cases):
- `run_benchmark()` — **L3316**
- `/models/load` (`load_model_endpoint`) — **L3829**
- **`_run_frame_benchmark()`** (reached via `GET /benchmark-frame`, route L4366) — **L4206**  ⚠️ *not* `/upscale-video-chunk` — that endpoint is at L4064 and has no `using_gpu`.

Also bump `main.py` `VERSION` fallback `1.7.8` → `1.7.9` (still env-driven via `APP_VERSION`).

### Verify
- `python -c "import ast; ast.parse(open('docker-ai-service/app/main.py').read())"`; `grep -c '"using_gpu": state.use_gpu' main.py` → **0**.
- After rebuild: `curl http://IP:5000/doctor | jq` returns valid JSON; on a no-model CPU instance it shows `warn`, not `fail`; AMD assert still prints `ROCMExecutionProvider present`.

### Release
- `gh workflow run docker-publish.yml -f version=1.7.9 -f push=true` (6 images). No plugin ZIP needed. Optionally re-point `latest` via the cleanup script.

---

## WS2 — Anime4K in the WebGL fallback · P2 · Plugin v1.7.9 · effort: low–med, risk: low (opt-in)

Real anime upscaling for clients **without WebGPU** (where playback currently falls back to plain Lanczos), and it brings the mpv-shim flagship to every web/TV/mobile-web client. **Honest label: "Anime4K (anime shader)" — a shader pipeline, NOT a neural net.**

### ⚠️ Pre-flight (clear BEFORE coding — the one real open risk in this plan)
- **Use a WebGL/WebGL2 port, NOT WebGPU.** WebGPU is already covered by `webgpu-ai-realtime.js`; a WebGPU Anime4K (`Anime4KWebBoost/Anime4K-WebGPU`) would make WS2 redundant.
- **Candidate:** `monyone/Anime4K.js` (WebGL, npm `anime4k.js`, **v0.0.1 — ~3 years old**; *verify exact version+date on the npm package page before pinning*). It exposes a clean `Anime4KJS.VideoUpscaler` API and is a genuine browser WebGL video upscaler — **still the best candidate despite its age**: a frozen shader port needs little maintenance, but confirm it still upscales (on-frame test below).
- **No fresher fork exists** — don't chase a "newer" one: `hex2f/Anime4K` (npm `anime4k`) is **~7 years old (1.0.1)**, i.e. *older*, not a staleness fallback; `@mpv-easy/anime4k` is recent but ships **GLSL-for-mpv**, not a browser WebGL runtime. So pin monyone at a tested commit and rely on the on-frame test as the real gate.
- **Verify the actual Anime4K version it implements** (the "v4" claim is unconfirmed — could be v2/v3-era). Pick whichever genuinely upscales.
- **Pin a specific commit/tag** (never `main`/`latest`).
- **Test on a real anime frame** that it visibly upscales — don't assume.

### NEW
- `Configuration/anime4k.js` — the vendored, pinned port. **Add a license header** (Anime4K core is MIT; **confirm the chosen port's license is MIT-compatible**) and a `THIRD-PARTY-NOTICES.md` (or `NOTICE`) entry in the repo — required because it ships **inside the DLL** delivered to users.
- `JellyfinUpscalerPlugin.csproj` — `<EmbeddedResource Include="Configuration\anime4k.js" />` (embedded in the DLL → the release ZIP file-list stays the same 6 files; `verify-release.ps1` unaffected).

### CHANGED
- `Configuration/webgl-upscaler.js` — add an "anime4k" path; instantiate the port's video upscaler when selected; **auto-fallback to existing Lanczos** if WebGL2 is unavailable.
- `Configuration/player-integration.js` / `quick-menu.js` — add mode **"Anime4K (anime shader)"**; relabel all tiers honestly: `WebGL (sharpen)` · `Anime4K (anime shader)` · `WebGPU AI (client GPU)` · `Server AI` · `Batch (best)`.
- Version strings: `meta.json` → 1.7.9, `csproj` Version/Assembly/File → 1.7.9.0.

### Verify
- `node --check Configuration/anime4k.js` + touched JS; `dotnet build` 0/0; `dotnet test` **123/123**.
- Manual: web client → "Anime4K" → anime title visibly sharper; non-WebGL2 client silently uses Lanczos.

---

## WS3 — Docs / positioning · P3 · Pages only (no release) · effort: low
- `site/` "**Use what you already have**" section: RTX/Intel VSR for desktop-browser, mpv-shim+Anime4K for mpv, **our batch** for the whole library + the TVs/phones nothing else reaches. We're the hub.
- **Surface the WebGPU-AI tier** (features.html / a realtime page) — stop implying the client tier "isn't AI".
- `README.md` pairing note; honest tier labels (match WS2).
- Optional `support-kb.json` topic: "real-time tiers / why it switches" (ties #70 + labels).

---

## WS4 — WebGPU-AI visibility · P4 · tiny (mostly verify, folded into WS2 release)
- Confirm `ai-webgpu` is **offered in the player mode picker** (already wired at `player-integration.js:226` → `_startWebGPUAI()`).
- Ensure a small web-optimized ONNX is bundled/fetched. **Source synergy (B.3):** `notaneimu/onnx-image-models` (where v1.7.8's 12 models came from) is built **for ONNX-Runtime-Web/WebGPU** — the same source can supply the client model (a light Compact/SPAN). One source feeds both the server catalog and the client tier.

---

## WS5 — Interpolation diversity (anime) · P5 · OPTIONAL

Current state: only **RIFE**. This is the one real structural gap — but it's genuinely hard to close:

- **GMFSS ⚠️ ONNX is the blocker (not "find a URL").** GMFSS is a **multi-network** model (flow-estimation + fusion + metric nets), shipped across the ecosystem as **PyTorch or compiled TensorRT engines**, *not* a single portable `.onnx`. The plan's old "HEAD-verify a GMFSS ONNX" would likely find nothing. It's also **much heavier than RIFE** (questionable for the weak-hardware target). **Defer.** If pursued: target **GMFSS Pro** (fixes text-warping), and **budget a real multi-net→ONNX conversion** — not a download. (GIMM has the same ONNX-tractability problem.)
- **CAIN 🟢 the tractable alternative (B.2).** `myungsub/CAIN` runs in the ecosystem as **NCNN** (e.g. `mafiosnik/vsynth-cain-NCNN-vulkan`) and is **lighter than GMFSS**. The plugin **already has an NCNN/Vulkan path** (`vulkan` category, 3 models) → a CAIN-NCNN integration is far more tractable than GMFSS-ONNX. Not as strong as GMFSS for anime, but realistic to ship and lighter at runtime. **If you want interpolation diversity, this is the realistic first step.**

---

## Catalog backlog (optional, cheap — independent of the above)
Open SISR recommendations from earlier model rounds (RGT-S already landed in v1.7.8 as `textures-rgt-s-x4`):
- **ESC** — real-time, weak-hardware-friendly (exactly the Intel-Arc audience from the #45/#66/#69 saga). Best cheap SISR add. **Check if it's in the `notaneimu/onnx-image-models` tree** — if so, a HEAD-verify + 3-layer sync is all it takes.
- **ATD** — top-quality transformer; heavier, lower priority.
- Process = the verified 3-layer sync: `main.py` AVAILABLE_MODELS → regenerate `Resources/models-fallback.json` → update `site/models.html`.

---

## Release mechanics (once, for both artifacts)

**Version-bump checklist (every display + manifest spot — the v1.7.8 lesson):**
- [ ] `meta.json` → `1.7.9` (3-part) · `csproj` Version/AssemblyVersion/FileVersion → `1.7.9.0`
- [ ] `manifest.json` + `repository-jellyfin.json` + `repository-simple.json` → new `1.7.9.0` entry (sourceUrl v1.7.9, **MD5 checksum**, targetAbi 10.11.8.0, timestamp)
- [ ] `README.md` title (L1) + "independently versioned at" line + ASCII box + docker-tag pin examples → v1.7.9
- [ ] `Scripts/sync-site-topbar-versions.ps1` (topbars) **+** footer pass over all `site/*.html` → v1.7.9
- [ ] docker `APP_VERSION=1.7.9` (workflow input)

**Plugin ZIP** = the curated **6 files** only: `CliWrap.dll`, `FFMpegCore.dll`, `Instances.dll`, `JellyfinUpscalerPlugin.dll`, `SixLabors.ImageSharp.dll`, `meta.json` (anime4k.js is *embedded in the DLL*, not a separate file). Build via `dotnet publish -c Release`, stage the 6, zip flat, MD5 → write that MD5 into the 3 feeds **before** uploading.

**Tag discipline (the v1.7.7 lesson):**
1. Commit **everything** to `main` first.
2. `gh release create v1.7.9 <zip> --target main` (tag at main HEAD).
3. `gh workflow run docker-publish.yml -f version=1.7.9 -f push=true`.
4. `pwsh Scripts/verify-release.ps1 -Tag v1.7.9` → must print **RELEASE VERIFICATION PASSED** (MD5 == manifest, meta version, 6-file list). If CDN-stale, also do the manual asset-MD5 round-trip.

---

## Verification gates (all must pass before "done")
- `dotnet build` 0/0 · `dotnet test` 123/123 (+ a pytest for `/doctor` if cheap).
- `main.py` AST-parses; `/doctor` returns valid JSON + degrades to `warn` with no model; **0** remaining `"using_gpu": state.use_gpu`.
- `node --check` on all touched/added JS.
- Quad-MD5 `verify-release.ps1` PASS.
- AMD build assert: `ROCMExecutionProvider present`.

## Out of scope (explicit)
- Chasing Topaz top-end quality. Server-side real-time arms race. Bumping the manifest before the asset exists (= install breakage). Pulling a WebGPU Anime4K port for WS2 (would duplicate the existing WebGPU-AI tier).

## Suggested order & start
1. **Commit this plan** (already corrected: A.1 label, A.3 license, A.4 model_smoke).
2. **WS1** — Doctor + sibling-fix (Docker, standalone, low-risk).
3. **Clear the Anime4K source (WS2 pre-flight)** *before* coding WS2 — the one open risk.
4. **WS2 + WS4** (Plugin, one release) → **WS3** (Pages) → **WS5/CAIN** + **ESC** (optional).

Each WS is independently shippable; none blocks the others. "Do nothing right now" stays defensible — but if you act, WS1 is the move.

*Author: maintainer session 2026-06-04 (FINAL rev. 3). Code findings verified against v1.7.8 `main.py` (enclosing-function attribution: L3316 `run_benchmark`, L3829 `load_model_endpoint`, L4206 `_run_frame_benchmark` via `/benchmark-frame` L4366); source/license findings against GitHub/npm. **Rev. 3 corrects one fact:** monyone/`Anime4K.js` is **v0.0.1 (~3 yrs)**, not 0.0.7 — and no fresher WebGL fork exists (hex2f `anime4k` is ~7 yrs/1.0.1; `@mpv-easy/anime4k` is GLSL-for-mpv). Conclusion unchanged: monyone stays the WebGL candidate; pin-a-commit + on-frame-test is the real gate. Earlier rev. 2 corrections (A.1–A.4, B.1–B.4) all re-verified and retained.*
