# Comprehensive State-of-Plugin Analyse — v1.7.3.1

**Datum:** 2026-05-12
**Version:** 1.7.3.1
**Stand:** 86 Releases (von v1.0.0 bis v1.7.3.1)
**Audit-Iterationen seit v21:** 14 (v1.6.1.17 → v1.7.3.1)
**Plugin-Health-Score:** 8.5 / 10

---

## 1. Executive Summary

Das **AI Upscaler Plugin** ist heute (v1.7.3.1) ein produktionsreifes Jellyfin-Plugin mit einer doppelten Architektur: Ein C#-Plugin (NET 9.0, Jellyfin ABI 10.11.8), das Library-Items scannt + persistiert, und ein Python/FastAPI Docker-Microservice (ONNX Runtime), der die eigentliche AI-Inferenz auf 6 Hardware-Backends übernimmt (CUDA/OpenVINO/ROCm/Apple/Vulkan/CPU). Beide Hälften sind über HTTP-API + Plugin-Config gekoppelt.

Die letzten 14 Audit-Iterationen haben einen **stabilen Drift-Prevention-Stack** etabliert (Registries + Drift-Lock-Tests + CI-Pre-Release-Gates + Quad-MD5-Verify), eine **konsolidierte AsyncPattern-Linie** für Frame-Pipelines (CancellationToken-Propagation + Linked-CTS + debounced Persist) und eine **honest UI** (keine Phantom-Features mehr — Mode-Dropdown spiegelt nur das, was der C#-Backend wirklich akzeptiert).

**Was es heute zuverlässig kann:**

- Scheduled Library-Scan mit selektivem Folder-Filter und Watched-Skip (fail-open).
- 4 Realtime-Modi im Player (Bilinear / Lanczos+Sharpen / Anime4K WebGL / WebGPU AI) + 5. ImportAlias `webgl` für Rückwärtskompatibilität.
- 59 Modell-Slots im Docker-Katalog (12 Kategorien), 6 Hardware-Backends mit Auto-Detect.
- 12 Output-Codecs für Scan-Re-Encode (Software + NVENC + QSV + Stream-Copy).
- Live-Filter-Preview (CSS-only, ~60 fps) + Server-side FFmpeg-Filter (Persist + Re-encode).
- GFPGAN/CodeFormer Face-Restoration.

**Was bewusst NICHT versucht wird:** Echtzeit-AI-Frame-Upscaling auf dem Server-FFmpeg-Pfad. AI-Upscale läuft entweder (a) **offline pre-cached** über Scan oder (b) **client-side WebGPU** im Browser. Server-side Realtime-AI ist Roadmap-Item v1.8.0/v2.0.0.

---

## 2. Plugin-Zweck und Architektur

### 2.1 Zwei-Hälften-Topologie

```
┌────────────────────────────────────────────────────────────────┐
│  Jellyfin Server (10.11.8)                                     │
│                                                                │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  JellyfinUpscalerPlugin (C# / NET 9.0)                  │  │
│  │  ─────────────────────────────────────────────────────  │  │
│  │  • Plugin manifest + Service registration               │  │
│  │  • LibraryUpscaleScanTask (IScheduledTask)              │  │
│  │  • UpscalerController (52 endpoints)                    │  │
│  │  • Persistent ProcessingQueue (debounced JSON)          │  │
│  │  • Configuration page (web UI, 2686 LoC HTML)           │  │
│  └─────────────────┬────────────────────────────────────────┘  │
│                    │                                            │
│                    │ HTTP (X-Api-Token auth)                    │
│                    │                                            │
└────────────────────┼────────────────────────────────────────────┘
                     │
                     ▼
┌────────────────────────────────────────────────────────────────┐
│  Docker Microservice (Python 3.11 / FastAPI)                   │
│  ────────────────────────────────────────────────────────────  │
│  • ONNX Runtime mit Provider-Auto-Detect                       │
│    (CUDA / OpenVINO / ROCm / CoreML / DirectML / CPU)          │
│  • 59 Modelle in models-fallback.json + main.py-Katalog        │
│  • /upscale, /models, /benchmark, /face-restore endpoints       │
│  • Operator-Dashboard (Grafana-Stil) mit Live-Latency-Sparkline│
└────────────────────────────────────────────────────────────────┘
```

### 2.2 Pfad-Übersicht: Was läuft wo?

| Funktion | Wo läuft die Logik? | Trigger |
|---|---|---|
| Library-Scan + Frame-Extraction | C# Plugin (FFmpegCore) | Scheduled-Task |
| AI-Inferenz auf extrahierten Frames | Docker Service (ONNX) | Plugin POST `/upscale` |
| Frame-Re-Encode in fertiges Video | C# Plugin (FFmpegCore) | Nach Inferenz |
| Realtime-Modes „Bilinear/Lanczos/Anime4K" | Browser (CSS / WebGL) | Player-UI Toggle |
| Realtime-Mode „WebGPU AI" | Browser (onnxruntime-web) | Player-UI Toggle |
| Live-Filter-Preview | Browser CSS-Filter (60 fps) | Filters-Tab Slider |
| Persisted Filter-Re-Encode | C# Plugin (FFmpeg-Filter-Graph) | POST `/filter-config` |
| Face-Restoration | Docker Service (GFPGAN/CodeFormer) | Plugin POST `/face-restore` |

---

## 3. Code-Inventar (verifiziert 2026-05-12)

| Komponente | Files | LoC | Anmerkung |
|---|---|---|---|
| **C# Produktion** | 30 | 9.767 | Services/, Controllers/, Models/, ScheduledTasks/, Configuration/ |
| **C# Tests** | 11 | ~1.500 | xUnit, 123 Tests, alle grün |
| **Python Service** | 1 (main.py) | 5.365 | FastAPI + ONNX Runtime |
| **Web UI** | 2.686 LoC HTML + 5 JS | — | configurationpage.html + player-integration + sidebar + webgpu-ai + ai-service-toggle |
| **Models-Fallback** | models-fallback.json | 59 entries | Mirror von Python-Katalog |
| **CI Workflows** | 5 | — | build, release, audit-checks (4 jobs), repo-sync, fallback-sync |
| **Marketing-Site** | 14 HTML files | — | site/ mit version-sync-Script |

### 3.1 Service-Klassen (26)

UpscalerCore (+ IUpscalerCore), VideoFrameProcessor, ProcessingMethodExecutor, ProcessingQueue, ProcessingStrategySelector, HardwareBenchmarkService, ModelAvailability, CacheManager, VideoFilterService, VideoAnalyzer, VideoProcessor, CodecRegistry, QualityLevelRegistry, ButtonPositionRegistry, RealtimeModeRegistry, HttpUpscalerService, FaceRestorationService, UserManagerAdapter (+ IUserManagerAdapter), PluginConfigManager, PathResolver, HardwareProfile, ModelManager, NotificationService, RemoteTranscodingService, SslHelper, WrapperInstaller.

### 3.2 Drift-Prevention-Registries (5)

Single-source-of-truth-Pattern für UI/Backend-Konsistenz:

- `CodecRegistry.OutputCodecs` (12 Einträge) + `RealtimeCodecs` (6 Subset) — eingeführt v1.6.1.23
- `QualityLevelRegistry.Levels` (3: low/medium/high) — eingeführt v1.7.0
- `ButtonPositionRegistry.Positions` (3: left/right/center) — eingeführt v1.7.0
- `RealtimeModeRegistry.UiModes` (5) + `BackwardsCompatAliases` (1: `webgl`) — eingeführt v1.7.1
- `VideoFilterService.SupportedPresets` — bereits vor v21 vorhanden

### 3.3 Test-Coverage

11 Test-Klassen mit 123 Tests, alle grün:

- **Registry-Drift-Lock**: `RegistryDriftLockTests` (Generic `[Theory]` über 5 Dropdowns, parst eingebettetes `configurationpage.html` und vergleicht UI-Werte gegen Registry-HashSet → bricht jeden Drift)
- **Pure-Logic**: 4× Registry-Tests + ProcessingStrategySelectorTests + ModelAvailabilityTests + UpscalerCoreAutoModelTests
- **I/O-Behavior**: CacheManagerTests + HttpUpscalerServiceTests + ProcessingQueueTests (debounced persist)
- **Adapter-Contracts**: UserManagerAdapterTests (fail-open guard)

Service-LoC-Coverage: ~28% (gemessen über die 9.767 productive LoC). Die wesentlichen Drift-Klassen sind aber abgedeckt — die ungetestete Mehrheit besteht aus Jellyfin-API-Wrappern, die ohne Integration-Test-Harness nicht sinnvoll mockbar sind.

---

## 4. Modell-Katalog (59 Slots, 12 Kategorien)

| Kategorie | Beispiele | Anzahl |
|---|---|---|
| Real-ESRGAN Familie | x4plus, anime6B, animevideo-v3 | 6 |
| ESRGAN klassisch | esrgan-x4, esrgan-x2 | 2 |
| SwinIR | classical-x2/x4, lightweight-x2/x4 | 4 |
| HAT (High-fidelity Att Trans) | hat-x2, hat-x4, hat-light | 3 |
| EDSR / RCAN | edsr-x2/x4, rcan-x2/x4 | 4 |
| Anime-spezifisch | animevideo-v3, waifu2x-cunet, apisr-x3 | 4 |
| Frame-Interpolation | rife-v4.7/v4.8/v4.9 | 3 |
| Video-Restoration | basicvsr, edvr-m-x4, realbasicvsr-x4 | 5 |
| Face-Restoration | gfpgan-v1.4, codeformer, gpen-512, restoreformer++ | 4 |
| Modern Compact | omnisr-x2/x4, dat-light-x2/x4, craft-x2/x4, man-x2/x4 | 8 |
| Denoise / Restore | nafnet-denoise, scunet | 2 |
| Misc / Compatibility | nomos8k-hat-x4, animesr-v2-x4 + Aliases | 14 |

**Auto-Selection-Logik**: `UpscalerCore.SelectModelAuto()` wählt anhand Hardware-Profil + Content-Hint (anime/realistic/face) + Quality-Setting den geeigneten Slot. Test-abgedeckt in `UpscalerCoreAutoModelTests`.

**Sync-Garantie**: `Scripts/sync-fallback-models.ps1` mirrort den Python-Katalog → `Resources/models-fallback.json` + CI-Job `verify-fallback-sync` bricht bei Drift.

**Self-Host-Hinweise**: 5 Modelle ohne öffentlichen ONNX-Mirror sind explizit als `[self-host required]` markiert mit Verweis auf `docs/MODEL-HOSTING.md` (PyTorch→ONNX-Rezept).

---

## 5. Audit-Trajektorie (v1.6.1.17 → v1.7.3.1)

14 Iterationen, jede mit externer Audit-Vorlage + Maintainer-Self-Verify + Release-Notes + Quad-MD5-Verification:

| Release | Highlight | Anzahl Fixes |
|---|---|---|
| v1.6.1.17 | Async-pattern audit, ScanTask CT propagation | 8 |
| v1.6.1.18 | Model-catalog cleanup (32→48), MODEL-HOSTING.md | 5 |
| v1.6.1.19 | ModelAvailability fail-open, HardwareBench timeout | 4 |
| v1.6.1.20 | CacheManager LRU contract, CodecRegistry seed | 6 |
| v1.6.1.21 | LibraryScan watched-skip (inline IsAnyUserPlayed) | 4 |
| v1.6.1.22 | UI dead-control purge (30 controls) | 30+ |
| v1.6.1.23 | CodecRegistry production-rollout (4 allowlists → 1) | 4 |
| **v1.7.0** | Frame-loop CT-Adoption, ProcessingQueue debounced persist, QualityLevel+ButtonPosition Registries, Anime4K integration | 12 |
| **v1.7.1** | RealtimeModeRegistry + alias-pattern, WebGPU AI Mode, RegistryDriftLockTests | 8 |
| **v1.7.2** | Math.Clamp DoS-hardening (18 Properties), 6 neue Modelle, ProcessingStatus.Analyzing weg, ProcessingQueueTests | 9 |
| **v1.7.3** | meta.json-in-ZIP verify gate, /cache/config endpoint removal, site/models.html sync (11 missing), UpscalerSettings dead-code purge | 7 |
| **v1.7.3.1** | Hotfix GetJavaScript endpoint, IUpscalerCore+IUserManagerAdapter Interface-Extraction, UserManagerAdapterTests | 5 |

**Cumulative Trend**: Pro Iteration ~5–10 echte Bugs gefangen. Drift-class bugs (Sibling-Bugs aus derselben Klasse) sind seit v1.7.1 deutlich rückläufig — Registry+Drift-Lock-Tests fangen sie zur Build-Zeit statt zur Audit-Zeit.

---

## 6. Was funktioniert (verifizierte Garantien)

### 6.1 Cold-Start Stability
`VideoProcessor.EnsureFFmpegReady()` late-resolved FFmpeg/FFprobe-Pfade aus MediaEncoder, propagiert in VideoAnalyzer + VideoFrameProcessor + ProcessingMethodExecutor. Scheduled-Task funktioniert auch wenn Plugin vor MediaEncoder bootstrapped (Issue #64).

### 6.2 Cancellation-Propagation
Frame-Pipeline akzeptiert CT vom Scheduler bis runter zum `UpscaleImageAsync(..., cancellationToken)`-Call (v1.7.0 schloss das letzte Loch in VideoFrameProcessor.cs:252). ProcessingMethodExecutor nutzt linked-CTS-Pattern für Timeout+CT-Combo.

### 6.3 Persist-Reliability
`ProcessingQueue` schreibt JSON via debounced Timer (500 ms quiet window) + SemaphoreSlim (non-overlapping writer). 5 synchrone Caller wurden auf `RequestPersist()` umgestellt — keine wartet mehr auf Disk-I/O.

### 6.4 Drift-Detection
5 Registries + 1 generic `[Theory]`-Drift-Test + 4 CI-Pre-Release-Gates. UI-Dropdown-Werte und Controller-Allowlists können nicht mehr ungestraft auseinanderlaufen.

### 6.5 Honest UI
Keine Phantom-Features. Realtime-Modes spiegeln 1:1 was im Player-JS implementiert ist (5 Modes + 1 backwards-compat-alias). 6 weitere Toggles tragen XML-doc-Disclaimer „currently no-op pending v1.8.0 pipeline".

### 6.6 DoS-Hardening
18 Property-Setter in PluginConfiguration nutzen `Math.Clamp(value, lower, upper)` statt `Math.Max(value, lower)` — int.MaxValue-Payloads können nicht mehr in saved configs durchschlüpfen.

### 6.7 Release-Integrity
**Quad-MD5 Verify** vor jedem Release: lokale ZIP-MD5 == GitHub-Asset-MD5 == manifest.json-checksum == repository-jellyfin.json-checksum. v1.7.3 fügte zusätzlich „meta.json-in-ZIP version match"-Gate hinzu (nach v1.7.0-Vorfall).

### 6.8 Test-Seams
`IUpscalerCore` (2 Methoden, minimal-surface) + `IUserManagerAdapter` (1 Methode) als Test-Schnittstellen. Production-Code nutzt weiter konkrete Klassen über DI-Factory-Pattern (`sp.GetRequiredService<UpscalerCore>()`), Tests können jetzt Frame-Pipeline + Watched-Logic isoliert testen.

---

## 7. Bekannte offene Surfaces (transparent dokumentiert)

### 7.1 v1.8.0-Pipeline (Pipeline-Parallelization)
`Channel<T>`-basierte concurrent extract/inference/encode-Stages. Aktuell sequential: 1 Frame extrahieren → 1 inference call → 1 encode. v1.8.0 wird das in 3 Channel-konnektierte Tasks aufteilen. Bottleneck-Messung steht aus.

### 7.2 v2.0.0-Multi-Frame VSR (Roadmap)
EDVR/RealBasicVSR brauchen Temporal-Context (5-frame window). Aktueller Single-Frame-Pfad reicht nicht. Self-Host-only bis dann.

### 7.3 Service-LoC-Coverage 28%
Trotz +2 Tests in v1.7.3.1 (UserManagerAdapter) bleibt ein großer Teil ungetestet:
- `VideoFrameProcessor` Frame-Loop (CT-Propagation via `IUpscalerCore`-Mock) — geplant v1.7.4
- `ProcessingMethodExecutor` Linked-CTS (Process-Mock) — geplant v1.7.4
- `UserManagerAdapter` PlayCount/Played-Flag-Szenarien (Jellyfin.Data Package-Ref nötig) — geplant v1.7.4
- `HardwareBenchmarkService` (volle Coverage erfordert ONNX-Inferenz-Mock)

### 7.4 WebGPU AI Mode = Best-Effort
`webgpu-ai-realtime.js` lädt onnxruntime-web@1.20.1 + Real-ESRGAN compact aus 2 CDN-Quellen mit 4-stage fallback. Falls beide CDNs blocked → graceful fallback auf Lanczos. Kein Server-side Mirror der Models.

### 7.5 Browser-Quirks beim Anime4K-Pfad
Anime4K.js lädt via CDN-Script-Tag. iOS-Safari hat WebGL2-Quirks, Anime4K-A-Mode kann auf älteren Geräten ruckeln. Doc-only-Hinweis in den Realtime-Mode-Tooltips ausstehend (v1.7.4 Polish).

---

## 8. Risiko-Bewertung

| Risiko | Wahrscheinlichkeit | Impact | Mitigations heute |
|---|---|---|---|
| Drift UI↔Controller | **niedrig** | hoch | 5 Registries + Drift-Lock-Tests + 1 CI-Gate (`audit-tryapply-lambdas`) |
| Wrong-ZIP-Upload | **niedrig** | mittel | Quad-MD5 + meta.json-in-ZIP Verify (v1.7.0-Vorfall festgenagelt) |
| Frame-Loop CT-Hole | **sehr niedrig** | mittel | v1.7.0 closed; Test-Seam in v1.7.3.1; Test geplant v1.7.4 |
| ProcessingQueue I/O blockt | **sehr niedrig** | mittel | v1.7.0 debounced persist; Tests in v1.7.2 |
| Watched-Skip schlägt fehl | **niedrig** | niedrig | Fail-open guard (returns false → Item wird gescannt); Test in v1.7.3.1 |
| DoS-Payload sprengt config | **niedrig** | niedrig | 18 Math.Clamp-Setter (v1.7.2) |
| Self-Host-Model down | **mittel** | niedrig | 5 explizit als self-host markiert; UI filtert sie aus |
| Docker-Service offline | mittel | hoch | Standalone-Mode + neutrale UI-State (kein rotes Error) + Filter-Live-Preview funktioniert client-only |
| Jellyfin-ABI-Break 10.12 | **mittel** | mittel | targetAbi pin auf 10.11.8; Major-Updates triggern Repo-Manifest-Refresh |

**Aggregat-Risk**: niedrig. Die hochfrequenten Drift-Klassen sind geschlossen. Verbleibende Risiken sind eher Roadmap-Items (Pipeline-Parallelization, Multi-Frame-VSR) als akute Bugs.

---

## 9. Roadmap-Bewertung

| Version | Scope | Realistisches Lieferdatum | Risiko |
|---|---|---|---|
| **v1.7.4** | Phase-E-Tests (VideoFrameProcessor, ProcessingMethodExecutor, UserManagerAdapter PlayCount) + Jellyfin.Data Package-Ref + Realtime-Mode-Tooltips (iOS/Browser-Quirks) | 1–2 Wochen | gering |
| **v1.8.0** | Pipeline-Parallelization (`Channel<T>`-extract/inference/encode) + Backpressure + Throughput-Bench | 4–6 Wochen | mittel — needs perf-baseline measurement |
| **v2.0.0** | Multi-Frame-VSR (EDVR temporal window, RealBasicVSR recurrent) in Realtime — server-side AI realtime | 3–6 Monate | hoch — ONNX export pipeline für temporal models + GPU-RAM-Pressure |

**Empfehlung**: v1.7.4 als reine **Test-Coverage-Iteration** (+ kosmetische Tooltips) ist die richtige nächste Stufe — kein neues Feature, dafür hebt es die Confidence-Schwelle, bevor v1.8.0 den Frame-Pipeline-Code chirurgisch aufschneidet.

---

## 10. Drift-Prevention-Architektur (4 Layer)

```
   Layer 1: Source-of-Truth                Layer 2: Drift-Tests
   ───────────────────────────             ───────────────────────────
   CodecRegistry           ──┐
   QualityLevelRegistry      ├──>  RegistryDriftLockTests
   ButtonPositionRegistry    │       [Theory] über 5 Dropdowns
   RealtimeModeRegistry      │       parst configurationpage.html
   VideoFilterService      ──┘       vergleicht UI ⇄ Registry

   Layer 3: CI-Pre-Release-Gates           Layer 4: Release-Verify
   ───────────────────────────             ───────────────────────────
   • verify-fallback-sync                  • Quad-MD5
   • audit-tryapply-lambdas                • meta.json-in-ZIP-version
   • zip-version-check                     • repo-feed sync
   • verify-site-sync
```

Diese 4 Layer arbeiten **stacked**: Wer eine UI ändert ohne Registry, scheitert an Layer 2. Wer eine Registry ändert ohne CI-Mirror-Update, scheitert an Layer 3. Wer ein ZIP mit falscher Version uploadet, scheitert an Layer 4. **Diamond-defense**.

---

## 11. Empfehlungen

### 11.1 Kurzfristig (v1.7.4, 1–2 Wochen)
1. `JellyfinUpscalerPlugin.Tests` um `Jellyfin.Data` Package erweitern → PlayCount + Played-Flag Tests
2. `VideoFrameProcessorTests` mit `Mock<IUpscalerCore>` → verifiziere CT-Adoption an L252 + Re-Encode-Skip-Bedingungen
3. `ProcessingMethodExecutorTests` mit Process-Mock → verifiziere Linked-CTS-Cleanup unter Timeout
4. Realtime-Mode-Tooltips: iOS-Safari WebGL2-Quirk, WebGPU-Browser-Support-Matrix
5. README + site/index.html: v1.7.3.1 → v1.7.4 sync

### 11.2 Mittelfristig (v1.8.0, 4–6 Wochen)
1. Throughput-Baseline-Bench gegen aktuellen sequential Path (Frames/Minute pro Backend)
2. `ProcessingPipeline` mit 3 `Channel<T>` (extract→inference→encode) + Backpressure
3. Bench wiederholen, mindestens 2× Speedup ist Akzeptanzkriterium — sonst lieber zurück und Re-Architektur
4. Doc: Pipeline-Diagramm, wann hilft Parallelisierung, wann ist Disk-I/O bottleneck

### 11.3 Langfristig (v2.0.0, 3–6 Monate)
1. ONNX-Export-Pipeline für temporal-context Models (EDVR-M, RealBasicVSR-x4)
2. Server-side AI Realtime: GPU-RAM-Profil + Frame-Buffer-Management
3. Decision: bleibt das ein Server-Pfad, oder wandert es komplett in den Browser (WebGPU AI Multi-Frame)?

### 11.4 Wartungs-Hygiene (laufend)
- Jeden externen Audit verifizieren bevor Code geändert wird (v1.7.3-Lesson: audit claimed CPUInfo/MemoryInfo dead, war es nicht — transitive-reachability via BenchmarkResults).
- Vor jedem File-Delete: `find . -type f \( -name '*.cs' -o -name '*.json' -o -name '*.html' \) | xargs grep -l 'TypeName'` und mindestens 1 Tag warten.
- Quad-MD5 + meta.json-in-ZIP-Verify bei JEDEM Release, nicht nur major.

---

## 12. Plugin-Health-Score: 8.5 / 10

| Dimension | Score | Begründung |
|---|---|---|
| **Code-Qualität** | 9 / 10 | Konsistente Pattern, Math.Clamp-Hardening, Interface-Extraction |
| **Test-Coverage** | 6 / 10 | 123 Tests grün, aber nur ~28% Service-LoC; Frame-Loop noch ungetestet |
| **Drift-Prevention** | 10 / 10 | 4-Layer-Diamond-Defense; sibling-bugs praktisch verschwunden |
| **Honest UI** | 9 / 10 | Phantom-Features weg; Tooltips für Browser-Quirks fehlen noch |
| **Release-Process** | 10 / 10 | Quad-MD5 + meta.json-in-ZIP + 4 CI-Gates; v1.7.0-Vorfall hat das ganze System gehärtet |
| **Docs** | 8 / 10 | MODEL-HOSTING.md + Release-Notes je Version + Audit-Trajektorie nachvollziehbar; Architektur-Diagram nur in dieser Analyse |
| **Roadmap-Realismus** | 9 / 10 | v1.7.4 ist achievable, v1.8.0 hat klares Akzeptanzkriterium (2× Speedup), v2.0.0 ist groß aber ehrlich-skoped |
| **Operational-Maturity** | 8 / 10 | Standalone-Mode wenn Docker offline, neutrale Error-States, Recovery-Path dokumentiert |

**Score-Treiber nach unten:** Test-Coverage und fehlende Architektur-Doku waren bisher Schwachpunkte. Beide adressiert in v1.7.3.1 (Interface-Extraction + diese Analyse) — v1.7.4 schließt die Test-Lücke vollständig.

---

## 13. Fazit

Das Plugin hat sich in 14 Audit-Iterationen von einem Plugin mit unklarem Surface (v21: Phantom-Realtime-Modes, Drift-überall, kein async-CT-Pattern) zu einem **vorhersagbaren, getesteten, ehrlichen System** entwickelt.

Die **drei prägenden Entscheidungen**:

1. **Registry-Pattern + Drift-Lock-Tests** statt manueller Konsistenz-Wartung. Eine Quelle, ein Test, fertig.
2. **Honest UI** statt Roadmap-Werbung. Was nicht implementiert ist, steht nicht im Dropdown — oder ist explizit als „pending" XML-doc-markiert.
3. **Interface-Extraction für Testbarkeit** statt Big-Bang-Refactor. Minimal-surface Interfaces (2-Methoden, 1-Methode), DI-Factory-Pattern für Single-Instance-Garantie. Production-Code bleibt unangetastet.

Die **realistische nächste Stufe** ist v1.7.4 als reine Test-Iteration. Erst danach v1.8.0 als Pipeline-Parallelization. v2.0.0 ist groß aber ehrlich-skoped und braucht einen ONNX-Export-Vorlauf.

**Stand v1.7.3.1**: Production-ready. Build grün. 123 Tests grün. Quad-MD5 verifiziert. Audit-Trajektorie nachvollziehbar. Plugin-Health 8.5 / 10.

---

## 14. Appendix: Versions-Schnellreferenz

```
v1.6.1.16  — FFmpeg cold-start fix (Issue #64)
v1.6.1.17  — Async pattern audit start
v1.6.1.22  — UI dead-control purge
v1.6.1.23  — CodecRegistry rollout
v1.7.0     — Frame-loop CT, debounced persist, Anime4K
v1.7.1     — RealtimeModeRegistry + WebGPU AI + Drift-Lock-Tests
v1.7.2     — Math.Clamp hardening + 6 neue Modelle + Analyzing-purge
v1.7.3     — meta.json-in-ZIP gate + /cache/config remove
v1.7.3.1   — Interface-Extraction + UserManagerAdapter tests   ← AKTUELL
v1.7.4     — Phase-E tests (geplant, 1–2 Wochen)
v1.8.0     — Pipeline parallelization (geplant, 4–6 Wochen)
v2.0.0     — Multi-Frame VSR realtime (geplant, 3–6 Monate)
```

---

**Analyse-Autor:** Claude Opus 4.8 als Maintainer-Assistant
**Quellen:** Code-Inspection 2026-05-12, alle Release-Notes seit v1.6.1.16, externer Audit-Trail (14 Iterationen), Build-Output, Test-Run (123/123).
