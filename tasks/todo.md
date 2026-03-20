# AI Upscaler Plugin — v1.5.2.8 Release Tasks

## Status: Complete

## Context
Config page was completely rebuilt. Old sidebar+flex layout replaced with
horizontal tab bar + `<details>`/`<summary>` sections — following patterns from working Jellyfin
plugins (Intro Skipper, Webhook).

---

## Phase 1: Verify Config Page Rebuild
- [x] 1.1 Build project with `dotnet build` — 0 errors, 0 warnings
- [x] 1.2 Review configurationpage.html — all 60+ element IDs match JS, no broken refs
- [x] 1.3 Fix CS8604 nullable warning in FFmpegWrapperService.cs (line 65)
- [x] 1.4 Fix parseInt race condition in saveConfig (NaN silently discarded)

## Phase 2: Build & Package
- [x] 2.1 `dotnet publish -c Release` — all DLLs generated
- [x] 2.2 ZIP created with 6 files (JellyfinUpscalerPlugin.dll, CliWrap.dll, FFMpegCore.dll, Instances.dll, SixLabors.ImageSharp.dll, meta.json)
- [x] 2.3 MD5: `90462a0696ce9224f2a0ce6fe67d3df3`

## Phase 3: Deploy to GitHub
- [x] 3.1 Committed: `9636271` — "v1.5.2.8: Rebuild config page for Jellyfin 10.11+ compatibility"
- [x] 3.2 Release created: https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/tag/v1.5.2.8
- [x] 3.3 All 3 manifests updated with checksum `90462a0696ce9224f2a0ce6fe67d3df3`
- [x] 3.4 Pushed to origin/main
- [x] 3.5 Verified: GitHub CDN checksum matches local (`90462a0696ce9224f2a0ce6fe67d3df3` = `90462a0696ce9224f2a0ce6fe67d3df3`)

## Phase 4: Community Issues
- [x] 4.1 Issue #48 "Plugin is very far from being usable" — config page rebuilt with Jellyfin-compatible layout
- [x] 4.2 Issue #43 "Checksum doesn't match" — all manifests now match ZIP
- [x] 4.3 Issue #39 "Save configuration does not work" — fixed parseInt race condition in saveConfig
- [x] 4.4 Issue #27 "Not seeing settings option" — may be related to layout, needs user verification

---

## Review

### What changed
| File | Change |
|------|--------|
| `Configuration/configurationpage.html` | Complete rebuild: sidebar→horizontal tabs, accordion→`<details>`, flex→inline-block |
| `Services/FFmpegWrapperService.cs` | Nullable fix: `config ?? new PluginConfiguration()` |
| `manifest.json` | Updated checksum + changelog |
| `repository-jellyfin.json` | Updated checksum + changelog |
| `repository-simple.json` | Updated checksum + changelog |

### Key decisions
1. **No flex/grid for layout** — Jellyfin 10.11+ parent containers block it (Lesson L2)
2. **Native `<details>`/`<summary>`** instead of custom JS accordion — guaranteed to work
3. **`inline-block` for multi-column** — safe CSS that works in any container context
4. **`data-require`** attribute added for Emby component loading (Lesson L3)
5. **Minimal CSS overrides** — only dark theme colors, not fighting Jellyfin's layout (Lesson L7)
