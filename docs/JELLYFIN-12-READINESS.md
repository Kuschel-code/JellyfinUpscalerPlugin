# Jellyfin 12.0 Readiness — Static Analysis & Runtime Test Plan

**Status:** static analysis complete, one API break found and fixed (v1.8.3.4). Runtime verification on a 12.0 RC box is still pending — test plan below.
**Analysis date:** 2026-07-03 · against `Jellyfin.Controller 12.0.0-rc2` (nuget.org) · plugin baseline v1.8.3.3/main.

---

## 1. Verified 12.0 facts (official sources)

| Fact | Source |
|---|---|
| 12.0 is in RC phase: v12.0-rc1 (2026-06-21), v12.0-rc2 (2026-06-28). Versioning drops the leading "10." (10.11.x → 12.x, no 11.x). | github.com/jellyfin/jellyfin releases |
| `Jellyfin.Controller` 12.0.0-rc1/-rc2 are on **nuget.org** (no separate unstable feed needed). Latest stable remains 10.11.11. | api.nuget.org flatcontainer index |
| **12.0 targets net10.0 only** (10.11.x was net9.0) — 12.0-native plugin builds must target .NET 10 (server PR #15475). | 12.0.0-rc2 nuspec |
| **Legacy auth is rejected by default** in the RCs (PR #16992): `X-Emby-Authorization`, `X-Emby-Token`, `X-MediaBrowser-Token` headers, `api_key` query param, `Emby` scheme. Supported: `Authorization: MediaBrowser …` header and `ApiKey` query param. Admins can temporarily re-enable via `EnableLegacyAuthorization` (PR #13306). | PRs #16992/#16754/#13306 |
| RC release notes tell users to **disable/remove external plugins before upgrading** and reinstall 12.0-compatible builds from the unstable plugin repo; first start runs multi-minute DB migrations. | v12.0-rc1/-rc2 release notes |
| targetAbi filtering is plain minimum-version: entries with `targetAbi <= ApplicationVersion` are shown. A 10.11-targetAbi entry therefore **still appears in the catalog on a 12.0 server** (it breaks at runtime instead, if at all). 12.0-native builds must stamp `targetAbi 12.0.0.0`. | InstallationManager.cs @ v12.0-rc2, SharedVersion.cs |
| Plugin-facing API changes in 12.0: .NET 10 (PR #15475), removal of deprecated API members (PR #16110), `UserDto.HasPassword` deprecated (PR #14950), HLS controllers hidden (PR #16715), `/Trailers` deprecated (PR #17094), unsafe plugin package names rejected (PR #17013, rc2). | v12.0-rc1/-rc2 changelogs |

## 2. Compile break list (plugin vs 12.0.0-rc2)

Test build: throwaway branch, `TargetFramework net10.0` + `Jellyfin.Controller 12.0.0-rc2`.

**Exactly one break:**

| File:Line | Error | Cause | Fix |
|---|---|---|---|
| `Services/UserManagerAdapter.cs:34` | CS1061 `IUserManager` has no `Users` | 12.0 replaced the `Users` **property** with a `GetUsers()` **method** | Fixed in v1.8.3.4: user enumeration + `GetUserData` dispatch now resolve via reflection at runtime — one DLL supports 10.11.x (`Users`) **and** 12.x (`GetUsers()`). Verified: 0 errors against 10.11.8 **and** against 12.0.0-rc2; xUnit 164/164 green. |

Everything else — `ILibraryManager` (GetVirtualFolders/GetItemById/GetItemList/FindByPath), `IMediaSourceManager.GetStaticMediaSources`, `ISessionManager`, `IMediaEncoder.EncoderPath/ProbePath`, `Jellyfin.Data.Enums` (BaseItemKind/MediaType), `BasePlugin`/`IHasWebPages`/`IPluginServiceRegistrator`, `[Authorize(Policy="RequiresElevation")]` — **compiles unchanged against rc2**.

> Caveat: compiling clean against rc2 proves the API members still exist, not that the 10.11-compiled DLL is binary-compatible at runtime (type moves between assemblies would only surface as `TypeLoadException` on a live 12.0 server). Hence the runtime plan below.

## 3. Auth audit result (the 12.0 default-rejection risk)

The plugin's embedded web JS already uses only modern mechanisms:

- `ApiClient.ajax` / `ApiClient.getJSON` (~40 calls) — jellyfin-web attaches whatever scheme the server version expects.
- Raw `fetch`/XHR calls use `Authorization: MediaBrowser Token="…"` (player-integration.js:575/1688/1914, configurationpage.html:2062) — this **is** the supported scheme.
- No hardcoded `X-Emby-Token` / `X-MediaBrowser-Token` / `api_key` usages remain.

**Verdict: no auth changes required for 12.0.**

## 4. Runtime test plan (needs a 12.0 RC box — do NOT use the production NAS)

Setup: `jellyfin/jellyfin` RC container on a test VM, fresh config volume, 2–3 short clips, AI-service container ≥ v1.8.2 alongside (service is Jellyfin-agnostic).

| # | Test | Pass criteria |
|---|---|---|
| 1 | Install plugin from our repo feed on 12.0 | Plugin visible in catalog (10.11.8-targetAbi passes the numeric filter), installs |
| 2 | Server start | Status "Active", no `TypeLoadException`/`MissingMethodException` with our namespace in the log |
| 3 | Config page opens + **Save** | Settings persist, no JS console errors |
| 4 | `GET /Upscaler/libraries` | Returns the virtual folders (ILibraryManager path) |
| 5 | Run `LibraryUpscaleScanTask` once | Completes; watched-detection works (UserManagerAdapter reflection path → 12.0 `GetUsers()`) |
| 6 | One short E2E upscale job | Frame extraction (IMediaEncoder paths) → service → encode → completed |
| 7 | In-player overlay + live filters | Buttons work, no 401s in browser console (auth scheme) |
| 8 | API-Tokens tab (RequiresElevation policy) | List loads, create works |
| 9 | Repeat 7+8 with legacy auth explicitly DISABLED (`EnableLegacyAuthorization=false`, the 12.0 default) | Still no 401s |

Log each row ✅/❌ + log excerpt here; then update the compatibility note in README/site.

## 5. Release/feed strategy

- v1.8.3.4 ships the dual-compatible DLL with `targetAbi 10.11.8.0` (works on 10.11.x; expected to load on 12.0 — runtime-unverified).
- Once the runtime matrix above is green on an RC box, add a **second feed entry** `targetAbi 12.0.0.0` (same or dedicated build). Feeds support multiple entries with different targetAbi — 10.11 users see the 10.11 build, 12.0 users the 12.0 entry.
- If 12.0-final still requires a net10.0-native build for anything, cut `release/12.0` with the Controller pin at 12.x — the only code delta is already reflection-safe.
- Reminder: the repo has **three** live feed files (`manifest.json`, `repository-jellyfin.json`, `repository-simple.json`) — any 12.0 entry must land in all three.
