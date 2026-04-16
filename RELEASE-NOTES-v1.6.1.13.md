# v1.6.1.13 — In-Player Quick-Menu Redesign + Live Filter Controls

**Released:** 2026-04-16
**SHA-256:** `47713f0a28a2dbde52ae9abd58d3b3c62a95a1547df6353191d439954323a70e`

## What's new

The small settings overlay (`#aiUpscalerQuickMenu`) that pops up from the **Upscaler** button during video playback has been redesigned as a **tabbed interface** and gained **live filter controls** that adjust the video look in real-time — no transcode, no AI service, no leaving playback.

### 1. New tabbed layout

Before: a single flat scroll containing Models + Scale + Realtime + Config stacked top-to-bottom.
Now: three tabs — `[Models | Filters | Realtime]` — with a persistent footer for the full configuration link.

| Tab | Contents |
|------|----------|
| **Models** | Category-grouped model grid + scale-factor picker (unchanged content, now scoped to a tab pane) |
| **Filters** | NEW — preset chips + 3 live sliders + advanced server-side params |
| **Realtime** | Existing real-time upscaling status + Toggle/Switch buttons (moved into its own tab) |

### 2. Live filter controls (Filters tab)

**Preset chips** — 16 looks in a 4-column grid:
`None · Cinematic · Vintage · Vivid · Noir · Warm · Cool · HDR Pop · Sepia · Pastel · Cyberpunk · Drama · Soft Glow · Sharp HD · Retro · Teal/Orange`

**3 live sliders** — drag to tune the look instantly:
- **Brightness** (−50 to +50)
- **Contrast** (−50 to +50)
- **Saturation** (−100 to +100)

The CSS `filter` property on the playing `<video>` element updates at ~60 fps while you drag. Zero delay, zero network round-trip. Works for **every authenticated user** — admin or not.

**LIVE indicator**: a green pulsing dot appears on the Filters tab whenever a CSS filter is actively applied to the video, so you know at a glance whether the overlay is on.

### 3. Advanced (server-persisted) filters

Collapsed by default. Expand to reveal six parameters that the CSS `filter` property can't express:
- **Gamma** (0.5 – 2.5)
- **Sharpness** (0 – 3)
- **Color Temperature** (3000 K – 10000 K)
- **Vignette** (0 – 3)
- **Film Grain** (0 – 50)
- **Denoise** (0 – 10)

These require the server-side FFmpeg filter chain (unsharp, colortemperature, vignette, noise, hqdn3d). They persist via the new endpoint and apply **on next seek / next playback**, not live.

### 4. Save / Reset

- **Reset** (any user) — clears the 3 live sliders to zero, sets preset to `None`, removes the CSS filter from `<video>`. Session only.
- **Save** (admin only) — POSTs the full filter state to `/Upscaler/filter-config` so it persists across sessions and other users. Non-admins see a `warning` toast: "Save failed — admin privileges required." Their live preview still works for the current session.

### 5. New controller endpoints

```
GET  /Upscaler/filter-config    — returns current filter config (any authed user)
POST /Upscaler/filter-config    — updates filter config (admin only; partial updates OK)
```

Partial updates: POST body fields are all nullable — send only what changed. Values are clamped by the PluginConfiguration setters, so out-of-range values saturate instead of throwing.

### 6. Quality-of-life

- The menu no longer auto-closes after 20 s if you're actively dragging a slider — any pointer or input event resets the 30 s idle timer. Pulling a slider for several seconds no longer kills the menu out from under you.

## Breaking changes

None. Existing filter configuration (presets 1-7, custom filter properties) is preserved — the new UI just exposes more of it, more conveniently, inside the player.

## Upgrade

```bash
# via repository
"https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/repository-jellyfin.json"
# or direct ZIP
https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/v1.6.1.13/JellyfinUpscalerPlugin-v1.6.1.13.zip
```

## Files in ZIP (6)

```
CliWrap.dll
FFMpegCore.dll
Instances.dll
JellyfinUpscalerPlugin.dll
SixLabors.ImageSharp.dll
meta.json
```

## Verification

```bash
pwsh ./Scripts/verify-release.ps1 -Tag v1.6.1.13
```
