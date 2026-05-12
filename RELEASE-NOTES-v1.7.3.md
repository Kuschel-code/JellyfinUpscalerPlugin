# v1.7.3 - Dead-Code Cleanup + Site-Doku-Sync + CI Drift-Prevention

**Release date:** 2026-05-12
**Type:** Hygiene release (no functional changes)
**Tests:** 121/121 (unchanged from v1.7.2)
**Build:** 0 warnings, 0 errors

## What changed

Closes the Code-Hygiene gaps that the v1.7.2 Full-Full-Scan audit uncovered.
Pure cleanup release - no new features, no behavior changes, all v1.7.x configs bit-compatible.

### Phase A: Dead-Code Removal

**3 unused Service-Classes deleted** (0 references, 0 DI-registrations):

| File | LoC | Why dead |
|---|---|---|
| `Services/TranscodingProfileManager.cs` | 63 | Public method `BuildUpscaleArguments` had 0 callers |
| `Services/UpscalerTranscodingManager.cs` | 187 | Contained class `UpscalerTranscodingHelper` (filename-drift). XML-doc disclosed it as architecturally obsolete since Jellyfin 10.10 dropped `ITranscoderFullCommandModifier`. |
| `Services/JellyfinConfigHelper.cs` | 264 | Replaced by `FFmpegWrapperService` in a past refactor, the old helper class was orphaned |

**1 unused Model-Type deleted**: `UpscalerSettings` (settings-patch-DTO that the actual import path bypasses - uses JsonElement + TryApply directly).

The audit also suggested `CPUInfo` + `MemoryInfo` - kept because they're transitively reachable via `BenchmarkResults.CPUInfo/MemoryInfo` properties.

**2 dead REST endpoints deleted from `UpscalerController.cs`**:
- `GET /Upscaler/js/{name}` - 0 callers. All JS-files load via Jellyfin's `/web/configurationpage?name=UPSCALERXyz` mechanism instead.
- `POST /Upscaler/cache/config` - wrote `EnablePreProcessingCache` which is a Ghost-Property (removed from UI in v22, 0 service consumers).

### Phase B: Site-Doku-Sync

Pre-v1.7.3 the audit caught:
- `site/models.html` listed **48 of 59** models - all 11 v1.7.1+v1.7.2 additions missing.
- **14 `site/*.html` pages** showed wildly inconsistent brand-versions: 12x v1.6.1.21, 1x v1.6.1.17, 1x v1.7.0.

**Fixes:**
- `site/models.html` extended with all 11 missing models (OmniSR x2/x4, DAT-light x2/x4, MAN x2/x4, CRAFT x2/x4, RestoreFormer++, GPEN-512, NAFNet-denoise).
- NEW `Scripts/sync-site-topbar-versions.ps1` - reads `.version` from `meta.json` and rewrites every brand-version span. Idempotent.
- Ran once: 14 of 14 pages now on v1.7.3.

### Phase C: CI Drift-Prevention

New `verify-site-sync` job in `.github/workflows/v1.7.1-audit-checks.yml`:

```yaml
verify-site-sync:
  steps:
    - pwsh Scripts/sync-site-topbar-versions.ps1
    - git diff --exit-code site/
```

Fails the build at PR time if brand-version drifted. Same pattern as the existing `verify-fallback-sync` + `zip-version-check`.

## What this release deliberately defers to v1.7.4

The audit also recommended Phase D (Interface-Extraction: `IUpscalerCore`, `IUserManagerAdapter`) and Phase E (3 mocked-async tests). Both require substantial refactor across multiple services and deserve their own test-iteration. v1.7.3 stays a pure hygiene release.

## Files touched

### New (3 files)
- `Scripts/sync-site-topbar-versions.ps1`
- `RELEASE-NOTES-v1.7.3.md`
- `.github/workflows/v1.7.1-audit-checks.yml` (extended with verify-site-sync job)

### Deleted (3 files)
- `Services/TranscodingProfileManager.cs`
- `Services/UpscalerTranscodingManager.cs`
- `Services/JellyfinConfigHelper.cs`

### Modified
- `Models/UpscalerModels.cs` - removed `class UpscalerSettings`
- `Controllers/UpscalerController.cs` - removed `GetJavaScript` + `ConfigurePreProcessingCache` endpoints
- `site/models.html` - 11 missing models added, header 48 -> 59
- `site/*.html` (14 files) - brand-version unified to v1.7.3
- `JellyfinUpscalerPlugin.csproj`, `meta.json`, `PluginConfiguration.cs` - version 1.7.2.0 -> 1.7.3.0
- `manifest.json`, `repository-jellyfin.json` - new v1.7.3.0 entry

## Roadmap

- **v1.7.4**: Phase D+E - `IUpscalerCore` + `IUserManagerAdapter` interface extraction + 3 mocked-async tests
- **v1.8.0**: Pipeline-Parallelization (`Channel<T>`-based: frame-extract / inference / encode concurrent)
- **v2.0.0**: Multi-Frame VSR (EDVR / RealBasicVSR) in realtime
