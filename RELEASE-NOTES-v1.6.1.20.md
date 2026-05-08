# v1.6.1.20 — Adoption Completion + Cancellation + Async-IO

**Released:** 2026-05-08
**Plugin ABI:** 10.11.8.0 (unchanged)
**Catalog size:** 48 models (unchanged from v1.6.1.19)

This is a **follow-up patch** to v1.6.1.19 catching the gaps that the v1.6.1.19 post-release self-audit found. The v1.6.1.19 refactor extracted the `ModelAvailability` static class but **did not adopt it everywhere**. v1.6.1.20 closes the adoption gap and adds 3 new bug-class fixes (Cancellation, Sync-IO, csproj Self-Reference) that surfaced during the deep-scan.

---

## Adoption Completion (Drift-Klasse)

### 1. `HardwareBenchmarkService.cs:123` Null-Coalescing was bypassing `EnsureModelAvailable`

```csharp
// Before:
RecommendedModel = status.CurrentModel ?? "realesrgan-x4",

// After:
RecommendedModel = EnsureModelAvailable(status.CurrentModel ?? "realesrgan-x4"),
```

The other 7 hardcoded model IDs in the file already routed through `EnsureModelAvailable` since v1.6.1.19 — this one was missed. If the Docker service reports a self-host model (e.g. `realbasicvsr-x4`) as `current_model`, it now falls back to `realesrgan-x4` instead of being silently propagated.

### 2. `UpscalerCore.cs:386-433` Single-Frame returns now route through `PickAvailable`

7 single-frame returns in `ResolveModelForVideo` previously emitted IDs directly:
```csharp
return "realesrgan-animevideo-x4";  // anime + batch
return "anime-compact-x4";          // anime + realtime
return "span-x2";                   // non-anime low-res realtime
return "nomosuni-compact-x2";       // non-anime HD realtime
return "ultrasharp-v2-x4";          // non-anime very-low-res batch
return "realesrgan-x4";             // non-anime low-res batch
return "realesrgan-x4";             // non-anime HD batch (default)
```

All 7 now wrap through `PickAvailable("preferred", "fallback1", "fallback2")`. **Today no behavior change** (none of these 7 are in `KnownUnavailable`), but if a future maintainer flips e.g. `nomosuni-compact-x2` to self-host, the resolver gracefully falls back instead of returning a 500-prone ID. Symmetric to the Multi-Frame paths that have been gated since v1.6.1.17.

---

## NEW Bug Classes

### 3. Missing CancellationToken (9 HttpClient calls fixed)

`Controllers/UpscalerController.cs` had 9 `GetAsync`/`PostAsync` calls without `HttpContext.RequestAborted`. When the Jellyfin client disconnects (settings page closed, network glitch), the server-side call hung until the default 120s HttpClient timeout. Added the parameter to:

```
L1514: GetAsync($"{serviceUrl}/gpus", HttpContext.RequestAborted)
L1594: PostAsync($"{serviceUrl}/models/load", formContent, HttpContext.RequestAborted)
L1616: GetAsync($"{serviceUrl}/benchmark", HttpContext.RequestAborted)
L1651: PostAsync($"{serviceUrl}/face-restore/load", form, HttpContext.RequestAborted)
L1673: GetAsync($"{serviceUrl}/face-restore/status", HttpContext.RequestAborted)
L1695: PostAsync($"{serviceUrl}/face-restore/unload", null, HttpContext.RequestAborted)
L1717: GetAsync($"{serviceUrl}/metrics", HttpContext.RequestAborted)
L1739: GetAsync($"{serviceUrl}/gpu-verify", HttpContext.RequestAborted)
L1761: GetAsync($"{serviceUrl}/health/detailed", HttpContext.RequestAborted)
```

Connection-pool efficiency improves significantly when many users are zappling between settings tabs.

### 4. `CacheManager.cs:307` synchronous `File.Copy` → async streaming

