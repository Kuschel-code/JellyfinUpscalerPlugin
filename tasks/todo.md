# AI Upscaler Plugin — v1.5.2.8 Release Tasks

## Status: In Progress

## Context
Config page was completely rebuilt in previous session. Old sidebar+flex layout replaced with
horizontal tab bar + `<details>`/`<summary>` sections — following patterns from working Jellyfin
plugins (Intro Skipper, Webhook). The new HTML is written but NOT yet built, tested, or deployed.

---

## Phase 1: Verify Config Page Rebuild
- [ ] 1.1 Build project with `dotnet build` — confirm zero errors
- [ ] 1.2 Review new configurationpage.html for correctness (all element IDs match JS, no broken references)
- [ ] 1.3 Fix CS8604 nullable warning in FFmpegWrapperService.cs

## Phase 2: Build & Package
- [ ] 2.1 Run `dotnet publish` to get all DLLs
- [ ] 2.2 Create ZIP with all 6 required files (plugin DLL + 4 dependency DLLs + meta.json)
- [ ] 2.3 Compute MD5 checksum of the ZIP

## Phase 3: Deploy to GitHub
- [ ] 3.1 Commit all changes (config page rebuild + any fixes)
- [ ] 3.2 Create/update GitHub release v1.5.2.8 with new ZIP
- [ ] 3.3 Update ALL manifests (manifest.json, repository-jellyfin.json, repository-simple.json) with new checksum
- [ ] 3.4 Commit and push manifest updates
- [ ] 3.5 Verify: download ZIP from GitHub CDN, compare checksum to local

## Phase 4: Address Community Issues
- [ ] 4.1 Issue #48 "Plugin is very far from being usable" — config page rebuild should help
- [ ] 4.2 Issue #43 "Checksum doesn't match" — proper manifest updates should fix
- [ ] 4.3 Issue #39 "Save configuration does not work" — verify save works in new config page
- [ ] 4.4 Issue #27 "Not seeing settings option" — verify config page loads properly

---

## Review
_To be filled after completion_
