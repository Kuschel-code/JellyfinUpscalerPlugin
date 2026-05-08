# v1.6.1.19 — Single-Source-of-Truth for Model Availability

**Released:** 2026-05-08
**Plugin ABI:** 10.11.8.0 (unchanged)
**Catalog size:** 48 models (unchanged from v1.6.1.18)

This is a **structural fix release** for the drift class that v1.6.1.17 and v1.6.1.18 patched point-by-point. Each prior audit found one more place where hardcoded model IDs lived without a `KnownUnavailable`-check. v1.6.1.19 collapses 3+ such places into one source-of-truth — the new `Services/ModelAvailability` static class.

---

## What changed

### 1. `Services/ModelAvailability` — new static class

Extracted from the v1.6.1.17 `UpscalerCore._knownUnavailable` HashSet:

```csharp
internal static class ModelAvailability
{
    public static readonly HashSet<string> KnownUnavailable = new(StringComparer.OrdinalIgnoreCase)
    {
        "nomos8k-hat-x4", "apisr-x3",
        "edvr-m-x4", "realbasicvsr-x4", "animesr-v2-x4"
    };

    public static bool IsKnownUnavailable(string? modelId);
    public static string PickAvailable(string preferred, params string[] fallbacks);
}
```

Now used by **both** resolver classes that previously duplicated (or worse, omitted) this list.

### 2. `UpscalerCore` refactored

- Removed: `_knownUnavailable` private HashSet (was duplicated logic)
- Removed: bespoke `PickAvailable()` body (was duplicated logic)
- Kept: `private string PickAvailable(...)` instance wrapper that adds `_logger` telemetry on top of `ModelAvailability.PickAvailable()`. Pure logic in static class; logging stays at the call site where `ILogger<UpscalerCore>` is available.
- All multi-frame and single-frame fallback chains in `ResolveModelForVideo` continue to call the wrapper — call-sites unchanged.

### 3. `HardwareBenchmarkService` hardened (P2a from audit)

`CalculateOptimalSettings()` had 6 hardcoded `RecommendedModel`/`FallbackModel` assignments + 1 in `CreateDefaultHardwareProfile()`. None of them consulted `KnownUnavailable`. Today both IDs (`realesrgan-x4`, `fsrcnn-x2`) are always-available so this was a **latent** drift bug, not an active one.

New `private string EnsureModelAvailable(string preferred)` helper wraps each assignment. If a future change ever flips one of these to `available: False` upstream, the helper logs a Warning and falls back to `realesrgan-x4` — no more silent recommendation of an unreachable model.

### 4. Face-Restore dropdown auto-populated (P2b from audit)

`Configuration/configurationpage.html` had `<option value="gfpgan-v1.4">` and `<option value="codeformer">` hardcoded. Hauptmodell-Dropdown went auto-populated in v1.6.1.17, anime/live-action dropdowns in v1.6.1.18 — Face-Restore was overlooked.

New `populateFaceRestoreDropdown()` JS helper wired into `loadModels()`. Pulls `category="face_restore"` entries from the live `/Upscaler/models` response, preserves the saved value, falls back to the hardcoded options if the registry returns 0 face-restore entries (defensive).

### 5. Homepage card content fix (P1 from audit)

The v1.6.1.18 release card on `site/index.html` showed the v1.6.1.16 FFmpeg-fix description because the v17/v18 version-bump scripts ran `s/1.6.1.16/1.6.1.18/g` and bumped the `<h3>` title without rewriting the body. Card now shows the correct v1.6.1.19 release content.

**Documented limitation:** The pauschal version-replace pattern is a recurring drift source. Future improvement (out of v1.6.1.19 scope): replace with `Scripts/sync-homepage-card.ps1` that autogenerates the card from `RELEASE-NOTES-v<version>.md`. Until then: avoid version-references inside newly-written copy that gets fed through the bump regex.

---

## Drift-Protection (new tests)

`JellyfinUpscalerPlugin.Tests/Services/ModelAvailabilityTests.cs` (new file, +14 test methods, +28 with InlineData expansion):

- 5x `[InlineData]` — every self-host ID returns true from `IsKnownUnavailable`
- 10x `[InlineData]` — every available ID returns false from `IsKnownUnavailable`
- 3x `[InlineData]` — null/empty/whitespace inputs return false
- 3x `[InlineData]` — case-insensitive match (saved configs sometimes mismatch casing)
- 6x `[Fact]` — `PickAvailable` semantics (preferred-when-available, fall-through, multi-stage, last-resort, null-skip)
- 2x `[Fact]` — drift-lock: `KnownUnavailable.Should().HaveCount(5)` + `Should().NotContain("realesrgan-x4")`

If a future contributor adds a 6th entry to `KnownUnavailable`, the count test turns red until they update the test. Intentional friction, ensures the list is reviewed.

`JellyfinUpscalerPlugin.csproj` got a new `<InternalsVisibleTo Include="JellyfinUpscalerPlugin.Tests" />` so the test assembly can directly reference `internal class ModelAvailability` without reflection or making the API public.

---

## Configuration Changes

None. No new config fields, no defaults changed. v1.6.1.18 saved configs are bit-for-bit forward-compatible.

---

## Test Results

- `dotnet build -c Release` — **0 warnings, 0 errors**
- `dotnet test` — **65/65 passing** (was 37, +28 new ModelAvailability drift-protection tests)

---

## Files Touched

```
New:
  Services/ModelAvailability.cs                                         (single source of truth)
  JellyfinUpscalerPlugin.Tests/Services/ModelAvailabilityTests.cs       (+28 drift-protection tests)
  RELEASE-NOTES-v1.6.1.19.md                                            (this file)

Modified (substantive):
  Services/UpscalerCore.cs                                              (refactor: use ModelAvailability)
  Services/HardwareBenchmarkService.cs                                  (P2a: EnsureModelAvailable wrapper)
  Configuration/configurationpage.html                                  (P2b: populateFaceRestoreDropdown)
  site/index.html                                                       (P1: card content corrected)
  JellyfinUpscalerPlugin.csproj                                         (InternalsVisibleTo for tests)

Modified (version-bump):
  PluginConfiguration.cs, docker-ai-service/app/main.py, meta.json,
  manifest.json, repository-jellyfin.json, README.md,
  Configuration/{html,js}, site/*.html (13 files)
```

---

## Audit-Driven Release Cadence

| Release | Bug class | Fix scope |
|---|---|---|
| v1.6.1.17 | Drift in 4 model-list locations + Auto-Mode multi-frame returning self-host IDs | Point fixes for each + first regression test theory |
| v1.6.1.18 | 3 sibling bugs the v17 verifiers missed (Live-Action twin, fastModels HashSet, MODEL-HOSTING.md) | Point fixes + +4 tests |
| **v1.6.1.19** | **3 more siblings the v18 verifiers missed** (HardwareBenchmark, FaceRestore dropdown, homepage card) | **Structural fix** — 1 source-of-truth class + +28 tests |

If v1.6.1.20 is needed it should be for **non-drift** bugs — the structural fix here closes the model-availability drift class entirely on the C# side. Python-side `AVAILABLE_MODELS` remains its own SoT in `docker-ai-service/app/main.py`.
