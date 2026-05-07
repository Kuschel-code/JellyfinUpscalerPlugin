# v1.6.1.17 — Auto-Mode Fix + Catalog Sync + 5 New Models

**Released:** 2026-05-07
**Plugin ABI:** 10.11.8.0 (unchanged)
**Plugin DLL size:** 1.41 MB (down from 1.60 MB — ModelManager dead-code removed)

---

## Critical Fixes

### 1. `ResolveModelForVideo` Multi-Frame Auto-Mode broken

`UpscalerCore.ResolveModelForVideo()` returned `animesr-v2-x4` / `realbasicvsr-x4` / `edvr-m-x4`
unconditionally for multi-frame batch jobs — but **all three are `available: False`** upstream
(no public ONNX mirror). Users with `EnableAutoModelSelection=true` on services that report
`inputFrames > 1` got 500 errors with no clear indication that self-hosting was required.

**Fix:** New `PickAvailable(preferred, ...fallbacks)` helper in `UpscalerCore.cs` consults a
`_knownUnavailable` HashSet and walks the fallback chain. Multi-frame paths now resolve to:

- Anime + multi-frame  → `animesr-v2-x4` → **`realesrgan-animevideo-x4`** → `anime-compact-x4`
- VeryLowRes + multi-frame → `realbasicvsr-x4` → **`ultrasharp-v2-x4`** → `realesrgan-x4`
- General + multi-frame → `edvr-m-x4` → **`ultrasharp-v2-x4`** → `nomos2-realplksr-x4` → `realesrgan-x4`

Existing single-frame paths were already correct and remain unchanged.

### 2. Anime default model fixed (config + auto-resolver hook)

Two-part fix — the default change alone was insufficient because the config field was dead:

**a)** `PluginConfiguration.PreferredAnimeModel` default changed from `""` to `anime-compact-x4`
(5 MB, `available: true`). Existing user configs are NOT overwritten — Jellyfin's deserializer
respects saved values.

**b)** `UpscalerCore.ResolveModelForVideo()` single-frame anime path now actually **reads**
`Config.PreferredAnimeModel` and routes through `PickAvailable()` with a fallback chain
(`override → realesrgan-animevideo-x4 → anime-compact-x4`). Previously the field was set in
the config but ignored by the resolver — the new default had no effect on auto-mode behavior.
Caught by Verifier-B during pre-release review.

### 3. Controller fallback list 12 → 43 models

`Controllers/UpscalerController.cs:178` had a hardcoded 12-model list used when the Docker
service is unreachable. Drift was 24+ models — UI hid the majority of available models during
brief Docker outages. Now reads from embedded `Resources/models-fallback.json` (auto-generated
from `app/main.py` via `Scripts/sync-fallback-models.ps1`).

### 4. `site/models.html` listed 8 fictional models

Removed: `waifu2x-cunet-x2`, `waifu2x-upconv-x2`, `hat-s-x4`, `hat-m-x4`, `hat-l-x4`,
`swinir-l-x4`, `realesrnet-x4plus`, `realesrgan-x4plus`, `realesrgan-x2plus`,
`realesrgan-anime-x4` — none of these existed in `AVAILABLE_MODELS`. Page now auto-generated
from the registry, lists all 48 models in 12 categories with correct status badges.

---

## Cleanup

### `ModelManager.cs` removed

`Services/ModelManager.cs` (200 LoC) was registered as a DI singleton in `PluginServiceRegistrator`
but **never consumed by any code path** — every HTTP call uses `HttpUpscalerService` directly.
Header still claimed `v1.5.5.4`. File deleted, DI registration removed. DLL size dropped 200 KB.

### Docker service VERSION bumped

`docker-ai-service/app/main.py:VERSION` was still `1.6.1.15` — bump was missed during the
v1.6.1.16 release. Now correctly `1.6.1.17`.

---

## New Models (5)

Catalog grew **43 → 48 models** with SOTA additions across three categories:

