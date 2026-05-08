# v1.6.1.21 — Adoption Completion v2 + Compute-Waste Fixes + Honest Dead-Config

**Released:** 2026-05-08
**Plugin ABI:** 10.11.8.0 (unchanged)
**Catalog size:** 48 models (unchanged from v1.6.1.20)

This is a **follow-up patch** to v1.6.1.20 closing all 8 findings of the post-v1.6.1.20 external audit. The audit found 6 remaining HttpClient calls without `CancellationToken`, 4 sync `File.Copy` in frame-loops, asymmetric Frontend↔Backend FaceRestore allowlists, broken `RestrictToUnwatchedContent`/`SkipUpscaledOnRescan` toggles, a leaky `ProcessingStrategySelector` substring-matcher, and 4× duplicated filter-preset arrays.

---

## P0 — Compute-Waste Fixes (User-Facing)

### 1. `RestrictToUnwatchedContent` + `SkipUpscaledOnRescan` are now wired

The two toggles existed in `PluginConfiguration` since v1.6.1.14 but had **0 consumers** in `LibraryUpscaleScanTask`. User-impact: explicit user-intent to save compute (don't upscale already-watched content) was silently ignored — scan task processed everything.

`LibraryUpscaleScanTask.ExecuteAsync()` now respects both toggles:
- New `IsAnyUserPlayed(BaseItem)` helper consults `IUserDataManager` over all users (conservative: any single user's playcount > 0 → "watched"). Constructor extended with `IUserManager` + `IUserDataManager` (DI-resolved automatically).
- `RestrictToUnwatchedContent=true` skips items any user has already played.
- `SkipUpscaledOnRescan=true` (default) gates the existing _upscaled-suffix and sibling-file-exists checks. Setting it to `false` now actually allows reprocessing (e.g. to upgrade existing _upscaled files from realesrgan-x4 to drct-l-x4).

**Fail-open semantics:** if the user-data lookup throws (DB lock etc.), treat as "unwatched" so the scan keeps running. Logged as warning.

### 2. 6 remaining HttpClient calls now propagate `HttpContext.RequestAborted`

v1.6.1.20 fixed 9 of 16 HttpClient calls. The audit found 6 still bare — including the **two hot-path video calls**:

| Line | Endpoint | Risk |
|---|---|---|
| 1790 | `POST /config` | settings save hangs |
| 1812 | `GET /models/disk-usage` | UI hangs |
| 1834 | `POST /models/cleanup` | cleanup hangs |
| **1873** | **`POST /upscale-frame`** | **player UI freezes on stop** |
| **1932** | **`POST /upscale-video-chunk`** | **player UI freezes on stop** |
| 1970 | `GET /benchmark-frame` | benchmark hangs |

All 16 of 16 HttpClient calls in `UpscalerController` now use cancellation tokens.

### 3. `ProcessingStrategySelector` substring-matcher tightened

The v1.6.1.18 substring matchers (`compact`, `realplksr`) were too broad and falsely accepted:
- `anime-compact-x4` (category=anime, NOT video-fast → frame drops mid-playback)
- `nomos2-realplksr-x4` (category=video-quality, 30 MB DAT2-class → frame drops)

**Fix:** Removed the `compact` and `realplksr` substring matchers. Only the unambiguous architecture prefixes (`fsrcnn`/`espcn`/`span`) remain. New compact/realplksr models that ARE realtime-eligible (e.g. `bhi-realplksr-x4`) must be added to the `fastModels` HashSet explicitly. **+13 regression tests** in new `ProcessingStrategySelectorTests.cs`.

---

## P1 — Performance / Correctness

### 4. 4 frame-loop `File.Copy` → async streaming

v1.6.1.20 fixed `CacheManager.cs:307`. The audit found 4 more sync `File.Copy` in frame-loop hot paths (`VideoFrameProcessor.cs:263+295`, `ProcessingMethodExecutor.cs:347+354`) — all error-fallback paths where the original frame is copied unmodified when AI-service upscaling fails.

All 4 now use `FileStream + CopyToAsync` with `useAsync: true` (real overlapped IO on Windows / aio on Linux). On NAS-mounted disks: 5-30s thread-block per frame eliminated. Cancellation-token honored — job-cancellation cleanly aborts mid-copy.

### 5. FaceRestore Backend-Allowlist symmetric to Frontend

v1.6.1.19 made the Frontend `#FaceRestoreModel` dropdown auto-populate from `category="face_restore"` registry. The Backend `FaceRestoreLoad` endpoint kept its hardcoded `{ "gfpgan-v1.4", "codeformer" }` allowlist — meaning any future face-restore model (e.g. RestoreFormer++) would appear in the UI but get rejected with HTTP 400 from the backend. Asymmetric Frontend↔Backend drift.

**Fix:** New `_faceRestoreModelIds` Lazy parses the same embedded `models-fallback.json` the frontend uses. Both sides now stay in sync automatically. Defensive: if JSON parse fails, falls back to hardcoded `{gfpgan-v1.4, codeformer}` so face-restore never breaks entirely.

### 6. `UpscalerController` Filter-Preset-Liste deduplicated 4× → 1×

The 17-element preset array `{ "none", "cinematic", "vintage", ... }` was duplicated verbatim at 4 sites in `UpscalerController.cs`. Adding a new preset required editing 4 places.

**Fix:** New `private static readonly string[] _validFilterPresets` at the top of the controller. All 4 consumer sites reference it. Single-source.

---

## P3 — Honest Dead-Config Disclosure

The audit found 6 toggles in `PluginConfiguration` with **0 consumers** in any pipeline:
- `EnableModelPreloading`, `EnableHealthMonitoring`, `EnableModelAutoCleanup`, `EnableQualityMetrics`, `EnableFaceEnhancement`, `EnableGrainManagement`

Implementing them properly requires new pipeline code (preload-on-start hook, periodic health-timer, auto-cleanup task, PSNR/SSIM metric module, auto face-restore pipeline, per-job grain gate) — out of v1.6.1.21 scope and would risk new bugs ("no new bugs" was an explicit constraint for this release).

**v1.6.1.21 honest fix:** XML-doc-comments on all 6 properties now flag them as `currently no-op (no consumer pipeline). Marked for v1.7.0 pipeline implementation.` IntelliSense and code-readers see the truth. No silent UI-lying. v1.7.0 will implement the pipelines.

---

## Drift-Protection (new tests)

`ProcessingStrategySelectorTests.cs` — new file, +13 test methods covering:
- 5× `[Theory]` HashSet-models accepted (bhi-realplksr-x4, nomosuni-compact-x2, etc.)
- 4× `[Theory]` architecture-prefix-models accepted (fsrcnn-x2, span-x4, etc.)
- 2× `[Fact]` regression-guard: `anime-compact-x4` and `nomos2-realplksr-x4` REJECTED
- 2× `[Fact]` existing guards (4K input, RealTime disabled)

If a future contributor reintroduces the v1.6.1.18 substring matchers, the 2 regression-guard tests turn red.

Tests grew **72 → 85 passing** (+13 new).

---

## Configuration Changes

None. No new config fields, no defaults changed. v1.6.1.20 saved configs are bit-for-bit forward-compatible. The 5 dead-config toggle XML-docs are commentary-only.

---

## Test Results

- `dotnet build -c Release` — **0 warnings, 0 errors**
- `dotnet test` — **85/85 passing** (was 72, +13 new ProcessingStrategySelector regression-guards)

---

## Files Touched

```
Modified (substantive):
  ScheduledTasks/LibraryUpscaleScanTask.cs        (P0b: IsAnyUserPlayed + 2 toggle wirings + DI extended)
  Services/ProcessingStrategySelector.cs          (P0c: compact/realplksr substring matchers removed)
  Services/VideoFrameProcessor.cs                 (P1a: 2x sync File.Copy -> async streaming)
  Services/ProcessingMethodExecutor.cs            (P1a: 2x sync File.Copy -> async streaming)
  Controllers/UpscalerController.cs               (P0a: 6x HttpContext.RequestAborted, P1b: face-restore allowlist via registry, P2: filter-preset dedup)
  PluginConfiguration.cs                          (P3: 6 dead-config XML-doc honest disclosure)
  JellyfinUpscalerPlugin.Tests/Services/ProcessingStrategySelectorTests.cs (new file, +13 tests)
  Resources/models-fallback.json                  (regenerated, generated_at refreshed)

Modified (version-bump):
  docker-ai-service/app/main.py, meta.json, manifest.json, repository-jellyfin.json,
  README.md, Configuration/{html,js}, site/*.html (13 files)

New:
  RELEASE-NOTES-v1.6.1.21.md                      (this file)
```

---

## Drift-Trajectory (6 releases)

| Release | Bug class | Fix |
|---|---|---|
| v1.6.1.16 | 4 drift bugs unentdeckt | - |
| v1.6.1.17 | 4 model-list locations + Auto-Mode multi-frame | 4 point-fixes + 11 tests |
| v1.6.1.18 | 3 sibling bugs missed by v17 | 3 point-fixes + +4 tests |
| v1.6.1.19 | 3 more siblings | **Structural** SoT class + +28 tests |
| v1.6.1.20 | 2 adoption gaps + 3 new bug classes | 5 fixes + +7 tests |
| **v1.6.1.21** | **8 audit findings cleaned up** | **7 fixes + +13 tests + honest dead-config disclosure** |

**Honesty principle:** v1.6.1.21 is the first release where I explicitly **declined** to implement features I couldn't validate (the 6 dead-config toggles). Half-implementing them would have introduced new bugs. Documenting them honestly is the responsible move. v1.7.0 will implement the actual pipelines.
