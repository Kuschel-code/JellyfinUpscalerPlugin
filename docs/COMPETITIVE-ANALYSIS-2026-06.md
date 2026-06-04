# Competitive Analysis — Jellyfin AI Upscaler Plugin vs. the field

**Date:** 2026-06-04 (rev. 2 — added §7 combinations + §8 recommendation) · **Our version:** plugin **v1.7.8** / Docker images v1.7.8 (71-model catalog, 6 backends) · a client-side Support Assistant bot is now live on the docs site
**Scope:** how this plugin compares to every other way a Jellyfin/home-media user can upscale or enhance video in 2026.

---

## 0. TL;DR — where we sit

**Inside the Jellyfin plugin ecosystem we are effectively unopposed.** A web search for "Jellyfin AI upscaling plugin" returns *this plugin* as "the primary option." Jellyfin's only native path is the FFmpeg `sr` filter — NVIDIA-CUDA only, and it requires compiling Jellyfin's bundled FFmpeg from source (impractical for normal users). There is an open feature request for built-in real-time upscaling, still unimplemented.

So the real competition is **other categories of tool**, each of which beats us on one axis and loses on others:

| Category | Example | Beats us on | We beat it on |
|---|---|---|---|
| Client shaders | Anime4K / FSRCNNX via mpv-shim | real-time efficiency, anime line-art, zero server load | client coverage, model depth, batch, vendor breadth, integration |
| Driver/browser VSR | NVIDIA RTX VSR, Intel VSR | zero-setup real-time, no server load | vendor lock-in (NVIDIA/Intel only), single model, no batch/face/interp, playback-device-bound |
| Commercial batch | Topaz Video AI | top-tier quality, polish | cost (free vs subscription), self-hosting, media-server integration, multi-vendor |
| OSS batch | Video2X 6, Real-ESRGAN, chaiNNer | raw flexibility for power users | turnkey integration, auto per-video selection, in-player UX, scheduling |

**Our moat:** the only solution that (a) lives *inside* Jellyfin and works across **all** clients, (b) supports **all five** GPU vendors + CPU, and (c) bundles batch + real-time + face restoration + frame interpolation + multi-frame VSR in one free, self-hosted package.

