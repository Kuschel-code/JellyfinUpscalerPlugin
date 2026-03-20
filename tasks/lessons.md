# Lessons Learned

## Session 2026-03-18 (Previous Context)

### L1: Jellyfin 10.11+ discards everything outside `<div data-role="page">`
- `viewContainer.js` uses `querySelector('div[data-role="page"]')` — any HTML/CSS outside the page div is silently dropped
- **Rule**: `<style>` and `<script>` tags MUST be inside the page div

### L2: Jellyfin parent containers block flex/grid layouts
- `.content-primary` and `[data-role="content"]` have Jellyfin CSS rules that prevent child flex/grid from rendering properly
- **Rule**: Never use flex/grid for page-level layout. Use `display: inline-block` or block-level flow instead
- Working plugins (Intro Skipper, Webhook) use simple block flow + `<details>`/`<summary>` for sections

### L3: Working Jellyfin plugin patterns
- Use `data-require="emby-input,emby-button,emby-select,emby-checkbox"` on page div
- Use `<fieldset class="verticalSection-extrabottompadding">` for grouping
- Use native `<details>`/`<summary>` for collapsible sections (NOT custom accordion divs)
- Use Emby components via `is="emby-*"` attribute pattern
- Keep CSS minimal — don't fight Jellyfin's styles

### L4: dotnet publish required for ZIP (not dotnet build)
- `dotnet build` doesn't include dependency DLLs
- ZIP must contain: JellyfinUpscalerPlugin.dll, CliWrap.dll, FFMpegCore.dll, Instances.dll, SixLabors.ImageSharp.dll, meta.json
- **Rule**: Always use `dotnet publish` and verify all 6 files are in the ZIP

### L5: Checksum mismatch = install failure
- Jellyfin refuses to install if manifest checksum doesn't match the actual ZIP
- **Rule**: After every new ZIP, compute MD5, update ALL manifests (manifest.json + repository-jellyfin.json + repository-simple.json), commit and push

### L6: GitHub releases can disappear if tag isn't pushed
- Local tag without `git push origin <tag>` means release has no valid ref
- **Rule**: Always push tags explicitly with `git push origin v<version>`

### L7: Don't fight Jellyfin's CSS — work with it
- Overriding Jellyfin styles with !important leads to fragile code
- Better to use transparent backgrounds and let Jellyfin handle the chrome
- Only override what's strictly necessary (input/select colors for dark theme)

### L8: Jellyfin ApiClient.getUrl() does NOT add api/ prefix
- `ApiClient.getUrl('Upscaler/service-health')` → `/Upscaler/service-health` (NOT `/api/Upscaler/service-health`)
- Controller with only `[Route("api/[controller]")]` causes 503 on ALL frontend API calls
- **Rule**: ALWAYS add dual routes on Jellyfin plugin controllers: `[Route("api/[controller]")]` AND `[Route("[controller]")]`
- Jellyfin returns HTTP 503 for unmatched routes (not 404) — misleading when debugging

### L9: Docker container networking — use host IP, not localhost
- When Jellyfin runs in Docker on TrueNAS, `localhost:5000` from inside the Jellyfin container won't reach the AI upscaler container
- Must use the host IP (e.g., `http://192.168.178.113:5000`) for inter-container communication
- **Rule**: Default AI Service URL should hint at host IP, not localhost

### L11: ALL controllers need dual route — not just the main one
- FFmpegWrapperController had `[Route("api/upscaler/wrapper")]` only → all FFmpeg buttons returned 404
- **Rule**: After fixing UpscalerController's route, check ALL other controllers in the project for the same issue
- Lesson: When fixing a pattern bug, grep the entire project for other instances of the same pattern

### L12: Health cache masks real connection state
- HttpUpscalerService caches health results for 30s. User changes URL → clicks Test → gets stale cached result → thinks it's fake
- **Rule**: Always invalidate caches when the underlying config changes, and on explicit user-triggered checks

### L10: TrueNAS Custom Apps — image must be pulled before container starts
- TrueNAS doesn't auto-pull Docker images when creating Custom Apps
- If the tag doesn't exist locally, the app stays in "Stopped" state with "No containers available"
- **Rule**: Always `sudo docker pull <image>:<tag>` manually before expecting the TrueNAS app to start

## Session 2026-03-19

### L13: CI workflow repackages ZIPs and changes checksums
- GitHub Actions workflow `chore: Update manifests for v*` rebuilds the ZIP and updates checksums in `repository-jellyfin.json` and `repository-simple.json` but NOT `manifest.json`
- This causes checksum mismatches between `manifest.json` (user-facing repo URL) and the actual ZIP
- **Rule**: After pushing a release, wait for the CI commit, pull it, then update `manifest.json` to match the CI-generated checksum

### L14: Docker containers have read-only web directories
- Jellyfin's `index.html` in Docker is read-only (`/jellyfin/jellyfin-web/`, `/usr/share/jellyfin/web/`, etc.)
- `Plugin.InjectPlayerScript()` silently fails with no logging in Docker environments
- **Rule**: Always provide a fallback mechanism (e.g., config page auto-bootstrap) when file-system injection might fail. Log all failure paths.

### L15: ILogger in Jellyfin plugin constructors
- Jellyfin DI supports `ILogger<T>` injection in plugin constructors (e.g., `ILogger<Plugin>`)
- This is the standard way to get logging in plugin code — no need to resolve it manually
- **Rule**: Always add ILogger to plugin classes that need diagnostics

### L16: Jellyfin Scheduled Task API types
- `TaskTriggerInfo.Type` uses the enum `TaskTriggerInfoType.DailyTrigger`, not a string constant like `TaskTriggerInfo.TriggerDaily`
- `MediaType.Video` requires `using Jellyfin.Data.Enums;`
- `MediaStreamType.Video` requires `using MediaBrowser.Model.Entities;`
- **Rule**: When using Jellyfin API types, check the actual enum/namespace — don't guess from naming conventions

### L17: TrueNAS Docker requires sudo
- `docker restart` and other Docker commands on TrueNAS need `sudo` for Docker socket permissions
- **Rule**: Always prefix Docker commands with `sudo` when running on TrueNAS
