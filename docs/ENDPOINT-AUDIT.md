# Endpoint audit — which routes anything actually calls

Regenerated for v1.8.3.24 by extracting every `[HttpVerb(...)]` from
`Controllers/UpscalerController.cs` and searching for its real call form
(`Upscaler/<path>` and `api/Upscaler/<path>`) across three consumer groups:

| Group | Files |
|---|---|
| **ui** | `Configuration/*.html`, `Configuration/*.js` — the config page, player panel, sidebar, quick menu |
| **site** | `site/*.html` — the public documentation |
| **docs** | `*.md` |

**This is a report. Nothing here has been deleted.** A route with no caller is
not automatically dead: several are deliberate external API, and one is
groundwork for a feature that has not shipped. The point is to know which is
which, rather than carrying 67 routes and guessing.

## Summary

| | Count |
|---|---:|
| Routes on the controller | **69** |
| Called by the plugin's own UI | 48 |
| Documented but not called by the UI | 1 |
| **No reference anywhere in this repo** | **20** |

## The 20 with no reference

Grouped by what they are, because the right answer differs per group.

### Deliberate external API — keep, document

> v1.8.3.24 added two routes and both are called, so the unreferenced count is
> unchanged at 20: `POST /detect-mask` is what the player's capture loop posts
> frames to during playback, and `POST /object-mask/load-model` is behind the
> button on the settings page.


These exist for other clients (scripts, Home Assistant, the `:5000` dashboard,
curl). Nothing in this repo calls them and nothing should.

| Route | Note |
|---|---|
| `POST /test` | Connection smoke test for external callers |
| `GET /info` | Plugin metadata |
| `GET /hardware`, `GET /hardware-info` | Two hardware reports with different shapes — see "Overlapping pairs" |
| `POST /upscale-images/{itemId}` | On-demand poster/backdrop upscale, documented in the README |
| `POST /process`, `POST /process/item/{itemId}` | Kick off a job without the UI |
| `POST /preprocess` | Batch entry point |
| `GET /health/detailed` | Monitoring probe |
| `GET /gpu-verify` | Diagnostic for support requests |

### Groundwork that has not shipped — keep, do not delete

| Route | Note |
|---|---|
| `POST /upscale-video-chunk` | Chunked/temporal upscaling plumbing from v1.8.2. No per-frame model needs it; a temporal-restoration sidecar would. Deleting it would mean rebuilding the same thing. |

### A capability the UI configures but never exposes — the real gap

| Route | Note |
|---|---|
| `GET /queue` | |
| `POST /queue/add` | |
| `POST /queue/{jobId}/cancel` | |
| `POST /queue/{jobId}/priority` | |
| `POST /queue/pause` | |
| `POST /queue/resume` | |

The settings page offers **Enable Processing Queue**, **Max Queue Size** and
**Pause Queue During Playback** — three controls over a queue the user can then
never look at. All six endpoints exist and work; no UI surface calls a single
one of them. (Verified: the string `queue` does appear in
`configurationpage.html`, but only as those config field names.)

This is the inverse of the problem v1.8.3.20 fixed elsewhere: not a promise
without an implementation, but an implementation without a surface. It is a
candidate for a future package, not for deletion.

### Admin/maintenance without a surface

| Route | Note |
|---|---|
| `POST /service-config` | Push settings to the AI service |
| `GET /recommendations` | Deprecated alias kept for external callers; the UI moved to `/hardware-benchmark` |
| `GET /models/disk-usage` | How much disk the model cache uses |
| `POST /models/cleanup` | Evict cached models |

`disk-usage` + `cleanup` are the natural pair for a "Models" tab footer. The
Cache tab already has an eviction UI for a different cache, so the pattern
exists.

## Overlapping pairs worth naming

Three route families look interchangeable and are not. This is the reason a
blanket "alias them onto one schema" would break things:

| Route | Returns | Consumers |
|---|---|---|
| `GET /recommend` | Proxies the AI service: `tier`, `model`, `scale` the **hardware** can run | Plugin-internal (caches the tier) |
| `GET /hardware-benchmark` | **Local hardware benchmark**: `hardware.cpuCores`, `hardware.cudaAvailable`, `system.platform`, `recommendations` | 4 UI call sites |
| `GET /recommendations` | Deprecated alias of the row above, identical payload, warns once | external callers only |
| `GET /recommend-model` | **Content** pick for one video: model, scale, reason, signals, filter suggestion | Player, dashboard, sidebar |

`/recommendations` is misnamed — it is a hardware benchmark, not a
recommendation of the same kind as the other two. Aliasing it onto the
`/recommend` schema would delete `hardware.*` and `system.*` and break the
sidebar, the quick menu and the System tab. v1.8.3.20 therefore added
`GET /hardware-benchmark` with the identical payload, pointed the UI at it, and
left `/recommendations` in place as a deprecated alias that logs a warning once.

The same overlap exists for `GET /hardware` vs `GET /hardware-info` — both
unreferenced, both hardware reports, shapes not compared here. Worth a look
before either is documented as the external one.

## How to regenerate

The scan is a dozen lines of Python: extract `\[Http(Get|Post|...)\("([^"]*)"\)\]`
from the controller, then search each consumer group for `Upscaler/<stem>` and
`api/Upscaler/<stem>`, where `<stem>` is the route path truncated at its first
`{`. Re-run it when the route count changes.
