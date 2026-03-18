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
