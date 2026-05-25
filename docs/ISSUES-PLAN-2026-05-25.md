# Issues-Triage & Analyse-Plan — 2026-05-25

**Scan-Datum:** 2026-05-25 (post-v1.7.4)
**Repo-Stand:** `main` @ `984db02` (v1.7.4 hotfix)
**Offene Issues:** 2 (#45 alt, #69 brandneu)
**Status:** **Nur Analyse — keine Code-Aktion in dieser Session.**

---

## Executive Summary

| # | Status | Prio | Diagnose | Empfohlene Aktion |
|---|---|---|---|---|
| **#45** | OPEN seit Feb 2026 | **CLOSE** | Vorgaenger-Issue von #66 (gleicher User FrRene06, 4 Monate frueher) | Comment + Close — durch v1.7.4 indirekt geloest |
| **#69** | OPEN heute | **SPLIT + Investigate** | 3-in-1 Issue: (a) GPU nicht aktiv, (b) Aspect-Ratio 4:3 gestretched, (c) Non-admin users koennen Plugin nicht nutzen | Split-Vorschlag + Diagnose-Anfrage |

**Gesamt-Aufwand fuer Fix-Session:** ca. 3 Stunden Investigation + Code wenn alle Diagnosen bestaetigt.

---

## Issue #45 — Intel ARC A380 (Februar 2026, FrRene06)

**Reporter:** [@FrRene06](https://github.com/FrRene06)
**Datum:** 2026-02-15 (vor 3 Monaten)
**Hardware:** Intel Arc A380, Docker
**Symptom:**
- AI-Service auf Port 5000 erreichbar, zeigt aber "openvino, cpu" (GPU not used)
- Upscale-Button im Jellyfin-Player erscheint nicht

### Diagnose

Dies ist der **urspruengliche Bug-Report** von dem User, der spaeter als #66 (Mai 2026) zum zweiten Mal das gleiche Problem meldete. Im prior session habe ich nur die neueren Issues gescannt — #45 ist im 30-Tage-Filter durchgerutscht.

**Beide gemeldeten Symptome sind durch v1.7.4 geloest:**

1. **"OpenVINO CPU statt GPU"**: durch v1.7.4 #66-Fix. Wenn User auf nicht-WSL2 laeuft (klassisches Linux + `/dev/dri` passthrough), war der Bug damals vermutlich:
   - Image-Version (v1.5.x hatte Detection-Bug der mit v1.6.1.x gefixt wurde)
   - oder OpenVINO-Konfiguration (`OPENVINO_DEVICE=GPU` env-var fehlt — User hat das im Setup nicht erwaehnt)
2. **"Player-Button erscheint nicht"**: dieser Bug wurde mehrfach gefixt:
   - v1.5.2.7 (player button MutationObserver)
   - v1.5.5.0 (Jellyfin 10.11 playbackManager removal)
   - v1.7.0 (RealtimeMode integration + honest UI cleanup)

   Aktuelle v1.7.4 sollte das ohne Probleme zeigen.

### Empfohlene Aktion

**Comment + Close** mit Hinweis:
- v1.7.4 + Image `:docker7` (latest) sollte beides loesen
- Setup-Check: braucht `OPENVINO_DEVICE=GPU` env-var (siehe #69 fuer korrektes docker-compose)
- Falls Player-Button immer noch fehlt: bitte separates Issue mit Jellyfin-Version + Browser-Konsolen-Log
- Cross-reference zu #66 (sein eigener neuerer Issue)

---

## Issue #69 — 3-in-1 (heute, "Daniel" via bot-account)

**Reporter:** Daniel (bot-account `app/` — vermutlich Apollo-issue-bot oder GitHub-app-proxy)
**Datum:** 2026-05-25 (heute)
**Hardware:** Intel Arc A310, Docker (Linux, klassisch)
**Setup-Auszug:**
```yaml
image: kuscheltier/jellyfin-ai-upscaler:docker7-v1.6.1.13-intel
devices:
  - /dev/dri:/dev/dri
environment:
  - API_TOKEN=disable
  - USE_GPU=true
  - GPU_DEVICE=0
```

**Drei separate Symptome** in einem Issue:

### Symptom 1 — GPU wird nicht genutzt

User sagt: GPU wird im Dashboard angezeigt + status sagt "active", aber **alles rendert auf CPU**.

**Hypothesen (priorisiert):**

1. **Alte Image-Version** (`docker7-v1.6.1.13-intel`). User nutzt **6-Wochen-altes Image**. Zwischen v1.6.1.13 und v1.7.4 sind 10 Releases passiert (#66 + #67 + Detection-improvements). **Wahrscheinlichste Ursache.** Workaround: `image: kuscheltier/jellyfin-ai-upscaler:docker7-intel` (rolling latest).
2. **Detection-vs-Provider-Decoupling** (v1.7.4-Audit P3-Finding). Dashboard zeigt GPU detected, aber OpenVINO oeffnet GPU-Device nicht erfolgreich → faellt auf CPU zurueck, ohne dass `state.gpu_name` zurueckgesetzt wird. Verifikation: `/gpu-verify` endpoint zeigt `active_providers` — wenn dort nur `CPUExecutionProvider` steht, ist es dieser Bug.
3. **Fehlende `OPENVINO_DEVICE=GPU` env-var**. User hat `USE_GPU=true` aber nicht `OPENVINO_DEVICE=GPU` — wahrscheinlich Default-Fallback auf CPU im OpenVINO-Provider-Konfiguration.

**Empfohlene Diagnose-Schritte fuer User:**
- Update auf `:docker7-intel` rolling tag (1-Min-Fix)
- Falls weiter Problem: `curl http://localhost:5000/gpu-verify` Output posten
- Zweite env-var hinzufuegen: `OPENVINO_DEVICE=GPU`

### Symptom 2 — Aspect Ratio 4:3 wird gestretched

User sagt: 4:3-Filme werden auf 16:9 gestretched (Full-Screen-Stretch).

**Hypothesen:**

1. **Frontend-CSS-Bug** im `player-integration.js` oder Overlay-Container. Wenn das Plugin ein `<canvas>` oder `<video>` Overlay ueber dem Jellyfin-Player legt mit `object-fit: fill` statt `object-fit: contain`, wird gestretched.
2. **Upscaled-Output-Aufloesung** ignoriert source-Aspect-Ratio. Wenn Source 720×540 (4:3) auf 4× upscaled wird, Output = 2880×2160 (immer noch 4:3), aber wenn der FFmpeg-Filter `scale=3840:2160` (16:9) erzwingt, wird gestretched.

**Empfohlene Investigation:**
- Code-Read `player-integration.js` fuer Overlay-CSS
- Code-Read `ProcessingMethodExecutor.cs` / `VideoFrameProcessor.cs` fuer FFmpeg-scale-filter

### Symptom 3 — Non-admin users koennen Plugin nicht nutzen

User sagt: Models laden/downloaden nicht fuer non-admin users.

**Hypothesen:**

1. **`RequiresElevation` zu aggressiv**. Memory says v1.6.1.21 added `RequiresElevation` zu 16 endpoints. Wenn `GET /Upscaler/models` oder `POST /Upscaler/models/download` darunter sind, sind sie admin-only. Aber: das sind read-only/user-specific ops, die alle authenticated users brauchen sollten.
2. **Plugin-config-page** ist hinter `Dashboard → Plugins → AI Upscaler` (admin-only by design — Jellyfins pattern). User-facing controls waeren im Player-Overlay (separate Auth-Path).

**Empfohlene Investigation:**
- Grep `Controllers/UpscalerController.cs` fuer `[RequiresElevation]` → Liste der admin-only endpoints
- Decision-call: welche endpoints sollten authenticated users zugaenglich sein vs. admin-only?

---

## Plan fuer Fix-Session (separate, nicht jetzt)

### Phase A — #45 Auto-Close (5 Min)

Comment + close. Templated message, kein Code-Change.

### Phase B — #69 Investigation Comment (20 Min)

Issue-Comment auf #69 mit:
- Sofort-Workaround #1: Update Image auf `:docker7-intel`
- Diagnose-Anfrage fuer #1: `/gpu-verify` output
- Split-Vorschlag: separate Issues #69a (GPU), #69b (Aspect-Ratio), #69c (Non-admin) fuer sauberes Tracking
- Keine Code-Aktion bis User Feedback gibt

### Phase C — Wenn User auf #69 antwortet (Investigation-Path-dependent)

Je nach Symptom:

| Symptom | Wenn bestaetigt | Fix-Plan |
|---|---|---|
| #69.1 GPU nicht aktiv (selbst auf v1.7.4) | Detection-vs-Provider-Decoupling | Post-Load-GPU-Verifikation: nach `session.run()` mit Test-Tensor pruefen ob tatsaechlich `OpenVINOExecutionProvider` aktiv war, sonst `state.gpu_name` resetten |
| #69.2 Aspect-Ratio | Frontend-CSS bug | `player-integration.js` Overlay-Container: `object-fit: contain` statt fill |
| #69.3 Non-admin | RequiresElevation zu broad | Audit aller `[RequiresElevation]`-Endpoints, downgraden wo User-Auth ausreicht |

### Phase D — Tests (vor Release)

- `docker-ai-service/tests/test_provider_verification.py` (neu) — mocked OpenVINO `session.run()` failure → assert `state.gpu_name` reset
- Frontend-Test fuer Aspect-Ratio (falls moeglich via Playwright)
- C#-Test fuer `[RequiresElevation]` audit-policy

---

## Backlog (deferred zu v1.7.5/v1.8.0)

- **Phase-E C#-Tests** (aus v1.7.4-Roadmap): VideoFrameProcessor, ProcessingMethodExecutor, UserManagerAdapter PlayCount
- **`docker-ai-service/tests/test_detection.py` + `test_inference.py`** (aus v1.7.4-Audit-P3)
- **README-Commit** ist noch uncommitted aus prior session (v1.7.4 changelog + version bumps) — separat-Commit-Candidate

---

## Wichtige Beobachtung — Issue-#69 Bot-Reporter

Issue #69s author ist `app/` mit `is_bot: true`. Das ist **ungewoehnlich** — entweder:
1. Apollo (GitHub-issue-bot) oder aehnlicher Cross-poster
2. User "Daniel" hat ueber eine 3rd-Party-app eingereicht (Jellyfin-Plugin-Bug-Tracker?)
3. Spam-Bot — aber Content ist substantiell + technisch korrekt, also unwahrscheinlich

**Maintainer-Note:** vor Antwort pruefen, ob direkter Kontakt zu Daniel moeglich (z.B. wenn er einen GitHub-Account hat). Ansonsten Antwort ueber das Bot-Proxy.

---

**Plan-Autor:** Maintainer-Session 2026-05-25
**Naechster Schritt:** User-Bestaetigung fuer Phase A/B Start. **Bisher keine Code-Aktion durchgefuehrt.**
