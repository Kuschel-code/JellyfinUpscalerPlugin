# Deep Scan Report v1.5.5.4 — Complete Codebase Audit

**Date**: 2026-03-30
**Scanned by**: 7 parallel specialized agents
**Version**: 1.5.5.4

---

## Executive Summary

| Agent | Findings |
|-------|----------|
| C# Deep Scan | 3 CRITICAL, 7 HIGH, 10 MEDIUM, 8 LOW |
| Docker Python Deep Scan | 3 CRITICAL, 7 HIGH, 12 MEDIUM, 10 LOW |
| Silent Failure Hunter | 3 CRITICAL, 5 HIGH, 11 MEDIUM |
| Type Design Analyzer | Overall 3.8/10 encapsulation |
| Code Reviewer | 7.0/10 quality score, 10 findings |
| Code Simplifier | 11 simplification opportunities |
| Comment Analyzer | 8 critical, 13 improvement, 4 removal candidates |

**Deduplicated Totals**: ~6 CRITICAL, ~12 HIGH, ~25 MEDIUM, ~20 LOW

---

## CRITICAL Issues (Must Fix)

### C1. Unauthenticated ONNX Model Upload = Remote Code Execution
- **Files**: `main.py:4301, 4224, 4406`
- **Source**: Docker Deep Scan + Code Reviewer
- **Issue**: `/models/upload`, `/models/upload-face-enhance`, DELETE `/models/{name}` have NO `_require_api_token()` check. ONNX models can contain custom ops = arbitrary code execution.
- **Fix**: Add `_require_api_token(request)` to all three endpoints.

### C2. FFmpeg Command Injection via File Paths
- **File**: `Services/VideoProcessor.cs:786, 993, 1016, 1251, 1435, 1483`
- **Source**: C# Deep Scan
- **Issue**: File paths interpolated directly into FFmpeg command strings. Specially crafted filenames with quotes could alter command structure.
- **Fix**: Use `Cli.Wrap(...).WithArguments(new[] {...})` array overload.

### C3. Silent Fallback to Non-AI Resize Masks Failures
- **Files**: `Services/UpscalerCore.cs:120-129, 336-355`
- **Source**: Silent Failure Hunter
- **Issue**: When ALL AI models fail, code silently returns cheap Lanczos3 resize. When even that fails, returns ORIGINAL unmodified image. User thinks AI upscaling succeeded.
- **Fix**: Return result type with `usedFallback` flag. Log at ERROR level.

### C4. Webhook Failures Swallowed at DEBUG Level
- **File**: `Services/UpscalerCore.cs:208-229`
- **Source**: Silent Failure Hunter
- **Issue**: All webhook errors caught and logged at DEBUG. Users' monitoring pipelines silently break.
- **Fix**: Log at WARNING minimum. After N consecutive failures, log ERROR.

### C5. Missing Pixel Dimension Validation on Multiple Endpoints
- **Files**: `main.py` — `/upscale-frame:3049`, `/interpolate-frames:3800`, `/upscale-video-chunk:3146`
- **Source**: Docker Deep Scan
- **Issue**: Several endpoints accept images without checking `MAX_INPUT_PIXELS`, allowing OOM via decompression bombs.
- **Fix**: Add pixel dimension check after decode on all endpoints.

### C6. `MAX_INPUT_PIXELS` Shadowed by Different Value
- **File**: `main.py:268 vs 1782`
- **Source**: Code Simplifier
- **Issue**: Global `MAX_INPUT_PIXELS = 16000*16000` but local in `upscale_image()` redefines as `8_294_400`. Confusing and inconsistent.
- **Fix**: Use the global constant everywhere.

---

## HIGH Issues (Should Fix)

### H1. SSRF via DNS Rebinding in Webhooks + Connections
- **Files**: `UpscalerCore.cs:162-203`, `main.py:2746-2799`
- DNS resolves to public IP during check, re-resolves to private IP when used.

### H2. Missing IDisposable on VideoProcessor + ProcessingQueue
- **Files**: `VideoProcessor.cs:30`, `ProcessingQueue.cs:27`
- SemaphoreSlim, timers, CTS instances not properly disposed.

### H3. Race Condition: String Reference Equality for Model Check
- **File**: `HttpUpscalerService.cs:163-165`
- `_currentlyLoadedModel == modelName` uses reference equality, may fail.

