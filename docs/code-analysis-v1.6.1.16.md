# Verifizierte Code-Analyse: JellyfinUpscalerPlugin v1.6.1.16

**Stand:** 2026-04-28 · **Branch:** `claude/analyze-jellyfin-upscaler-WYPoA` · **TargetAbi:** 10.11.8.0 · **Framework:** net9.0

> Diese Analyse basiert auf direkter Quellcode-Verifikation, nicht auf Release-Changelogs.
> Eine ursprüngliche, größere Analyse stützte sich überwiegend auf Selbstbeschreibungen
> in Changelogs; mehrere ihrer Behauptungen wurden hier widerlegt.

---

## 1. Architektur (verifiziert)

Plugin + Microservice-Modell, bewusst entkoppelt nach den `BadImageFormatException`-Crashes
historischer Versionen (Issue #6, gelöst in v1.5.0.0).

- **C#-Plugin** lädt nur lightweight Dependencies (`Jellyfin.Controller 10.11.8`,
  `FFMpegCore`, `SixLabors.ImageSharp`, `CliWrap`) — siehe `JellyfinUpscalerPlugin.csproj:18-28`
- **Python-Microservice** in `docker-ai-service/` mit FastAPI, ONNX Runtime,
  ncnn-Vulkan und OpenCV-DNN als drei parallele Backends
- **Sechs Docker-Backends:** `Dockerfile` (NVIDIA), `Dockerfile.amd`, `Dockerfile.intel`,
  `Dockerfile.apple`, `Dockerfile.vulkan`, `Dockerfile.cpu` — alle mit `USER appuser`,
  kein SSH-Port

## 2. Code-Qualität — was wirklich stimmt

### 2.1 VideoProcessor wurde gesplittet

Die behauptete 1930-Zeilen-God-Class existiert heute nicht mehr.
`VideoProcessor.cs` umfasst 384 Zeilen und delegiert an fünf Spezial-Klassen:

| Klasse | Verantwortung |
|---|---|
| `VideoAnalyzer` | ffprobe-Wrapper, Stream-Metadaten |
| `ProcessingStrategySelector` | Wahl zwischen Pre-/Realtime-/WebGL-Pfad |
| `VideoFrameProcessor` | Frame-Extraction via ffmpeg |
| `ProcessingMethodExecutor` | Eigentliche Inferenz-Orchestrierung |
| `VideoJobManager` | Persistent Job-State |

### 2.2 Issue #64 wurde sauber gefixt

Der ffmpeg/ffprobe-Cold-Start-Race in drei Singletons ist gelöst:
- `_ffprobePath` `Services/VideoAnalyzer.cs:21` mit `UpdateFFprobePath()` Zeile 33
- `_ffmpegPath` `Services/VideoFrameProcessor.cs:24` und
  `Services/ProcessingMethodExecutor.cs:21` mit jeweils `UpdateFFmpegPath()`
- `EnsureFFmpegReady()` `Services/VideoProcessor.cs:121` als idempotenter Eintritts-Check

### 2.3 Bestehende Test-Infrastruktur

`JellyfinUpscalerPlugin.Tests/` mit 19 Test-Methoden (11 in
`HttpUpscalerServiceTests.cs`, 8 in `CacheManagerTests.cs`).
Python-Seite: `docker-ai-service/tests/` mit fünf Test-Dateien
(`test_auth.py`, `test_health.py`, `test_validation.py`, `test_semaphore.py`,
`conftest.py`), 373 Zeilen total.

### 2.4 Sicherheits-Hardening (was greift)

- `_require_api_token` `docker-ai-service/app/main.py:338` mit `hmac.compare_digest`
  Zeile 350, an 25+ Endpoints als Dependency
- 48× `[Authorize(Policy = "RequiresElevation")]` über die Controller verteilt
- `RealtimeStats` nutzt `deque(maxlen=...)` statt unbounded Akkumulatoren
  (`docker-ai-service/app/main.py:225,227`)
- `Scripts/verify-release.ps1` scannt Release-ZIPs auf Test-Artefakte
  (`Moq`, `Mono.Cecil`, `.pdb`, `runtimes/`)

## 3. Code-Qualität — was *nicht* stimmt (offene Probleme)

### 3.1 HttpUpscalerService bleibt eine Mini-God-Class

`Services/HttpUpscalerService.cs` umfasst 426 Zeilen und mischt fünf
Verantwortlichkeiten:

| Zeilen | Verantwortung |
|---|---|
| 19-69 | HttpClient-Lifecycle + Factory-Lookup |
| 71-87 | URL-Validierung (Scheme-Whitelist) |
| 24-42, 89-127 | 30 s-Health-Cache mit `_healthLock` |
| 156-228 | Currently-loaded-Model-Tracking + Semaphore |
| 230-386 | HTTP-Calls mit Retry/Exponential-Backoff |

Bricht das SRP. Tests können einzelne Concerns nicht isolieren.

### 3.2 Sicherheits-Lücken (heute, nicht in alter Analyse erwähnt)

| Schwere | Befund | Beweis |
|---|---|---|
| **Hoch** | `GetAvailableModels()` ohne `[Authorize]` — anonymer Modell-Katalog-Leak | `Controllers/UpscalerController.cs:150-152` |
| **Hoch** | `docker-publish.yml` 6 Actions ungepinnt — Tag-Swap-Angriff möglich. Datei-Kommentar `:15` weiß das selbst | `.github/workflows/docker-publish.yml:77,93,96,100,125,145,156` |
| **Hoch** | Python-SSRF-Blocklist fehlt komplett (kein 169.254/100.64/`::ffff:`-Schutz) | `docker-ai-service/app/main.py` |
| **Mittel** | C# `GetValidatedServiceUrl()` prüft nur Scheme, keine IP-Klassen | `Controllers/UpscalerController.cs:124-148` |

