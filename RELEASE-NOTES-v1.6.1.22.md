# v1.6.1.22 — UI Honesty Cleanup (No-Op Toggles Removed)

**Release date:** 2026-05-09
**Type:** UI hotfix (zero behavior change, zero new code paths)
**Tests:** 85/85 (unchanged from v21)
**Build:** 0 warnings, 0 errors

## What changed

v1.6.1.21 introduced *honest disclosure* in code comments for 6 no-op config toggles. v1.6.1.22 follows through on the same principle in the UI: **30 dead-backend config controls are removed from `Configuration/configurationpage.html`**. The user no longer sees toggles, sliders, and inputs that had no effect.

## How v22 was scoped — triple-pass verification

Previous releases applied focused fixes to the audit findings they were given. v22 used a complete property-by-property triple-pass over **all 88 properties** in `PluginConfiguration.cs` (the v21 scan only found 54 because the regex missed property-bodies like `{ get => _x; set => _x = Math.Clamp(...); }`):

| Pass | Method | Purpose |
|---|---|---|
| 1 | Multi-line aware regex over `PluginConfiguration.cs` | Enumerate ALL 88 properties (was: 54) |
| 2 | Per-property: count consumers in `Services/`, `ScheduledTasks/`, `Controllers/`, `Plugin.cs`, `Configuration/*.{js,html}`, `docker-ai-service/app/main.py` | Categorize as LIVE / DEAD-CTRL-ONLY / PURE-ORPHAN / UI-ONLY |
| 3 | Line-level inspection of every DEAD-CTRL-ONLY with js>=1 | Distinguish real JS consumers (player-integration.js) from pure save/load array entries |

**Result:** 30 confirmed dead-backend properties (was: 18 from v21 audit + 2 my own = 20).

## What's removed from UI

### Whole sections deleted (Section + all children dead)

| Section | Removed Fields |
|---|---|
| Quality Metrics (`<details>`) | `EnableQualityMetrics` |
| Face Enhancement (`<details>`) | `EnableFaceEnhancement`, `FaceEnhanceStrength` |
| Film Grain Management (`<details>`) | `EnableGrainManagement`, `GrainDenoiseStrength`, `GrainReaddIntensity` |
| Health & Circuit Breaker (`<details>`) | `HealthCheckIntervalSeconds`, `CircuitBreakerThreshold`, `CircuitBreakerResetSeconds` |
| Model Management (`<details>`) | `ModelDiskQuotaMB`, `ModelCleanupDays` (toggles already removed in v21 disclosure) |

### Individual fields removed from mixed-live sections

| Field | Reason |
|---|---|
| `PlayerButton`, `Notifications`, `AutoRetryButton` | Player section: kept `ButtonPosition` (live in player-integration.js) |
| `EnableProcessingQueue` | kept `MaxQueueSize`, `PauseQueueDuringPlayback`, `PersistQueueAcrossRestarts` (live) |
| `EnableProgressNotifications` | kept `WebhookUrl`, `WebhookOnComplete`, `WebhookOnFailure` (live) |
| `EnableModelPreloading`, `EnableModelAutoCleanup` | (toggles for the now-deleted Model Management subsection) |
| `EnableHealthMonitoring`, `EnableGpuFallbackToCpu` | (toggles for the now-deleted Health subsection) |
| `EnableComparisonView`, `EnableCustomModelUpload`, `EnableApiDocs` | Features section: kept `EnablePerformanceMetrics`, `EnableAutoBenchmarking` (live) |
| `EnablePreProcessingCache` | Cache section: kept `MaxCacheAgeDays`, `CacheSizeMB` (live in CacheManager.cs) |
| `MaxVRAMUsage`, `CpuThreads` | Hardware section: kept `HardwareAcceleration`, `MaxConcurrentStreams` (live) |
| `MaxUpscaledFileSizeMB` | Library Scan section: kept `MinResolution*`, `MaxItemsPerScan` (live) |
| `RealtimeTargetFps` | Real-Time section: kept `RealtimeMode`, `RealtimeCaptureWidth`, `EnableRealtimeUpscaling` (live in player-integration.js) |
| `FaceRestoreMaxPerFrame`, `FaceRestoreMaxWidth` | Face Restore card: kept `EnableFaceRestore`, `FaceRestoreModel`, all buttons (live - JS sends to /face-restore/load REST API) |

### JS arrays cleaned in lockstep

The `loadConfig` and `saveConfig` arrays (`fields`, `nums`, `floats`, `checks`, `sliderMap`, `longs`) are pruned of the same 30 names. Three orphaned slider listeners (`#FaceRestoreMaxPerFrame`, `#FaceRestoreMaxWidth`, plus the `longs` array which only contained `MaxUpscaledFileSizeMB`) are removed.

## What's preserved (deliberately)

- **Properties remain** in `PluginConfiguration.cs` - saved user configs continue to load without crash. Removed inputs simply mean the values stay at their last-saved (or default) state forever, which has no observable effect since no code reads them.
- **Controller Save/Load (`TryApply`)** for the 30 fields stays - backwards-compatible with any external client (e.g. scripts) that still sends these in a settings PATCH.
- **`EnableFaceRestore` + `FaceRestoreModel`** stay in UI - they have C# consumers = 0 but are JS-live: the in-page Face Restoration buttons read these values and send them to `/face-restore/load` REST endpoint. The C# pipeline doesn't consume them, but the UI-direct-to-REST path does.
- **`ButtonPosition`, `EnableRealtimeUpscaling`, `RealtimeMode`, `RealtimeCaptureWidth`** stay - `Configuration/player-integration.js` reads them in player-overlay code (false-positive in the dead-config algo's first pass; verified live in pass 3).

## Numbers

| Metric | v21 | v22 |
|---|---|---|
| `configurationpage.html` lines | 2807 | 2680 (-127, -4.5%) |
| `<details>` sections | 14 | 9 (5 entirely dead - removed) |
| Dead UI inputs | 30 | 0 |
| Live properties surfaced in UI | 50 | 50 |
| dotnet test | 85/85 | 85/85 |
| Build warnings | 0 | 0 |

## Compat note

If a user's saved config XML contains values for the 30 removed fields, those values **persist** on disk - no migration is performed, no values are lost. They simply won't be edited via the settings page anymore. v1.7.0 may either delete those properties (one-time migration) or actually wire them to a working pipeline. This release does neither - pure UI cleanup.

## Discovery methodology footnote

This v22 audit's bash one-liner serves as a continuous safeguard against the dead-config drift class - run before every future release to catch new dead-backend toggles before they ship.