| ID | Family | Source | Use-Case |
|---|---|---|---|
| `real-cugan-x2` | cugan | `mayhug/Real-CUGAN` | Anime 2x — cleaner linework than Real-ESRGAN-anime, sharper than waifu2x. ~12MB |
| `real-cugan-x4` | cugan | `mayhug/Real-CUGAN` | Anime 4x — better than `realesrgan-animevideo-x4` for anime line-art |
| `drct-l-x4` | drct | `aaronespasa/drct-super-resolution` | SOTA photo — sharper than DAT2/UltraSharp on real-world photo content |
| `bhi-realplksr-x4` | realplksr | `Phhofm/models` | Speed champion — 2x throughput vs DAT2 at comparable quality (~5fps@256² on RTX 3060) |
| `rife-v4.25` | rife | `yuvraj108c/rife-onnx` | Frame interpolation — current SOTA, better scene-bleeding handling than v4.7-4.9 |

> Note: `nomos8k-hat-x4` and `apisr-x3` remain `available: False`. HAT has known ONNX-export
> issues with window-attention ops on CPU EP; APISR Xenova HF mirror is gated. Use DRCT-L as
> a HAT replacement and self-host APISR-x3 per `docs/MODEL-HOSTING.md` if needed.

---

## Tooling

### `Scripts/sync-fallback-models.ps1`

New PowerShell script that regenerates `Resources/models-fallback.json` from
`docker-ai-service/app/main.py:AVAILABLE_MODELS`. Run after editing the registry:

```powershell
pwsh Scripts/sync-fallback-models.ps1
```

Producing 48-model JSON in ~1 second. Embedded into the DLL via
`<EmbeddedResource>` in `JellyfinUpscalerPlugin.csproj`. Future CI step recommendation:
verify `git diff --exit-code Resources/models-fallback.json` after running the sync script
(blocks PRs that drift the C# fallback from the Python truth).

---

## Configuration Changes

| Property | Before | After | Migration |
|---|---|---|---|
| `PreferredAnimeModel` | `""` | `"anime-compact-x4"` | Saved configs preserved — only fresh installs get the new default. |

No breaking changes. No `targetAbi` change. No Docker image API change.

---

## Test Results

- `dotnet build -c Release` — **0 warnings, 0 errors**
- `dotnet test` — **33/33 tests passing** (+11 new drift-protection tests)
- New: `UpscalerCoreAutoModelTests` locks down all multi-frame fallback chains AND a
  `[Theory]` that asserts `ResolveModelForVideo` never returns a known-unavailable model
  (across 6 input combinations: anime/non-anime × batch/realtime × multi/single-frame).
- DLL embedded resource verified: `JellyfinUpscalerPlugin.Resources.models-fallback.json` present, contains 48-model JSON.
- Plugin DLL: 1.41 MB (down from 1.60 MB)

---

## Files Touched (summary)

```
Modified:
  PluginConfiguration.cs                           (PreferredAnimeModel default)
  PluginServiceRegistrator.cs                      (ModelManager DI removed)
  JellyfinUpscalerPlugin.csproj                    (version + embedded resource)
  Services/UpscalerCore.cs                         (PickAvailable helper + multi-frame fix)
  Controllers/UpscalerController.cs                (fallback now from JSON resource)
  docker-ai-service/app/main.py                    (VERSION + 5 new models)
  manifest.json                                    (v1.6.1.17 entry)
  repository-jellyfin.json                         (v1.6.1.17 entry)
  meta.json                                        (version)
  README.md                                        (version banner)
  Configuration/{configurationpage.html, sidebar-upscaler.js, player-integration.js}
  site/*.html (12 files)                           (header version)
  site/models.html                                 (regenerated from registry)

New:
  Resources/models-fallback.json                                     (auto-generated, 48 models)
  Scripts/sync-fallback-models.ps1                                   (regen tool)
  JellyfinUpscalerPlugin.Tests/Services/UpscalerCoreAutoModelTests.cs  (drift-protection tests)
  RELEASE-NOTES-v1.6.1.17.md                                         (this file)

Deleted:
  Services/ModelManager.cs                         (dead code)
```
