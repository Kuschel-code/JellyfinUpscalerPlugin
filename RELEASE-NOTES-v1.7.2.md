# v1.7.2 - DoS-Hardening + 6 Wishlist Models + Phase-2 Test Coverage Start

**Release date:** 2026-05-11
**Type:** Hardening + content release
**Tests:** 121/121 (was 117, +4 new ProcessingQueueTests)
**Build:** 0 warnings, 0 errors

## DoS-Hardening: 18 properties get Math.Clamp upper-bounds

Pre-v1.7.2 most numeric properties had `Math.Max(value, lower)` lower-clamps but no upper bound. Settings-Import could write arbitrarily large values - a denial-of-service surface. v1.7.2 caps all 18:

| Property | Before | After | Reason |
|---|---|---|---|
| `MaxVRAMUsage` | `Max(0)` | `Clamp(0, 65536)` | 64 GB cap |
| `CpuThreads` | `Max(1)` | `Clamp(1, 256)` | sane |
| `MaxConcurrentStreams` | `Max(1)` | `Clamp(1, 16)` | UI max=8 |
| `MaxCacheAgeDays` | `Max(1)` | `Clamp(1, 3650)` | 10 years |
| `CacheSizeMB` | `Max(0)` | `Clamp(0, 1048576)` | 1 TB |
| `GpuDeviceIndex` | `Max(0)` | `Clamp(0, 64)` | sane |
| `MinResolutionWidth` | `Max(0)` | `Clamp(0, 7680)` | 8K |
| `MinResolutionHeight` | `Max(0)` | `Clamp(0, 4320)` | 8K |
| `MaxItemsPerScan` | `Max(0)` | `Clamp(0, 100000)` | sane |
| `RealtimeTargetFps` | `Max(1)` | `Clamp(1, 120)` | UI max=120 |
| `RealtimeCaptureWidth` | `Max(64)` | **`Clamp(160, 1920)`** | **fix lower-bound drift** (UI min=160) |
| `MaxUpscaledFileSizeMB` | `Max(0L)` | `Clamp(0L, 1099511627776L)` | 1 TB (long) |
| `MaxQueueSize` | `Max(1)` | `Clamp(1, 10000)` | UI max=10000 |
| `ModelDiskQuotaMB` | `Max(0)` | `Clamp(0, 1048576)` | 1 TB |
| `ModelCleanupDays` | `Max(1)` | `Clamp(1, 3650)` | 10 years |
| `HealthCheckIntervalSeconds` | `Max(5)` | **`Clamp(10, 3600)`** | **fix lower-bound drift** (UI min=10) |
| `CircuitBreakerThreshold` | `Max(1)` | `Clamp(1, 1000)` | sane |
| `CircuitBreakerResetSeconds` | `Max(1)` | **`Clamp(10, 3600)`** | **fix lower-bound drift** (UI min=10) |

**3 lower-bound drift fixes:** RealtimeCaptureWidth, HealthCheckIntervalSeconds, CircuitBreakerResetSeconds - all had C#-clamp lower than UI-min, so Settings-Import could write below the UI minimum.

## 6 wishlist models added

| Model | Category | Why |
|---|---|---|
| **MAN x2/x4** | nextgen | Multi-scale Attention Network (ICME 2023). Lightweight transformer, ~8MB. |
| **CRAFT x2/x4** | nextgen | Compositional Refinement (2023). Texture-aware, ~12MB. |
| **GPEN-512** | face_restore | Face-restoration alternative to GFPGAN/CodeFormer. ~280MB. |
| **NAFNet-denoise** | film-restore | Non-AI baseline denoising pre-pass. ECCV 2022. ~17MB. |

Catalog: 53 -> **59 models**.

## Phase-2 test coverage: ProcessingQueue regression guards (NEW)

4 new tests in `ProcessingQueueTests.cs`:
- `Enqueue_TriggersPersistWithin1Second` - debounce + async-write actually fires
- `MultipleEnqueues_WithinDebounceWindow_CoalesceIntoOneFinalWrite` - burst-of-5 coalesces correctly
- `Enqueue_DoesNotBlockOnDiskIO_WhenPersistPathIsSet` - regression-guard, asserts <100ms return
- `RequestPersist_AfterCancel_IsBenign` - defensive check

Uses reflection to inject `_persistPath` field (Plugin.Instance is null in unit tests).

## Polish-Closure

- **`ProcessingStatus.Analyzing` enum value removed** - was never assigned, only referenced once in `ProcessingStrategySelector.cs:231` as unreachable arm. Both sites cleaned.
- **`CleanupOldEntriesAsync(CancellationToken ct = default)`** - cancellation now flows through to `_cleanupLock.WaitAsync(ct)`. Backward-compat via default param.

## What this release deliberately defers to v1.7.3

3 of 4 Phase-2 mocked-async test files: `LibraryUpscaleScanTaskTests`, `VideoFrameProcessorTests`, `ProcessingMethodExecutorTests`. Each needs significant interface-extraction (`IUpscalerCore`, `IUserManagerAdapter`) or process-mocking infrastructure - not justified for a single test each. v1.7.3 will lift those interfaces first, then write the tests against them.

## Files touched

### New (2 files)
- `JellyfinUpscalerPlugin.Tests/Services/ProcessingQueueTests.cs` - 4 tests
- `RELEASE-NOTES-v1.7.2.md` - this file

### Modified
- `PluginConfiguration.cs` - 18 Math.Max -> Math.Clamp upgrades
- `Models/UpscalerModels.cs:290-298` - `ProcessingStatus.Analyzing` removed
- `Services/ProcessingStrategySelector.cs:231` - unreachable arm removed
- `Services/CacheManager.cs:444` - `CleanupOldEntriesAsync(CancellationToken ct = default)`
- `docker-ai-service/app/main.py` - 6 new wishlist model entries
- `Resources/models-fallback.json` - same 6 models, total 53 -> 59
- `JellyfinUpscalerPlugin.csproj`, `meta.json`, `PluginConfiguration.cs:538` - version 1.7.1.0 -> 1.7.2.0
- `manifest.json`, `repository-jellyfin.json` - new v1.7.2.0 entry

## Roadmap

- **v1.7.3**: Remaining Phase-2 test coverage (LibraryUpscaleScanTask, VideoFrameProcessor, ProcessingMethodExecutor) after `IUpscalerCore` + `IUserManagerAdapter` interface extraction
- **v1.8.0**: Pipeline parallelization (`Channel<T>`-based: extract / inference / encode concurrent)
- **v2.0.0**: Multi-Frame VSR (EDVR / RealBasicVSR temporal context) in realtime
