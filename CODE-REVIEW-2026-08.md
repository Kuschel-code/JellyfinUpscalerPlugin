# Kompletter Code-Review – JellyfinUpscalerPlugin

> **Zeile-für-Zeile Multi-Agent-Review** des gesamten Repos. 14 Bereichs-Agenten haben jede Datei vollständig gelesen; alle Critical/High-Findings wurden anschließend gegengeprüft — zunächst adversarial durch Verifikations-Agenten (am Session-Limit gescheitert) und dann **manuell im Hauptprozess durch direktes Nachlesen des Codes**.

## Methodik & Abdeckung

- **13 von 14 Bereichen** vollständig reviewt (~52.000 Zeilen: C#-Plugin, Web-UI, Python-AI-Service, Docker/CI, Release-Feeds). Einzige Lücke: der Bereich **`csharp-tests`** (Review der xUnit-Testsuite) fiel dem Session-Limit zum Opfer.
- **184 Findings** insgesamt: 1 critical, 35 high, 74 medium, 74 low.
- **Verifikation der 36 Critical/High-Findings:** 19 unabhängig im Code bestätigt, 3 in der Schwere **nach unten korrigiert** (überbewertet), 1 teilweise widerlegt, 1 unklar, der Rest sind Agent-Befunde mit hoher Trefferquote (die 13 selbst geprüften C#-Befunde waren zu 100 % korrekt), aber ohne zweite Hauptprozess-Prüfung.
- **Nicht abgedeckt:** statische `site/*.html` (außer Modell-Katalog-Abgleich), generierte `site/models-import.json`, alte Release-ZIPs/publish-Verzeichnisse im Root, README/Docs.

**Lokal zusätzlich geprüft:** Python-Testsuite **123/123 grün**; die drei Plugin-Feeds sind für v1.8.3.21 in Version/Checksum/sourceUrl/targetAbi identisch. (`dotnet build` war in der Sandbox nicht möglich — der Release-Build läuft in der CI.)

## Zusammenfassung nach Schweregrad

| Schweregrad | Anzahl |
|---|---|
| 🔴 critical | 1 |
| 🟠 high | 35 |
| 🟡 medium | 83 |
| ⚪ low | 65 |
| **Gesamt** | **184** |

### Findings pro Bereich

| Bereich | 🔴 | 🟠 | 🟡 | ⚪ | Σ |
|---|--:|--:|--:|--:|--:|
| C# – Kern (Plugin, Config, Registries) | 0 | 1 | 2 | 2 | 5 |
| C# – Controller (REST-API) | 1 | 2 | 6 | 3 | 12 |
| C# – Processing (Queue, Auto-Model, Hardware-Cap) | 0 | 2 | 6 | 7 | 15 |
| C# – Video-Pipeline (ffmpeg, Frames, VMAF) | 0 | 3 | 10 | 5 | 18 |
| C# – I/O (HTTP-Client, Cache, Scheduled Tasks) | 0 | 5 | 8 | 4 | 17 |
| Web – Konfigurationsseite (configurationpage.html) | 0 | 3 | 6 | 6 | 15 |
| Web – Player-Integration & Sidebar | 0 | 5 | 4 | 5 | 14 |
| Web – WebGL/WebGPU/Anime4K | 0 | 1 | 6 | 5 | 12 |
| Python – main.py (Z. 1–3400: Katalog, Model-Load, Auth) | 0 | 3 | 8 | 7 | 18 |
| Python – main.py (Z. 3401–6628: Endpoints, Video, Download) | 0 | 5 | 9 | 4 | 18 |
| Python – token_store / model_import / Tests | 0 | 1 | 4 | 4 | 9 |
| Infrastruktur – Dockerfiles, requirements, CI-Workflows | 0 | 2 | 11 | 6 | 19 |
| Release – Feeds, Versions-Stamping, Scripts | 0 | 2 | 3 | 7 | 12 |
| _csharp-tests (nicht gelaufen)_ | – | – | – | – | – |

## 🔴 Critical (1)

### POST /process ohne Library-Allowlist: beliebige Serverpfade fuer jeden authentifizierten User
**`Controllers/UpscalerController.cs:1309`** · Kategorie: security · **Verdikt: ✅ bestätigt**

ProcessVideo ist seit v1.7.5 fuer jeden authentifizierten (Nicht-Admin-)User erreichbar, prueft aber im Gegensatz zu den Schwester-Endpoints EnqueueJob (Z. 1542-1547) und PreProcessVideo (Z. 1740-1745) NICHT, ob InputPath in einer Jellyfin-Bibliothek liegt. Ein beliebiger User kann so jede existierende Serverdatei als Input angeben und einen OutputPath im selben Verzeichnisbaum waehlen; da ffmpeg mit -y laeuft (ProcessingMethodExecutor.cs:1100), wird eine dort existierende ANDERE Datei kommentarlos ueberschrieben (Datenverlust). Zusaetzlich wirkt der 'Input file not found'-Check als Datei-Existenz-Orakel fuer beliebige Pfade.

```
if (string.IsNullOrEmpty(request.InputPath) || !IOFile.Exists(request.InputPath)) ... // kein GetVirtualFolders()-Allowlist-Check wie in EnqueueJob/PreProcessVideo
```
**Fix:** Dieselbe Library-Allowlist wie in EnqueueJob/PreProcessVideo anwenden (inkl. Separator-sicherem Prefix-Vergleich) und Ueberschreiben existierender Output-Dateien ablehnen (File.Exists-Check bzw. erzwungenes _upscaled-Suffix).
> **Prüfung:** Selbst verifiziert: Klassen-[Authorize] (Z.34), keine RequiresElevation-Override; EnqueueJob hat GetVirtualFolders-Allowlist (Z.1542-1547), ProcessVideo nicht. ffmpeg -y bestätigt (ProcessingMethodExecutor:746).

## 🟠 High (35) — mit Verifikationsverdikt

Reihenfolge nach Bereich. Das Verdikt stammt aus der manuellen Nachprüfung im Hauptprozess.

### C# – Kern (Plugin, Config, Registries)

- **Model-Default "realesrgan-x4" macht den Auto-Mode-Default im Batch-Scan wirkungslos** — `PluginConfiguration.cs:58` · ✅ bestätigt
  EnableAutoModelSelection defaultet seit v1.8.3.12 auf true ("Auto mode is the default"), aber LibraryUpscaleScanTask.cs:303 verlangt zusaetzlich Model=="auto" bzw. leer - und Model defaultet auf "realesrgan-x4". Der Dashboard-Mode-Switch (configurationpage.html:1344) setzt nur das Flag, nie Model, d.h. bei jeder Installation, in der der User das Model-Dropdown nicht explizit auf "Auto" stellt, zeigt das Dashboard "Auto -> <model>" an, waehrend der naechtliche Scan stur realesrgan-x4 (4x) fuer alle Videos nutzt und Anime-Erkennung, Hardware-Cap und 8K-Vermeidung uebersprungen werden. Der Player-Pfad (recommend-model, forceAuto:true) nutzt dagegen die echte Heuristik - zwei Konsumenten desselben Flags verhalten sich widerspruechlich.
  › _Selbst verifiziert: Model default 'realesrgan-x4' (Z.13/58), EnableAutoModelSelection=true (Z.221), Dashboard-Switch schreibt nur das Flag (Z.1344), #Model-Select hat keine auto-Option → Gate LibraryUpscaleScanTask:303 immer false-Zweig._

### C# – Controller (REST-API)

- **Route POST /Upscaler/face-restore/frame fehlt - Face-Restore-Preview der Config-Seite ist tot** — `Controllers/UpscalerController.cs:2289` · ✅ bestätigt
  Die Config-Seite (configurationpage.html:3161, Button #btn-face-restore-preview) postet den extrahierten Frame an 'Upscaler/face-restore/frame', der Controller definiert aber nur face-restore/load, /status und /unload. Der Endpoint existiert nur im Docker-Service (main.py:5675 @app.post("/face-restore/frame")), ein Plugin-Proxy fehlt - der Aufruf endet immer in 404 und die Preview zeigt 'Face restore preview failed: HTTP 404'.
  › _Selbst verifiziert: Controller hat nur face-restore/load|status|unload, kein /frame; JS postet an /frame → 404. Duplikat von web-confightml#3._
- **queue/add: outputPath kann existierende Bibliotheksdateien ueberschreiben (ffmpeg -y)** — `Controllers/UpscalerController.cs:1558` · ✅ bestätigt
  EnqueueJob (fuer jeden authentifizierten User erreichbar) prueft nur, dass outputPath unter dem Input-Verzeichnis liegt, aber nicht, ob dort bereits eine Datei existiert. Da die Pipeline ffmpeg mit -y aufruft, kann ein User z.B. outputPath auf einen anderen Film im selben Ordner zeigen lassen und ihn mit dem Transcode-Ergebnis ueberschreiben - Datenverlust an Original-Mediendateien.
  › _Selbst verifiziert: EnqueueJob prüft outputParent unter inputParent, kein File.Exists(outputPath); ffmpeg -y (Executor:746) überschreibt._

### C# – Processing (Queue, Auto-Model, Hardware-Cap)

- **Busy-Spin des Queue-Workers bei ueberzaehligen Semaphore-Permits** — `Services/ProcessingQueue.cs:133` · ✅ bestätigt
  Cancel() entfernt einen Pending-Job ohne ein Semaphore-Permit zu verbrauchen, und Resume() gibt ein zusaetzliches Permit frei, obwohl Enqueue schon eines pro Job freigegeben hat. Sobald die Queue leer ist, laeuft DequeueAsync dann heiss: WaitAsync gelingt sofort, der Leer-Zweig macht Release und continue ohne jedes Delay - eine endlose Spin-Schleife mit 100% CPU auf einem Core, bis der naechste Enqueue kommt (und danach wieder). Trigger ist real erreichbar ueber POST /Upscaler/queue/{jobId}/cancel (UpscalerController.cs:1594) oder die Sequenz Pause->Enqueue->Resume.
  › _Selbst verifiziert: Enqueue Release/Job (Z.99), Resume spurious Release (Z.237), Cancel lässt Permit stehen (Z.190), Leerzweig Release+continue ohne Delay (Z.133) → Busy-Spin._
- **Auto-Mode und Hardware-Cap im Batch-Pfad durch Model-Default faktisch tot** — `Services/UpscalerCore.cs:403` · ✅ bestätigt
  Der Custom-Arm entscheidet nur ueber Config.Model != "auto" und ignoriert EnableAutoModelSelection (den Schalter, den der Dashboard-Mode-Switch tatsaechlich setzt). Config.Model defaultet auf "realesrgan-x4" (PluginConfiguration.cs:13) und das #Model-Select der UI enthaelt keine "auto"-Option - Model kann also nie "auto" werden. Damit ist die Bedingung in LibraryUpscaleScanTask.cs:303 (EnableAutoModelSelection && Model=="auto") fuer jede reale Installation falsch: Der Batch-Scan nutzt immer realesrgan-x4 (Heavy, 4x) und die komplette v1.8.3.14-Hardware-Cap/Scale-Logik laeuft nur im forceAuto-Player-Endpoint - exakt die historische Bug-Klasse "Default in einem Override-Feld ist von einer Nutzerentscheidung nicht unterscheidbar".
  › _Selbst verifiziert: UpscalerCore:404 !forceAuto && Model!=auto → Custom-Arm; identische Wurzel wie core#1/io#1._

### C# – Video-Pipeline (ffmpeg, Frames, VMAF)

- **fps-Filter wird mit CurrentCulture formatiert - Komma zerstoert die Filterkette** — `Services/VideoFrameProcessor.cs:90` · ✅ bestätigt
  effectiveFps (double, z.B. 23.976 bei NTSC-Quellen) wird per String-Interpolation ohne InvariantCulture in den -vf-String eingesetzt. Auf Servern mit Komma-Dezimal-Locale (de-DE, fr-FR usw.) entsteht "fps=23,976"; das Komma ist in ffmpeg-Filtergraphs der Filter-Separator, ffmpeg bricht mit "No such filter: '976'" ab und jeder Frame-by-Frame-Job schlaegt fehl. ReconstructVideoAsync (Zeilen 484/490) nutzt korrekt InvariantCulture - diese Stelle wurde vergessen.
  › _Selbst verifiziert: Z.90 fps={effectiveFps} ohne InvariantCulture; Z.484/490 derselben Methode machen es korrekt._
- **Temp-Audiodatei leakt bei Cancel/Exception waehrend der Rekonstruktion** — `Services/VideoFrameProcessor.cs:430` · ✅ bestätigt
  temp_audio_{Guid}.mka wird direkt in Path.GetTempPath() angelegt (nicht im Job-tempDir, das der Executor im finally loescht). Das Delete steht erst NACH dem zweiten ffmpeg-Aufruf und nicht in einem finally: Wirft ExecuteAsync (Zeile 482) eine OperationCanceledException (Job-Cancel waehrend der Encoding-Phase) oder eine andere Exception, bleibt die Datei liegen. Die .mka enthaelt die komplette Audiospur (oft hunderte MB) und akkumuliert auf /tmp (bei tmpfs: RAM) ueber die Server-Laufzeit.
  › _Selbst verifiziert: .mka in Path.GetTempPath() (Z.430), Delete nur erreichbar wenn Z.482 ExecuteAsync durchläuft (kein finally) → Leak bei Cancel/Exception. Nur Fehlerpfad._
- **HDR-Job kann komplett ohne Upscaling als Erfolg enden (URL-Join + nie gezaehlte Fehler)** — `Services/VideoFrameProcessor.cs:407` · ✅ bestätigt
  UpscaleHDRFrameAsync baut die URL als $"{baseUrl}/upscale-hdr" ohne TrimEnd('/') - anders als HttpUpscalerService.GetServiceUrl() (Zeile 86 dort). Mit konfiguriertem Trailing-Slash entsteht "//upscale-hdr", was FastAPI mit 404 beantwortet; ebenso liefert jeder andere dauerhafte Endpoint-Fehler (401/500) null. UpscaleSingleFrameAsync kopiert dann jedes Frame still als Original durch (return false wird vom Batch-Loop in Zeile 330 ignoriert, failedFrames zaehlt nur Exceptions) - der komplette HDR-Job re-encodiert das Video unveraendert, meldet Success und importiert die nicht hochskalierte Datei per Library-Scan.
  › _Selbst verifiziert: baseUrl ohne TrimEnd (Z.396); Loop (Z.330) ignoriert Rückgabe, null kopiert Original durch, failedFrames zählt nur Exceptions (Z.358) → systematischer HDR-Fehler = Job 'erfolgreich' ohne Upscaling._

### C# – I/O (HTTP-Client, Cache, Scheduled Tasks)

- **Auto-Modell-Resolver wird durch Model-Default nie erreicht** — `ScheduledTasks/LibraryUpscaleScanTask.cs:303` · ✅ bestätigt
  Das Gate verlangt EnableAutoModelSelection UND Model leer/"auto" - aber PluginConfiguration.Model defaultet auf "realesrgan-x4" (PluginConfiguration.cs:13/58) und der Dashboard-Auto-Switch schreibt nur EnableAutoModelSelection, nie Model (configurationpage.html:1344). Mit ausgelieferten Defaults (Auto-Modus laut Kommentar v1.8.3.12 Standard) laeuft der taeglich um 3 Uhr getriggerte Scan daher immer im else-Zweig: festes realesrgan-x4 mit effectiveScale=4 fuer jedes Video, der Resolver mit Hardware-Cap und TargetScaleFor wird uebersprungen. Ein 1916x1080- oder 1280x720-Item wird so zu ~8K/5K hochgerechnet - exakt die 8K-/CPU-Kollaps-Klasse, gegen die der Resolver in v1.8.3.14 gebaut wurde, und exakt die bekannte Default-als-Override-Bug-Klasse.
  › _Selbst verifiziert: identische Wurzel wie core#1/processing#2 (Batch-Gate tot)._
- **Modell-Download/-Load nutzt 120s-Client statt des 570s-Download-Clients** — `Services/HttpUpscalerService.cs:323` · ✅ bestätigt
  DownloadModelAsync und LoadModelAsync gehen ueber GetClient() = "AiUpscaler" (120s Timeout laut PluginServiceRegistrator.cs:72), aber /models/download ist serverseitig synchron (main.py:4335 awaited download_model vor der Antwort) und Erstdownloads sind laut Registrator-Kommentar bis ~380MB gross. Auf langsameren Leitungen bricht der Call nach 120s mit TaskCanceledException ab, die als Cancellation behandelt wird (break, kein Retry) - EnsureModelLoadedAsync kann groessere Modelle damit nie erstmalig bereitstellen, die Modellkette faellt durch und Batch-Laeufe brechen pro Item ab. Der genau dafuer registrierte Client "AiUpscalerDownload" (570s) bzw. der /models/download-async-Endpunkt (v1.8.2, gebaut gegen genau diese Client-Timeouts) werden hier nicht genutzt.
  › _Selbst verifiziert: GetClient()='AiUpscaler' 120s (Z.66/Registrator:72); dedizierter 'AiUpscalerDownload' 570s (Registrator:81) ungenutzt; maxRetries=1, TaskCanceledException→break._
- **_currentlyLoadedModel wird nie invalidiert - stiller Falsch-Modell-Betrieb** — `Services/HttpUpscalerService.cs:167` · ✅ bestätigt _(→ medium)_
  Stimmt der gecachte Wert mit dem angefragten Modell ueberein, kehrt EnsureModelLoadedAsync ohne jeden Service-Kontakt mit true zurueck. Der Cache wird aber nie invalidiert: Laedt der Nutzer ueber das Dashboard ein anderes Modell (UpscalerController.cs:2197 postet /models/load direkt am Service vorbei) oder startet der Docker-Container neu, verarbeitet der naechste Batch-Lauf alle Frames still mit dem falschen bzw. keinem geladenen Modell - Output-Scale und Logs (die den Modellnamen des Plugins melden) luegen dann. Das ist die im Projekt bekannte Klasse "Report muss den realen Modell-Scale melden".
  › _Korrigiert high→medium: _currentlyLoadedModel (Z.157) nur gesetzt (188/218), nie invalidiert; Quick-Path (167) stale-true. Braucht Container-Restart/Out-of-band-Load als Trigger._
- **Abgelaufene Cache-Entries hinterlassen verwaiste Dateien auf Disk** — `Services/CacheManager.cs:266` · ✅ bestätigt
  GetCachedContentAsync entfernt einen abgelaufenen Entry (IsEntryExpired nach MaxCacheAgeDays) nur aus dem Index, loescht aber die Datei nicht und dekrementiert _totalCacheSize nicht. Ohne Index-Eintrag sieht weder der stuendliche Cleanup noch ValidateCacheEntries die Datei je wieder; nur ClearCacheAsync (manuell) raeumt das videos-Verzeichnis komplett. Multi-GB-Videodateien akkumulieren so im Normalbetrieb unbegrenzt auf der Platte, waehrend der ueberhoehte Size-Zaehler zusaetzlich vorzeitige Evictions gueltiger Entries ausloest.
  › _Selbst verifiziert: else-Zweig (Z.265) TryRemove ohne File.Delete/Size-Dekrement; abgelaufene Multi-GB-Videos verwaisen._
- **Bicubic-Fallback wird als Erfolg gespeichert und blockiert AI-Nachbearbeitung dauerhaft** — `ScheduledTasks/ImageUpscaleScanTask.cs:234` · ✅ bestätigt
  UpscalerCore.UpscaleImageAsync liefert nie null: Bei jedem Fehler (Service down, Modell nicht ladbar) kommt FallbackResizeAsync-Output oder als letzte Stufe die unveraenderten Original-Bytes zurueck (UpscalerCore.cs:183/188/712). Der Task kann das nicht unterscheiden, schreibt das Nicht-AI-Ergebnis als _upscaled-Datei, zaehlt success und feuert den "complete"-Webhook - und der Scan-Filter (Z.146-150) ueberspringt das Bild in allen kuenftigen Laeufen dauerhaft. Faellt der AI-Service mitten im woechentlichen Lauf aus, wird so die gesamte restliche Bibliothek mit Lanczos-Resizes oder 1:1-Kopien vergiftet, die nie wieder per AI ersetzt werden.
  › _Selbst verifiziert: UpscaleImageAsync gibt nie null (FallbackResize Z.183/188); Task speichert Lanczos als _upscaled, success++, Scan-Filter überspringt dauerhaft._

### Web – Konfigurationsseite (configurationpage.html)

- **XSS: esc() escapet keine Anfuehrungszeichen, wird aber in HTML-Attributen verwendet** — `Configuration/configurationpage.html:1191` · ✏️ korrigiert _(→ medium)_
  esc() (Z. 1007) nutzt den textContent/innerHTML-Trick, der nur &, < und > escapet - Anfuehrungszeichen bleiben erhalten. In refreshJobs wird esc(j.inputPath) aber in ein title-Attribut eingesetzt; inputPath ist der Dateiname der Mediendatei (Path.GetFileName in VideoJobManager.cs:52), der unter Linux doppelte Anfuehrungszeichen enthalten darf. Ein Dateiname wie 'a" onmouseover="<js>' bricht aus dem Attribut aus und fuehrt beim Hover Admin-Session-JavaScript aus (Token-Diebstahl via ApiClient.accessToken()); dieselbe Luecke besteht fuer Model-IDs vom AI-Service in id="bench-..." und data-get-model/data-bench-model (Z. 2288-2292).
  › _Korrigiert high→medium: esc() (Z.1007) escapet < > & (textContent-Trick), also KEINE Tag-Injektion; nur Quotes ungeschützt → reine Attribut-Injektion (onmouseover=) über bösartige Dateinamen. Admin-Seite._
- **Listener-Akkumulation bei SPA-Revisit: jeder Button feuert ab dem zweiten Besuch mehrfach** — `Configuration/configurationpage.html:3425` · 🔍 Agent-Befund (nicht re-verifiziert)
  viewbeforehide setzt _initialized = false, wodurch onPageShow beim naechsten Anzeigen initNav/attachEvents/attachImportEvents usw. erneut anhaengt; zusaetzlich ueberleben die document-level Listener (pageshow/viewshow Z. 3401-3409, delegierter Job-Click Z. 1206) jede SPA-Navigation und initialisieren bei erneut ausgefuehrtem Inline-Script auch die neue DOM-Instanz mit. Nach k Besuchen der Config-Seite in einer Browser-Session feuert jeder Klick k-fach: 'Create token' erzeugt mehrere Tokens, Import/Benchmark/Save werden mehrfach abgeschickt, confirm-Dialoge erscheinen doppelt, und es laufen k parallele Poll-Intervalle.
  › _Nicht im Hauptprozess re-verifiziert._
- **Face-Restore-Preview ruft nicht existierenden Endpoint auf - Button liefert immer HTTP 404** — `Configuration/configurationpage.html:3161` · ✅ bestätigt
  'Preview on Selected Media' POSTet an ApiClient.getUrl('Upscaler/face-restore/frame'), aber der UpscalerController kennt nur face-restore/load, /status und /unload - eine /frame-Proxy-Route existiert nur im Python-Service (main.py:5675), nicht im Plugin. Jeder Klick endet mit 'Face restore preview failed: HTTP 404'; das Feature ist damit vollstaendig tot. Zusaetzlich verlangt der Button die Medienauswahl aus #filter-preview-item, das auf dem Filters-Tab liegt, waehrend der Button im Models-Tab steht.
  › _Duplikat von controller#2 (Route fehlt)._

### Web – Player-Integration & Sidebar

- **Filter-Vorschlag-Apply ist ein stiller No-Op (falsche JSON-Feldnamen)** — `Configuration/player-integration.js:2186` · ✅ bestätigt
  _applySuggestedFilter postet { ActiveFilterPreset, EnableVideoFilters } an Upscaler/filter-config, aber die DTO FilterConfigUpdate (UpscalerController.cs:2781) hat nur Preset/Enabled/... ASP.NET ignoriert unbekannte Properties, alle Felder bleiben null, nichts wird gespeichert, der Server antwortet trotzdem success:true. Der Nutzer sieht 'Filter preset set to X', die Config bleibt unveraendert und der Vorschlag erscheint beim naechsten Render sofort wieder.
  › _Selbst verifiziert: JS sendet ActiveFilterPreset/EnableVideoFilters (Z.2186), Endpoint bindet body.Preset/body.Enabled (Z.2637/2638) → still No-Op, gibt success:true zurück._
- **Server-Realtime-Modus kollidiert mit 10-Requests/Minute-Rate-Limit** — `Configuration/player-integration.js:583` · 🔍 Agent-Befund (nicht re-verifiziert)
  Die Capture-Loop sendet pro Roundtrip einen Frame an Upscaler/upscale-frame, der Controller limitiert aber auf 10 Requests/Minute pro User (RateLimitMaxRequests=10, UpscalerController.cs:2455). Nach ~1 Sekunde liefert jeder Frame 429; der Client behandelt das als stillen Skip, _lastSuccessfulFrame friert ein und nach 10s faellt der Modus immer auf WebGL zurueck ('server unresponsive'). Der beworbene Server-AI-Realtime-Tier kann so nie laenger als wenige Sekunden laufen.
  › _Nicht im Hauptprozess re-verifiziert._
- **Master-Schalter EnablePlugin hat keinerlei Wirkung auf Realtime-Upscaling** — `Configuration/player-integration.js:2194` · ❓ unklar
  startRealtimeUpscaling prueft nur EnableRealtimeUpscaling; EnablePlugin (das Feld des grossen Menue-Schalters, toggleUpscaling Z. 2063) wird clientseitig nirgends als Gate benutzt, toggleUpscaling stoppt eine laufende RT-Session nicht, und der Server-Proxy upscale-frame prueft EnablePlugin ebenfalls nicht. Wer Upscaling im Player 'disabled', laesst die laufende Session weiterlaufen und beim naechsten Video startet RT erneut.
  › _startRealtimeUpscaling prüft nur EnableRealtimeUpscaling (Z.2194), nicht EnablePlugin; ob Z.1117 den Init global gated, nicht abschließend geprüft._
- **RealtimeUpscaler.start() ohne stop() leakt Overlay-Canvas und Intervalle** — `Configuration/player-integration.js:1764` · 🔍 Agent-Befund (nicht re-verifiziert)
  _applyAutoNow ('Re-apply to this video') und gestapelte once-'playing'-Listener (Z. 800) rufen _startRtWithConfig/start() waehrend _active===true. _startServer (Z. 481-520) ueberschreibt dann _overlayCanvas und _fallbackCheckInterval ohne Cleanup: die alte Canvas bleibt mit eingefrorenem Frame im DOM, das alte 2s-Intervall laeuft ewig weiter und cleart im Callback sogar das Handle des NEUEN Intervalls (clearInterval(RealtimeUpscaler._fallbackCheckInterval) statt der eigenen ID), dazu laufen zwei RAF-Loops.
  › _Nicht im Hauptprozess re-verifiziert; vgl. web-gpu#1 (gleiche Klasse)._
- **Sidebar-Panel doppelt tot: nie geladen und alle API-Routen falsch (api/-Praefix)** — `Configuration/sidebar-upscaler.js:288` · ⚠️ teilw. widerlegt _(→ low)_
  Die Datei ist nur als PluginPageInfo 'UPSCALERSidebarIntegration' registriert (Plugin.cs:187), aber kein Loader bindet sie ein - die index.html-Injection umfasst nur UPSCALERPlayerIntegration, configurationpage.html laedt sie nicht. Zusaetzlich nutzen alle 11 Server-Aufrufe das Praefix 'api/Upscaler/...', der Controller registriert aber nur [Route("[controller]")] = 'Upscaler/...' - jeder Call wuerde 404 liefern (Status/Hardware/Jobs/Cache/Benchmark/Auto-Optimize saemtlich tot). Das beworbene Sidebar-Feature existiert fuer Nutzer schlicht nicht.
  › _api/-Präfix ist laut docs/ENDPOINT-AUDIT.md:5 eine dokumentiert GÜLTIGE Form → 'alle Routen falsch' widerlegt. 'Sidebar nie geladen' nicht abschließend geprüft._

### Web – WebGL/WebGPU/Anime4K

- **stop() waehrend async start() geht verloren - verwaister Inferenz-Loop** — `Configuration/webgpu-ai-realtime.js:98` · ✅ bestätigt _(→ medium)_
  start() hat mehrere await-Luecken (requestAdapter, ORT-CDN-Script, Modell-Download von HuggingFace, Session-Erzeugung), prueft danach aber nie, ob inzwischen stop() gerufen wurde. Der Modell-Download dauert realistisch Sekunden; stoppt der Nutzer in dieser Zeit die Wiedergabe, laeuft _stopWebGPUAI ins Leere (_running noch false, _session null), anschliessend setzt start() _running=true, haengt das Canvas-Overlay ein und startet _renderLoop. Die Vollbild-Inferenz laeuft dann unbegrenzt weiter; ein spaeterer erneuter start() ueberschreibt den Singleton-State, waehrend der alte rAF-Loop weiterlaeuft (doppelte Inferenz pro Frame, geleaktes Canvas).
  › _Korrigiert high→medium: _running=true erst am start()-Ende (Z.98) nach async, stop() (Z.108) davor wird von Z.98 überschrieben → verwaister renderLoop. Enges Zeitfenster._

### Python – main.py (Z. 1–3400: Katalog, Model-Load, Auth)

- **Globale Body-Size-Middleware blockiert Model-Uploads ueber 50 MB** — `docker-ai-service/app/main.py:1959` · ✅ bestätigt
  Die limit_body_size-Middleware weist JEDEN Request mit Content-Length > MAX_UPLOAD_BYTES (Default 50 MB) mit 413 ab, auch /models/upload, /models/convert-upload und /models/upload-face-enhance, die laut MAX_MODEL_UPLOAD_BYTES bis 500 MB (Default) erlauben sollen. Reale ONNX-Modelle (GFPGAN ~340 MB, HAT-L ~162 MB, NAFNet ~446 MB, DAT ~86 MB) koennen damit nie hochgeladen werden; die Endpoint-Checks in Z. 6079/6153/6494 sind fuer 50-500 MB toter Code. Selbst per Env ist MAX_UPLOAD_BYTES auf 500 MB gedeckelt, waehrend MAX_MODEL_UPLOAD_BYTES bis 2 GB konfigurierbar ist.
  › _Selbst verifiziert: globale limit_body_size-Middleware (Z.1954-1963) cappt JEDEN Body inkl. Modell-Uploads. MAX_UPLOAD_BYTES-Wert (50MB laut Agent) nicht re-verifiziert._
- **rife-v4.25 (empfohlener Default) kann nicht laufen: 6-Kanal-Feed fuer 7-Kanal-Modell** — `docker-ai-service/app/main.py:3195` · 🔍 Agent-Befund (nicht re-verifiziert)
  Der Single-Input-Zweig von interpolate_frame_rife fuettert nur die 6-Kanal-Konkatenation der beiden Frames, ohne Timestep-Kanal. Der Katalog-Kommentar in Z. 1126 dokumentiert den rife-v4.25-Export aber explizit als '1-input 7-channel signature' (img0+img1+timestep). Jeder /interpolate-frames-Aufruf mit model=rife-v4.25 (available:True, Beschreibung 'Recommended new default') scheitert damit an ORT INVALID_ARGUMENT und liefert 500; die Tests decken nur die 2- und 3-Input-Faelle ab.
  › _Nicht im Hauptprozess re-verifiziert._
- **FP16-Cast ohne _session_input_is_fp16-Guard macht Face-Restore auf FP16-GPUs wirkungslos** — `docker-ai-service/app/main.py:3332` · 🔍 Agent-Befund (nicht re-verifiziert)
  _restore_face_crop castet den Input-Blob allein anhand von state.use_fp16 nach float16; die Face-Restore-Modelle (GFPGAN/CodeFormer/GPEN/RestoreFormer, alle fp32-Exports) erwarten aber tensor(float). Auf CUDA-GPUs mit Compute Capability >= 7 (USE_FP16=auto => use_fp16=True) wirft session.run fuer jeden Face-Crop INVALID_ARGUMENT; restore_faces_in_frame faengt das pro Crop mit warning+continue, sodass Face-Restore still gar nichts tut. Das ist exakt die Bug-Klasse aus Issue #67, deren Guard (Z. 1508-1519) nur _onnx_infer_tile/_onnx_infer_multiframe_tile abdeckt.
  › _Nicht im Hauptprozess re-verifiziert._

### Python – main.py (Z. 3401–6628: Endpoints, Video, Download)

- **/upscale-stream leakt Semaphore-Slot bei Client-Abbruch** — `docker-ai-service/app/main.py:4938` · 🔍 Agent-Befund (nicht re-verifiziert)
  Das try/finally mit sem.release() beginnt erst NACH der async-for-Schleife (Zeile 4991); bricht der Client ab, wirft request.stream() ClientDisconnect bzw. Starlette injiziert GeneratorExit am yield, und der finally-Block wird nie erreicht. Jeder abgebrochene Stream verliert dauerhaft einen Semaphore-Slot und laesst processing_count erhoeht. Nach max_concurrent Abbruechen antworten alle /upscale*-Endpoints nur noch 429/503, bis /config die Semaphore ersetzt.
  › _Nicht re-verifiziert; Streaming-Semaphore-Leak-Muster ist real-plausibel._
- **413 in /upscale und /upscale-hdr wird zu 500 und oeffnet den Circuit-Breaker** — `docker-ai-service/app/main.py:4546` · 🔍 Agent-Befund (nicht re-verifiziert)
  Der Groessen-Check (Zeile 4527) wirft HTTPException(413) im try-Block, aber es fehlt ein 'except HTTPException: raise' (das /upscale-frame Zeile 4699 hat) — die 413 faellt in 'except Exception', wird als 500 gemeldet und via _record_failure als Modell-Fehler gezaehlt. Sendet der Client fuenfmal in Folge ein zu grosses Bild (realistisch: 4K-16bit-HDR-PNGs des Plugins ueber MAX_UPLOAD_BYTES=50MB an /upscale-hdr, identischer Bug Zeile 4597/4617), oeffnet der Circuit-Breaker (threshold=5) und der gesamte Service liefert 503. Der bestehende Test akzeptiert 400 ODER 413 und deckt den Pfad mit geladenem Modell nicht ab.
  › _Nicht im Hauptprozess re-verifiziert._
- **/models/cleanup loescht nach Restart praktisch alle Modelle inkl. Custom-Modelle** — `docker-ai-service/app/main.py:5399` · ✅ bestätigt
  state.model_last_used ist rein in-memory (Zeile 216) — nach einem Container-Restart ist last_used fuer alle Dateien 0, und dry_run=false loescht jedes nicht gerade geladene Modell statt nur 'seit N Tagen ungenutzte'. Zusaetzlich werden .custom.json-Sidecars, face_enhance.onnx und Face-Restore-Modelle (gfpgan-*) nie in model_last_used eingetragen (kein _record_success-Pfad) und daher selbst bei aktiver Nutzung geloescht. Custom-Modelle (url="") sind danach unwiederbringlich weg bzw. verlieren durch den geloeschten Sidecar ihre Registrierung beim naechsten Restart.
  › _Selbst verifiziert: model_last_used In-Memory, default 0 (Z.5399); nach Restart alle <cutoff → alle nicht-geladenen Modelle (inkl. Custom) gelöscht (Z.5406/5414). Nur wenn cleanup non-dry nach Restart läuft._
- **/quality-metrics blockiert das Event-Loop mit voller KI-Inferenz** — `docker-ai-service/app/main.py:5813` · 🔍 Agent-Befund (nicht re-verifiziert)
  Der async-Handler ruft upscale_image_array und compute_quality_metrics synchron auf statt via run_in_executor — auf einer CPU-Box blockiert eine einzige Anfrage das gesamte Event-Loop fuer Sekunden bis Minuten (kein /health, keine /upscale-frame-Antworten, SSE tot; Docker-Healthchecks koennen den Container als unhealthy markieren). ENABLE_QUALITY_METRICS ist per Default true; alle anderen Inferenz-Endpoints nutzen korrekt _cpu_executor.
  › _Nicht im Hauptprozess re-verifiziert._
- **/process-grain blockiert das Event-Loop (fastNlMeans + Inferenz)** — `docker-ai-service/app/main.py:5884` · 🔍 Agent-Befund (nicht re-verifiziert)
  remove_grain (cv2.fastNlMeansDenoisingColored — bei HD-Bildern viele Sekunden CPU) und im 'both'-Pfad zusaetzlich upscale_image_array laufen synchron im async-Handler; der komplette Service ist waehrenddessen eingefroren. Das Feature ist per Default aktiv (ENABLE_GRAIN_MANAGEMENT=true) und hat keine Concurrency-Begrenzung.
  › _Nicht im Hauptprozess re-verifiziert._

### Python – token_store / model_import / Tests

- **Download-Groessencap greift erst nach vollstaendigem Puffern im RAM** — `docker-ai-service/app/model_import.py:164` · 🔍 Agent-Befund (nicht re-verifiziert)
  _download_capped laedt mit client.get() den kompletten Response-Body in den Speicher (resp.content) und prueft MAX_MODEL_UPLOAD_BYTES erst danach. _import_gate prueft nur das size_bytes-Feld des Katalogs, nicht die reale Antwort - liefert der Upstream (GitHub-Release/HF-Repo geaendert, genau das Szenario, das die sha-Mismatch-Fehlertexte selbst beschreiben) ein z.B. 10-GB-File, wird alles gepuffert und der Container laeuft in den OOM-Kill, bevor der Cap oder der sha-Pin greift. Das widerspricht der im Moduldocstring zugesicherten Eigenschaft 'hard size cap on both the download...' und trifft sowohl die Sync-Endpoints als auch _run_import_job (main.py:6324/6392).
  › _Nicht re-verifiziert; vgl. csharp-controller#9 (gleiches RAM-Puffer-Muster)._

### Infrastruktur – Dockerfiles, requirements, CI-Workflows

- **DockerHub-Cleanup wuerde alle v1.8.x-Tags loeschen und :latest auf v1.7.8 zurueckdrehen** — `Scripts/cleanup-dockerhub-tags.ps1:27` · ✅ bestätigt
  Der von dockerhub-cleanup.yml (execute=true) ausgefuehrte Script hat $CurrentNvidiaTag='v1.7.8' hartkodiert und behaelt nur v1.7.7*/v1.7.8*-Pins; aktuelle Releases sind aber v1.8.3.x. Ein Lauf mit execute=true wuerde heute saemtliche v1.8.x- und docker7-v1.8.x-Tags unwiderruflich loeschen und :latest (Watchtower-Nutzer!) auf ein v1.7.8-Image downgraden. Zusaetzlich fehlt der seit v1.8.3.8 existierende Rolling-Tag docker7-converter in der Keep-Liste (Zeile 37) und wuerde mitgeloescht.
  › _Selbst verifiziert: CurrentNvidiaTag='v1.7.8' (Z.27) stale, Test-Keep behält nur v1.7.7/v1.7.8 (Z.38/39) → -Execute löscht v1.8.x + latest→v1.7.8. Guardrail: Dry-Run default. =release#1._
- **trivy-action@master unpinnt direkt nach Docker-Hub-Login** — `.github/workflows/docker-publish.yml:155` · ✅ bestätigt _(→ medium)_
  aquasecurity/trivy-action ist auf den mutablen Branch master gepinnt und laeuft nachdem docker/login-action das DOCKERHUB_TOKEN in die Docker-Config des Runners geschrieben hat. Eine Kompromittierung des Upstream-Repos (vgl. tj-actions-Vorfall) koennte das Token exfiltrieren und boesartige Images an alle Nutzer pushen. Der Header (Zeile 15) fordert selbst 'pin to SHAs via Dependabot before first merge' - das ist nie passiert, waehrend build.yml/build-and-release.yml korrekt per SHA pinnen.
  › _Korrigiert high→medium: trivy-action@master (Z.155) unpinned im selben Job wie Docker-Login. Standard-Supply-Chain-Risiko, aber weit verbreitet; continue-on-error._

### Release – Feeds, Versions-Stamping, Scripts

- **Stale Keep-Regeln: -Execute loescht alle v1.8.x-Docker-Tags und setzt :latest auf v1.7.8 zurueck** — `Scripts/cleanup-dockerhub-tags.ps1:27` · 🔍 Agent-Befund
  Die Keep-Liste pinnt nur v1.7.7/v1.7.8 und die sechs alten Rolling-Tags; docker-publish pusht aber seit langem docker7-v<ver>-<suffix>- und v<ver>-<suffix>-Tags fuer v1.8.x sowie seit v1.8.3.8 den Rolling-Tag docker7-converter, der in Test-Keep fehlt. Ein Lauf mit -Execute loescht damit unwiederbringlich alle v1.8.x-Pins plus das Converter-Image und zeigt :latest wieder auf ein v1.7.8-Image - exakt der ':latest stuck on v1.7.8'-Bug, den v1.8.3.4 behoben hat. Der Dry-Run-Default mildert das, aber -Execute ist der dokumentierte Zweck des Scripts.
- **Release-Verifikation schlaegt bei 3-teiligen Tags (x.y.z.0-Releases) komplett fehl** — `Scripts/verify-release.ps1:106` · ✅ bestätigt _(→ medium)_
  Fuer einen Tag wie v1.8.4 sucht der Live-Feed-Check nach version -eq '1.8.4', die Feeds tragen aber konventionsgemaess 4-part '1.8.4.0' - kein Feed wird gefunden und das Script bricht mit exit 1 ab; ebenso erwarten die lokalen Feed-Checks (Zeilen 155-157) $tagVersion statt $tagVersion4. Jedes bisherige .0-Release (v1.8.3, v1.8.2, ...) haette diesen Check nicht bestanden; das naechste Minor-Release triggert den Fehlalarm garantiert und verleitet dazu, 3-part-Versionen in die Feeds zu schreiben oder die Verifikation zu ueberstimmen. Der CI-Job zip-version-check (meta.json 3-part vs. manifest[0] 4-part) hat dieselbe Klasse.
  › _Korrigiert high→medium: Z.89 tagVersionFeed=TrimStart('v') 3-teilig, Feeds 4-teilig; Autor normalisiert nur lokal (Z.144 tagVersion4). Nur bei x.y.z.0-Releases, reine Tooling-Blockade._

## ✏️ Korrigierte Befunde (Schwere heruntergestuft)

Die adversariale Nachprüfung hat drei vom Erst-Reviewer zu hoch eingestufte Befunde entschärft — ein Beleg, dass die Verifikationsschicht ihren Zweck erfüllt:

- **XSS: esc() escapet keine Anfuehrungszeichen, wird aber in HTML-Attributen verwendet** (`Configuration/configurationpage.html:1191`): Korrigiert high→medium: esc() (Z.1007) escapet < > & (textContent-Trick), also KEINE Tag-Injektion; nur Quotes ungeschützt → reine Attribut-Injektion (onmouseover=) über bösartige Dateinamen. Admin-Seite.
- **Sidebar-Panel doppelt tot: nie geladen und alle API-Routen falsch (api/-Praefix)** (`Configuration/sidebar-upscaler.js:288`): api/-Präfix ist laut docs/ENDPOINT-AUDIT.md:5 eine dokumentiert GÜLTIGE Form → 'alle Routen falsch' widerlegt. 'Sidebar nie geladen' nicht abschließend geprüft.
- **trivy-action@master unpinnt direkt nach Docker-Hub-Login** (`.github/workflows/docker-publish.yml:155`): Korrigiert high→medium: trivy-action@master (Z.155) unpinned im selben Job wie Docker-Login. Standard-Supply-Chain-Risiko, aber weit verbreitet; continue-on-error.
- **Release-Verifikation schlaegt bei 3-teiligen Tags (x.y.z.0-Releases) komplett fehl** (`Scripts/verify-release.ps1:106`): Korrigiert high→medium: Z.89 tagVersionFeed=TrimStart('v') 3-teilig, Feeds 4-teilig; Autor normalisiert nur lokal (Z.144 tagVersion4). Nur bei x.y.z.0-Releases, reine Tooling-Blockade.
- **_currentlyLoadedModel wird nie invalidiert - stiller Falsch-Modell-Betrieb** (`Services/HttpUpscalerService.cs:167`): Korrigiert high→medium: _currentlyLoadedModel (Z.157) nur gesetzt (188/218), nie invalidiert; Quick-Path (167) stale-true. Braucht Container-Restart/Out-of-band-Load als Trigger.
- **stop() waehrend async start() geht verloren - verwaister Inferenz-Loop** (`Configuration/webgpu-ai-realtime.js:98`): Korrigiert high→medium: _running=true erst am start()-Ende (Z.98) nach async, stop() (Z.108) davor wird von Z.98 überschrieben → verwaister renderLoop. Enges Zeitfenster.

## 🟡 Medium & ⚪ Low — kompakt pro Bereich

Vollständige Liste; nicht einzeln adversarial verifiziert (Erst-Reviewer-Befunde).

<details><summary><b>C# – Kern (Plugin, Config, Registries)</b> — 4 Befunde</summary>

| Sev | Ort | Titel | Kurzbeschreibung |
|---|---|---|---|
| 🟡 | `ModelScale.cs:33` | ScalePatterns verfehlen drei Katalog-IDs (x4v3-Suffix und spanx2-Praefix) | Die vier Regex-Patterns erkennen "realesr-general-x4v3" und "realesr-general-wdn-x4v3" (beide 4x laut models-fallback.json, ersteres als "Best modern default" beworben) sowie "span |
| 🟡 | `PluginConfiguration.cs:551` | PluginVersion veraltet nach Plugin-Updates bis zum naechsten manuellen Save | Der Property-Initializer "1.8.3.21" greift nur bei frischer Config; bei Bestandsinstallationen ueberschreibt der XmlSerializer ihn mit dem persistierten alten Wert, und serverseiti |
| ⚪ | `PluginConfiguration.cs:219` | Mehrere XML-Doku-Kommentare widersprechen dem tatsaechlichen Verhalten | Die Doku von EnableAutoModelSelection behauptet "Default false - user must opt in", der Code setzt aber = true (Z. 221). Weitere Drifts: QualityLevel-Doku nennt "fast" statt des UI |
| ⚪ | `Plugin.cs:147` | InjectPlayerScript meldet Erfolg auch ohne erfolgte Injektion und kann alten Tag entfernen | Findet headEndRegex kein </head> (Replace ohne Treffer), wird die Datei trotzdem zurueckgeschrieben und true geliefert; Zeile 85 loggt dann faelschlich "Player script injected". Da |

</details>

<details><summary><b>C# – Controller (REST-API)</b> — 9 Befunde</summary>

| Sev | Ort | Titel | Kurzbeschreibung |
|---|---|---|---|
| 🟡 | `UpscalerController.cs:1123` | Item-Endpoints ignorieren Per-User-Bibliotheksrechte (Parental Controls umgehbar) | GetComparisonData (Z. 1123), GetFilterPreviewFrame (Z. 2707), UpscaleItemImages (Z. 1212) und ProcessItem (Z. 1382) laden Items via _libraryManager.GetItemById(itemGuid) ohne Pruef |
| 🟡 | `UpscalerController.cs:1545` | Library-Allowlist per StartsWith ohne Verzeichnis-Separator - Nachbarordner-Bypass | EnqueueJob (Z. 1543-1545) und PreProcessVideo (Z. 1741-1743) pruefen die Bibliothekszugehoerigkeit mit inputPath.StartsWith(Path.GetFullPath(loc)) ohne angehaengten DirectorySepara |
| 🟡 | `UpscalerController.cs:1886` | ImportSettings: FormatException entkommt TryApply - 500 mit teilweise mutierter Live-Config | TryApply faengt nur InvalidOperationException; JsonElement.GetInt32()/GetInt64() werfen bei nicht darstellbaren Zahlen (z.B. "ScaleFactor": 2.5 oder 2^31) aber FormatException. Die |
| 🟡 | `UpscalerController.cs:292` | GET /libraries gibt Server-Dateisystempfade an Nicht-Admins preis | GetLibraries liefert fuer jede Bibliothek die physischen locations (Serverpfade) an JEDEN authentifizierten User; Jellyfin selbst gated das Pendant /Library/VirtualFolders hinter R |
| 🟡 | `UpscalerController.cs:1112` | compare/{itemId}: scale unvalidiert und Response meldet den angefragten statt den echten Modell-Scale | GetComparisonData uebernimmt scale ohne Bereichspruefung (UpscaleImage erlaubt nur {2,3,4,8}); negative/riesige Werte laufen bis in UpscalerCore.FallbackResizeAsync (width*scale) u |
| 🟡 | `UpscalerController.cs:573` | ImportModel puffert den kompletten Download im Jellyfin-Heap, Groessencheck erst danach | Der lokale Importpfad liest die Datei mit ReadAsByteArrayAsync vollstaendig in den Speicher; der 500-MB-Check via Content-Length greift bei chunked Responses nicht und der data.Lon |
| ⚪ | `UpscalerController.cs:2655` | filter-preview-Endpoints als '(admin only)' dokumentiert, aber fuer alle User offen und ohne Rate-Limit | Die XML-Docs von FilterPreview (Z. 2655) und GetFilterPreviewFrame (Z. 2686) behaupten '(admin only)', tatsaechlich gilt nur das Klassen-[Authorize] - v1.7.5 hat beide bewusst fuer |
| ⚪ | `UpscalerController.cs:961` | GET /hardware-info liefert statische Fantasiewerte statt Erkennung | GetHardwareInfo meldet fest FFmpegAvailable=true und OnnxRuntime="Available" und setzt GpuAvailable auf das Config-Flag HardwareAcceleration statt auf ein Erkennungsergebnis. Als i |
| ⚪ | `UpscalerController.cs:2004` | Settings-Roundtrip verliert AiServiceApiToken still | ImportSettings kann AiServiceApiToken setzen (Z. 2004), ExportSettings exportiert das Feld aber nie (bewusst als Secret ausgelassen, ohne Platzhalter wie bei WebhookUrl). Ein Expor |

</details>

<details><summary><b>C# – Processing (Queue, Auto-Model, Hardware-Cap)</b> — 13 Befunde</summary>

| Sev | Ort | Titel | Kurzbeschreibung |
|---|---|---|---|
| 🟡 | `ProcessingMethodExecutor.cs:92` | RealTime-Pfad: Space-Split zerlegt gequotete FFmpeg-Argumente | BuildFFmpegCommand baut Strings mit eingebetteten Anfuehrungszeichen (-i "path", -vf "...", -y "out"), ProcessRealTimeAsync splittet sie an Leerzeichen und uebergibt jedes Token al |
| 🟡 | `ProcessingStrategySelector.cs:42` | Model "auto" wird zu Leerstring aufgeloest und laesst den Job abbrechen | OptimizeProcessingOptions ersetzt Model "auto"/leer durch hardwareProfile.RecommendedModel - aber das von VideoProcessor.ProcessVideoAsync verwendete Profil stammt aus UpscalerCore |
| 🟡 | `ProcessingMethodExecutor.cs:202` | Cancellation wird als Failure gemeldet (inkl. Failure-Webhook) | ProcessFrameByFrameAsync (Z.202), ProcessFrameByFrameOverlappedAsync (Z.368), ProcessBatchAsync (Z.418) und ProcessRealTimeAIAsync (Z.909) fangen alle Exceptions inklusive Operatio |
| 🟡 | `ProcessingMethodExecutor.cs:328` | Original-Frame-Fallback erzeugt gemischte Aufloesungen, die die Rekonstruktion sprengen | Bei einem fehlgeschlagenen AI-Upscale wird das ORIGINAL-Frame (Eingangsaufloesung) nach processedDir kopiert (hier, in ProcessMultiFrameAsync Z.572-590 und in VideoFrameProcessor.U |
| 🟡 | `HardwareBudget.cs:170` | Weak-CPU + 4K-Quelle: Light-Ladder ohne 1x-Eintrag fuehrt zu 8K-Output | Der 1x-Branch (UpscalerCore Z.535-539) bevorzugt fuer bereits-4K-Quellen die Medium-schweren 1x-Restaurationsmodelle. Auf tier weak-cpu (max Light) fallen die durch, und die Light- |
| 🟡 | `ProcessingMethodExecutor.cs:647` | RealTimeAI leitet Output-Dimensionen aus dem Config-Scale statt dem Modell-Scale ab | outputWidth/Height = input * OptimizedOptions.ScaleFactor (Config-Wert), aber der AI-Service skaliert mit dem nativen Faktor des geladenen Modells. Bei Abweichung (z.B. span-x2 gel |
| ⚪ | `UpscalerCore.cs:435` | Hardware-Tier wird nur von Controller-Endpoints refresht - Headless-Batch laeuft ungecappt | _hardwareTier wird ausschliesslich in UpscalerController (RefreshHardwareTierAsync im recommend-model-Endpoint, CacheHardwareTier beim proxied /recommend) gesetzt. Der ScheduledTas |
| ⚪ | `ProcessingQueue.cs:317` | Persistenz verliert den aktiven Job beim Neustart | PersistDebouncedAsync snapshotet nur _queue (Pending); DequeueAsync entfernt den Job vor der Verarbeitung daraus. Stirbt der Server mitten in einem Job, ist genau dieser Job nach d |
| ⚪ | `ProcessingMethodExecutor.cs:340` | Overlapped-Pfad meldet bei unbekannter Dauer total=processed statt -1-Sentinel | Bei estTotalFrames==0 wird SendFrameProgress mit total=processed aufgerufen - der Frame-Anteil ist damit ab dem ersten Frame 100% und CalculateJobProgress zeigt die ganze Laufzeit  |
| ⚪ | `UpscalerCore.cs:542` | Multi-Frame-Branch uebergeht PreferredAnimeModel/PreferredLiveActionModel-Override stillschweigend | Der Branch isBatch && inputFrames>1 steht VOR den Override-Checks (Z.560, Z.584). Hat der Nutzer ein Preferred-Model gesetzt und die Service-Instanz meldet Multi-Frame-Support (sel |
| ⚪ | `UpscalerCore.cs:176` | Exception-Filter in UpscaleImageAsync schluckt Cancellation und beantwortet sie mit Fallback-Resize | Der Filter faengt fuer Nicht-letzte Chain-Modelle jede Exception inkl. OperationCanceledException und probiert weitere Modelle (weitere HTTP-Calls nach Cancel); beim letzten Modell |
| ⚪ | `ProcessingQueue.cs:308` | Debounced-Persist kann den letzten Zustand verlieren | Laeuft bereits ein Writer, kehrt PersistDebouncedAsync ohne Schreiben zurueck. Hat dieser Writer seinen Snapshot schon VOR der juengsten Mutation genommen, wird der neue Zustand ni |
| ⚪ | `ProcessingMethodExecutor.cs:148` | Disk-Space-Schaetzung mit 500KB/Frame unterschaetzt grosse Quellen massiv | Die Schaetzung Dauer*25fps*500KB passt fuer SD/HD-PNGs, aber 4K-Extraktionsframes liegen bei 10-30MB und upscaled 8K-Frames noch darueber - die Pruefung winkt Jobs durch, die die P |

</details>

<details><summary><b>C# – Video-Pipeline (ffmpeg, Frames, VMAF)</b> — 15 Befunde</summary>

| Sev | Ort | Titel | Kurzbeschreibung |
|---|---|---|---|
| 🟡 | `VideoFrameProcessor.cs:172` | -ss Position mit CurrentCulture formatiert - Frame-Preview bricht auf Komma-Locales | position.TotalSeconds.ToString("F2") ohne InvariantCulture erzeugt auf de-DE z.B. "300,00"; ffmpeg lehnt das als "Invalid duration specification" ab, exit code != 0, und ExtractSin |
| 🟡 | `VideoAnalyzer.cs:44` | AnalyzeVideoAsync ignoriert Cancellation komplett - haengendes ffprobe blockiert die Pipeline | Die Methode hat keinen CancellationToken-Parameter; FFProbe.AnalyseAsync (Zeile 48) sowie beide CliWrap-ffprobe-Aufrufe (ExecuteAsync() in Zeile 150 und 222/252) laufen ohne Token. |
| 🟡 | `VideoFrameProcessor.cs:437` | Rekonstruktion verliert zusaetzliche Audiospuren, alle Untertitel und Kapitel | Die Audio-Extraktion nutzt "-vn -acodec copy" ohne -map: ffmpeg kopiert dann nur EINEN Audiostream (den mit den meisten Kanaelen, nicht zwingend die Default-Spur). Bei mehrsprachig |
| 🟡 | `VideoFrameProcessor.cs:368` | 50%-Fehler-Abbruch bricht den Job nicht ab - Exception wird erst bei Task.WhenAll beobachtet | Die InvalidOperationException "Too many frame failures" wird innerhalb eines gequeueten Task geworfen; der for-Loop merkt davon nichts und enqueued weiterhin ALLE restlichen Frames |
| 🟡 | `VideoJobManager.cs:150` | History-Trim kann KeyNotFoundException werfen und einen erfolgreichen Job als Failed melden | Beim Trimmen auf 100 Eintraege wird ueber einen Keys-Snapshot sortiert und dabei _performanceHistory[k] per Indexer gelesen. Beenden zwei Jobs gleichzeitig (MaxConcurrentStreams >  |
| 🟡 | `VideoJobManager.cs:137` | PerformanceHistory meldet Output-Aufloesung/Scale aus dem Config-Wert statt dem Modell-Scale | OutputResolution und Scale werden aus OptimizedOptions.ScaleFactor berechnet - genau die im Repo dokumentierte Bug-Klasse: der AI-Service nutzt den nativen Faktor des geladenen Mod |
| 🟡 | `VideoFilterService.cs:62` | nlmeans-Staerke unter 1.0 liegt ausserhalb des ffmpeg-Wertebereichs und laesst jeden Job scheitern | Der ffmpeg-nlmeans-Parameter s hat den Range [1.0, 30.0]; der UI-Slider (DenoisePrefilterStrength, min=0 max=10 step=0.5) erlaubt aber 0.5. Damit erzeugt BuildDenoisePrefilter "nlm |
| 🟡 | `VideoFilterService.cs:151` | Vignette-Slider-Semantik invertiert: kleine Werte ergeben maximale, grosse Werte schwache Vignette | Der Slider-Wert (0-5, dokumentiert als "0.0 off to 5.0 heavy") wird als Divisor benutzt: vignette=PI/x. Damit ergibt 5 die schwaechste und 0.1 die staerkste Einstellung - fuer alle |
| 🟡 | `UpscalerProgressHub.cs:114` | Progress-Broadcast missbraucht SessionMessageType.UserDataChanged mit fremdem Payload | Alle ~2s pro laufendem Job wird ein eigenes Progress-Objekt als UserDataChanged an alle Admin-Sessions gesendet. Offizielle Clients erwarten dort ein UserDataChangeInfo mit UserDat |
| 🟡 | `VideoProcessor.cs:318` | Bei Cancel/Exception wird kein SendJobCompleted gesendet und keine History geschrieben | SendJobCompleted laeuft nur im Erfolgs-Durchlauf (Zeile 293) und im modelLoaded-Fehlpfad (Zeile 269). Wirft AnalyzeVideoAsync/DetectHardwareAsync oder wird der Job gecancelt, erhae |
| ⚪ | `VideoFrameProcessor.cs:301` | Semaphore wird bei Cancel disposed, waehrend laufende Tasks noch Release() aufrufen | Wirft await semaphore.WaitAsync(cancellationToken) im Loop eine OperationCanceledException, verlaesst die Methode sofort und `using var semaphore` disposed die SemaphoreSlim, waehr |
| ⚪ | `UpscalerProgressHub.cs:176` | Negatives EstimatedTimeRemaining bei unbekanntem Frame-Total | Bei totalFrames <= 0 (Pipe-Pfad) wird framesRemaining negativ und secondsRemaining damit ebenfalls; die WebSocket-Message traegt dann ein negatives EstimatedTimeRemaining. Der Stat |
| ⚪ | `UpscalerService.cs:159` | Task.Delay im generischen catch wirft bei Shutdown und beendet den Worker unsauber | Im catch-Block wird await Task.Delay(2000, ct) mit dem Worker-Token aufgerufen. Faellt eine Job-Exception mit einem gleichzeitigen Shutdown zusammen, wirft das Delay eine ungefange |
| ⚪ | `UpscalerProgressHub.cs:131` | SendJobStarted hat keinen einzigen Aufrufer | Die Methode wird nirgends im Repo aufgerufen (repo-weiter Grep); das "Starting"-Event erreicht Clients nie. Entweder toter Code oder ein vergessener Aufruf am Job-Beginn in Process |
| ⚪ | `VideoAnalyzer.cs:73` | EstimatedQuality wird berechnet, aber nirgends verwendet - und waere bei MKV meist falsch | info.EstimatedQuality wird gesetzt, aber kein Produktionscode liest das Feld (Strategy-Selector, Controller: keine Treffer). Zudem liefert FFprobe fuer MKV-Streams meist keine BitR |

</details>

<details><summary><b>C# – I/O (HTTP-Client, Cache, Scheduled Tasks)</b> — 12 Befunde</summary>

| Sev | Ort | Titel | Kurzbeschreibung |
|---|---|---|---|
| 🟡 | `HttpUpscalerService.cs:284` | HttpClient-Timeout wird als Nutzer-Cancellation fehlinterpretiert | HttpClient meldet Timeouts als TaskCanceledException; UpscaleImageAsync (und ebenso DownloadModelAsync Z.328, LoadModelAsync Z.370) behandelt das als "request was cancelled" und br |
| 🟡 | `CacheManager.cs:396` | Temp-Dateien von fehlgeschlagenem Pre-Processing werden nie aufgeraeumt | PreProcessContentAsync legt temp/<guid>.mp4 an und loescht die Datei nur im Erfolgsfall (Z.421-424). Schlaegt ProcessVideoAsync fehl oder wirft (auch OperationCanceledException lan |
| 🟡 | `ImageUpscaleScanTask.cs:230` | 2x/4x-Scale-Entscheidung ist wirkungslos (Service ignoriert scale) | Der berechnete scale wird an UpscalerCore.UpscaleImageAsync uebergeben, aber ResolveAutoModel() beruecksichtigt ihn nicht (liefert config.Model, Default realesrgan-x4) und der AI-S |
| 🟡 | `ImageUpscaleScanTask.cs:240` | PNG-Daten werden unter der Original-Extension (.jpg) gespeichert | Der /upscale-Endpunkt liefert immer image/png (main.py:4536) und auch FallbackResizeAsync speichert per SaveAsPngAsync als PNG; der Task schreibt diese Bytes aber unter der Extensi |
| 🟡 | `ImageUpscaleScanTask.cs:239` | Upscalte Bilder werden Jellyfin nie zugeordnet und bleiben unsichtbar | Der Task schreibt <name>_upscaled.<ext> neben das Original, registriert das Ergebnis aber nirgends am Item (kein SetImage, kein RefreshMetadata) - und der Suffix _upscaled matcht k |
| 🟡 | `LibraryUpscaleScanTask.cs:372` | Kein Abbruch bei Serienfehlern nach Service-Ausfall mitten im Batch | Die Service-Erreichbarkeit wird nur einmal vor dem Scan geprueft. Stirbt der AI-Service danach, durchlaeuft jedes verbleibende Item die volle Kaskade aus Modellketten-Versuchen mit |
| 🟡 | `LibraryUpscaleScanTask.cs:403` | Nutzerabbruch wird als Fehlschlag gezaehlt und feuert Failure-Webhook | VideoProcessor.ProcessVideoAsync faengt OperationCanceledException intern und liefert Success=false mit Error="Processing cancelled" (VideoProcessor.cs:318-323) - der OCE-catch hie |
| 🟡 | `CacheManager.cs:335` | Doppel-Store desselben Keys orphant alte Cache-Datei und zaehlt Groesse doppelt | Bei zwei parallelen PreProcess-Aufrufen fuer dasselbe Video (Doppelklick auf /Upscaler/preprocess) speichern beide: Der Index-Overwrite ersetzt den Entry, aber die alte Cache-Datei |
| ⚪ | `HardwareBenchmarkService.cs:64` | Auto-Benchmark-Timer verwirft die Ergebnisse - Feature ohne Wirkung | RunBenchmarkCallback ignoriert den Rueckgabewert von RunHardwareBenchmark, und die Methode hat keinerlei Seiteneffekte (nichts wird persistiert oder in die Config uebernommen). Das |
| ⚪ | `HardwareBenchmarkService.cs:317` | GetFallbackStatusAsync fragt Status auch bei bekannt totem Service ab | Selbst wenn IsServiceAvailableAsync gerade false geliefert hat, wird GetServiceStatusAsync aufgerufen - das blockiert bei nicht erreichbarem Service bis zu 10 Sekunden und loggt je |
| ⚪ | `LibraryScanHelper.cs:54` | Library-Zuordnung: nur erste Location, ungesicherter Praefix-Match, Leer-Location matcht alles | Der Match prueft nur Locations.FirstOrDefault() (Libraries koennen mehrere Ordner haben), vergleicht ohne Verzeichnistrenner-Grenze ("/media/mov" matcht "/media/movies/...") und ei |
| ⚪ | `CacheManager.cs:153` | Index-Save nicht serialisiert; Dispose schreibt non-atomic | Parallele SaveCacheIndexAsync-Aufrufe (z.B. zwei gleichzeitige Stores) schreiben dieselbe feste .tmp-Datei und kollidieren mit IOException, wodurch ein Save verworfen wird (nur gel |

</details>

<details><summary><b>Web – Konfigurationsseite (configurationpage.html)</b> — 12 Befunde</summary>

| Sev | Ort | Titel | Kurzbeschreibung |
|---|---|---|---|
| 🟡 | `configurationpage.html:1035` | ReferenceError bei jedem Klick auf den Models-Tab: refreshFaceRestoreStatus ausserhalb des Scopes | initNav referenziert refreshFaceRestoreStatus, die Funktion ist aber nur lokal innerhalb von attachEvents deklariert (Z. 3039) und im Scope von initNav nicht sichtbar. Jeder Klick  |
| 🟡 | `configurationpage.html:1129` | Dashboard-Kacheln 'Completed'/'Failed' stehen konstruktionsbedingt immer auf 0 | refreshDashboard zaehlt Completed/Failed aus data.jobs, aber /Upscaler/jobs liefert nur LAUFENDE Jobs (VideoProcessor entfernt Jobs im finally auf jedem Terminalpfad; genau das dok |
| 🟡 | `configurationpage.html:2154` | Scale-Dropdown ist mit den Shipped-Defaults leer (ScaleFactor 2 vs. realesrgan-x4 nativ 4x) | loadConfig ruft updateScaleOptions auf (baut Optionen aus dem nativen Scale-Array des Modells, fuer das Default-Modell realesrgan-x4 nur [4]) und setzt danach value = config.ScaleF |
| 🟡 | `configurationpage.html:2234` | Save loescht gespeicherte Model-Auswahl still, wenn die ID nicht in der aktuellen Optionsliste ist | Ist das gespeicherte Modell (z.B. ein OMDB-Import wie omdb-4x-...) nicht in der Optionsliste - etwa weil der AI-Service offline ist und der Fallback-Katalog importierte Modelle nic |
| 🟡 | `configurationpage.html:603` | Auto-Mode-Beschreibung behauptet 'Default: off.', tatsaechlicher Default ist seit v1.8.3.12 true | Die fieldDescription zu EnableAutoModelSelection sagt fett 'Default: off.', aber PluginConfiguration.cs:221 initialisiert EnableAutoModelSelection = true ('v1.8.3.12 - Auto mode is |
| 🟡 | `configurationpage.html:1495` | Poll-Intervall wird nach Verlassen der Seite durch _setStripLive-Race wiederbelebt | viewbeforehide raeumt refreshInterval auf, aber eine zu dem Zeitpunkt noch laufende /jobs-Antwort ruft renderActivityStrip -> _setStripLive auf; aendert sich dabei der Live-Zustand |
| ⚪ | `configurationpage.html:1213` | Pause/Resume/Cancel-POST ohne catch: Fehler bleiben stumm | Der delegierte Job-Control-Handler schickt den POST ohne .catch. Schlaegt die Aktion fehl (Job inzwischen beendet -> 404, Service-Fehler -> 500), gibt es weder Toast noch UI-Update |
| ⚪ | `configurationpage.html:1293` | updateRangeLabels/attachRangeLabels sind toter Code (kein Element matcht .range-val[data-rv]) | Beide Funktionen selektieren '.range-val[data-rv]' bzw. spiegeln in solche Spans, aber im gesamten Markup existiert kein Element mit der Klasse range-val oder dem Attribut data-rv  |
| ⚪ | `configurationpage.html:2796` | Token-Copy-Button meldet 'Copied' auch ohne Clipboard (HTTP-Setups) | navigator.clipboard existiert nur in Secure Contexts; auf den bei Jellyfin ueblichen HTTP-LAN-Installationen ist es undefined, der Schreibvorgang wird uebersprungen, der Toast 'Cop |
| ⚪ | `configurationpage.html:238` | Tabs als <div> statt <button type="button"> - nicht per Tastatur bedienbar | Die Projektregel verlangt fuer In-Page-Tabs <button type="button">. Die aktuellen <div class="upscaler-tab">-Tabs routen zwar nicht weg (das eigentliche emby-linkbutton-Problem), s |
| ⚪ | `configurationpage.html:2449` | Perf-Monitor pollt alle 5s weiter, auch wenn ein anderer Tab aktiv ist | startPerfMonitor wird bei pageshow und jedem Dashboard-Tab-Klick gestartet, aber nur bei viewbeforehide gestoppt - beim Wechsel auf Settings/Models/etc. laufen weiterhin 3 Requests |
| ⚪ | `configurationpage.html:1084` | Banner-Klasse 'service-banner standalone' existiert im CSS nicht | Der Standalone-Zweig von checkServiceHealth setzt die Klasse 'standalone', das Stylesheet definiert aber nur .online/.offline/.checking (Z. 109-111). Das Banner verliert im Standal |

</details>

<details><summary><b>Web – Player-Integration & Sidebar</b> — 9 Befunde</summary>

| Sev | Ort | Titel | Kurzbeschreibung |
|---|---|---|---|
| 🟡 | `player-integration.js:978` | Status-Zeile prueft nicht existierendes Feld EnableUpscaling | _refreshStatusRow prueft cfg.EnableUpscaling === false, dieses Feld existiert weder in PluginConfiguration.cs noch sonst im Repo - das Master-Feld heisst EnablePlugin. Der Zweig is |
| 🟡 | `player-integration.js:1057` | Config-Strings landen unescaped in innerHTML (Model, FavoriteModels) | _renderModelCard interpoliert m.id/m.name/title unescaped; fuer Favoriten (Z. 1126-1137) stammen id und name direkt aus dem freien Config-String FavoriteModels, ebenso landet confi |
| 🟡 | `player-integration.js:2106` | _getPlayingItemId erwartet '#/video?id=...' - Genre-Signal des Auto-Modus laeuft leer | Der Parser sucht einen id-Parameter im Hash, jellyfin-webs Video-OSD-Route ist aber schlicht '#/video' ohne Query (der Playback-State liegt im playbackManager, nicht in der URL). i |
| 🟡 | `player-integration.js:913` | Player-Feature haengt an admin-only getPluginConfiguration - fuer normale Nutzer stumm defekt | Das Script wird global fuer alle Nutzer injiziert, liest die Config aber ueber /Plugins/{id}/Configuration, das in Jellyfin die RequiresElevation-Policy hat. Fuer Nicht-Admins reje |
| ⚪ | `sidebar-upscaler.js:394` | Erneutes Oeffnen des Panels leakt das Live-Monitoring-Intervall | showUpscalerPanel entfernt ein vorhandenes Panel (Z. 59-62) ohne stopLiveMonitoring(); startLiveMonitoring ueberschreibt dann _monitorInterval ohne clearInterval. Jeder erneute Kli |
| ⚪ | `quick-menu.js:322` | Netzwerk-Test nutzt nicht existierende Route und ignoriert den HTTP-Status | testNetworkConnectivity ruft '/api/system/info' auf - diese Route existiert in Jellyfin nicht (richtig: '/System/Info', zudem ignoriert der absolute Pfad eine Base-URL) - und pruef |
| ⚪ | `player-integration.js:510` | Fallback-Log widerspricht der tatsaechlichen Schwelle (5s vs. 10s) | Der Watchdog schaltet nach 10000 ms ohne erfolgreichen Frame auf WebGL um, das Log meldet aber 'No frames for 5s'. Zusammen mit der Notification 'server unresponsive' (die auch bei |
| ⚪ | `player-integration.js:1119` | Doppelklick auf den Player-Button erzeugt zwei Menues, erstes wird unschliessbar per Outside-Click | toggleUpscalerMenu prueft nur VOR dem async Config-Fetch auf ein vorhandenes Menue; _buildMenu prueft nie. Zwei schnelle Klicks vor Fetch-Ende appenden zwei #aiUpscalerQuickMenu-Ov |
| ⚪ | `sidebar-upscaler.js:38` | Veralteter Selektor 'a[href="#/dashboard.html"]' matcht in Jellyfin 10.9+ nicht mehr | Die Routen von jellyfin-web haben seit 10.9 kein '.html'-Suffix mehr (Dashboard = '#/dashboard'), der Selektor findet daher nie einen Treffer und der Eintrag landet ueber den Fallb |

</details>

<details><summary><b>Web – WebGL/WebGPU/Anime4K</b> — 11 Befunde</summary>

| Sev | Ort | Titel | Kurzbeschreibung |
|---|---|---|---|
| 🟡 | `webgl-upscaler.js:96` | CAS-Sharpening: Adaptivitaet tot und Sharpness-Regler invertiert | d = 1.0/(maxRGB-minRGB+0.05) liegt in [0.95, 20], nach Multiplikation mit -0.125 in [-2.5, -0.119]; clamp(x, -0.1, 0.0) saturiert daher fuer JEDEN Kontrastwert auf konstant -0.1 -  |
| 🟡 | `webgpu-ai-realtime.js:200` | Dauerhaft werfende Inferenz erzeugt endlosen 60-Hz-Fehlerloop ohne Fallback | Wirft _processFrame eine Exception (z.B. WebGPU device lost nach Treiber-Reset, wonach session.run bei jedem Aufruf wirft; ebenso denkbar bei Input-Typ-Mismatch der fp16-Modelle),  |
| 🟡 | `webgpu-ai-realtime.js:207` | Kein Aufloesungs-Cap: Volle Videoaufloesung als Inferenz-Input | _processFrame baut den Input-Tensor immer aus der nativen Videoaufloesung (bei 4K: 3840x2160x3 floats ca. 100 MB pro Frame) und laesst Real-ESRGAN darueber laufen - im Browser Seku |
| 🟡 | `anime4k.js:50` | attachVideo-Fehlschlag ist nicht erkennbar - anime4k-Modus wird stiller No-Op | VideoUpscaler.attachVideo verlangt zusaetzlich zur isSupported()-Pruefung (nur OES_texture_float/_linear) die Extension EXT_color_buffer_half_float und ruft bei deren Fehlen still  |
| 🟡 | `webgl-upscaler.js:170` | webglcontextrestored-Handler laesst Upscaler deaktiviert und erzeugt Textur doppelt | Bei Context-Loss wird disable() gerufen (korrekt), aber der restored-Handler baut nur Shader/Geometrie neu auf und ruft nie enable() - der Upscaler bleibt dauerhaft aus, obwohl 're |
| 🟡 | `webgl-upscaler.js:394` | destroy() gibt den WebGL-Kontext nicht explizit frei (kein loseContext) | Jeder Start-Zyklus erzeugt in init() ein frisches Canvas mit neuem WebGL-Kontext; destroy() entfernt nur das Canvas und nullt Referenzen, ruft aber nie WEBGL_lose_context.loseConte |
| ⚪ | `webgpu-ai-realtime.js:211` | Neues Canvas plus 2D-Kontext pro Frame im Hot-Loop | _processFrame erzeugt bei jedem Frame ein neues srcCanvas samt getContext('2d') fuer den Video-Readback und setzt zudem _canvas.width/height jedes Mal neu (was das Ziel-Canvas auch |
| ⚪ | `webgpu-ai-realtime.js:28` | Modell-Fallback-URL nutzt jsdelivr gh-CDN fuer ein HuggingFace-Repo | Die zweite URL fuer realesrgan-compact-x2 zeigt auf cdn.jsdelivr.net/gh/onnx-community/Real-ESRGAN-Anime/... - der gh-Prefix von jsdelivr bedient ausschliesslich GitHub-Repos, das  |
| ⚪ | `webgl-upscaler.js:401` | destroy() nicht idempotent: ungeguardete deleteBuffer-Aufrufe und nicht genullte Handles | Die deleteBuffer-Aufrufe fuer _positionBuffer/_texCoordBuffer pruefen im Gegensatz zu den nachfolgenden Deletes nicht auf this.gl, und destroy() nullt _positionBuffer, _texCoordBuf |
| ⚪ | `webgl-upscaler.js:307` | Render-Loop laeuft bei pausiertem Video mit voller Rate weiter | render() prueft weder video.paused noch readyState und laedt daher auch im Pausen-/Idle-Zustand mit Display-Refresh-Rate denselben Frame per texImage2D hoch und zeichnet ihn - unno |
| ⚪ | `webgl-upscaler.js:347` | Unbehandelte Exception im Render-Loop hinterlaesst Schwarzbild (Video bleibt opacity:0) | enable() blendet das Video-Element aus (opacity '0') und verlaesst sich darauf, dass der Canvas-Loop rendert. render() hat aber kein try/catch: wirft z.B. texImage2D eine SecurityE |

</details>

<details><summary><b>Python – main.py (Z. 1–3400: Katalog, Model-Load, Auth)</b> — 15 Befunde</summary>

| Sev | Ort | Titel | Kurzbeschreibung |
|---|---|---|---|
| 🟡 | `main.py:466` | realesrgan-x4-256 (fixed-shape 256) ist mit Default-ONNX_TILE_SIZE=512 unbenutzbar | Der dynamische Tiler (_run_onnx_tiled/upscale_with_onnx) schneidet Kacheln der Groesse ONNX_TILE_SIZE (Default 512) bzw. der Bildgroesse; ein Modell mit fest einkompiliertem 256x25 |
| 🟡 | `main.py:914` | ncnn-Katalogeintraege referenzieren Modellnamen, die das installierte realsr-Paket nicht buendelt | Dockerfile.vulkan installiert realsr-ncnn-vulkan-python, das nur die RealSR-DF2K-Modelle (Namen 'models-DF2K'/'models-DF2K_JPEG') mitbringt. Der Katalog uebergibt aber 'realesrgan- |
| 🟡 | `main.py:2371` | Blockierende ort.InferenceSession/session.run direkt im Event-Loop von load_onnx_model | load_onnx_model ist async, erzeugt die Sessions, die GPU-Verifikations-Inferenz (Z. 2393) und den TensorRT-Reload (Z. 2454, Engine-Build kann Minuten dauern) aber synchron im Event |
| 🟡 | `main.py:2649` | cv2.imdecode laeuft vor der MAX_IMAGE_PIXELS-Pruefung (Decompression-Bomb-OOM) | upscale_image (und upscale_image_hdr, Z. 2763-2776) dekodiert das Bild vollstaendig, bevor das eigene 256-MP-Limit greift; massgeblich ist bis dahin nur OpenCVs internes Limit von  |
| 🟡 | `main.py:2032` | load_model ohne Kategorie-Guard: Interpolations-/Face-Restore-Modelle als Haupt-Upscaler ladbar | Anders als load_rife_model (Z. 3112-3114) und load_face_restore_model (Z. 3277-3278) prueft load_model die Kategorie nicht. Ueber POST /models/load laesst sich z.B. rife-v4.9 (scal |
| 🟡 | `main.py:2011` | load_opencv_model setzt current_model_input_frames nicht zurueck | load_onnx_model und load_ncnn_model setzen state.current_model_input_frames aus model_info, load_opencv_model nicht. Wurde zuvor ein ONNX-Modell mit input_frames>1 geladen (z.B. RI |
| 🟡 | `main.py:1388` | nomos8k-hat-l-x4 aktiv, obwohl HAT-S wegen CPU-EP-Inkompatibilitaet deaktiviert wurde | nomos8k-hat-x4 steht mit dem Kommentar 'HAT transformer uses ops (LayerNorm with dynamic shape) that fail on CPUExecutionProvider' auf available:False (Z. 775-777), waehrend nomos8 |
| 🟡 | `main.py:3426` | restore_faces_in_frame meldet fehlgeschlagene Crops als restauriert | Wenn _restore_face_crop fuer einen Crop eine Exception wirft, wird nur gewarnt und continue ausgefuehrt, der Rueckgabewert bleibt aber len(faces). Der Aufrufer (z.B. /face-restore/ |
| ⚪ | `main.py:1955` | Body-Size-Limit nur Content-Length-basiert - Chunked-Requests umgehen es | Die Middleware prueft ausschliesslich den Content-Length-Header; Requests mit Transfer-Encoding: chunked passieren ungeprueft. Die len()-Checks der Endpoints greifen erst NACH voll |
| ⚪ | `main.py:3282` | Fehlermeldung verweist auf nicht existenten Endpoint /download-model | load_face_restore_model raet bei fehlender Modelldatei zu 'POST /download-model?model_name=...' - diesen Endpoint gibt es nicht; tatsaechlich heisst er POST /models/download und er |
| ⚪ | `main.py:1932` | DEFAULT_MODEL: kein Alias-Resolve und unbekannte Namen gelten als 'available' | Anders als die Endpoints (Z. 4327/4390/4433) schickt lifespan den DEFAULT_MODEL-Wert nicht durch _resolve_model_key, Legacy-Keys wie 'rife-v4.6' scheitern daher beim Start. Zudem l |
| ⚪ | `main.py:1842` | Prefix-Pfadcheck ohne Trennzeichen und ignoriertes Sidecar-filename | str(model_path).startswith(str(models_dir.resolve())) akzeptiert Geschwisterpfade wie /app/models-x/... (via filename '../models-x/f.onnx' im Sidecar); dieselbe schwache Pruefung s |
| ⚪ | `main.py:1629` | rocm-smi-VRAM-Parsing interpretiert Bytes als MB | Neuere rocm-smi-Versionen geben 'VRAM Total Memory (B): 17163091968' aus; der erste isdigit-Token (der Byte-Wert) wird unveraendert als MB uebernommen, sodass /hardware und die Plu |
| ⚪ | `main.py:3053` | Zero-Padding statt Reflect bei Multiframe-Randkacheln | upscale_multiframe padded Randkacheln mit Schwarz, wenn eine Bilddimension kleiner als tile_size ist; das VSR-Modell sieht dadurch harte schwarze Kanten und produziert dunkle Saeum |
| ⚪ | `main.py:2179` | Raw-ncnn-Fallback: non-contiguous Numpy-Slices und hartkodierte Blob-Namen | img[y_start:y_end, x_start:x_end] ist bei x-Teilschnitten nicht C-contiguous; ncnn.Mat.from_pixels erwartet einen zusammenhaengenden Pixel-Puffer, sodass Kacheln verschoben/verwuer |

</details>

<details><summary><b>Python – main.py (Z. 3401–6628: Endpoints, Video, Download)</b> — 13 Befunde</summary>

| Sev | Ort | Titel | Kurzbeschreibung |
|---|---|---|---|
| 🟡 | `main.py:3934` | subprocess.run und getaddrinfo blockieren das Event-Loop in Diagnose-Endpoints | /gpu-verify (clinfo + nvidia-smi, je timeout=10), /gpus (Zeile 3866), /doctor (Zeile 4070) und /connections/register (socket.getaddrinfo, Zeile 4251) fuehren blockierende Aufrufe d |
| 🟡 | `main.py:5375` | /models/cleanup dry_run=false scheitert mit managed Tokens und API_TOKEN=disable | Der destruktive Double-Check vergleicht den Header nur gegen den env-API_TOKEN: Mit API_TOKEN=disable ist expected_token="disable" (truthy) und jeder normale Aufruf ohne dieses Lit |
| 🟡 | `main.py:3622` | /logs/recent, /logs/stream und /connections ohne API-Token abrufbar | Die kompletten Server-Logs (inkl. uvicorn-Access-Logs, Fehlerdetails, Modell- und Jellyfin-URLs) sowie die registrierten Plugin-Verbindungen (/connections, Zeile 4214) sind ohne To |
| 🟡 | `main.py:6157` | _ingest_onnx_bytes blockiert das Event-Loop (Session-Load, 500-MB-IO, sha256) | Validierung via ort.InferenceSession auf bis zu 500 MB, Temp-Write und shutil.move laufen synchron, und die Funktion wird direkt aus async-Kontexten aufgerufen: /models/upload (653 |
| 🟡 | `main.py:6053` | /enhance-faces blockiert das Event-Loop | enhance_faces_in_image (Haar-Cascade-Detection auf bis zu 256-MP-Bildern plus pro Gesicht eine 512x512-ONNX-Inferenz bzw. bilateralFilter) laeuft synchron im async-Handler; das Fea |
| 🟡 | `main.py:5436` | /interpolate-frames und /face-restore/frame ohne Concurrency-Limit | Anders als alle Upscale-Endpoints holen /interpolate-frames und /face-restore/frame (Zeile 5675, dort fehlt zusaetzlich _check_circuit_breaker) keine _upscale_semaphore: N parallel |
| 🟡 | `main.py:4409` | Background-Tasks ohne gehaltene Referenz; Job-Registries wachsen unbegrenzt | asyncio.create_task-Rueckgaben fuer Download- (4409) und Import-Jobs (6461) werden verworfen; der Event-Loop haelt nur schwache Referenzen, sodass ein laufender Task laut asyncio-D |
| 🟡 | `main.py:6218` | Upload/Import ueberschreibt Built-in-Modelle bei Namenskollision | _ingest_onnx_bytes prueft nicht, ob model_name bereits ein Built-in-Katalogeintrag ist: Ein Upload namens z.B. "fsrcnn-x2" ersetzt Modelldatei und Registry-Eintrag (Download-URL ge |
| 🟡 | `main.py:4984` | /upscale-stream: Fehlerpfad dropt Frames still, Kommentar behauptet Marker-Frame | Bei einem Inferenzfehler wird nur 'continue' ausgefuehrt — es wird kein Marker-Frame geyieldet, obwohl der Kommentar es behauptet und der adaptive Drop-Pfad (Zeile 4981) extra Fram |
| ⚪ | `main.py:4542` | /upscale meldet Decode-Fehler als 413 | upscale_image wirft ValueError sowohl fuer "Image too large" als auch fuer "Failed to decode image"; der Handler mappt beides pauschal auf 413, sodass ein korruptes Bild als "Paylo |
| ⚪ | `main.py:4455` | /models/load: globale GPU-State-Mutation und Rollback ohne Serialisierung | Parallele /models/load-Requests mutieren state.use_gpu/gpu_device_id global vor dem Laden; schlaegt einer fehl, stellt sein Rollback unter Umstaenden Werte wieder her, die der ande |
| ⚪ | `main.py:5510` | /interpolate-frames: check-then-act beim RIFE-Modell-Load | Zwischen dem needs_load-Check, dem Laden im Executor und dem erneuten Auslesen von state.rife_session liegt kein gemeinsamer Lock: Zwei parallele Requests mit unterschiedlichen RIF |
| ⚪ | `main.py:4928` | /upscale-stream: globales _realtime_stats.reset() bei jedem Stream-Start | Das globale Stats-Objekt wird bei jedem neuen Stream zurueckgesetzt; bei parallelen Streams loeschen sich die Sessions gegenseitig die Zaehler, /realtime-stats zeigt vermischte Wer |

</details>

<details><summary><b>Python – token_store / model_import / Tests</b> — 8 Befunde</summary>

| Sev | Ort | Titel | Kurzbeschreibung |
|---|---|---|---|
| 🟡 | `token_store.py:86` | Tz-naives expires_at crasht verify() mit TypeError statt fail-closed | _is_expired faengt nur ValueError; ein parsebares, aber timezone-naives expires_at (z.B. "2030-01-01T00:00:00" aus einer Hand-Editierung von tokens.json) liefert bei now >= fromiso |
| 🟡 | `test_validation.py:33` | Drei Validierungstests sind wirkungslos (falsches Form-Feld, Auth-403 als Erfolg gewertet) | test_model_name_path_traversal_rejected und test_model_name_with_special_chars_rejected posten das Feld "model", der Endpoint erwartet aber model_name (main.py:4324, Form(...)) - F |
| 🟡 | `convert_to_onnx.py:115` | strict=False plus Erfolgsmeldung maskiert komplett unpassende Checkpoints | load_state_dict(strict=False) ignoriert saemtliche fehlenden/unerwarteten Keys und danach wird bedingungslos "Loaded pretrained ... weights" gedruckt (ebenso Z.180/310). Die hier d |
| 🟡 | `test_catalog_import.py:1` | Import-Download-Pfad (Cap, sha-Pin, Async-Job) komplett ungetestet | Getestet sind nur _import_gate, _extract_pinned_onnx_from_zip und die Job-Fehlercodes; _download_capped (Cap-Durchsetzung), _download_pinned (502 bei sha-Mismatch) und der eigentli |
| ⚪ | `convert_to_onnx.py:40` | Abgebrochener Weight-Download vergiftet den Cache dauerhaft | urllib.request.urlretrieve laesst bei einem Abbruch (Verbindungsreset, Ctrl-C) eine partielle Datei unter weights/ liegen; der naechste Lauf sieht os.path.exists(filepath) und meld |
| ⚪ | `token_store.py:112` | Korrupte tokens.json wird still als leerer Store behandelt | _load degradiert OSError/JSONDecodeError kommentarlos zu _empty(): alle Managed Tokens hoeren schlagartig auf zu funktionieren (403), ohne dass irgendein Log auf die eigentliche Ur |
| ⚪ | `main.py:5099` | Endpoint-Doku verspricht expires_days=0 als 'nie ablaufend', Store lehnt 0 ab | Der Docstring von POST /auth/tokens sagt "Omit expires_days (or 0/null) for a token that never expires", token_store.create_token (token_store.py:171) wirft fuer expires_days<=0 ab |
| ⚪ | `test_token_store.py:1` | Kein Test fuer die zentrale Concurrency-Zusicherung des Token-Stores | Der Moduldocstring von token_store verspricht, dass parallele Requests die Datei nicht korrumpieren und frisch erzeugte Tokens nicht ueberschrieben werden (Lock + atomischer Replac |

</details>

<details><summary><b>Infrastruktur – Dockerfiles, requirements, CI-Workflows</b> — 17 Befunde</summary>

| Sev | Ort | Titel | Kurzbeschreibung |
|---|---|---|---|
| 🟡 | `Dockerfile.amd:80` | --force-reinstall ohne --no-deps kann die bewusste numpy<2-Kappe aushebeln | pip install --force-reinstall "onnxruntime-rocm<=1.22.99" reinstalliert auch alle Dependencies und waehlt dabei die NEUESTE Version, die nur die Constraints dieses einen Aufrufs er |
| 🟡 | `docker-publish.yml:146` | Dry-Run (push=false) scheitert am Registry-Cache-Export ohne Login | Bei workflow_dispatch mit push='false' wird der Docker-Hub-Login uebersprungen (Zeile 104), aber cache-to: type=registry versucht trotzdem unauthentifiziert Cache-Layer nach docker |
| 🟡 | `build-and-release.yml:51` | workflow_dispatch-Lauf schlaegt immer fehl: VERSION wird 'refs/heads/main' | Bei workflow_dispatch ist GITHUB_REF=refs/heads/main; die Prefix-Entfernung ${GITHUB_REF#refs/tags/v} greift nicht und laesst den vollen String stehen. Der [ -z "$VERSION" ]-Fallba |
| 🟡 | `build.yml:105` | AMD-Dependency-Drift wird entgegen dem Kommentar von KEINEM Workflow abgedeckt | Der Kommentar behauptet, die AMD-Variante werde 'by the weekly lock-requirements workflow' abgedeckt - aber lock-requirements.yml hat keinen amd-Eintrag in der Matrix (nur cpu/inte |
| 🟡 | `import-catalog-refresh.yml:28` | Woechentlicher Katalog-Commit deployt die Live-Site nie (GITHUB_TOKEN triggert pages.yml nicht) | Der Push von site/models-import.json erfolgt mit dem Default-GITHUB_TOKEN, und von GITHUB_TOKEN erzeugte Push-Events starten keine weiteren Workflows - pages.yml (Trigger: push auf |
| 🟡 | `Dockerfile.converter:43` | requirements-converter.lock wird woechentlich erzeugt und committet, aber nie installiert | lock-requirements.yml generiert einen (bewusst hashlosen, aber versions-gepinnten) Converter-Lock, doch Dockerfile.converter installiert weiterhin die Range-Datei requirements-conv |
| 🟡 | `docker-publish.yml:28` | Kein concurrency-Guard: parallele Runs racen :latest und die Rolling-Tags | CLAUDE.md dokumentiert als bekannte Falle, dass zwei gleichzeitige docker-publish-Runs den :latest- und docker7-Rolling-Tag racen und der Operator den ueberholten Run manuell cance |
| 🟡 | `docker-publish.yml:23` | Stales Dispatch-Default '1.6.1.13' ueberschreibt gepinnte Versions-Tags mit aktuellem Code | Das workflow_dispatch-Input version hat den veralteten Default 1.6.1.13 (aktuell: 1.8.3.21). Wer beim Dispatch das vorbefuellte Feld nicht aendert, published aktuellen main-Code un |
| 🟡 | `build.yml:80` | pip-audit prueft die Range-Datei statt der tatsaechlich installierten Locks | pip-audit -r requirements-cpu.txt aufloest die Ranges frisch und prueft damit die NEUESTEN Versionen - die Images installieren aber die bis zu eine Woche alten, gepinnten requireme |
| 🟡 | `docker-publish.yml:170` | Fehlendes SARIF nach Trivy-Timeout faerbt den Job trotzdem rot | Trivy hat continue-on-error:true ('never let a scan hiccup paint a shipped build red'), aber wenn der Scan am 20-GB-ROCm-Image timeoutet, existiert trivy-amd.sarif nicht und der Up |
| 🟡 | `build.yml:17` | contents:write auf Build-only-Workflows, die per Repo-Regel nie releasen duerfen | build.yml und build-and-release.yml (Zeilen 19-20) fordern permissions: contents: write, obwohl beide nur bauen/testen/Artefakte hochladen - vermutlich ein Relikt der bewusst entfe |
| ⚪ | `v1.7.1-audit-checks.yml:87` | zip-version-check kollidiert mit der dokumentierten 3-Part-Konvention bei X.Y.Z.0-Releases | Laut Release-Prozess (CLAUDE.md) ist meta.json bei einer .0-Version 3-teilig (z.B. 1.9.0), waehrend die Feeds immer 4-teilig sind (1.9.0.0). Der String-Vergleich PUB_VERSION != MAN |
| ⚪ | `lock-requirements.yml:70` | Action-Pinning inkonsistent: 6 von 8 Workflows nutzen mutable Tags statt SHAs | build.yml und build-and-release.yml pinnen alle Actions vorbildlich per Commit-SHA, aber lock-requirements.yml, docker-publish.yml, pages.yml, dockerhub-cleanup.yml, import-catalog |
| ⚪ | `Dockerfile.vulkan:83` | Ungepinnte pip-Installs nach dem hash-gelockten Layer unterlaufen das Lock-Konzept (vulkan) | Nach dem --require-hashes-Install folgen ungepinnte, hashlose Installs: realsr-ncnn-vulkan-python/ncnn (breite Ranges, stderr unterdrueckt) und pybind11 ganz ohne Constraint; zudem |
| ⚪ | `Dockerfile.vulkan:56` | Build-Toolchain (cmake, build-essential, ninja, git) verbleibt im finalen Vulkan-Image | Die Compiler-Toolchain wird nur fuer den Source-Build-Fallback von ncnn gebraucht, bleibt aber auch dann im Image (mehrere hundert MB plus groessere Angriffsflaeche), wenn der Whee |
| ⚪ | `lock-requirements.yml:17` | variants-Input ist wirkungslos und der numpy<2-Assert kann nie greifen | Das workflow_dispatch-Input 'variants' wird im Workflow-Body nirgends referenziert - die Matrix ist hartkodiert, ein Dispatch mit variants='cpu' lockt trotzdem alle 7. Zudem prueft |
| ⚪ | `Dockerfile.amd:90` | CPU-Fallback des AMD-Images ist nur eine Logzeile in einem gruenen Publish-Run | Faellt der onnxruntime-rocm-Install auf plain onnxruntime zurueck, druckt der Sichtbarkeits-RUN lediglich ein WARNING in das Build-Log - docker-publish bleibt gruen und pusht das C |

</details>

<details><summary><b>Release – Feeds, Versions-Stamping, Scripts</b> — 10 Befunde</summary>

| Sev | Ort | Titel | Kurzbeschreibung |
|---|---|---|---|
| 🟡 | `meta.json:11` | meta.json-Changelog ist 8 Releases veraltet (neuester Eintrag v1.8.3.13 bei Version 1.8.3.21) | Das changelog-Feld ist eine kumulierte Historie, deren juengster Eintrag v1.8.3.13 ist, waehrend version 1.8.3.21 traegt - seit acht Releases wurde kein Eintrag mehr vorangestellt. |
| 🟡 | `manifest.json:440` | v1.5.6.0-Eintrag traegt SHA256 statt MD5 als checksum - Version aus dem Katalog nicht installierbar | Der checksum-Wert von v1.5.6.0 ist 64 Hex-Zeichen (SHA256), alle anderen 79 Eintraege sind 32-Zeichen-MD5; Jellyfin berechnet MD5 des ZIPs und lehnt bei Nichtuebereinstimmung ab, v |
| 🟡 | `sync-fallback-models.ps1:11` | Katalog-Sync-Gate prueft nur Model-IDs; available/scale-Drift bleibt unentdeckt, beschriebener Diff-Gate existiert nicht | Der Header verspricht 'CI gate: no-op on clean tree (git diff --exit-code)' - dieser Gate existiert nicht und koennte so auch nie bestehen, weil generated_at das Tagesdatum einbett |
| ⚪ | `JellyfinUpscalerPlugin.csproj:54` | Tote FFmpeg-Wrapper-Scripts werden weiterhin in den Build-Output kopiert (Release-ZIP-Falle) | Das Wrapper-Feature wurde in v1.8.3.2 komplett entfernt; upscale-ffmpeg.sh/.bat haben keinerlei Code-Referenzen mehr, werden aber per CopyToOutputDirectory=Always als Scripts/ in d |
| ⚪ | `upscale-ffmpeg.bat:38` | Batch-Wrapper-Logik ist mehrfach kaputt (findstr-Literal, fehlende Delayed Expansion, stale API-Route) | findstr /C: sucht den LITERALEN Text 'SupportsCUDA.*true' (Regex braeuchte /R), CUDA wuerde also nie erkannt; %errorlevel%, %ARGS% und %UPSCALE_FILTER% werden innerhalb des Klammer |
| ⚪ | `manifest.json:261` | Changelog-Texte von 19 Alt-Versionen weichen zwischen manifest.json und den beiden Repository-Feeds ab | Fuer die Versionen 1.6.1.14 bis 1.7.7.0 traegt manifest.json laengere Changelog-Texte als repository-jellyfin.json/repository-simple.json (letztere sind untereinander byteidentisch |
| ⚪ | `verify-release.ps1:122` | targetAbi der Feed-Eintraege wird nur geloggt, nie validiert | Der Triple-Feed-Check asserted checksum-Format/-Gleichheit und sourceUrl, gibt targetAbi aber nur aus; ein Feed-Eintrag mit falschem oder 3-teiligem targetAbi (Konvention: 4-part 1 |
| ⚪ | `bump-version.py:4` | Docstring behauptet '16 sites', das Script stampt 13 (und spiegelt verify-release nicht 'exactly') | Die sites-Liste hat 13 Eintraege (CLAUDE.md dokumentiert korrekt 13); verify-release.ps1 prueft 16 Stellen, weil die drei Feed-Dateien dort zusaetzlich gecheckt werden, die bump-ve |
| ⚪ | `check_ui_field_consistency.py:37` | ID-Definitions-Regex maskiert Phantom-Referenzen (data-id, JS-Variable 'id', Backtick-Selektoren ungeprueft) | ID_DEF_RE matcht wegen \bid auch data-id="..." (Wortgrenze nach '-') und jede JS-Zuweisung id = '...'; solche Treffer registrieren Werte als 'definierte' Element-IDs und koennen ge |
| ⚪ | `verify-release.ps1:74` | Script bricht unter Linux/macOS sofort ab ($env:TEMP ist dort nicht gesetzt) | Join-Path $env:TEMP wirft bei nicht gesetzter TEMP-Variable (Standard auf Linux-pwsh) einen Binding-Fehler, und mit ErrorActionPreference=Stop stirbt das Script vor jeder Pruefung. |

</details>

## Offene Punkte / Coverage-Gaps

- **`csharp-tests`-Review fehlt** (Session-Limit): Die xUnit-Suite (25+ Dateien) wurde nicht bewertet. Bekannte Verdachtspunkte laut Projektwissen: fehlende Tests für `VideoProcessor`, `ProcessingMethodExecutor`, `UpscalerController` sowie für beide Scale-Namenskonventionen und die 8K/CPU-only-Szenarien.
- **10 High-Findings (v.a. Python & 2 Web) sind Agent-Befunde ohne zweite Hauptprozess-Prüfung** — angesichts der 100 %-Trefferquote bei den 13 selbst geprüften C#-Befunden mit hoher Wahrscheinlichkeit korrekt, aber vor einem Fix kurz gegenzulesen.
- **Adversariale Verifikations-Agenten liefen nie** (Session-Limit, Reset 10:10 UTC). Bei Bedarf kann der Workflow per Resume nachgeholt werden — fertige Reviews kommen aus dem Cache.

## Empfohlene Sofort-Reihenfolge

1. **🔴 `POST /process` absichern** (controller#1): Library-Allowlist wie in `EnqueueJob` + Überschreibschutz. Einziger Critical.
2. **🟠 Auto-Mode-Wurzel fixen** (core#1/processing#2/io#1): `Model`-Default auf `"auto"`/leer ODER Batch-Gate auf `EnableAutoModelSelection` allein reduzieren. Ein Fix, drei Symptome — reaktiviert Hardware-Cap & 8K-Vermeidung im nächtlichen Scan.
3. **🟠 Queue-Busy-Spin** (processing#1): Resume-`Release()` entfernen + Leerzweig-Permit nicht restaurieren. 100 % CPU-Dauerlast nach jedem Job-Cancel.
4. **🟠 Locale-Bugs** (video#1/#4): `InvariantCulture` bei `fps=`/`-ss` — sonst scheitert auf de-DE/fr-FR jeder Frame-by-Frame-Job.
5. **🟠 Stille Daten-'Vergiftung'** (io#5, video#3): Nicht-AI-Fallbacks nicht als Erfolg speichern/melden.
6. Danach die restlichen High-Findings (io#2/#3/#4, controller#2/#3, python-main-b#3, python-main-a#1) und die Release-Tooling-Bugs.

---
_Multi-Agent Code-Review · 13/14 Bereiche · 184 Findings · 36 Critical/High mit Verifikationsverdikt · erstellt 2026-08-03_