### H4. Unbounded Collections (Memory Leaks)
- `_performanceHistory` in VideoProcessor (never trimmed)
- `plugin_connections` in main.py (no limit)
- `_completedJobs` hysteresis already fixed but `_performanceHistory` not

### H5. ONNX Session Leak on GPU Verification Failure
- **File**: `main.py:1538-1614`
- Session created, GPU check fails, session abandoned without `del`.

### H6. Queue Persistence Failures Logged at DEBUG
- **File**: `ProcessingQueue.cs:262-277`
- Disk full/permissions = silent data loss on restart.

### H7. FFmpeg Init Failure: Empty Path, Cryptic Errors
- **File**: `VideoProcessor.cs:101-127`
- Continues with empty `_ffmpegPath`, all jobs fail with confusing errors.

### H8. Cache Init Failure Silently Ignored
- **File**: `CacheManager.cs:78-104`
- Cache directory creation fails → every cache op fails repeatedly.

### H9. CacheSizeMB=0 Means "Always Full" Instead of "Unlimited"
- **File**: `CacheManager.cs:356-359`
- `CheckCacheSizeLimit` returns false (full) when `CacheSizeMB=0`.

### H10. Division by Zero in Cache Stats
- **File**: `CacheManager.cs:517`
- `UsagePercentage` divides by `CacheSizeMB * 1024 * 1024` — zero when unlimited.

### H11. Global ONNX_TILE_SIZE Mutated Without Thread Safety
- **File**: `main.py:2055, 2080-2083`
- Multiple concurrent requests read/write global tile size.

### H12. `/quality-metrics`, `/enhance-faces` Bypass Upscale Semaphore
- **File**: `main.py:3954-3983`
- These endpoints call `upscale_image_array` without acquiring `_upscale_semaphore`.

---

## Type Design Summary

| Dimension | Score |
|-----------|-------|
| Encapsulation | 3.8/10 |
| Invariant Expression | 4.0/10 |
| Invariant Usefulness | 5.5/10 |
| Invariant Enforcement | 3.5/10 |

**Top recommendation**: Convert string-typed enumerations (`QualityLevel`, `RealtimeMode`, `ButtonPosition`, `OutputCodec`) to C# enums.

---

## Code Simplification Opportunities

1. Consolidate 3 duplicated blend-weight functions (main.py)
2. Extract exception handling into ASP.NET exception filter (Controller)
3. Eliminate URL validation duplication (Controller + HttpUpscalerService)
4. Merge `upscale_image()` dispatch into `upscale_image_array()` (main.py)
5. Consolidate Pause/Resume/Cancel into single helper (Controller)
6. Extract shared model-finalization logic (main.py)
7. Remove/reduce duplicated model dictionary (ModelManager.cs)

---

## Comment Issues (Stale/Incorrect)

| File | Issue |
|------|-------|
| Plugin.cs:14 | Version says v1.5.4.3, should be v1.5.5.4 |
| HttpUpscalerService.cs:15,59 | Version says v1.5.2.9 |
| ModelManager.cs:13,127 | Version says v1.5.2 |
| HardwareBenchmarkService.cs:18,42 | Version says v1.4.9.5 |
| CacheManager.cs:21 | "Phase 3 Implementation" label |
| VideoProcessor.cs:29 | "Phase 2 Implementation" label |
| UpscalerProgressHub.cs:9 | Says "SignalR" but uses SessionManager |
| UpscalerProgressHub.cs:51 | Says "GeneralCommand" but uses UserDataChanged |
| main.py:279 | Scene change threshold range description inverted |
| LibraryScanHelper.cs:62 | Says "targeted scan" but does full library scan |

---

## Positive Findings

- Circuit breaker pattern well-implemented in Python
- Semaphore capture pattern correct for config changes
- Atomic file writes for cache index and model downloads
- CUDA OOM adaptive tile sizing with retry
- Good SSRF protection on webhooks (just needs DNS pinning)
- Consistent clamped setters on numeric PluginConfiguration properties
- `RealtimeStats` class is best-designed Python type (self-contained, thread-safe)
- `PlatformDetectionService` is best-designed C# type (clean interface, Lazy<T>)