```csharp
// Before (sync, blocks thread-pool thread):
File.Copy(outputPath, cacheFilePath, true);

// After (real async I/O):
await using (var src = new FileStream(outputPath, FileMode.Open, FileAccess.Read,
                                       FileShare.Read, bufferSize: 81920, useAsync: true))
await using (var dst = new FileStream(cacheFilePath, FileMode.Create, FileAccess.Write,
                                       FileShare.None, bufferSize: 81920, useAsync: true))
{
    await src.CopyToAsync(dst);
}
```

Frame outputs can be tens of MB. On a network-mounted disk (NAS) the sync call was blocking the thread-pool thread for 5-30s per file. `useAsync: true` enables real overlapped IO on Windows / aio on Linux — releases the thread back to the pool while disk I/O is in flight.

### 5. csproj-Comments without explicit version-strings

Two comments in `JellyfinUpscalerPlugin.csproj` were referencing `v1.6.1.19` as a string — meaning the next pauschal version-bump regex (`s/1.6.1.19/1.6.1.20/g`) would have falsely re-attributed v1.6.1.19 features to v1.6.1.20. Same drift class as the homepage card v1.6.1.19 just fixed. Comments are now timeless (no version-string).

---

## Drift-Protection (new tests)

`UpscalerCoreAutoModelTests.cs` got a new `[Theory]` `SingleFramePaths_AlwaysRouteThroughPickAvailable` with 7 InlineData cases covering every single-frame branch. If a future maintainer reverts a `PickAvailable(...)` to a bare `return "some-model";` and that model lands in `KnownUnavailable`, this test goes red.

Tests grew **65 → 72 passing** (+7 single-frame-adoption guards).

---

## Configuration Changes

None. No new config fields, no defaults changed. v1.6.1.19 saved configs are bit-for-bit forward-compatible.

---

## Test Results

- `dotnet build -c Release` — **0 warnings, 0 errors**
- `dotnet test` — **72/72 passing** (was 65, +7 new SingleFramePaths theory cases)

---

## Files Touched

```
Modified (substantive):
  Services/HardwareBenchmarkService.cs      (Befund 1: status.CurrentModel through EnsureModelAvailable)
  Services/UpscalerCore.cs                  (Befund 2: 7 single-frame returns through PickAvailable)
  Controllers/UpscalerController.cs         (Befund 3: 9× HttpContext.RequestAborted)
  Services/CacheManager.cs                  (Befund 4: async streaming File.Copy)
  JellyfinUpscalerPlugin.csproj             (Befund 5: timeless comments)
  JellyfinUpscalerPlugin.Tests/Services/UpscalerCoreAutoModelTests.cs (+1 [Theory] +7 cases)
  Resources/models-fallback.json            (regenerated, generated_at refreshed)

Modified (version-bump):
  PluginConfiguration.cs, docker-ai-service/app/main.py, meta.json,
  manifest.json, repository-jellyfin.json, README.md,
  Configuration/{html,js}, site/*.html (13 files)

New:
  RELEASE-NOTES-v1.6.1.20.md                (this file)
```

---

## Drift-Trajectory (5 releases)

| Release | Bug class | Fix |
|---|---|---|
| v1.6.1.16 | 4 drift bugs unentdeckt | — |
| v1.6.1.17 | Drift in 4 model-list locations + Auto-Mode | 4 point-fixes + first regression theory |
| v1.6.1.18 | 3 sibling bugs missed by v17 verifiers | 3 point-fixes + +4 tests |
| v1.6.1.19 | 3 more siblings missed by v18 verifiers | **Structural** — 1 source-of-truth class + +28 tests |
| **v1.6.1.20** | **2 adoption gaps + 3 new bug classes** | **5 fixes + +7 tests** (this release) |

**Erkenntnis:** Strukturelle Refactors (v1.6.1.19) brauchen einen Adoption-Audit-Pass. Was ein Pattern *kann* ist nicht was es *tut* — der Static-Class-Approach erlaubte Bypässe (Null-Coalescing, bare returns). Mittel-Term (v1.7.0): `IModelAvailabilityValidator` Interface mit DI-Injection würde Adoption *erzwingen* statt nur ermöglichen.