**Our soft spot:** live server-side AI is heavy (an Arc A380 can't sustain 4× HD in real time — issue #70), and setup (Docker + GPU passthrough) is harder than a driver toggle. *(The often-repeated "client tier is only Lanczos, not AI" is largely outdated — a real **WebGPU + ONNX client-AI tier ships since v1.7.1**; it only falls back to Lanczos on browsers without WebGPU.)*

---

## 1. What we ship (baseline for comparison)

- **4 modes:** overnight batch (Scheduled Task), image/poster upscaling, real-time during playback (two-tier: WebGL Lanczos+CAS → server AI), and an in-player model picker.
- **71 ONNX/ncnn models across 12 categories** — Real-ESRGAN family, SPAN, SwinIR, DAT2/DAT-light, DRCT-L, HAT/HAT-L, OmniSR, MAN, CRAFT, RGT-S, RealPLKSR, compact/fast lanes, anime (Real-CUGAN, APISR, AnimeSR), web/compressed-source specialists, 1× artifact-cleanup pre-passes.
- **6 hardware backends:** NVIDIA CUDA/TensorRT, AMD ROCm, Intel Arc/iGPU OpenVINO, Apple Silicon, Vulkan (ncnn), CPU.
- **Beyond plain upscaling:** multi-frame VSR (EDVR-M / RealBasicVSR / AnimeSR, 5-frame temporal), RIFE frame interpolation (v4.7–4.25), face restoration (GFPGAN / CodeFormer / RestoreFormer++ / GPEN), auto per-video model selection (genre + resolution).
- **Ops:** Docker microservice + HTTP API, multi-arch (amd64/arm64), TrueNAS app, Watchtower, build-time GPU-provider asserts.
- **License/cost:** free, MIT, fully self-hosted.

---

## 2. Competitor-by-competitor

### 2.1 Jellyfin native (FFmpeg `sr` filter)
- **What:** a super-resolution video filter in FFmpeg; official GPU inference is NVIDIA-CUDA-only and needs a from-source FFmpeg build.
- **Reality:** essentially unused by normal users — no UI, no model choice, no packaging. **Not a practical competitor**; it's the gap we fill.

### 2.2 Client shaders — Anime4K / FSRCNNX (jellyfin-mpv-shim)
- **What:** `jellyfin-mpv-shim` ships shader packs with Anime4K + FSRCNNX preconfigured; switchable via Video Playback Profiles. Runs on the **client GPU**, real-time, zero server load.
- **Strengths:** excellent real-time anime line-art sharpening; trivial once you use the mpv client; no server hardware needed; free.
- **Weaknesses vs us:** **mpv client only** (not the web app, browsers, smart-TV/Cast/mobile clients most people use); shader-based, not deep AI restoration models; no batch/pre-processing; per-client setup; no face restoration / interpolation / compressed-source training.
- **Verdict:** the strongest real-time option *for mpv users watching anime*. We win on client coverage and depth; they win on real-time efficiency for that niche. **Complement, not enemy** — we should document pairing.

### 2.3 NVIDIA RTX Video Super Resolution (RTX VSR) + Intel VSR
- **What:** driver/app-level AI upscaling + de-artifacting that applies to **VLC and Chromium browsers (Chrome/Edge/Firefox)** automatically when toggled. 2026 NVIDIA app: ~30 % more efficient model, now upscales HDR. RTX 30/40+. Intel ships an analogous VSR for Arc in Chromium.
- **Strengths:** **zero setup beyond a driver toggle**; real-time; no server load; and crucially it **already enhances the Jellyfin *web* client** in a Chromium browser on an RTX/Arc machine — for free, no plugin.
- **Weaknesses vs us:** **vendor-locked** (NVIDIA or Intel only); **playback-device-bound** (the device you watch on needs the GPU — useless for a Shield/Fire TV/phone streaming from a beefy server); single fixed model, no choice; no batch, no face restoration, no interpolation, no library-wide pre-processing; only helps browser/VLC playback.
- **Verdict:** the biggest "why do I need your plugin?" question for desktop-browser viewers on RTX/Arc. Our answer: we upscale **once, server-side, for every client** (including TVs/phones that can never run VSR), with model choice and restoration features. **Position batch pre-processing as the differentiator.**

### 2.4 Topaz Video AI (commercial)
- **What:** best-known commercial enhancer; 19+ models (Starlight, Astra 2, Proteus, Apollo/Chronos interpolation, Nyx denoise); up to 4K/8K/16K. Moved to **subscription-only** in late 2025 (~$49/mo or ~$299/yr).
- **Strengths:** arguably the highest output quality (Starlight/Proteus), strong denoise + interpolation, polished desktop GUI.
- **Weaknesses vs us:** **paid + closed-source**; desktop GUI with **no media-server integration** (manual export/import, no library scan, no per-video automation, no in-player use); NVIDIA-centric acceleration.
- **Verdict:** quality king for one-off manual restoration. We trade a slice of top-end quality for **free, automated, integrated, multi-vendor** — and our DAT2/HAT-L/DRCT-L close much of the gap. **Don't compete on the GUI; compete on "set it and forget it across your whole library."**

### 2.5 OSS batch — Video2X 6, Real-ESRGAN, chaiNNer, Upscayl
- **What:** Video2X 6.0 (C/C++ rewrite, fast; Anime4K v4 + Real-ESRGAN + Real-CUGAN + RIFE), raw Real-ESRGAN, chaiNNer (node graphs), Upscayl (images). CLI/desktop, free.
- **Strengths:** maximal flexibility for power users; same underlying model families we use; free.
- **Weaknesses vs us:** **no Jellyfin integration**, no library scan, no auto per-video model pick, no in-player UX, no scheduling, manual frame-pipeline plumbing.
- **Verdict:** we are essentially "Video2X's engine, but wired into Jellyfin with automation + a UI + multi-backend packaging." Power users may prefer raw Video2X; everyone else wants our turnkey path.

---

## 3. Feature matrix

Legend: ✅ full · ◑ partial/conditional · ❌ none

| Capability | **Us** | FFmpeg sr | Anime4K/mpv-shim | RTX/Intel VSR | Topaz Video AI | Video2X/OSS |
|---|:--:|:--:|:--:|:--:|:--:|:--:|
| Runs inside Jellyfin | ✅ | ◑ | ◑ (mpv client) | ❌ | ❌ | ❌ |
| Works on **all** clients (web/TV/mobile) | ✅ | ◑ | ❌ | ❌ | ❌ | ❌ |
| Real-time during playback | ◑ | ◑ | ✅ | ✅ | ❌ | ◑ |
| Batch / library pre-processing | ✅ | ❌ | ❌ | ❌ | ✅ | ✅ |
| Auto per-video model selection | ✅ | ❌ | ❌ | ❌ | ◑ | ❌ |
| Model variety | ✅ 71 | ❌ 1 | ◑ few | ❌ 1 | ◑ 19 | ✅ many |
| NVIDIA accel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| AMD accel | ✅ | ❌ | ✅ | ❌ | ◑ | ◑ |
| Intel accel | ✅ | ❌ | ✅ | ✅ | ◑ | ◑ |
| Apple Silicon | ✅ | ❌ | ✅ | ❌ | ✅ | ◑ |
| CPU fallback | ✅ | ✅ | ◑ | ❌ | ◑ | ✅ |
| Face restoration | ✅ | ❌ | ❌ | ❌ | ◑ | ◑ |
| Frame interpolation (RIFE) | ✅ | ❌ | ◑ | ❌ | ✅ | ✅ |
| Multi-frame (temporal) VSR | ✅ | ❌ | ❌ | ❌ | ✅ | ◑ |
| Top-end output quality | ◑ | ❌ | ◑ | ◑ | ✅ | ◑ |
| Real-time efficiency | ◑ | ◑ | ✅ | ✅ | n/a | ◑ |
| Zero/low setup | ◑ | ❌ | ◑ | ✅ | ◑ | ◑ |
| Free + open source | ✅ | ✅ | ✅ | ✅ (driver) | ❌ | ✅ |

---

## 4. Honest weaknesses (and what to do)

1. **Live server-side AI is heavy.** Real-time 4× HD exceeds the per-frame budget on mid GPUs (Arc A380 → falls back to WebGL, issue #70). RTX VSR and Anime4K win here because they're client-GPU + lighter. **Action:** keep steering quality-seekers to **batch pre-processing**; for live, *document* pairing with RTX VSR / mpv-shim instead of pretending to beat them.
2. **Client real-time leans on WebGPU for AI.** *Correction:* a real **WebGPU + ONNX** client-AI tier already ships (since v1.7.1, wired at `player-integration.js:226` → `_startWebGPUAI()`). Plain **Lanczos2 + CAS** is only the *fallback* for browsers without WebGPU. **Action:** for that fallback, Anime4K shaders (WebGL2, far broader support) beat Lanczos — and label honestly (Lanczos = sharpen; Anime4K = anime-tuned shader, *not* a neural net; WebGPU tier = the actual client AI).
3. **Setup friction.** Docker + GPU passthrough (the recurring #66/#69/#70 support load) is harder than a driver toggle or an mpv shader pack. **Action:** the build-time provider asserts + `/gpu-verify` + the new FIX-4 honest GPU reporting already reduce this; a guided "first-run doctor" endpoint would cut it further.
4. **Top-end quality vs Topaz.** Starlight/Proteus still edge our best models on hardest footage. **Action:** compete on automation + cost + integration, not pixel-peeping; keep adding SOTA models (the v1.7.8 catalog refresh — RGT-S, HAT-L, web-photo models — is exactly this).

---

## 5. Strategic positioning (one sentence)

> *"The only AI upscaler that lives inside Jellyfin, enhances every client (not just the device you're sitting at), runs on any GPU vendor or CPU, and does batch + real-time + restoration + interpolation — free and self-hosted."*

Lean into **(1) all-client coverage**, **(2) all-vendor support**, **(3) one integrated package**, **(4) free**. Treat RTX VSR and Anime4K as **complements to document**, Topaz as the **paid premium we undercut**, and Video2X as **our engine with the integration they lack**.

---

## 7. Combinations & improvement opportunities (fresh 2026 research)

**Correction from a cross-review (verified in code):** the **WebGPU + ONNX client-AI tier is already built and wired** (`player-integration.js:226` → `_startWebGPUAI()`, embedded resource, live since v1.7.1) — so "our client tier isn't AI" is **largely false**; it only holds on browsers without WebGPU. With that corrected, the real opportunities below run **inside the jellyfin-web player our plugin already injects into**, reaching the web/TV/mobile-web clients mpv-shim (mpv-only) and RTX VSR (desktop-browser/VLC-only) **cannot** — zero server load, any vendor.

### 7.1 Anime4K in the WebGL tier ⭐ — HIGH impact / LOW–MED effort
- **What:** `monyone/Anime4K.js` is a maintained **WebGL port of Anime4K v4** (GLSL), shipping `ImageUpscaler` / `VideoUpscaler` classes (also on npm). It upscales a `<video>` in real time on the **client GPU** in milliseconds.
- **Why it matters:** the client-AI tier (WebGPU) only runs where WebGPU exists; **without it, playback falls back to plain Lanczos2+CAS**. Anime4K (WebGL2 — far broader client support, incl. older TV web-views) is a much better fallback there, and gives strong real-time **anime** upscaling on **every** web client, no server.\n- **Honesty:** Anime4K is a hand-tuned **shader pipeline, not a neural net** — label it \"Anime4K (client GPU, anime-tuned)\", never \"AI\". It's anime-specific, not a general upscaler.
- **Strategic kill-shot:** Anime4K is the *headline feature* of the mpv-shim competitor — but mpv-shim only works in the mpv client. Bringing Anime4K to the **web/TV/mobile** player turns their strength into ours and erases their "real-time anime" edge for the 90 % of users not on mpv.
- **How:** vendor `Anime4K.js` into `Configuration/webgl-upscaler.js`, expose a player option **"Anime4K (client GPU)"** next to the existing WebGL mode; auto-fallback to Lanczos if WebGL2 is unavailable.
- **Risk:** low — opt-in, client-side, additive. Adds ~one JS dependency (vendored, no build step).

### 7.2 Client-side WebGPU AI tier — ✅ ALREADY DONE & WIRED (not a project)
- **Status:** built and live **since v1.7.1**. Verified in code: `Configuration/player-integration.js:226` routes the `ai-webgpu` mode to `_startWebGPUAI()`, which loads `onnxruntime-web` (WebGPU EP) and runs real Real-ESRGAN-compact inference on the **viewer's** GPU, with a documented 3-stage fallback to Lanczos. It's an embedded resource (ships in the DLL).
- **So the "task" is purely visibility + docs**, not engineering: make sure the mode is offered in the player picker and stop describing the client tier as "not AI".
- **It's already our RTX-VSR answer:** same "zero-server, real-time, on the playback device" benefit, but **vendor-agnostic** (WebGPU = NVIDIA/AMD/Intel/Apple), integrated, model-selectable — and it exists today. WebGPU now ships in Chrome/Edge/FF142+/Safari 26+, a growing majority.
- **Optional polish:** confirm a small web-optimized ONNX is bundled/fetched; surface the tier more prominently.

### 7.3 Cheap positioning / docs wins — LOW effort
- **"Use what you already have" page:** on an RTX/Arc desktop in Chrome, just toggle RTX/Intel VSR (free, live); on mpv clients, mpv-shim + Anime4K; use **our batch** for the whole library and for the TVs/phones nothing else can reach. Position us as the **hub**, not the rival.
- **Honest tier labels in the player:** "WebGL (sharpen)" vs "Anime4K (anime shader)" vs "WebGPU AI (client GPU)" vs "Server AI" vs "Batch (best quality)" — sets expectations and stops the "real-time looks soft" complaints (#70). Note the WebGPU-AI tier already exists — surface it, don't hide it.
- **First-run `/doctor` endpoint** (see §7.4 / §8): the structural answer to the entire recent support load, not a footnote.

### 7.4 Bundle-ins from the v1.7.8 audit
- **Sibling-bug (confirmed, 3 lines):** after FIX-4, three spots still report the raw `state.use_gpu` instead of `gpu_is_active()` — `run_benchmark` (`main.py` L3316), `/models/load` (L3829), `/upscale-video-chunk` (L4206). So `/doctor` would report honestly while `/models/load` keeps lying. **Fix all three in the same pass as `/doctor`** — one-liners, same value in the normal case, strictly more correct in the edge cases.
- **GMFSS interpolation (anime companion):** if you invest in Anime4K for the anime audience, add **GMFSS** interpolation alongside the existing RIFE — same target audience on the motion side. Anime4K + GMFSS = a coherent "anime pack".
- **Tag discipline:** the v1.7.7 tag once diverged from `main` (site updates landed after the tag). For v1.7.9, cut the tag only after everything is on `main`, then re-run `Scripts/verify-release.ps1`.

---

## 8. Recommendation — act, or do nothing?

**Honest take: you're under zero pressure.** Inside the Jellyfin ecosystem you remain unopposed (no new competitor this pass — the directories still list *this* plugin as the upscaling answer), v1.7.8 just shipped, the support bot is live, and the client-AI tier already exists. Nothing below is needed to "stay competitive" — it's all upside. **Revised priority (a cross-review re-weighted this — and it's right):**

1. ✅ **Setup Doctor `/doctor` + the sibling-bug fix — highest ROI, do first.** Every recent support case (#66, #69, #70, Laurent, Daniel) was **setup friction**: wrong image, missing render group, wrong onnxruntime package, stale image. A green/red checklist endpoint with copy-paste fixes turns days of back-and-forth into one `curl`. It builds on `gpu_is_active()` (v1.7.8), pairs with the bot (bot answers questions, doctor diagnoses the *running* instance), and is read-only/low-risk. Bundle the 3-line sibling-bug fix (§7.4) into the same pass. **Release: Docker v1.7.9.**
2. ✅ **Anime4K in the WebGL fallback (honestly labeled).** Real anime upscaling for clients without WebGPU + brings the mpv-shim flagship to all web clients. Opt-in, low-risk. Label "Anime4K (anime shader)", not "AI". **Release: Plugin v1.7.9.**
3. ✅ **Docs/positioning (cheap, Pages-only):** "use what you have" pairing page + honest tier labels — and **surface the already-shipped WebGPU-AI tier** instead of treating it as a gap.
4. 🟢 **WebGPU-AI:** no engineering — just confirm it's offered in the mode picker (it's wired) and maybe ship a small model.
5. 🟡 **GMFSS:** only if you commit to the anime investment (companion to Anime4K + RIFE).
6. ⛔ **Don't:** chase Topaz top-end quality or a server-side real-time arms race — diminishing returns, not our moat.

**Bottom line:** if you do one thing, it's now the **Setup Doctor** (bigger lever than Anime4K — it kills the whole support saga, and the sibling-bug gets fixed in the same pass). "Do nothing right now" stays completely defensible — the position is strong and nothing is breaking.

---

## 9. Sources

- [JellyfinUpscalerPlugin (GitHub)](https://github.com/Kuschel-code/JellyfinUpscalerPlugin)
- [Awesome Jellyfin Plugins 2026 (JellyWatch)](https://jellywatch.app/blog/awesome-jellyfin-plugins-complete-guide-2026)
- [Jellyfin Feature Request — real-time upscaling](https://features.jellyfin.org/posts/729/add-options-for-real-time-upscaling)
- [jellyfin-mpv-shim (GitHub)](https://github.com/jellyfin/jellyfin-mpv-shim)
- [Anime4K with MPV (Aiarty)](https://www.aiarty.com/ai-video-enhancer/anime4k.htm)
- [RTX Video FAQ (NVIDIA)](https://nvidia.custhelp.com/app/answers/detail/a_id/5448/)
- [NVIDIA App — RTX VSR/HDR update](https://www.nvidia.com/en-us/geforce/news/nvidia-app-beta-update-rtx-vsr-hdr-controls-and-more/)
- [RTX VSR in VLC (VideoCardz)](https://videocardz.com/newz/nvidia-rtx-video-super-resolution-is-now-supported-by-vlc-media-player)
- [Topaz Video AI review/pricing 2026 (VideoProc)](https://www.videoproc.com/resource/topaz-video-ai-review.htm)
- [Topaz Video (Topaz Labs)](https://www.topazlabs.com/topaz-video)
- [Video2X (GitHub)](https://github.com/k4yt3x/video2x/)
- [Best open-source video upscalers 2026 (VideoProc)](https://www.videoproc.com/video-editor/open-source-video-upscaler.htm)

- [monyone/Anime4K.js — Anime4K WebGL port (GitHub)](https://github.com/monyone/Anime4K.js/)
- [bloc97/Anime4K — GLSL instructions (GitHub)](https://github.com/bloc97/Anime4K)
- [free.upscaler.video — in-browser SR architecture (WebGPU/WebGL/WebCodecs)](https://free.upscaler.video/technical/architecture/)
- [ONNX Runtime Web + WebGPU (Microsoft Open Source Blog)](https://opensource.microsoft.com/blog/2024/02/29/onnx-runtime-web-unleashes-generative-ai-in-the-browser-using-webgpu/)

*Author: maintainer session, rev. 2 on 2026-06-04 (rev. 1 2026-06-03). Competitor capabilities/pricing from web research on those dates — re-verify before quoting publicly.*