### 3.3 Versions-Drift (aktiv, betrifft Endnutzer)

Aktuell zehn Stellen mit der Plugin-Version, davon **drei mit Drift**:

| Datei | Wert |
|---|---|
| `JellyfinUpscalerPlugin.csproj:10-12` | 1.6.1.16 |
| `meta.json:6` | 1.6.1.16 |
| `manifest.json:12` | 1.6.1.16 |
| `repository-jellyfin.json:12` | 1.6.1.16 |
| **`repository-simple.json:11`** | **1.6.1.13** ← publiziertes Repo zeigt alte Version |
| `Configuration/configurationpage.html:3` | 1.6.1.16 |
| `Configuration/sidebar-upscaler.js:1,6` | 1.6.1.16 |
| **`docker-ai-service/app/main.py` `VERSION`** | **1.6.1.15** ← Container-Tag drifts |
| **`.github/workflows/docker-publish.yml:23`** | **1.6.1.13** (Default-Workflow-Input) |
| `docker-ai-service/Dockerfile*` | per `ARG APP_VERSION` aus Workflow-Input |

### 3.4 Manifest-Checksums sind weiterhin MD5

Trotz Behauptung der älteren Analyse, dass v1.5.5.9 SHA-256 brachte:
`manifest.json:16` zeigt einen 32-Hex-Hash (MD5). Nur die Einzel-Version
1.5.6.0 hat einen SHA-256.

## 4. Behauptungen der Vor-Analyse, die nicht stimmen

| Behauptung | Realität |
|---|---|
| Repo voller `backup_v*`/`publish_*`/`harmony.log.txt`/`temp_js.js`/`commit*.bat` | `git ls-files` → 0 Treffer. `csproj:50-52` schließt sie ohnehin aus |
| `[XmlIgnore]` auf Dictionary-Property in `PluginConfiguration` | Nicht vorhanden. 54 Properties, 16 Math.Clamp-Setter |
| Sprachverteilung HTML 37% / C# 36% | Real C# 52% / HTML 26% / Py 15% / JS 8% |
| SSH-Port 2222 aus Docker-Default exposed | Nicht mehr exposed (alle 6 Dockerfiles) |
| 11 Versions-Strings im Repo | 10 Stellen real |
| RealtimeStats unbounded | Bereits gefixt — `deque(maxlen=...)` |

## 5. Performance-Risiken (aus dem Code, nicht Benchmarks)

- **JPEG-Roundtrip:** Browser → JPEG-encode → HTTP-POST → JPEG-decode → ONNX → JPEG-encode → HTTP-Response → JPEG-decode → Render. Verlustbehaftet **doppelt**, blockiert HDR.
- **TensorRT-Default = Skip:** `SKIP_TENSORRT=true` per Default seit v1.5.3.1 — auf NVIDIA werden ~5–10× Performance verschenkt.
- **If/Elif-Backend-Selection** in `main.py:1429-1431,1983-1986,2105,2745-2747,2763,2878,2982` statt zentraler Factory — jede neue Backend-Integration muss alle Stellen anfassen.

## 6. Modell-Katalog (aktuell)

Backend-Selection-Code prüft sequentiell `cv_model` (OpenCV-DNN), `onnx_session`
(ONNX-Runtime), `ncnn_upscaler` (ncnn-Vulkan). Keine zentrale Factory.

`AVAILABLE_MODELS` ab `main.py:420`. Vier Modelle als `available: false`:
- `edvr-m-x4` (Zeile 790)
- `realbasicvsr-x4` (Zeile 802)
- `animesr-v2-x4` (Zeile 814)
- `apisr-x3` (Zeile 774)

Begründung: keine öffentlichen ONNX-Mirrors. Self-Host-Anleitung in `docs/MODEL-HOSTING.md`.

## 7. Empfehlungen (priorisiert)

| Priorität | Maßnahme | Aufwand |
|---|---|---|
| 1 | `RequiresElevation` auf `GetAvailableModels` | 5 min |
| 2 | `repository-simple.json` Versions-Fix | 1 min |
| 3 | SHA-Pinning `docker-publish.yml` | 1 h |
| 4 | `version.json` Single-Source-of-Truth + CI-Sync | 2 h |
| 5 | `HttpUpscalerService` SOLID-Split (4 Interfaces) | 6 h |
| 6 | Python-SSRF-Blocklist (analog zu C#) | 2 h |
| 7 | TensorRT-Engine-Pre-Compilation-Pipeline | 8-12 h |
| 8 | Modell-Backend-Factory (Spandrel-Pattern) | 16 h |

Die ersten vier laufen unter "Quick Wins" — alle separat committable, alle reviewbar.
Punkt 5 wird in Phase C dieses Plans umgesetzt; Punkt 7 in Phase D.

## 8. Vergleich mit Konkurrenz (kurz)

Der Plugin-Ansatz ist einzigartig durch direkte Jellyfin-Server-Integration
(Scheduled Tasks, Library-Scan, Player-Button) — diese Tiefe haben weder
`jellyfin-mpv-shim` (Cast-Client), `video2x` (Pre-Upscaling-CLI) noch
`chaiNNer` (Image-Editor). Pipeline-Qualität liegt jedoch hinter
`VSGAN-tensorrt-docker` (echte HDR mit RGBH FP16) und `mpv-upscale-2x_animejanai`
(Pre-compiled TensorRT-Engines).

**Stärke:** Server-side End-to-End-Integration für Jellyfin.
**Schwäche:** JPEG-HTTP-Frame-Transport limitiert Pipeline-Qualität fundamental.
