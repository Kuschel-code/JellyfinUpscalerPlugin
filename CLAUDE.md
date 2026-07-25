# JellyfinUpscalerPlugin — "AI Upscaler Plugin" for Jellyfin

Slim C# plugin (~1.6 MB) that talks HTTP to an external Docker AI microservice (Python/FastAPI/ONNX), plus a GitHub Pages site. GitHub: `Kuschel-code/JellyfinUpscalerPlugin`. Plugin GUID `f87f700e-679d-43e6-9c7c-b3a410dc3f22`.

## Stack
- Plugin: `net9.0`, Jellyfin.Controller 10.11.8, FFMpegCore, CliWrap, ImageSharp; xUnit tests in `JellyfinUpscalerPlugin.Tests/`.
- AI service: `docker-ai-service/` — FastAPI + ONNX Runtime; 7 image variants (`Dockerfile` + `.amd/.apple/.converter/.cpu/.intel/.vulkan`), per-backend `requirements-*.txt`.
- Site: static HTML in `site/`, deployed by the Pages workflow on pushes touching `site/**`.

## Commands
```bash
dotnet build JellyfinUpscalerPlugin.csproj -c Release   # Release build; warnings are treated as errors
pwsh Scripts/verify-release.ps1        # AFTER a release: re-downloads assets, SHA round-trip vs all 3 feeds, ZIP content check
pwsh Scripts/sync-fallback-models.ps1  # regenerates Resources/models-fallback.json from docker AVAILABLE_MODELS
```

## Layout (what matters)
- `Plugin.cs`, `Configuration/`, `Controllers/`, `Services/`, `ScheduledTasks/` — plugin code.
- `manifest.json` + `repository-jellyfin.json` + `repository-simple.json` — the THREE plugin feeds.
- `meta.json` — plugin metadata inside the release ZIP.
- `docker-ai-service/app/main.py` — `AVAILABLE_MODELS` = source of truth for the model catalog.
- `site/models.html`, `site/models-import.json` — public model catalog.
- The repo root is littered with dozens of old release ZIPs and `publish*`/`zip-stage*` dirs — NEVER list the root recursively; navigate directly to what you need.

## Release process (manual — CI does NOT release!)
1. Stamp the version: csproj (`Version`/`AssemblyVersion`/`FileVersion`, always 4-part) and `meta.json` (`version` mirrors the git tag: 3-part when the 4th part is 0; its `targetAbi` stays 3-part `10.11.8`). Assembly version ≠ manifest version ⇒ Jellyfin restart-loop. Feeds are stamped in step 4.
2. Build the ZIP. Forbidden inside: `Scripts/`, `*.pdb`, `*.deps.json`, test DLLs. `dotnet build` outputs only the main DLL — the transitive NuGet deps (FFMpegCore, CliWrap, ImageSharp, Instances) MUST be added or the plugin dies with NotSupported + tombstone (recovery: `/Plugins/{id}/{ver}/Enable`).
3. `gh release create vX.Y.Z <zip>` (check for an existing tag first — global rule).
4. Update version (4-part) + checksum + sourceUrl in ALL THREE feeds and keep them identical (`targetAbi` there is 4-part `10.11.8.0`). One missed feed = users see the update but cannot install it (caused issue #74).
5. Run `pwsh Scripts/verify-release.ps1`. A release is not done until it passes.

## Things That Will Bite You
- **CI auto-release was deliberately removed** after v1.6.1.11/12, when two workflows raced manual uploads and shipped corrupt ZIPs. Never re-add release/upload steps to `build.yml` or `build-and-release.yml`.
- **Model catalog lives in 3 places** that must stay in sync: `main.py AVAILABLE_MODELS` → `Resources/models-fallback.json` (via sync script) → `site/models.html` (manual). CI (`v1.7.1-audit-checks.yml`) diffs the first two; the third is on you.
- **Never install plain `onnxruntime` next to `onnxruntime-rocm`/`-openvino`** — the plain wheel (AzureExecutionProvider) shadows the GPU providers → silent CPU fallback.
- **pip version floors need a FULL dry-run** of every requirements set against the image's Python — testing partial pairs lies (opencv>=4.12 vs the old numpy<2 cap once killed all 6 builds). AMD/ROCm: torch 2.3 → keep numpy<2 and opencv<4.12.
- **README and `site/` are CRLF** — the global CRLF edit rule applies (single-line anchors only).
- **docker-publish "Build amd" goes red** on the Trivy scan timeout (20 GB ROCm image) — the image IS pushed anyway; not a release blocker.
- **Docker images publish via `docker7*` / `v*-docker7` tags**, not plugin `v*` tags.
- Jellyfin config pages are HTML FRAGMENTS: everything outside the `data-role="page"` div (including `<style>` in `<head>`) is stripped — CSS/JS must live inside the page div. In-page tabs: plain `<button type="button">`, never `<a is="emby-linkbutton">` (it routes away and ignores preventDefault).
- Jellyfin 10.11 removed the global `window.playbackManager` — hook DOM-native `<video>` events instead.
- **The AI service IGNORES the requested `scale`** and uses the loaded model's native factor (`main.py upscale_endpoint` only logs a warning). Only the ffmpeg hardware-filter path honours the configured value. Report the model's own scale (`Services/ModelScale.cs`), never the config's, or logs and job metadata lie.
- **Model ids use BOTH scale conventions**: `realesrgan-x4` (x-then-digit) and `dejpg-realplksr-1x` / `omdb-4x-...` (digit-then-x). Parsing only one silently returns "unknown" for the whole 1x restoration family.
- **A shipped config default is indistinguishable from a user override.** `PreferredAnimeModel` defaulted to a model, so every anime pick took the override path and skipped the hardware cap and scale logic. Defaults for "override" fields must be empty.
- **Deploy to the test server before claiming done.** v1.8.3.14-.19: five defects passed every unit test and only showed on a real CPU-only box (8K output, crudest-model collapse, a default posing as a decision). `Scripts/bump-version.py <old> <new>` stamps the 13 version sites that `verify-release.ps1` guards.
- **Two docker-publish runs in flight race the `:latest` tag** — cancel the superseded run before the newer one finishes.

## CI workflows (7, in .github/workflows/)
`build-and-release.yml` + `build.yml` build/test ONLY (no release steps!); `dockerhub-cleanup.yml` manual dispatch, dry-run default; `v1.7.1-audit-checks.yml` diffs the model catalog. Routine: `docker-publish.yml`, `pages.yml`, `import-catalog-refresh.yml` (weekly).
