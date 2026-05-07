# v1.6.1.18 — Live-Action Resolver + RealTime-AI Whitelist + Docs Sync

**Released:** 2026-05-07
**Plugin ABI:** 10.11.8.0 (unchanged)
**Catalog size:** 48 models (unchanged from v1.6.1.17)

This is a **follow-up patch** to v1.6.1.17 that fixes 3 issues an external audit flagged after release. The v1.6.1.17 review found 4 drift bugs and fixed them — but the audit identified **3 surviving siblings of those same bug classes** that the original 2 verifier agents missed. All three are surgical, no schema changes, no new models.

---

## Critical Fixes

### 1. `PreferredLiveActionModel` was Dead Config — symmetric to the v1.6.1.17 anime fix

The v1.6.1.17 release fixed `PreferredAnimeModel` (default was `""`, resolver never read the field). The audit found that **`PreferredLiveActionModel` had the exact same problem** and was missed:

- ✅ Field defined ([`PluginConfiguration.cs:257`](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/blob/main/PluginConfiguration.cs#L257))
- ✅ UI dropdown rendered + populated (v1.6.1.17 added this for both fields)
- ✅ Persisted by Controller, loaded back from saved config
- ❌ **Never read by `ResolveModelForVideo()`** — silent ignore in auto-mode

**Fix:** Symmetric hook added in [`UpscalerCore.cs`](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/blob/main/Services/UpscalerCore.cs) — if `Config.PreferredLiveActionModel` is non-empty in the non-anime path, route through `PickAvailable(override → ultrasharp-v2-x4 → nomos2-realplksr-x4 → realesrgan-x4)`. Now setting "Preferred Live-Action Model" to e.g. `drct-l-x4` in the settings page actually does what the UI says it does.

### 2. RealTime-AI was rejecting v1.6.1.17's "Speed Champion" + 4 other fast models

`Services/ProcessingStrategySelector.IsRealTimeAIFeasible()` had a hardcoded 9-entry HashSet of fast-eligible models — but the registry has **14 models** in `category in {fast, video-fast}`. Result: a user picks `bhi-realplksr-x4` (the model marketing literally calls "Speed Champion"), enables RealTime-AI, and the log silently says `RealTimeAI skipped: model 'bhi-realplksr-x4' is not in the fast model list`. Falls back to per-frame mode without warning.

**Fix:** HashSet expanded from 9 → 14 entries. Plus 2 new substring matchers (`compact`, `realplksr`) as a forgiving safety-net for future variants. Now eligible:

| Newly RealTime-eligible | Reason |
|---|---|
| `nomosuni-compact-x2` | category=video-fast, 2.4 MB |
| `lsdir-compact-x4` | category=video-fast, 2.5 MB |
| `swinir-small-x2` / `swinir-small-x4` | category=video-fast, 8 MB lightweight transformers |
| `bhi-realplksr-x4` | category=video-fast, 2× DAT2 throughput — the v1.6.1.17 "Speed Champion" |

### 3. `docs/MODEL-HOSTING.md` still referenced "v1.6.1.12 catalog"

Doc was 5 releases out of date. Plus: it told users to self-host HAT and APISR, but v1.6.1.17 added `drct-l-x4` (HAT alternative, public ONNX) and `real-cugan-x4` (APISR alternative, public ONNX). Users following the link from the UI didn't see these no-self-host alternatives.

**Fix:** Header updated to "v1.6.1.18, registry size 48". New paragraph at the top recommending `drct-l-x4` over `nomos8k-hat-x4` and `real-cugan-x4` over `apisr-x3` for users who don't want to self-host. Multi-frame VSR (`edvr-m-x4`/`realbasicvsr-x4`/`animesr-v2-x4`) still legitimately need self-hosting — that section unchanged.

---

## Drift-Protection (new)

`UpscalerCoreAutoModelTests.cs` got a new `[Theory]`:

```csharp
[Theory]
[InlineData(true,  1920, 1080)]   // HD batch
[InlineData(false, 1920, 1080)]   // HD realtime
[InlineData(true,  640,  360)]    // low-res batch
[InlineData(false, 640,  360)]    // low-res realtime
public void LiveAction_NonAnime_NeverReturnsKnownUnavailableModel(...)
```

If a future contributor flips `realbasicvsr-x4` to `available: True` without updating `_knownUnavailable` AND a non-anime live-action path somehow reaches the override branch, this test will turn red.

---

## Configuration Changes

None. No new config fields, no defaults changed. v1.6.1.17 saved configs are bit-for-bit forward-compatible.

---

## Test Results

- `dotnet build -c Release` — **0 warnings, 0 errors**
- `dotnet test` — **37/37 tests passing** (was 33, +4 new Live-Action drift-protection tests)
- DLL embedded resource regenerated: 48 models, generated_at refreshed

---

## What this release teaches

The audit pattern that caught these: **when you fix a bug of class X in path A, search for class X in parallel paths B/C/D before shipping.** Verifier-A and Verifier-B in v1.6.1.17 had overlapping but symmetric search-spaces and both missed the live-action twin (which sits literally next to the anime config field in `PluginConfiguration.cs:254-257`). The v1.6.1.18 audit ran an explicit "find-siblings" pass and caught all three.

If you're maintaining a multi-resolver-path system: keep a "siblings checklist" — every new heuristic branch should be reviewed for whether parallel content-types/quality-tiers/hardware-modes need the same treatment.

---

## Files Touched

```
Modified:
  Services/UpscalerCore.cs                                              (PreferredLiveActionModel hook)
  Services/ProcessingStrategySelector.cs                                (fastModels HashSet 9 → 14)
  docs/MODEL-HOSTING.md                                                 (v1.6.1.18 header + DRCT-L recommendation)
  JellyfinUpscalerPlugin.Tests/Services/UpscalerCoreAutoModelTests.cs   (+1 [Theory])
  Resources/models-fallback.json                                        (regenerated, generated_at refreshed)
  + version-bump in: csproj, PluginConfiguration.cs, main.py,
    meta.json, manifest.json, repository-jellyfin.json, README.md,
    Configuration/{html,js}, site/*.html (13 files)

New:
  RELEASE-NOTES-v1.6.1.18.md                                            (this file)
```
