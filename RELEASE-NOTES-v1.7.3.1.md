# v1.7.3.1 - Hotfix + Interface-Extraction + Adapter Test Coverage

**Release date:** 2026-05-12
**Type:** Hotfix + audit-roadmap continuation
**Tests:** 123/123 (was 121, +2 new UserManagerAdapterTests)
**Build:** 0 warnings, 0 errors

## Hotfix

External v1.7.3 audit caught a **release-note-vs-code inconsistency**: the v1.7.3 release notes announced deletion of `GET /Upscaler/js/{name}` endpoint, but a batch-edit interrupt during the v1.7.3 release process left the endpoint intact in `UpscalerController.cs:261`. v1.7.3.1 actually deletes it.

User-impact: zero (0 callers across the codebase). But release-notes-vs-code consistency is its own bug class - audit caught it, this release fixes it.

## Phase D - Interface Extraction (audit roadmap)

The v1.7.2 audit identified that **77% of Service-LoC had no test coverage** because v1.7.0 async-pattern fixes lived inside services with tight coupling to Jellyfin APIs. Two test seams extracted:

### NEW `IUpscalerCore`

Minimal interface over `UpscalerCore` exposing only the methods `VideoFrameProcessor` consumes (`UpscaleImageAsync` + `DetectHardwareAsync`). Other consumers of `UpscalerCore` keep using the concrete class. DI uses the `sp.GetRequiredService<UpscalerCore>()` factory pattern so there's exactly one instance shared.

### NEW `IUserManagerAdapter`

Wraps Jellyfin's `IUserManager` + `IUserDataManager` behind a single `IsAnyUserPlayed(BaseItem)` method. Fail-open semantics (DB exception -> return false -> treat as unwatched) lives in the adapter now. `LibraryUpscaleScanTask` previously had this logic inline as a private method; now it's a 1-line caller.

## Phase E - Test Coverage (partial)

### NEW `UserManagerAdapterTests.cs` (+2 tests)

Critical regression-guards on the fail-open contract introduced inline in v1.6.1.21:

- `IsAnyUserPlayed_ReturnsFalse_WhenItemIsNull` - defensive null-handling
- `IsAnyUserPlayed_ReturnsFalse_WhenUsersEnumerableThrows` - **THE fail-open guard**

If a future refactor flips the contract to fail-closed, test #2 fails loudly.

Scope-limited: PlayCount/Played-flag scenarios need to construct real `Jellyfin.Data.Entities.User` instances - requires additional package ref not in test project yet. The fail-open guard - the most critical contract - is fully covered. Remaining scenarios deferred to v1.7.4.

## Polish

- `docs/MODEL-HOSTING.md` annotation updated: `v1.6.1.18, registry size 48` -> `v1.7.3.1, registry size 59`.

## Files touched

### New
- `Services/IUpscalerCore.cs`
- `Services/IUserManagerAdapter.cs`
- `Services/UserManagerAdapter.cs`
- `JellyfinUpscalerPlugin.Tests/Services/UserManagerAdapterTests.cs`
- `RELEASE-NOTES-v1.7.3.1.md`

### Modified
- `Controllers/UpscalerController.cs` - removed `GetJavaScript` endpoint (audit-caught hotfix)
- `Services/UpscalerCore.cs` - implements `IUpscalerCore`
- `Services/VideoFrameProcessor.cs` - depends on `IUpscalerCore`
- `ScheduledTasks/LibraryUpscaleScanTask.cs` - uses `IUserManagerAdapter`, private `IsAnyUserPlayed` removed
- `PluginServiceRegistrator.cs` - registers both interfaces
- `docs/MODEL-HOSTING.md` - registry-size annotation 48 -> 59
- `site/*.html` (14 files) - brand-version sync v1.7.3 -> v1.7.3.1
- `JellyfinUpscalerPlugin.csproj`, `meta.json`, `PluginConfiguration.cs` - version 1.7.3.0 -> 1.7.3.1
- `manifest.json`, `repository-jellyfin.json` - new v1.7.3.1 entry

Saved v1.7.x configs are bit-for-bit compatible.

## Roadmap

- **v1.7.4**: Complete Phase E - add Jellyfin.Data package ref, add PlayCount/Played-flag tests, write `VideoFrameProcessorTests` (CT propagation via mocked IUpscalerCore) + `ProcessingMethodExecutorTests` (process-mock for linked-CTS).
- **v1.8.0**: Pipeline-Parallelization (`Channel<T>`-based concurrent extract/inference/encode).
- **v2.0.0**: Multi-Frame VSR (EDVR / RealBasicVSR temporal context) in realtime.
