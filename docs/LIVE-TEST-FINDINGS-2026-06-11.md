# Live-Test-Findings — v1.7.12 on TrueNAS / Celeron J4125

**Date:** 2026-06-11 · **Build:** plugin **v1.7.12** (auto-updated from the feed, confirmed live)
**Mode:** observe-only — no code changes (per instruction "keine fixes, schreib es auf").

---

## What works (verified live)
- **Plugin is on v1.7.12** — dashboard header `v1.7.12 · Online`, the self-updating repo feed picked it up. Circuit Breaker **Closed**, latency 4–49 ms.
- **B3 phase panel works** — once the page is authenticated, the Live Performance Monitor shows **"Upscaling…"** (the job's real phase), not the old "Idle — waiting for jobs". The earlier "Idle" was a login-timing artifact, not a bug (see Note).
- **Progress is honest (not "stuck at 95%")** — the running job moved **27% → 28% over 75 s**; it climbs, slowly. The v1.7.10/11 honest-progress work holds up.

---

## 🔴 Finding 1 — Full-episode Batch upscale on the J4125 is ~10 h (practically unusable)
**Observed (Jobs tab):** `A Place Further Than the Universe – S01E001`, Status **Processing**, Progress **28%**, Method **Batch**, **Duration 10359.3 s (≈ 2 h 53 min)**.

At ~1 %/75 s, the 15→99 % upscale band alone implies **~10 h** for one episode. The user reports it has "been loading for hours" — confirmed: ~3 h elapsed, 28 %.

- **Not a v1.7.12 regression.** The bar is honest and moving; phase is correct. This is **hardware**: 2 CPU cores doing frame-by-frame extract + per-frame inference of a full episode (~30 k frames).
- **Where it comes from:** a `Batch` job — almost certainly auto-queued by the scheduled `LibraryUpscaleScanTask`. On this box that means it will *always* queue multi-hour jobs.
- **Mitigation (no code fix):** don't run full-episode batch on this CPU — short clips, or the client-side real-time tier (Anime4K/Lanczos, no server extraction), or disable the scheduled library scan. The hardware is the limit; even the lightest model can't make per-frame CPU inference of a whole episode fast.

## 🟠 Finding 2 — Observability gap: a grinding Batch job looks idle
**Observed:** while the Batch job is actively at 28 % ("Upscaling…", Duration counting up), the **Service Status → Model = "-"**, and the **Live Performance Monitor → Current Model "-", FPS "-", Avg Frame "-", Frames Total 0, Processing 0/4**.

- The plugin clearly *has* the frame progress (the 28 % bar is derived from `SendFrameProgress`, ≈15 % of frames upscaled), but the **service-metrics panels don't reflect the plugin's per-frame proxy path** — they read the Docker service's own `current_model`/frame counters, which stay empty for frame-by-frame `/upscale` proxy calls.
- **Effect:** a job that has been grinding for ~3 h looks **idle / broken** (no model, 0 frames) to the user, even though it is progressing. Combined with Finding 1's slowness, this is the worst of both — slow *and* looks dead.
- **Candidate fix (later, not now):** feed the Live Performance Monitor + Service-Status "Model" from the **active plugin job's** real frame stats (current/total frames + fps + the job's model) instead of only the Docker service self-metrics. The data exists plugin-side.

## 🟡 Finding 3 — (not tested) v1.7.12 face-restore timeout fix
The headline v1.7.12 fix (570 s download client for face-restore/models load) was **not** exercised live: the box's CPU is saturated by the ~3 h Batch job, so a 340 MB GFPGAN load would compete and wouldn't cleanly test the timeout path. **Test it on an idle box** (cancel the Batch job first).

## 🔴 Finding 4 — "Cancel" doesn't stop a running Batch job
Tried to cancel the runaway Batch job **3×** (UI Cancel button, via stable selector) — the job **stayed `Processing` and kept climbing** (29% → 30%, Duration 10764 s → 11304 s) across all attempts. No confirm dialog surfaced in the click responses, so either the cancel request isn't reaching/aborting the job, or the cancellation token isn't honored inside the Batch frame loop while a per-frame `/upscale` is in flight. **Effect:** a multi-hour job that the user explicitly cancels keeps burning CPU. Needs root-cause verification (UI dialog vs token-not-checked vs cancel endpoint). *No fix applied (per instruction).*

## Setup applied (2026-06-11) + "reaches 100%?" answer
**Config set + saved** (it was already mostly optimal): Model **FSRCNN x2**, Scale **2×**, Real-Time **Anime4K**, Service URL/token correct — I additionally set **Quality → Low**. **Not changed (recommended, user's call):** Library-Scan codec is **AV1 (libaom-av1, "highest quality, slow")** — a poor choice for a 2-core Celeron re-encode; **H.264 (libx264)** would cut the encode phase massively.

**"Do jobs reach 100%?" — not on this hardware, in any usable timeframe.** The running full-episode **Batch** job was at **30% after ~3 h 08 min** (11304 s) → extrapolates to **~10 h** to 100%. Even the light FSRCNN x2 / 2× config can't make a full-episode frame-by-frame CPU batch complete in a session. **The progress mechanism is correct** (climbs monotonically, phase shown, no stuck-at-95) — the limit is purely the 2-core CPU doing ~30 k per-frame inferences + a slow AV1 re-encode.

---

## Note (not a bug) — 401 burst on fresh login
Right after logging in, the dashboard's first poll round (`/Upscaler/jobs`, `/service-health`, `/metrics`, `/models`, …) returned **401** because the access token wasn't attached yet; this is what made the panel briefly show "Idle"/"Model -"/0. It clears once the page settles — a login-timing artifact in the test session, **not** a plugin issue.

---

## Recommendation
1. **Cancel** the ~3 h Batch job (28 %, ≈7 h to go) — it's wasting the CPU and won't finish in reasonable time on this box.
2. Avoid full-episode **Batch** on the J4125 (disable the scheduled library scan, or use short clips / the real-time client tier).
3. Then run the **face-restore timeout test** (Finding 3) on the idle box.
4. Findings 1+2 are the next-friction class for weak-CPU hosts: *batch is too slow to be usable, and the dashboard hides that it's even working.* Worth a docs/Known-Issues note + the Finding-2 wiring fix in a future release — not a v1.7.12 blocker.

*Observed live via the NAS dashboard (192.168.178.113); plugin v1.7.12; Jobs tab Duration 10359.3 s / 28 % / Batch. No code changed.*
