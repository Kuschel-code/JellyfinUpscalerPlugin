# Release v1.7.5 — Non-Admin User Support + 4:3 Aspect-Ratio Fix

**Release date:** 2026-05-25
**Build:** 0 warnings, 0 errors
**Tests:** 123/123 (unchanged)
**Bit-compat:** v1.7.x saved configs unchanged. Closes issue #69.

## Issue-#69-driven release

Issue [#69](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/69) (reported 2026-05-25 by "Daniel") contained three separate bugs. Two of them — the **most user-blocking** — are fixed here. The third (Intel Arc A310 GPU not used despite being detected) traced back to user-config issues (12-releases-old image tag + commented-out `group_add: render`) plus the known Detection-vs-Provider-Selection decoupling I noted as a v1.7.4-Audit-P3 finding — covered in the issue-response, polished separately later.

### Fix #69 (Auth) — Non-admin users blocked from the plugin

**Audit found**: 47 of 52 endpoints in `UpscalerController.cs` were guarded with `[Authorize(Policy = "RequiresElevation")]` — Jellyfin's admin-only policy. Including all playback/upscale/queue endpoints. **A normal Jellyfin user could see the model list but not load any model or trigger any upscale operation.** The plugin was effectively admin-only despite being marketed as a per-user feature.

**Fix**: Reclassified the 47 admin-only endpoints into two groups:

- **31 endpoints downgraded to `[Authorize]`** (authenticated-user OK):
  - Playback ops: `process`, `process/item/{itemId}`, `upscale/image`, `upscale-frame`, `upscale-video-chunk`, `upscale-images/{itemId}`, `preprocess`
  - Models: `models/load`, `face-restore/load`, `face-restore/unload`
  - Queue (user-specific): `queue/add`, `queue/{jobId}/cancel`, `queue/{jobId}/priority`
  - Jobs (user-specific): `jobs/{jobId}/pause`, `jobs/{jobId}/resume`, `jobs/{jobId}/cancel`
  - Read-only diagnostics: `jobs`, `queue`, `compare/{itemId}`, `recommendations`, `recommend-model`, `hardware`, `hardware-info`, `gpus`, `gpu-verify`, `service-health`, `model-benchmark`, `benchmark-frame`, `face-restore/status`
  - User-specific filter preview: `filter-preview`, `filter-preview/frame/{itemId}`

- **16 endpoints remain `RequiresElevation`** (admin-only, by design):
  - Server config: `service-config`, `settings/import`, `settings/export`, `filter-config`
  - Destructive: `models/cleanup`, `cache/clear`
  - Security-sensitive: `ssh/test`
  - Global state changes affecting all users: `queue/pause`, `queue/resume`
  - Detailed observability: `cache/stats`, `metrics`, `health/detailed`, `models/disk-usage`, `fallback`
  - Performance/load test: `test`, `benchmark`

**Class-level `[Authorize]` on `UpscalerController` (L33) ensures all 52 endpoints still require an authenticated session** — only the elevation requirement was removed. Anonymous access remains blocked everywhere.

**Security note:** GPU-intensive operations are now reachable by any authenticated user. Existing `MaxConcurrentStreams` and `MaxQueueSize` clamps (v1.7.2 Math.Clamp-hardened) provide global DoS-caps. Per-user quota deferred to v1.7.6.

### Fix #69 (Aspect-Ratio) — 4:3 movies no longer stretched

Both real-time renderers (`Configuration/webgl-upscaler.js` + `Configuration/webgpu-ai-realtime.js`) set the canvas-overlay CSS to `width:100%; height:100%` without `object-fit`, blindly stretching 4:3 source video to 16:9 player container.

**Fix**: Added `object-fit: contain` to both canvas overlays. The canvas drawbuffer already carries the correct `videoWidth x videoHeight` (preserving native aspect-ratio), so `object-fit: contain` lets the browser letterbox correctly without additional geometry calculation.

```javascript
// webgl-upscaler.js L135 (new)
this.canvas.style.objectFit = 'contain';  // #69: respect source aspect-ratio

// webgpu-ai-realtime.js L89 (new)
this._canvas.style.cssText = '...;object-fit:contain;...';
```

Zero risk to existing 16:9 content (`contain` on a 16:9 canvas in a 16:9 container is a no-op).

## #45 closed as obsolete

[#45](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/45) — Intel ARC A380 — originally reported Feb 2026 by FrRene06 (same reporter as #66). Both symptoms (OpenVINO-CPU instead of GPU + missing player button) were indirectly resolved across the v1.6.x to v1.7.4 stack. Cross-referenced to #66.

## Issue-Status

| # | Title | Status |
|---|---|---|
| #69 | Intel Arc A310, Aspect Ratio & Non-admin users | CLOSED (2 fixes this release, 1 user-config) |
| #45 | Intel ARC A380 | CLOSED (obsolete, see #66 in v1.7.4) |

Repo now has **0 open issues**.

## Files touched

### Modified
- `Controllers/UpscalerController.cs` — 31 `[Authorize(Policy = "RequiresElevation")]` lines removed (-31 LoC)
- `Configuration/webgl-upscaler.js` — +1 line `objectFit: 'contain'`
- `Configuration/webgpu-ai-realtime.js` — +1 cssText fragment `object-fit:contain;`
- `meta.json`, `manifest.json`, `repository-jellyfin.json`, `repository-simple.json`, `PluginConfiguration.cs`, `JellyfinUpscalerPlugin.csproj` — version `1.7.4` to `1.7.5`
- `README.md` — title + tags + new changelog section
- `site/index.html`, `site/changelog.html` — v1.7.5 entry
- `site/*.html` (14 files) — topbar brand-version synced

### New
- `docs/ISSUES-PLAN-2026-05-25.md` — pre-fix analysis plan
- `RELEASE-NOTES-v1.7.5.md`

## Roadmap

- **v1.7.6**: Per-user quota for GPU-ops (Issue #69 security follow-up). Optional: Detection-vs-Provider gpu_inference_active flag (v1.7.4-Audit-P3).
- **v1.8.0**: Pipeline-Parallelization (`Channel<T>`-based concurrent extract/inference/encode).
- **v2.0.0**: Multi-Frame VSR (EDVR / RealBasicVSR temporal context) in realtime.

## Verification

- **Build:** 0 warnings, 0 errors (verified via `dotnet publish -c Release`).
- **RequiresElevation count:** 47 to 16 (verified via grep — exactly the 31 expected downgrades).
- **Quad-MD5:** local ZIP md5 == GitHub-asset md5 == manifest.json checksum == repository-*.json checksum (verified post-release via `Scripts/verify-release.ps1`).
- **meta.json-in-ZIP:** version matches tag (`1.7.5`).
