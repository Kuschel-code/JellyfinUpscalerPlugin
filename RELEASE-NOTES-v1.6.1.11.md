# JellyfinUpscalerPlugin v1.6.1.11

Bugfix release. Addresses multiple auth-related UI regressions and makes the Docker model catalog consistent with what actually works end-to-end.

## What's fixed

### Model name validation (Bug A)
The server-side regex used to validate model names rejected any ID containing a dot. That meant `rife-v4.6`, `rife-v4.6-lite` and `gfpgan-v1.4` all returned `HTTP 400` before ever hitting the download or load path.

- [Controllers/UpscalerController.cs:44](Controllers/UpscalerController.cs#L44): widened to `^[a-zA-Z0-9_-]+(?:\.[a-zA-Z0-9_-]+)*$` — dots only as separators between alphanumeric segments.
- Still rejects: `..`, leading/trailing dots, path separators, empty segments — so path traversal stays blocked.

### Broken model catalog entries (Bug B)
Nine models advertised as `available: true` in the Docker AI service pointed at URLs that no longer resolve (unpublished HF repos, placeholder GitHub release tags, or incompatible ONNX ops). Clicking them produced confusing errors.

Flipped to `available: false` so the UI filters them out instead of fast-failing:

- `nomos8k-hat-x4` — HAT transformer ops fail on CPU execution provider
- `apisr-x3` — Xenova HF repo returns 401
- `edvr-m-x4`, `realbasicvsr-x4`, `animesr-v2-x4` — `kuscheltier/jellyfin-vsr-models` HF repo not yet published
- `rife-v4.6`, `rife-v4.6-lite` — GitHub release tag `v0.0.0` is a placeholder (404)
- `gfpgan-v1.4`, `codeformer` — same pending HF repo as above

Existing saved configs referencing these keys still resolve at lookup (no KeyError); they simply can't be downloaded/loaded until the upstream assets are published.

### Silent 401 in player quick-menu (Bug C)
Three raw `fetch()` calls in `Configuration/player-integration.js` referenced `ApiClient.getRequestHeader` (singular) — which does not exist in current Jellyfin web builds. The fallback was an empty `Authorization` header, so every call returned `401` silently.

Fixed to use the same `ApiClient.accessToken()` bearer-header pattern the rest of the codebase uses:

- [player-integration.js:995](Configuration/player-integration.js#L995) — `POST /Upscaler/models/load` (the primary user action when clicking a model in the quick-menu)
- [player-integration.js:347](Configuration/player-integration.js#L347) — `POST /Upscaler/upscale-frame` (real-time frame upscaling)
- [player-integration.js:1128](Configuration/player-integration.js#L1128) — `GET /Upscaler/benchmark-frame` (realtime capture benchmark)

This completes the auth-fix work started in v1.6.1.10 (which addressed the same root cause in `_fetchModelStates`).

### `GET /Upscaler/jobs` elevation (Bug D)
[Controllers/UpscalerController.cs:746](Controllers/UpscalerController.cs#L746) was previously only gated by the class-level `[Authorize]`, which accepts any authenticated Jellyfin user. The endpoint returned the active-jobs list including server-side file paths — a small information-disclosure issue. Added `[Authorize(Policy = "RequiresElevation")]` to match the mutation endpoints (pause/resume/cancel) that were already correctly restricted.

### Jellyfin.Controller SDK pin (Bug E)
[JellyfinUpscalerPlugin.csproj:19](JellyfinUpscalerPlugin.csproj#L19) pinned `Jellyfin.Controller` to `10.11.6` while `targetAbi` in `meta.json` advertises `10.11.8.0`. Bumped to `10.11.8` so the compiled assembly matches what the target server actually exposes — eliminates potential patch-level API surface mismatches.

### Version-string hygiene
- `docker-ai-service/app/main.py` `VERSION`: 1.6.1.7 → 1.6.1.11 (so `/health` reports accurately).
- `Dockerfile.amd`, `Dockerfile.apple` `LABEL version`: 1.6.1.10 → 1.6.1.11.

## Verification

- 22/22 unit tests pass (`dotnet test -c Release`)
- Live bulk-load test on Jellyfin 10.11.8: **30 of 30 available models load successfully, 0 failures**
- 12 models correctly flagged unavailable (3 Vulkan-only ncnn + 9 broken URLs above)

## Deep-scan audit

This release was verified through four parallel code-review agents covering the C# controller, the Python AI service, the HTML/JS frontend, and release integrity. All findings — including the two pre-existing issues above — were either addressed in this release or explicitly triaged.

## Install

1. Download `JellyfinUpscalerPlugin-v1.6.1.11.zip`
2. Extract into your Jellyfin plugin directory:
   - Linux / Docker: `/var/lib/jellyfin/plugins/AI Upscaler Plugin_1.6.1.11/` (or your configured data dir)
   - Windows: `%ProgramData%\Jellyfin\Server\plugins\AI Upscaler Plugin_1.6.1.11\`
   - TrueNAS SCALE: `<dataset>/Jellyfin/Config/plugins/AI Upscaler Plugin_1.6.1.11/`
3. Restart Jellyfin

Or add the plugin manifest URL under **Dashboard → Plugins → Repositories**:

```
https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/repository-jellyfin.json
```

## Checksum

**SHA-256** (`JellyfinUpscalerPlugin-v1.6.1.11.zip`):
```
db3409c4df0b4d7db0a81f42b5bc910b75b6f8d52b5d921bec87403dc34781cb
```

Verify locally (PowerShell):
```powershell
(Get-FileHash -Algorithm SHA256 .\JellyfinUpscalerPlugin-v1.6.1.11.zip).Hash
```

Or (Linux/macOS):
```bash
sha256sum JellyfinUpscalerPlugin-v1.6.1.11.zip
```

## Compatibility

- Jellyfin server: **10.11.8** (targetAbi `10.11.8.0`)
- .NET runtime: **net9.0**
- Docker AI service: `kuscheltier/jellyfin-ai-upscaler:docker6.1.1-cpu` (CPU variant) and the other GPU tags — all rebuilt against 1.6.1.11
