# Kompletter Code-Review – JellyfinUpscalerPlugin

> **Zeile-für-Zeile Multi-Agent-Review** des gesamten Repos, mit **zweistufiger Verifikation**: (1) 14 Bereichs-Agenten lasen jede Datei vollständig, (2) separate Skeptiker-Agenten prüften jeden Critical/High-Befund adversarial, und (3) ich habe die 36 Critical/High-Befunde zusätzlich manuell im Code nachgelesen. Wo manuelle und adversariale Prüfung sich in der Schwere unterschieden, zeigt der Bericht beide.

## Ergebnis in einem Satz

Von 36 gemeldeten Critical/High-Befunden sind **35 als real bestätigt** (beide Prüfungen), **1 widerlegt** (`python-main-b#2`); die adversariale Prüfung hat mehrere Schweregrade nach unten (und zwei nach oben) korrigiert. Der schwerwiegendste Befund bleibt die **fehlende Library-Allowlist in `POST /process`**.

## Methodik & Abdeckung

- **14 von 14 Bereichen** vollständig reviewt (~55.000 Zeilen: C#-Plugin inkl. Testsuite, Web-UI, Python-AI-Service, Docker/CI, Release-Feeds).
- **192 Findings** insgesamt. Die 36 Critical/High wurden **doppelt verifiziert** (adversariale Agenten + manuelle Code-Prüfung); medium/low sind Erst-Reviewer-Befunde.
- **Verifikations-Bilanz der 36 C/H:** 35 real, 1 widerlegt. Endgültige Schwere nach adversarialer Kalibrierung: **20 high, 15 medium, 1 hinfällig**. (Erst-Einstufung war 1 critical / 35 high — die Skeptiker-Agenten waren durchweg konservativer und gut begründet.)
- **Lokal geprüft:** Python-Testsuite **123/123 grün**; die drei Plugin-Feeds für v1.8.3.21 identisch in Version/Checksum/sourceUrl/targetAbi. `dotnet build` war in der Sandbox nicht möglich (Proxy blockt die SDK-Server) — deckt die CI ab.
- **Nicht abgedeckt:** statische `site/*.html` (außer Katalog-Abgleich), generierte `site/models-import.json`, alte Release-ZIPs im Root, README/Docs.

## Verteilung (nach finaler, verifizierter Schwere)

| Schwere | Anzahl |
|---|--:|
| 🟠 high | 20 |
| 🟡 medium | 102 |
| ⚪ low | 69 |
| ❌ widerlegt | 1 |
| **Gesamt gemeldet** | **192** |

### Findings pro Bereich (gemeldet)

| Bereich | 🔴 | 🟠 | 🟡 | ⚪ | Σ |
|---|--:|--:|--:|--:|--:|
| C# – Kern (Plugin, Config, Registries) | 0 | 1 | 2 | 2 | 5 |
| C# – Controller (REST-API) | 1 | 2 | 6 | 3 | 12 |
| C# – Processing (Queue, Auto-Model, Hardware-Cap) | 0 | 2 | 6 | 7 | 15 |
| C# – Video-Pipeline (ffmpeg, Frames, VMAF) | 0 | 3 | 10 | 5 | 18 |
| C# – I/O (HTTP-Client, Cache, Scheduled Tasks) | 0 | 5 | 8 | 4 | 17 |
| C# – Testsuite (xUnit) | 0 | 0 | 4 | 4 | 8 |
| Web – Konfigurationsseite (configurationpage.html) | 0 | 3 | 6 | 6 | 15 |
| Web – Player-Integration & Sidebar | 0 | 5 | 4 | 5 | 14 |
| Web – WebGL/WebGPU/Anime4K | 0 | 1 | 6 | 5 | 12 |
| Python – main.py (Z. 1–3400) | 0 | 3 | 8 | 7 | 18 |
| Python – main.py (Z. 3401–6628) | 0 | 5 | 9 | 4 | 18 |
| Python – token_store / model_import / Tests | 0 | 1 | 4 | 4 | 9 |
| Infrastruktur – Dockerfiles, requirements, CI | 0 | 2 | 11 | 6 | 19 |
| Release – Feeds, Versions-Stamping, Scripts | 0 | 2 | 3 | 7 | 12 |

## Verifikations-Highlights (wo die zweite Prüfung etwas geändert hat)

**❌ Widerlegt:**
- **`python-main-b#2`** – 413 in /upscale und /upscale-hdr wird zu 500 und oeffnet den Circuit-Breaker. Die adversariale Prüfung zeigt: Die globale `limit_body_size`-Middleware liefert oversize-Requests schon **vor** dem Handler ein sauberes 413 — der In-Handler-413→500-Pfad ist unerreichbar, der Circuit-Breaker wird nie geöffnet. Konsistent mit dem bestätigten `python-main-a#1` (dieselbe Middleware blockt große Uploads).

**🔴→🟠 Herabgestuft (real, aber weniger schwer als erst gemeldet):**
- **`csharp-controller#1`** (der einzige Critical) → **high**: Bestätigt real, aber Ausnutzung setzt einen **angemeldeten** User und einen dekodierbaren Input im Zielordner voraus; echte Arbitrary-Overwrite außerhalb von Medienordnern hängt vom ffmpeg-Verhalten ab. Bleibt der schwerwiegendste Befund.
- Weitere High→Medium (Existenz bestätigt, Schwere kalibriert): `controller#2` (reine Admin-Preview), `io#4` (schmaler Trigger: preprocess + 30-Tage-Ablauf), `video#2` (nur Cancel/Fehlerpfad, kein Datenverlust), `infra#1` (Dry-Run zeigt Löschliste vorher), sowie `web-player#3/#4/#5`, `web-confightml#2/#3`, `python-main-b#4/#5`, `python-rest#1`.

**🟡→🟠 Hochgestuft (Skeptiker sah es strenger als ich):**
- **`infra#2`** (`trivy-action@master`) → **high**: läuft im selben Job direkt nach `docker/login-action`, das das `DOCKERHUB_TOKEN` in die Runner-Config schreibt — ein manipulierter `@master` könnte es exfiltrieren.
- **`release#2`** (`verify-release.ps1` 3-teilige Tags) → **high**: Die sourceUrls belegen, dass `.0`-Releases real 3-teilige Git-Tags tragen (v1.8.0, v1.8.3, v1.7.8) — die Verifikation schlägt für diese ganze Release-Klasse fehl, nicht nur hypothetisch.

## 🔴🟠 Die verifizierten Critical/High-Befunde

Sortiert nach finaler Schwere, dann Bereich. **✅** = beide Prüfungen bestätigen · Schwere-Annotation zeigt Erst→final, wenn geändert.

### 🟠 POST /process ohne Library-Allowlist: beliebige Serverpfade fuer jeden authentifizierten User
**`Controllers/UpscalerController.cs:1309`** · csharp-controller · ✅ bestätigt (2×) _(Erst: critical → final: **high**)_

ProcessVideo ist seit v1.7.5 fuer jeden authentifizierten (Nicht-Admin-)User erreichbar, prueft aber im Gegensatz zu den Schwester-Endpoints EnqueueJob (Z. 1542-1547) und PreProcessVideo (Z. 1740-1745) NICHT, ob InputPath in einer Jellyfin-Bibliothek liegt. Ein beliebiger User kann so jede existierende Serverdatei als Input angeben und einen OutputPath im selben Verzeichnisbaum waehlen; da ffmpeg mit -y laeuft (ProcessingMethodExecutor.cs:1100), wird eine dort existierende ANDERE Datei kommentarlos ueberschrieben (Datenverlust). Zusaetzlich wirkt der 'Input file not found'-Check als Datei-Existenz-Orakel fuer beliebige Pfade.
> **Verifikation:** Bestaetigt: POST /Upscaler/process hat nur Klassen-[Authorize] (UpscalerController.cs:34) ohne RequiresElevation (vgl. ExportSettings:1768), ist also fuer jeden angemeldeten Nicht-Admin erreichbar, und der Pfadblock Z.1309-1336 prueft nur Existenz + gleiches Verzeichnis, aber KEIN GetVirtualFolders()-Allowlist wie EnqueueJob (Z.1542) und PreProcessVideo (Z.1740). Downstream revalidiert VideoProcessor.ProcessVideoAsync (Z.186) nichts und ffmpeg laeuft mit -y (ProcessingMethodExecutor.cs:1100) ohne File.Exists(outputPath), sodass eine benachbarte Mediendatei ueberschrieben wird. Severity auf hig
> **Fix:** Dieselbe Library-Allowlist wie in EnqueueJob/PreProcessVideo anwenden (inkl. Separator-sicherem Prefix-Vergleich) und Ueberschreiben existierender Output-Dateien ablehnen (File.Exists-Check bzw. erzwungenes _upscaled-Suffix).

### 🟠 queue/add: outputPath kann existierende Bibliotheksdateien ueberschreiben (ffmpeg -y)
**`Controllers/UpscalerController.cs:1558`** · csharp-controller · ✅ bestätigt (2×)

EnqueueJob (fuer jeden authentifizierten User erreichbar) prueft nur, dass outputPath unter dem Input-Verzeichnis liegt, aber nicht, ob dort bereits eine Datei existiert. Da die Pipeline ffmpeg mit -y aufruft, kann ein User z.B. outputPath auf einen anderen Film im selben Ordner zeigen lassen und ihn mit dem Transcode-Ergebnis ueberschreiben - Datenverlust an Original-Mediendateien.
> **Verifikation:** Bestaetigt: EnqueueJob prueft in Z.1558-1562 nur, dass outputParent unter inputParent liegt, aber kein File.Exists(outputPath) und kein outputPath==inputPath. Der Queue-Worker (UpscalerService.cs:130) reicht job.OutputPath direkt an ProcessVideoAsync weiter, das mit ffmpeg -y (ProcessingMethodExecutor.cs:1100) schreibt; da der Input laut Allowlist (Z.1542) in einer Library liegt, kann ein angemeldeter User eine benachbarte Original-Mediendatei ueberschreiben (Datenverlust). Gemeldete Severity high passt.
> **Fix:** Vor dem Enqueue File.Exists(outputPath) ablehnen (oder nur das feste _upscaled-Suffix zulassen) und outputPath == inputPath explizit verbieten.

### 🟠 Model-Default "realesrgan-x4" macht den Auto-Mode-Default im Batch-Scan wirkungslos
**`PluginConfiguration.cs:58`** · csharp-core · ✅ bestätigt (2×)

EnableAutoModelSelection defaultet seit v1.8.3.12 auf true ("Auto mode is the default"), aber LibraryUpscaleScanTask.cs:303 verlangt zusaetzlich Model=="auto" bzw. leer - und Model defaultet auf "realesrgan-x4". Der Dashboard-Mode-Switch (configurationpage.html:1344) setzt nur das Flag, nie Model, d.h. bei jeder Installation, in der der User das Model-Dropdown nicht explizit auf "Auto" stellt, zeigt das Dashboard "Auto -> <model>" an, waehrend der naechtliche Scan stur realesrgan-x4 (4x) fuer alle Videos nutzt und Anime-Erkennung, Hardware-Cap und 8K-Vermeidung uebersprungen werden. Der Player-Pfad (recommend-model, forceAuto:true) nutzt dagegen die echte Heuristik - zwei Konsumenten desselben Flags verhalten sich widerspruechlich.
> **Verifikation:** Belegt: Model defaultet auf 'realesrgan-x4' (PluginConfiguration.cs:58 via :13), EnableAutoModelSelection auf true (:221). In LibraryUpscaleScanTask.cs:303 lautet die Auto-Bedingung true && (leer\|\|'auto') => bei Defaults false, also else-Zweig (:331-348), der config.Model direkt nutzt und ResolveModelForVideoDetailed (Anime-/Hardware-/8K-Logik) nie aufruft. Der Task hat einen DailyTrigger 3 Uhr (:59-68), laeuft also automatisch. Der Player-Pfad (UpscalerController.cs:1067) nutzt forceAuto:true und umgeht damit den Early-Return in UpscalerCore.cs:404 - echte Divergenz. Der Dashboard-Switch sc
> **Fix:** Entweder den Scan-Task-Check auf EnableAutoModelSelection allein reduzieren (analog zum forceAuto-Pfad des Players), oder Model auf "auto" defaulten und den Dashboard-Switch Model mitschreiben lassen - die Doppelbedingung macht den shipped Default von einer Nutzerentscheidung ununterscheidbar (dokumentierte Bugklasse dieses Repos).

### 🟠 Auto-Modell-Resolver wird durch Model-Default nie erreicht
**`ScheduledTasks/LibraryUpscaleScanTask.cs:303`** · csharp-io · ✅ bestätigt (2×)

Das Gate verlangt EnableAutoModelSelection UND Model leer/"auto" - aber PluginConfiguration.Model defaultet auf "realesrgan-x4" (PluginConfiguration.cs:13/58) und der Dashboard-Auto-Switch schreibt nur EnableAutoModelSelection, nie Model (configurationpage.html:1344). Mit ausgelieferten Defaults (Auto-Modus laut Kommentar v1.8.3.12 Standard) laeuft der taeglich um 3 Uhr getriggerte Scan daher immer im else-Zweig: festes realesrgan-x4 mit effectiveScale=4 fuer jedes Video, der Resolver mit Hardware-Cap und TargetScaleFor wird uebersprungen. Ein 1916x1080- oder 1280x720-Item wird so zu ~8K/5K hochgerechnet - exakt die 8K-/CPU-Kollaps-Klasse, gegen die der Resolver in v1.8.3.14 gebaut wurde, und exakt die bekannte Default-als-Override-Bug-Klasse.
> **Verifikation:** Das Gate LibraryUpscaleScanTask.cs:303 ist mit Auslieferungs-Defaults nie wahr: EnableAutoModelSelection=true (PluginConfiguration.cs:221), aber Model="realesrgan-x4" (PluginConfiguration.cs:13/58) ist weder leer noch "auto"; der Dashboard-Switch schreibt nur das Flag (configurationpage.html:1344) und der Config-Save normalisiert Model nie (UpscalerController.cs:1895). Damit laeuft der Daily-3-Uhr-Scan (Trigger Z.65) stets im else-Zweig mit realesrgan-x4 nativ 4x, waehrend der Resolver via ModelScale.TargetScaleFor 1080p auf 2x deckelt (ModelScale.cs:76-78) - ein 1916x1080-Item wird real auf ~
> **Fix:** Model-Default auf "auto" (oder leer) setzen bzw. das Gate auf EnableAutoModelSelection allein reduzieren, sodass der Dashboard-Switch tatsaechlich den Resolver aktiviert; alternativ muss der Auto-Switch in der Config-Seite auch cfg.Model="auto" schreiben.

### 🟠 Modell-Download/-Load nutzt 120s-Client statt des 570s-Download-Clients
**`Services/HttpUpscalerService.cs:323`** · csharp-io · ✅ bestätigt (2×)

DownloadModelAsync und LoadModelAsync gehen ueber GetClient() = "AiUpscaler" (120s Timeout laut PluginServiceRegistrator.cs:72), aber /models/download ist serverseitig synchron (main.py:4335 awaited download_model vor der Antwort) und Erstdownloads sind laut Registrator-Kommentar bis ~380MB gross. Auf langsameren Leitungen bricht der Call nach 120s mit TaskCanceledException ab, die als Cancellation behandelt wird (break, kein Retry) - EnsureModelLoadedAsync kann groessere Modelle damit nie erstmalig bereitstellen, die Modellkette faellt durch und Batch-Laeufe brechen pro Item ab. Der genau dafuer registrierte Client "AiUpscalerDownload" (570s) bzw. der /models/download-async-Endpunkt (v1.8.2, gebaut gegen genau diese Client-Timeouts) werden hier nicht genutzt.
> **Verifikation:** DownloadModelAsync/LoadModelAsync nutzen GetClient() = "AiUpscaler" mit 120s (HttpUpscalerService.cs:62-68, 323, 366), waehrend /models/download serverseitig synchron ist (main.py:4335 awaited download_model, Kommentar 320-323 bestaetigt Timeout-Problem bei grossen Modellen ~380MB). Ein 120s-Timeout wirft TaskCanceledException -> break -> return false ohne Retry (328-331); der eigens dafuer gebaute 570s-Client AiUpscalerDownload (PluginServiceRegistrator.cs:81) und /models/download-async bleiben ungenutzt. EnsureModelLoadedAsync liegt auf dem Batch-Pfad (VideoProcessor.cs:242, UpscalerCore.cs:
> **Fix:** In DownloadModelAsync/LoadModelAsync den "AiUpscalerDownload"-Client (570s) verwenden oder auf /models/download-async plus Status-Polling umstellen.

### 🟠 Bicubic-Fallback wird als Erfolg gespeichert und blockiert AI-Nachbearbeitung dauerhaft
**`ScheduledTasks/ImageUpscaleScanTask.cs:234`** · csharp-io · ✅ bestätigt (2×)

UpscalerCore.UpscaleImageAsync liefert nie null: Bei jedem Fehler (Service down, Modell nicht ladbar) kommt FallbackResizeAsync-Output oder als letzte Stufe die unveraenderten Original-Bytes zurueck (UpscalerCore.cs:183/188/712). Der Task kann das nicht unterscheiden, schreibt das Nicht-AI-Ergebnis als _upscaled-Datei, zaehlt success und feuert den "complete"-Webhook - und der Scan-Filter (Z.146-150) ueberspringt das Bild in allen kuenftigen Laeufen dauerhaft. Faellt der AI-Service mitten im woechentlichen Lauf aus, wird so die gesamte restliche Bibliothek mit Lanczos-Resizes oder 1:1-Kopien vergiftet, die nie wieder per AI ersetzt werden.
> **Verifikation:** UpscalerCore.UpscaleImageAsync liefert nie null: bei erschoepfter Modellkette FallbackResizeAsync (Lanczos, UpscalerCore.cs:183/188) und im Ausnahmefall die Originalbytes (712). ImageUpscaleScanTask.cs:234 prueft nur !=null && Length>0, schreibt das Nicht-AI-Ergebnis als _upscaled (240), zaehlt success und feuert den complete-Webhook (247). Der Scan-Filter (134-150) ueberspringt jedes Item mit vorhandener _upscaled-Datei dauerhaft, sodass ein AI-Ausfall mitten im woechentlichen Lauf die restliche Bibliothek permanent mit Lanczos-/1:1-Kopien vergiftet, die nie wieder per AI ersetzt werden.
> **Fix:** Fuer den Batch-Task eine API-Variante ohne stillen Fallback nutzen (z.B. direkt _httpUpscalerService.UpscaleImageAsync, das bei Fehlern null liefert) oder UpscalerCore ein Flag "IsAiResult" zurueckgeben lassen und Fallback-Ergebnisse als Fehler zaehlen statt zu speichern.

### 🟠 Busy-Spin des Queue-Workers bei ueberzaehligen Semaphore-Permits
**`Services/ProcessingQueue.cs:133`** · csharp-processing · ✅ bestätigt (2×)

Cancel() entfernt einen Pending-Job ohne ein Semaphore-Permit zu verbrauchen, und Resume() gibt ein zusaetzliches Permit frei, obwohl Enqueue schon eines pro Job freigegeben hat. Sobald die Queue leer ist, laeuft DequeueAsync dann heiss: WaitAsync gelingt sofort, der Leer-Zweig macht Release und continue ohne jedes Delay - eine endlose Spin-Schleife mit 100% CPU auf einem Core, bis der naechste Enqueue kommt (und danach wieder). Trigger ist real erreichbar ueber POST /Upscaler/queue/{jobId}/cancel (UpscalerController.cs:1594) oder die Sequenz Pause->Enqueue->Resume.
> **Verifikation:** Deterministisch nachvollzogen: Cancel (ProcessingQueue.cs:190) entfernt den Job ohne Permit-Verbrauch, Enqueue released aber immer (Z.99) und der Leerzweig restauriert das Permit via Release+continue ohne jedes Delay (Z.133-134), sodass ein stale Permit nie gedraint wird. Einziger Consumer QueueWorkerLoopAsync (UpscalerService.cs:98) ruft DequeueAsync in enger while-Schleife, und await _signal.WaitAsync kehrt bei sofort verfuegbarem Permit synchron ohne Yield zurueck -> 100% CPU-Spin auf einem Core bis Neustart. Repro real: Enqueue+Cancel desselben Jobs vor dem Dequeue ueber POST /Upscaler/que
> **Fix:** Im Leer-Zweig das Permit NICHT restaurieren (ein Permit ohne Job ist stale; jeder konkurrierende Enqueue bringt sein eigenes Permit mit) und das zusaetzliche Release in Resume() entfernen. Alternativ in Cancel() ein Permit via WaitAsync(0) verbrauchen.

### 🟠 Auto-Mode und Hardware-Cap im Batch-Pfad durch Model-Default faktisch tot
**`Services/UpscalerCore.cs:403`** · csharp-processing · ✅ bestätigt (2×)

Der Custom-Arm entscheidet nur ueber Config.Model != "auto" und ignoriert EnableAutoModelSelection (den Schalter, den der Dashboard-Mode-Switch tatsaechlich setzt). Config.Model defaultet auf "realesrgan-x4" (PluginConfiguration.cs:13) und das #Model-Select der UI enthaelt keine "auto"-Option - Model kann also nie "auto" werden. Damit ist die Bedingung in LibraryUpscaleScanTask.cs:303 (EnableAutoModelSelection && Model=="auto") fuer jede reale Installation falsch: Der Batch-Scan nutzt immer realesrgan-x4 (Heavy, 4x) und die komplette v1.8.3.14-Hardware-Cap/Scale-Logik laeuft nur im forceAuto-Player-Endpoint - exakt die historische Bug-Klasse "Default in einem Override-Feld ist von einer Nutzerentscheidung nicht unterscheidbar".
> **Verifikation:** Bestaetigt: Model defaultet auf 'realesrgan-x4' (PluginConfiguration.cs:58), das #Model-Select erzeugt ausschliesslich echte Modell-IDs ohne auto/leer-Option (configurationpage.html:1862-1902), und der DashboardAutoMode-Schalter schreibt nur EnableAutoModelSelection, nie Model (configurationpage.html:1344). Damit ist das Batch-Gate LibraryUpscaleScanTask.cs:303 real immer falsch -> else-Zweig (Z.333) nimmt config.Model=realesrgan-x4 ohne Heuristik/Hardware-Cap; forceAuto=true laeuft nur im recommend-model-Endpoint (UpscalerController.cs:1054), der Scan-Task-Aufruf (Z.310) uebergibt forceAuto n
> **Fix:** Den Custom-Arm an EnableAutoModelSelection koppeln (Auto-Mode an => Heuristik laeuft, unabhaengig vom gespeicherten Model), oder DefaultModel auf "auto"/leer aendern und die UI eine explizite Auto-Option schreiben lassen. Danach die Gate-Bedingung im Scan-Task vereinfachen.

### 🟠 fps-Filter wird mit CurrentCulture formatiert - Komma zerstoert die Filterkette
**`Services/VideoFrameProcessor.cs:90`** · csharp-video · ✅ bestätigt (2×)

effectiveFps (double, z.B. 23.976 bei NTSC-Quellen) wird per String-Interpolation ohne InvariantCulture in den -vf-String eingesetzt. Auf Servern mit Komma-Dezimal-Locale (de-DE, fr-FR usw.) entsteht "fps=23,976"; das Komma ist in ffmpeg-Filtergraphs der Filter-Separator, ffmpeg bricht mit "No such filter: '976'" ab und jeder Frame-by-Frame-Job schlaegt fehl. ReconstructVideoAsync (Zeilen 484/490) nutzt korrekt InvariantCulture - diese Stelle wurde vergessen.
> **Verifikation:** framesFps stammt aus job.InputInfo.FrameRate (ProcessingMethodExecutor.cs:162), das in VideoAnalyzer.cs:60 direkt vom fraktionalen ffprobe-Wert (z.B. 23.976) gesetzt wird - keine Rundung auf Integer -, und Zeile 90 formatiert diesen double per Interpolation ohne InvariantCulture. Auf Komma-Locale entsteht 'fps=23,976'; das Komma ist ffmpeg-Filtertrenner -> 'No such filter', ExitCode!=0, Zeile 144 wirft, Job scheitert. Dieselbe fps/-r-Formatierung nutzt an allen anderen Stellen InvariantCulture (Executor 468/724, Reconstruct 484/490) - Zeile 90 ist die einzige Auslassung.
> **Fix:** effectiveFps.ToString(System.Globalization.CultureInfo.InvariantCulture) verwenden, wie in ReconstructVideoAsync bereits geschehen.

### 🟠 HDR-Job kann komplett ohne Upscaling als Erfolg enden (URL-Join + nie gezaehlte Fehler)
**`Services/VideoFrameProcessor.cs:407`** · csharp-video · ✅ bestätigt (2×)

UpscaleHDRFrameAsync baut die URL als $"{baseUrl}/upscale-hdr" ohne TrimEnd('/') - anders als HttpUpscalerService.GetServiceUrl() (Zeile 86 dort). Mit konfiguriertem Trailing-Slash entsteht "//upscale-hdr", was FastAPI mit 404 beantwortet; ebenso liefert jeder andere dauerhafte Endpoint-Fehler (401/500) null. UpscaleSingleFrameAsync kopiert dann jedes Frame still als Original durch (return false wird vom Batch-Loop in Zeile 330 ignoriert, failedFrames zaehlt nur Exceptions) - der komplette HDR-Job re-encodiert das Video unveraendert, meldet Success und importiert die nicht hochskalierte Datei per Library-Scan.
> **Verifikation:** Kein Health-Preflight vor der Frame-Verarbeitung (Executor 156-176 geht direkt Extract->Process, IsServiceAvailableAsync wird hier nicht aufgerufen), daher greift ein systematischer HDR-Endpoint-Fehler ungebremst durch: UpscaleHDRFrameAsync liefert bei non-2xx null (415), UpscaleSingleFrameAsync kopiert das Original und gibt false zurueck (272-278), der Loop ignoriert den bool (330), und failedFrames zaehlt nur Exceptions (358) -> die 50%-Abbruchlogik (368) greift nie, der Job meldet Success (Executor 182-188) mit unveraendertem Video. Die URL in Zeile 407 nutzt baseUrl ohne TrimEnd('/') - and
> **Fix:** baseUrl.TrimEnd('/') wie im HttpUpscalerService verwenden und die false-Rueckgaben von UpscaleSingleFrameAsync im Loop als Fehler mitzaehlen, damit die 50-Prozent-Abbruchlogik auch fuer systematische HDR-Fehler greift.

### 🟠 trivy-action@master unpinnt direkt nach Docker-Hub-Login
**`.github/workflows/docker-publish.yml:155`** · infra · ✅ bestätigt (2×)

aquasecurity/trivy-action ist auf den mutablen Branch master gepinnt und laeuft nachdem docker/login-action das DOCKERHUB_TOKEN in die Docker-Config des Runners geschrieben hat. Eine Kompromittierung des Upstream-Repos (vgl. tj-actions-Vorfall) koennte das Token exfiltrieren und boesartige Images an alle Nutzer pushen. Der Header (Zeile 15) fordert selbst 'pin to SHAs via Dependabot before first merge' - das ist nie passiert, waehrend build.yml/build-and-release.yml korrekt per SHA pinnen.
> **Verifikation:** Bestaetigt: docker-publish.yml:155 nutzt aquasecurity/trivy-action@master (mutabler Branch) im selben build-push-Job nach docker/login-action (Z.103-108), das DOCKERHUB_TOKEN in die Runner-Docker-Config schreibt; beide Steps laufen bei push!='false', also beim Release-Tag-Push. Ein Upstream-Kompromiss (vgl. tj-actions) koennte das Push-Token aus der Config exfiltrieren oder direkt boesartige Images an alle :latest/Watchtower-Nutzer pushen. Der eigene Header Z.15 fordert SHA-Pinning, und build.yml/build-and-release.yml pinnen tatsaechlich per SHA (z.B. checkout@34e114...), waehrend dieser Workf
> **Fix:** Mindestens trivy-action auf einen Commit-SHA pinnen (z.B. @<sha> # 0.28.x); idealerweise alle Actions in diesem Workflow wie in build.yml per SHA pinnen.

### 🟠 Globale Body-Size-Middleware blockiert Model-Uploads ueber 50 MB
**`docker-ai-service/app/main.py:1959`** · python-main-a · ✅ bestätigt (2×)

Die limit_body_size-Middleware weist JEDEN Request mit Content-Length > MAX_UPLOAD_BYTES (Default 50 MB) mit 413 ab, auch /models/upload, /models/convert-upload und /models/upload-face-enhance, die laut MAX_MODEL_UPLOAD_BYTES bis 500 MB (Default) erlauben sollen. Reale ONNX-Modelle (GFPGAN ~340 MB, HAT-L ~162 MB, NAFNet ~446 MB, DAT ~86 MB) koennen damit nie hochgeladen werden; die Endpoint-Checks in Z. 6079/6153/6494 sind fuer 50-500 MB toter Code. Selbst per Env ist MAX_UPLOAD_BYTES auf 500 MB gedeckelt, waehrend MAX_MODEL_UPLOAD_BYTES bis 2 GB konfigurierbar ist.
> **Verifikation:** Die globale Middleware main.py:1955-1963 prueft JEDEN Request per Content-Length gegen MAX_UPLOAD_BYTES (Default 50 MB, Env-Cap 500 MB; main.py:404) ohne Pfad-Ausnahme, laeuft also vor den Endpoint-Checks. Die Model-Uploads pruefen erst danach gegen MAX_MODEL_UPLOAD_BYTES (Default 500 MB, bis 2 GB; main.py:444, 6079/6153/6494), sodass ein 340-MB-GFPGAN-Multipart-Upload bereits mit 413 abgewiesen wird und der 50-500-MB-Zweig toter Code ist. Kein Bypass gefunden; die 500-MB-Deckelung von MAX_UPLOAD_BYTES verhindert zudem dauerhaft die per MAX_MODEL_UPLOAD_BYTES erlaubten >500-MB-Modelle.
> **Fix:** In der Middleware fuer die Model-Upload-Routen (request.url.path startswith /models/upload, /models/convert-upload) das groessere Limit MAX_MODEL_UPLOAD_BYTES (plus Multipart-Overhead) anwenden.

### 🟠 rife-v4.25 (empfohlener Default) kann nicht laufen: 6-Kanal-Feed fuer 7-Kanal-Modell
**`docker-ai-service/app/main.py:3195`** · python-main-a · ✅ bestätigt (2×)

Der Single-Input-Zweig von interpolate_frame_rife fuettert nur die 6-Kanal-Konkatenation der beiden Frames, ohne Timestep-Kanal. Der Katalog-Kommentar in Z. 1126 dokumentiert den rife-v4.25-Export aber explizit als '1-input 7-channel signature' (img0+img1+timestep). Jeder /interpolate-frames-Aufruf mit model=rife-v4.25 (available:True, Beschreibung 'Recommended new default') scheitert damit an ORT INVALID_ARGUMENT und liefert 500; die Tests decken nur die 2- und 3-Input-Faelle ab.
> **Verifikation:** Der Single-Input-Zweig main.py:3195-3197 fuettert nur combined (1,6,H,W) ohne Timestep und ohne Kanalzahl-Pruefung, waehrend der Erst-Party-Katalogkommentar main.py:1126 rife-v4.25 als verifizierte '1-input 7-channel signature' dokumentiert (available:True, 'Recommended new default'; main.py:1119/1127). interpolate_frame_rife ist der einzige Inferenzpfad (main.py:5539) und die Shape-Ausnahme wird zu HTTP 500 (main.py:5555-5558); die Tests decken nur 2-/3-Input ab (test_interpolation.py:73-112), der 1-Input-7ch-Fall ist ungetestet. Einzige nicht per Code belegbare Annahme ist die tatsaechliche 
> **Fix:** Im Single-Input-Zweig die Kanalzahl des Inputs pruefen und bei 7 Kanaelen np.concatenate([f1_t, f2_t, ts], axis=1) fuettern; einen Test fuer den 1-Input-7ch-Fall ergaenzen.

### 🟠 FP16-Cast ohne _session_input_is_fp16-Guard macht Face-Restore auf FP16-GPUs wirkungslos
**`docker-ai-service/app/main.py:3332`** · python-main-a · ✅ bestätigt (2×)

_restore_face_crop castet den Input-Blob allein anhand von state.use_fp16 nach float16; die Face-Restore-Modelle (GFPGAN/CodeFormer/GPEN/RestoreFormer, alle fp32-Exports) erwarten aber tensor(float). Auf CUDA-GPUs mit Compute Capability >= 7 (USE_FP16=auto => use_fp16=True) wirft session.run fuer jeden Face-Crop INVALID_ARGUMENT; restore_faces_in_frame faengt das pro Crop mit warning+continue, sodass Face-Restore still gar nichts tut. Das ist exakt die Bug-Klasse aus Issue #67, deren Guard (Z. 1508-1519) nur _onnx_infer_tile/_onnx_infer_multiframe_tile abdeckt.
> **Verifikation:** USE_FP16 defaultet auf 'auto' (main.py:411) und liefert auf CUDA CC>=7 state.use_fp16=True (main.py:1478-1480); _restore_face_crop castet den Blob allein anhand state.use_fp16 nach fp16 (main.py:3332) ohne den _session_input_is_fp16-Guard, den _onnx_infer_tile bei main.py:2808/2825 verwendet. Laut Guard-Docstring (main.py:1511-1514, Issue #67) wirft session.run bei fp32-Modellen dann INVALID_ARGUMENT; restore_faces_in_frame ist via main.py:5714 erreichbar und faengt den Fehler pro Crop mit warning+continue (main.py:3413-3417) ab, sodass Face-Restore auf FP16-GPUs still gar nichts tut. Refutati
> **Fix:** Wie in _onnx_infer_tile den Guard verwenden: use_fp16 = state.use_fp16 and _session_input_is_fp16(sess); dasselbe gilt fuer den Result-Cast in Z. 3345 (und fuer upscale_frame_realtime Z. 3486/3517, Kollegen-Zone).

### 🟠 /upscale-stream leakt Semaphore-Slot bei Client-Abbruch
**`docker-ai-service/app/main.py:4938`** · python-main-b · ✅ bestätigt (2×)

Das try/finally mit sem.release() beginnt erst NACH der async-for-Schleife (Zeile 4991); bricht der Client ab, wirft request.stream() ClientDisconnect bzw. Starlette injiziert GeneratorExit am yield, und der finally-Block wird nie erreicht. Jeder abgebrochene Stream verliert dauerhaft einen Semaphore-Slot und laesst processing_count erhoeht. Nach max_concurrent Abbruechen antworten alle /upscale*-Endpoints nur noch 429/503, bis /config die Semaphore ersetzt.
> **Verifikation:** Das try/finally mit sem.release() (Zeilen 4991-5003) steht sequenziell NACH der async-for-Schleife, umschliesst sie nicht; der Endpoint hat keinen aeusseren try/finally (returnt nur StreamingResponse Zeile 5005). Bei Consumer-Abbruch wird GeneratorExit am yield (4974) injiziert bzw. request.stream() wirft ClientDisconnect an Zeile 4938 - beides ist kein Exception bzw. liegt ausserhalb des inneren try (4953-4988), propagiert aus dem Generator und ueberspringt das finally, sodass sem.release() und der processing_count-Dekrement entfallen. Da reale Playback-Streams staendig abbrechen (Seek/Stop/P
> **Fix:** Den gesamten Generator-Body (inkl. der async-for-Schleife) in try/finally einschliessen und release/processing_count-Dekrement dort ausfuehren; GeneratorExit/CancelledError durchreichen.

### 🟠 /models/cleanup loescht nach Restart praktisch alle Modelle inkl. Custom-Modelle
**`docker-ai-service/app/main.py:5399`** · python-main-b · ✅ bestätigt (2×)

state.model_last_used ist rein in-memory (Zeile 216) — nach einem Container-Restart ist last_used fuer alle Dateien 0, und dry_run=false loescht jedes nicht gerade geladene Modell statt nur 'seit N Tagen ungenutzte'. Zusaetzlich werden .custom.json-Sidecars, face_enhance.onnx und Face-Restore-Modelle (gfpgan-*) nie in model_last_used eingetragen (kein _record_success-Pfad) und daher selbst bei aktiver Nutzung geloescht. Custom-Modelle (url="") sind danach unwiederbringlich weg bzw. verlieren durch den geloeschten Sidecar ihre Registrierung beim naechsten Restart.
> **Verifikation:** state.model_last_used ist ein rein in-memory Dict (Zeile 216) mit nur 5 Schreibern (Modell-Load 2022/2114/2496/3140 und _record_success 5193) und wird nirgends von Platte geladen - nach Restart also leer. In /models/cleanup ist cutoff=now-max_age_days*86400 positiv, sodass last_used=0 < cutoff fuer jede nicht gerade geladene Datei gilt und bei dry_run=false os.remove greift (5406-5418); da os.listdir ALLE Dateien ohne Endungsfilter durchlaeuft, treffen .custom.json-Sidecars und die nie getrackten face_enhance/gfpgan-Modelle es ebenfalls, Custom-Modelle (url='') sind danach unwiederbringlich. G
> **Fix:** Datei-mtime als Fallback fuer last_used nutzen, Sidecars und Nebenmodelle (custom.json, face_enhance, face_restore, RIFE) vom Loeschen ausnehmen bzw. deren Nutzung tracken, und last_used persistieren.

### 🟠 Stale Keep-Regeln: -Execute loescht alle v1.8.x-Docker-Tags und setzt :latest auf v1.7.8 zurueck
**`Scripts/cleanup-dockerhub-tags.ps1:27`** · release · ✅ bestätigt (2×)

Die Keep-Liste pinnt nur v1.7.7/v1.7.8 und die sechs alten Rolling-Tags; docker-publish pusht aber seit langem docker7-v<ver>-<suffix>- und v<ver>-<suffix>-Tags fuer v1.8.x sowie seit v1.8.3.8 den Rolling-Tag docker7-converter, der in Test-Keep fehlt. Ein Lauf mit -Execute loescht damit unwiederbringlich alle v1.8.x-Pins plus das Converter-Image und zeigt :latest wieder auf ein v1.7.8-Image - exakt der ':latest stuck on v1.7.8'-Bug, den v1.8.3.4 behoben hat. Der Dry-Run-Default mildert das, aber -Execute ist der dokumentierte Zweck des Scripts.
> **Verifikation:** Test-Keep (cleanup-dockerhub-tags.ps1:37) fuehrt nur docker7[-amd/intel/cpu/apple/vulkan] und v1.7.7/v1.7.8 (Zeilen 38-39); docker7-converter fehlt, obwohl docker-publish.yml:76-79 das converter-Backend mit Rolling-Tag docker7-converter sowie docker7-v<ver>* und v<ver>* (Zeilen 121-123) pusht. Alle v1.8.x-Pins matchen weder die Rolling-Liste noch die v1.7.x-Regex -> Test-Keep=false -> Loeschung (Zeilen 58,83). :latest wird auf $CurrentNvidiaTag='v1.7.8' (Zeilen 27,77) zurueckgesetzt = exakt der Stale-latest-Bug aus docker-publish.yml:124-125. Konkreter Pfad: -Execute (dokumentiert Zeile 14, pe
> **Fix:** Keep-Regeln parametrisieren (Current/Rollback-Version als Pflichtparameter statt Hardcode), docker7-converter in die Rolling-Liste aufnehmen und $CurrentNvidiaTag aus meta.json ableiten oder als Parameter erzwingen.

### 🟠 Release-Verifikation schlaegt bei 3-teiligen Tags (x.y.z.0-Releases) komplett fehl
**`Scripts/verify-release.ps1:106`** · release · ✅ bestätigt (2×)

Fuer einen Tag wie v1.8.4 sucht der Live-Feed-Check nach version -eq '1.8.4', die Feeds tragen aber konventionsgemaess 4-part '1.8.4.0' - kein Feed wird gefunden und das Script bricht mit exit 1 ab; ebenso erwarten die lokalen Feed-Checks (Zeilen 155-157) $tagVersion statt $tagVersion4. Jedes bisherige .0-Release (v1.8.3, v1.8.2, ...) haette diesen Check nicht bestanden; das naechste Minor-Release triggert den Fehlalarm garantiert und verleitet dazu, 3-part-Versionen in die Feeds zu schreiben oder die Verifikation zu ueberstimmen. Der CI-Job zip-version-check (meta.json 3-part vs. manifest[0] 4-part) hat dieselbe Klasse.
> **Verifikation:** Die sourceUrls belegen: .0-Releases tragen 3-teilige Git-Tags (v1.8.0, v1.8.3, v1.7.8, v1.7.11), die Feeds aber 4-teilige Versionen (1.8.0.0, 1.8.3.0). verify-release.ps1:89 setzt $tagVersionFeed=$Tag.TrimStart('v') ohne .0-Normalisierung; Zeile 106 sucht $_.version -eq 'x.y.z' gegen Feed-Eintrag 'x.y.z.0' -> kein Treffer -> Zeilen 107-110 FAIL fuer alle drei Feeds -> Zeilen 129-132 exit 1. Ebenso lokale Feed-Checks Zeilen 155-157 (Expect=$tagVersion) melden 'found x.y.z.0, expected x.y.z'. $tagVersion4 (Zeile 144) existiert, wird aber nur fuer csproj (165-167) genutzt. Naechstes Minor/.0-Rele
> **Fix:** Beim Feed-Lookup und den drei lokalen Feed-Checks $tagVersion4 verwenden (bzw. beide Formen akzeptieren: $_.version -in @($tagVersionFeed, "$tagVersionFeed.0")); zip-version-check analog normalisieren.

### 🟠 Filter-Vorschlag-Apply ist ein stiller No-Op (falsche JSON-Feldnamen)
**`Configuration/player-integration.js:2186`** · web-player · ✅ bestätigt (2×)

_applySuggestedFilter postet { ActiveFilterPreset, EnableVideoFilters } an Upscaler/filter-config, aber die DTO FilterConfigUpdate (UpscalerController.cs:2781) hat nur Preset/Enabled/... ASP.NET ignoriert unbekannte Properties, alle Felder bleiben null, nichts wird gespeichert, der Server antwortet trotzdem success:true. Der Nutzer sieht 'Filter preset set to X', die Config bleibt unveraendert und der Vorschlag erscheint beim naechsten Render sofort wieder.
> **Verifikation:** Bestaetigt: Client sendet {ActiveFilterPreset, EnableVideoFilters} (player-integration.js:2186), doch das DTO FilterConfigUpdate kennt nur Preset/Enabled (UpscalerController.cs:2782-2784) — die Fremdnamen werden ignoriert, body.Preset/body.Enabled bleiben null und die Zuweisungen (2637-2638) greifen nie. SaveConfiguration + Ok(success=true) laufen trotzdem (2649-2651), also liefert _applySuggestedFilter true und zeigt 'Filter preset set to X' (1367-1369) ohne jede Wirkung. Erreichbar ueber die Menue-Aktion 'filter-suggest-apply' (1364) im geladenen Player-Panel.
> **Fix:** Body auf die DTO-Namen umstellen: JSON.stringify({ preset: presetKey, enabled: true }). Gleiches im identischen POST in sidebar-upscaler.js:355 korrigieren.

### 🟠 Server-Realtime-Modus kollidiert mit 10-Requests/Minute-Rate-Limit
**`Configuration/player-integration.js:583`** · web-player · ✅ bestätigt (2×)

Die Capture-Loop sendet pro Roundtrip einen Frame an Upscaler/upscale-frame, der Controller limitiert aber auf 10 Requests/Minute pro User (RateLimitMaxRequests=10, UpscalerController.cs:2455). Nach ~1 Sekunde liefert jeder Frame 429; der Client behandelt das als stillen Skip, _lastSuccessfulFrame friert ein und nach 10s faellt der Modus immer auf WebGL zurueck ('server unresponsive'). Der beworbene Server-AI-Realtime-Tier kann so nie laenger als wenige Sekunden laufen.
> **Verifikation:** Bestaetigt: upscale-frame nutzt dasselbe IsRateLimited() mit 10 Requests/Minute (UpscalerController.cs:2455, 40, 126), waehrend die RAF-Schleife pro Roundtrip einen Frame sendet (player-integration.js:558-577) — nach 10 Frames nur noch 429. Der Client behandelt !resp.ok als stillen Skip (583-586), _lastSuccessfulFrame friert ein (605) und der 10s-Watchdog faellt auf WebGL 'server unresponsive' zurueck (509-517). Server-Modus wird bei ausreichender Benchmark-fps real gewaehlt (_decideTier:257), der Tier laeuft damit nie laenger als Sekunden.
> **Fix:** upscale-frame vom generischen 10/min-Limit ausnehmen (eigenes, frameratetaugliches Limit serverseitig) und clientseitig 429 explizit erkennen und als Rate-Limit melden statt als 'server unresponsive'.

### 🟡 Route POST /Upscaler/face-restore/frame fehlt - Face-Restore-Preview der Config-Seite ist tot
**`Controllers/UpscalerController.cs:2289`** · csharp-controller · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

Die Config-Seite (configurationpage.html:3161, Button #btn-face-restore-preview) postet den extrahierten Frame an 'Upscaler/face-restore/frame', der Controller definiert aber nur face-restore/load, /status und /unload. Der Endpoint existiert nur im Docker-Service (main.py:5675 @app.post("/face-restore/frame")), ein Plugin-Proxy fehlt - der Aufruf endet immer in 404 und die Preview zeigt 'Face restore preview failed: HTTP 404'.
> **Verifikation:** Bestaetigt: configurationpage.html:3161 postet an 'Upscaler/face-restore/frame', der Controller definiert per Grep aber nur face-restore/load (Z.2236), /status (Z.2268) und /unload (Z.2289) und keine Catch-all-Route. Der JS-Zweig wirft bei !resp.ok 'HTTP '+status (Z.3167) und zeigt in Z.3183 'Face restore preview failed: HTTP 404' - die Preview ist tot. Severity auf medium korrigiert, da es eine reine Admin-Config-Preview ohne Daten- oder Sicherheitsfolgen ist.
> **Fix:** Proxy-Endpoint [HttpPost("face-restore/frame")] ergaenzen (Raw-Body an {serviceUrl}/face-restore/frame durchreichen, X-Face-Count/X-Duration-Ms-Header weiterleiten), analog zu UpscaleFrame.

### 🟡 _currentlyLoadedModel wird nie invalidiert - stiller Falsch-Modell-Betrieb
**`Services/HttpUpscalerService.cs:167`** · csharp-io · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

Stimmt der gecachte Wert mit dem angefragten Modell ueberein, kehrt EnsureModelLoadedAsync ohne jeden Service-Kontakt mit true zurueck. Der Cache wird aber nie invalidiert: Laedt der Nutzer ueber das Dashboard ein anderes Modell (UpscalerController.cs:2197 postet /models/load direkt am Service vorbei) oder startet der Docker-Container neu, verarbeitet der naechste Batch-Lauf alle Frames still mit dem falschen bzw. keinem geladenen Modell - Output-Scale und Logs (die den Modellnamen des Plugins melden) luegen dann. Das ist die im Projekt bekannte Klasse "Report muss den realen Modell-Scale melden".
> **Verifikation:** _currentlyLoadedModel wird nur in 188/218 gesetzt und nie zurueckgesetzt, InvalidateHealthCache ruehrt es nicht an, und der Quick-Path Z.167 kehrt ohne Service-Kontakt zurueck; UI/Player laden Modelle direkt ueber den Proxy models/load an HttpUpscalerService vorbei (UpscalerController.cs:2197, configurationpage.html:2402/2578, player-integration.js:1992/2261), sodass der Server-Slot vom Cache abweichen kann -> stiller Falsch-Modell-Betrieb mit luegenden Logs/Scale. Einschraenkung: der Container-Neustart-Fall fuehrt NICHT zu stiller Verarbeitung, sondern zu einem Fehler, weil /upscale bei curre
> **Fix:** Den Schnellpfad durch einen Abgleich mit GetServiceStatusAsync().CurrentModel ersetzen (der Health-Cache-Mechanismus mit kurzer TTL bietet sich an) und _currentlyLoadedModel bei InvalidateHealthCache sowie nach Controller-seitigen /models/load-Proxies zuruecksetzen.

### 🟡 Abgelaufene Cache-Entries hinterlassen verwaiste Dateien auf Disk
**`Services/CacheManager.cs:266`** · csharp-io · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

GetCachedContentAsync entfernt einen abgelaufenen Entry (IsEntryExpired nach MaxCacheAgeDays) nur aus dem Index, loescht aber die Datei nicht und dekrementiert _totalCacheSize nicht. Ohne Index-Eintrag sieht weder der stuendliche Cleanup noch ValidateCacheEntries die Datei je wieder; nur ClearCacheAsync (manuell) raeumt das videos-Verzeichnis komplett. Multi-GB-Videodateien akkumulieren so im Normalbetrieb unbegrenzt auf der Platte, waehrend der ueberhoehte Size-Zaehler zusaetzlich vorzeitige Evictions gueltiger Entries ausloest.
> **Verifikation:** GetCachedContentAsync entfernt abgelaufene Eintraege nur aus dem Index (CacheManager.cs:266), ohne File.Delete und ohne _totalCacheSize zu dekrementieren; beide Aufraeumpfade sind indexbasiert (ValidateCacheEntries 173-206 nur bei Init, CleanupOldEntriesAsync iteriert _cacheIndex.Values 469 und ist rein size-getrieben 461), nur ClearCacheAsync loescht den videos-Ordner (552-557). Reachable ueber den preprocess-Endpoint (UpscalerController.cs:1747), und da der Dateiname eine per-Lauf-GUID enthaelt (304 mit tempPath 396), ueberschreibt ein Re-Process die alte Multi-GB-Datei nicht -> echter Leak.
> **Fix:** Beim TryRemove die Datei wie in ValidateCacheEntries loeschen (try File.Delete(entry.FilePath)) und Interlocked.Add(ref _totalCacheSize, -entry.FileSize) ausfuehren; zusaetzlich koennte der Cleanup unreferenzierte Dateien im videos-Ordner einsammeln.

### 🟡 Temp-Audiodatei leakt bei Cancel/Exception waehrend der Rekonstruktion
**`Services/VideoFrameProcessor.cs:430`** · csharp-video · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

temp_audio_{Guid}.mka wird direkt in Path.GetTempPath() angelegt (nicht im Job-tempDir, das der Executor im finally loescht). Das Delete steht erst NACH dem zweiten ffmpeg-Aufruf und nicht in einem finally: Wirft ExecuteAsync (Zeile 482) eine OperationCanceledException (Job-Cancel waehrend der Encoding-Phase) oder eine andere Exception, bleibt die Datei liegen. Die .mka enthaelt die komplette Audiospur (oft hunderte MB) und akkumuliert auf /tmp (bei tmpfs: RAM) ueber die Server-Laufzeit.
> **Verifikation:** tempAudioPath liegt in Path.GetTempPath() (VideoFrameProcessor.cs:430), NICHT im Job-tempDir JellyfinUpscaler/{id}, das der Executor im finally loescht (ProcessingMethodExecutor.cs:194); kein Sweeper raeumt temp_audio_* (einzige Referenz Zeile 430). Das Delete (498-508) steht nach dem zweiten ffmpeg-ExecuteAsync (482) und nicht in finally, also leakt die (oft grosse) .mka wenn 482 wirft (OperationCanceledException bei Cancel in der Encoding-Phase oder Encode-Fehler). Real, aber nur auf dem Ausnahme-/Cancel-Pfad und ohne Datenverlust - daher medium statt high; der RealTimeAI-Zwilling macht es k
> **Fix:** Den gesamten Methodenrumpf ab Anlage der Datei in try/finally packen und das Delete ins finally verschieben (oder die Datei ins job-tempDir legen, das der Executor ohnehin loescht).

### 🟡 DockerHub-Cleanup wuerde alle v1.8.x-Tags loeschen und :latest auf v1.7.8 zurueckdrehen
**`Scripts/cleanup-dockerhub-tags.ps1:27`** · infra · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

Der von dockerhub-cleanup.yml (execute=true) ausgefuehrte Script hat $CurrentNvidiaTag='v1.7.8' hartkodiert und behaelt nur v1.7.7*/v1.7.8*-Pins; aktuelle Releases sind aber v1.8.3.x. Ein Lauf mit execute=true wuerde heute saemtliche v1.8.x- und docker7-v1.8.x-Tags unwiderruflich loeschen und :latest (Watchtower-Nutzer!) auf ein v1.7.8-Image downgraden. Zusaetzlich fehlt der seit v1.8.3.8 existierende Rolling-Tag docker7-converter in der Keep-Liste (Zeile 37) und wuerde mitgeloescht.
> **Verifikation:** Bestaetigt: cleanup-dockerhub-tags.ps1:27 hat $CurrentNvidiaTag='v1.7.8' hartkodiert, Test-Keep (Z.34-41) behaelt nur v1.7.7/v1.7.8 plus die 6 Rolling-Tags aus Z.37 (docker7-converter fehlt), obwohl die aktuelle Version laut meta.json:6 v1.8.3.21 ist. Mit execute=true (dockerhub-cleanup.yml:35-36) re-pointet Z.75-77 :latest auf v1.7.8 und Z.81-89 loeschen jeden nicht-behaltenen Tag, also alle v1.8.x-, docker7-v1.8.x- und den Rolling-Tag docker7-converter (real erzeugt in docker-publish.yml:121). Severity auf medium korrigiert, weil der Lauf manuellen Dispatch + nicht-default execute!=false bra
> **Fix:** Aktuelle/Rollback-Version aus meta.json oder als Workflow-Input ableiten statt hartkodieren, docker7-converter in die Keep-Liste aufnehmen und den irrefuehrenden Header in dockerhub-cleanup.yml (Zeile 6) korrigieren.

### 🟡 /quality-metrics blockiert das Event-Loop mit voller KI-Inferenz
**`docker-ai-service/app/main.py:5813`** · python-main-b · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

Der async-Handler ruft upscale_image_array und compute_quality_metrics synchron auf statt via run_in_executor — auf einer CPU-Box blockiert eine einzige Anfrage das gesamte Event-Loop fuer Sekunden bis Minuten (kein /health, keine /upscale-frame-Antworten, SSE tot; Docker-Healthchecks koennen den Container als unhealthy markieren). ENABLE_QUALITY_METRICS ist per Default true; alle anderen Inferenz-Endpoints nutzen korrekt _cpu_executor.
> **Verifikation:** /quality-metrics ist async def und ruft upscale_image_array (5813) und compute_quality_metrics (5817) direkt im Event-Loop auf, ohne run_in_executor - anders als /upscale (4532) bzw. /upscale-frame (4688), die _cpu_executor nutzen; ENABLE_QUALITY_METRICS ist per Default true (425). Eine Anfrage blockiert damit den Worker-Event-Loop fuer die Dauer der CPU-Inferenz, waehrend /health und Frame-Requests desselben Workers stallen. Severity auf medium korrigiert: Der Block ist transient (endet mit der Inferenz) und die Container-Restart-Kaskade haengt von der Healthcheck-Konfiguration ab, ist also n
> **Fix:** Inferenz und Metrikberechnung via await loop.run_in_executor(_cpu_executor, ...) auslagern und die _upscale_semaphore wie in /upscale anwenden.

### 🟡 /process-grain blockiert das Event-Loop (fastNlMeans + Inferenz)
**`docker-ai-service/app/main.py:5884`** · python-main-b · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

remove_grain (cv2.fastNlMeansDenoisingColored — bei HD-Bildern viele Sekunden CPU) und im 'both'-Pfad zusaetzlich upscale_image_array laufen synchron im async-Handler; der komplette Service ist waehrenddessen eingefroren. Das Feature ist per Default aktiv (ENABLE_GRAIN_MANAGEMENT=true) und hat keine Concurrency-Begrenzung.
> **Verifikation:** /process-grain ist async def und ruft remove_grain (cv2.fastNlMeansDenoisingColored, sehr CPU-intensiv, Zeilen 5885/5890) sowie im 'both'-Pfad upscale_image_array (5892) direkt ohne Executor und ohne Concurrency-Limit auf; ENABLE_GRAIN_MANAGEMENT ist per Default true (435). Der Worker-Event-Loop friert fuer die Verarbeitungsdauer ein (bei HD/4K viele Sekunden). Severity auf medium korrigiert: transiente, pro Aufruf begrenzte Blockade; die Unhealthy/Restart-Folge ist konditional vom Healthcheck-Timing abhaengig.
> **Fix:** Die gesamte Verarbeitung in _cpu_executor auslagern und die Upscale-Semaphore anwenden.

### 🟡 Download-Groessencap greift erst nach vollstaendigem Puffern im RAM
**`docker-ai-service/app/model_import.py:164`** · python-rest · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

_download_capped laedt mit client.get() den kompletten Response-Body in den Speicher (resp.content) und prueft MAX_MODEL_UPLOAD_BYTES erst danach. _import_gate prueft nur das size_bytes-Feld des Katalogs, nicht die reale Antwort - liefert der Upstream (GitHub-Release/HF-Repo geaendert, genau das Szenario, das die sha-Mismatch-Fehlertexte selbst beschreiben) ein z.B. 10-GB-File, wird alles gepuffert und der Container laeuft in den OOM-Kill, bevor der Cap oder der sha-Pin greift. Das widerspricht der im Moduldocstring zugesicherten Eigenschaft 'hard size cap on both the download...' und trifft sowohl die Sync-Endpoints als auch _run_import_job (main.py:6324/6392).
> **Verifikation:** Bestaetigt: _download_capped (model_import.py:165-168) holt via client.get() den kompletten Body in resp.content und prueft den Cap erst danach (Zeile 169) - kein Streaming, kein laufender Byte-Zaehler; _import_gate prueft nur das Katalog-Feld size_bytes (model_import.py:115). Der Pfad ist ueber /models/import-from-catalog (main.py:6324) und /models/import-async (main.py:6392) erreichbar; bei Upstream-Groessendrift einer Allowlist-URL (genau das Szenario, das die sha-Mismatch-Texte in main.py:6397/6411 vorsehen) wird alles in den RAM gepuffert -> OOM vor Cap/sha-Pin, entgegen der Docstring-Zus
> **Fix:** Mit client.stream() chunkweise lesen, laufende Byte-Summe gegen MAX_MODEL_UPLOAD_BYTES pruefen und bei Ueberschreitung sofort abbrechen; zusaetzlich Content-Length vorab pruefen, falls vorhanden.

### 🟡 XSS: esc() escapet keine Anfuehrungszeichen, wird aber in HTML-Attributen verwendet
**`Configuration/configurationpage.html:1191`** · web-confightml · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

esc() (Z. 1007) nutzt den textContent/innerHTML-Trick, der nur &, < und > escapet - Anfuehrungszeichen bleiben erhalten. In refreshJobs wird esc(j.inputPath) aber in ein title-Attribut eingesetzt; inputPath ist der Dateiname der Mediendatei (Path.GetFileName in VideoJobManager.cs:52), der unter Linux doppelte Anfuehrungszeichen enthalten darf. Ein Dateiname wie 'a" onmouseover="<js>' bricht aus dem Attribut aus und fuehrt beim Hover Admin-Session-JavaScript aus (Token-Diebstahl via ApiClient.accessToken()); dieselbe Luecke besteht fuer Model-IDs vom AI-Service in id="bench-..." und data-get-model/data-bench-model (Z. 2288-2292).
> **Verifikation:** Bestaetigt: esc() (configurationpage.html:1007) nutzt textContent->innerHTML und escapet nur &<>, keine Anfuehrungszeichen, waehrend _escHtml (Z.1537) korrekt auch " und ' ersetzt; inputPath ist Path.GetFileName(job.InputPath) (VideoJobManager.cs:52), ein roher Linux-Dateiname der " enthalten darf, und landet ungefiltert im title-Attribut (Z.1191), sodass ein Name wie a" onmouseover=... im [Authorize]-Admin-Kontext ausbricht. Einschraenkung ohne Widerlegung: da < und > doch escaped werden, ist nur Attribut-/Event-Handler-Injektion (kein neuer Tag) moeglich, der Vektor braucht also Hover/Focus;
> **Fix:** esc() um Quote-Escaping erweitern (wie das bereits vorhandene, korrekte _escHtml, das &<>"' ersetzt) oder in allen Attribut-Kontexten konsequent _escHtml statt esc verwenden.

### 🟡 Listener-Akkumulation bei SPA-Revisit: jeder Button feuert ab dem zweiten Besuch mehrfach
**`Configuration/configurationpage.html:3425`** · web-confightml · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

viewbeforehide setzt _initialized = false, wodurch onPageShow beim naechsten Anzeigen initNav/attachEvents/attachImportEvents usw. erneut anhaengt; zusaetzlich ueberleben die document-level Listener (pageshow/viewshow Z. 3401-3409, delegierter Job-Click Z. 1206) jede SPA-Navigation und initialisieren bei erneut ausgefuehrtem Inline-Script auch die neue DOM-Instanz mit. Nach k Besuchen der Config-Seite in einer Browser-Session feuert jeder Klick k-fach: 'Create token' erzeugt mehrere Tokens, Import/Benchmark/Save werden mehrfach abgeschickt, confirm-Dialoge erscheinen doppelt, und es laufen k parallele Poll-Intervalle.
> **Verifikation:** Bestaetigt: viewbeforehide setzt bedingungslos _initialized=false (Z.3425), und attachEvents (Z.2791ff) bindet alle Aktions-Listener direkt per page.querySelector(...).addEventListener ohne Element-Guard, sodass onPageShow (Z.3372 if(!_initialized)) sie bei jedem Re-Show erneut anhaengt. Zusaetzlich liegen die document-Listener (Job-Klick Z.1206, pageshow/viewshow Z.3401-3409, viewbeforehide Z.3417) auf IIFE-Top-Level und ueberleben jede Neu-Injektion des Fragment-Scripts, wodurch Save/Create-Token/Import mehrfach feuern und je Scope ein Poll-Intervall laeuft; die Autoren-Kommentare (Z.3423 'p
> **Fix:** Init-Guard an das konkrete DOM-Element binden statt an eine Script-Variable (z.B. page.dataset.upscalerInit = '1' pruefen/setzen) und die document-level Listener nur einmal global registrieren (window-Flag) bzw. auf das page-Element statt document haengen.

### 🟡 Face-Restore-Preview ruft nicht existierenden Endpoint auf - Button liefert immer HTTP 404
**`Configuration/configurationpage.html:3161`** · web-confightml · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

'Preview on Selected Media' POSTet an ApiClient.getUrl('Upscaler/face-restore/frame'), aber der UpscalerController kennt nur face-restore/load, /status und /unload - eine /frame-Proxy-Route existiert nur im Python-Service (main.py:5675), nicht im Plugin. Jeder Klick endet mit 'Face restore preview failed: HTTP 404'; das Feature ist damit vollstaendig tot. Zusaetzlich verlangt der Button die Medienauswahl aus #filter-preview-item, das auf dem Filters-Tab liegt, waehrend der Button im Models-Tab steht.
> **Verifikation:** Bestaetigt: der Controller-Basispfad ist Upscaler ([Route("[controller]")], UpscalerController.cs:35) und kennt nur face-restore/load (Z.2236), /status (Z.2268), /unload (Z.2289) - face-restore/frame existiert in keiner .cs-Datei (Grep: kein Treffer), also liefert der POST (configurationpage.html:3161) 404. Der Button #btn-face-restore-preview liegt im Models-Tab (Z.490, innerhalb tab-models 380-538) und liest #filter-preview-item aus dem Filters-Tab (Z.958, tab-filters ab 837), sodass ohne dort gewaehltes Item nur ein Toast kommt und bei gewaehltem Item der zweite Request zuverlaessig am 404 
> **Fix:** Im UpscalerController eine [HttpPost("face-restore/frame")]-Proxy-Route zum AI-Service ergaenzen (analog zu face-restore/load) oder den Button samt Preview-Container entfernen, bis der Proxy existiert.

### 🟡 stop() waehrend async start() geht verloren - verwaister Inferenz-Loop
**`Configuration/webgpu-ai-realtime.js:98`** · web-gpu · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

start() hat mehrere await-Luecken (requestAdapter, ORT-CDN-Script, Modell-Download von HuggingFace, Session-Erzeugung), prueft danach aber nie, ob inzwischen stop() gerufen wurde. Der Modell-Download dauert realistisch Sekunden; stoppt der Nutzer in dieser Zeit die Wiedergabe, laeuft _stopWebGPUAI ins Leere (_running noch false, _session null), anschliessend setzt start() _running=true, haengt das Canvas-Overlay ein und startet _renderLoop. Die Vollbild-Inferenz laeuft dann unbegrenzt weiter; ein spaeterer erneuter start() ueberschreibt den Singleton-State, waehrend der alte rAF-Loop weiterlaeuft (doppelte Inferenz pro Frame, geleaktes Canvas).
> **Verifikation:** Bestaetigt: start() (webgpu-ai-realtime.js:53-105) hat awaits bei requestAdapter (65), _loadOrt (75) und _loadModel (84, inkl. mehrsekuendigem HuggingFace-Fetch) und setzt danach ohne jede Pruefung _running=true, haengt das Canvas ein und startet _renderLoop (98-104); ein Generation-Token fehlt im gesamten File. Ruft der Nutzer waehrend des Downloads stop() (player-integration.js:1389 rt-toggle -> 470 _stopWebGPUAI -> WebGPUAIUpscaler.stop 107-116), ist es ein No-Op, da _running noch false und _session/_canvas null sind, sodass die Inferenz-Schleife danach verwaist weiterlaeuft und ein zweiter
> **Fix:** Einen Generation-Counter/Token einfuehren: bei start() inkrementieren, nach jedem await sowie vor _running=true pruefen, ob der Token noch aktuell ist und stop() nicht zwischenzeitlich lief; stop() inkrementiert den Counter ebenfalls.

### 🟡 Master-Schalter EnablePlugin hat keinerlei Wirkung auf Realtime-Upscaling
**`Configuration/player-integration.js:2194`** · web-player · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

startRealtimeUpscaling prueft nur EnableRealtimeUpscaling; EnablePlugin (das Feld des grossen Menue-Schalters, toggleUpscaling Z. 2063) wird clientseitig nirgends als Gate benutzt, toggleUpscaling stoppt eine laufende RT-Session nicht, und der Server-Proxy upscale-frame prueft EnablePlugin ebenfalls nicht. Wer Upscaling im Player 'disabled', laesst die laufende Session weiterlaufen und beim naechsten Video startet RT erneut.
> **Verifikation:** Bestaetigt fuer RT: startRealtimeUpscaling prueft nur EnableRealtimeUpscaling (player-integration.js:2194), toggleUpscaling setzt EnablePlugin ohne RealtimeUpscaler.stop() (2065-2073), und der Proxy upscale-frame prueft EnablePlugin nicht (UpscalerController.cs:2451-2456). EnablePlugin wirkt zwar auf Service und Scan-Tasks (UpscalerService.cs:102,174) — daher medium statt high — aber die Realtime-Overlay-Kette ignoriert es vollstaendig, sodass 'disabled' die laufende RT-Session weiterlaufen laesst und beim naechsten Video neu startet.
> **Fix:** In startRealtimeUpscaling zusaetzlich config.EnablePlugin === false abbrechen und in toggleUpscaling bei newState===false RealtimeUpscaler.stop() aufrufen.

### 🟡 RealtimeUpscaler.start() ohne stop() leakt Overlay-Canvas und Intervalle
**`Configuration/player-integration.js:1764`** · web-player · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

_applyAutoNow ('Re-apply to this video') und gestapelte once-'playing'-Listener (Z. 800) rufen _startRtWithConfig/start() waehrend _active===true. _startServer (Z. 481-520) ueberschreibt dann _overlayCanvas und _fallbackCheckInterval ohne Cleanup: die alte Canvas bleibt mit eingefrorenem Frame im DOM, das alte 2s-Intervall laeuft ewig weiter und cleart im Callback sogar das Handle des NEUEN Intervalls (clearInterval(RealtimeUpscaler._fallbackCheckInterval) statt der eigenen ID), dazu laufen zwei RAF-Loops.
> **Verifikation:** Bestaetigt: _applyAutoNow ruft _startRtWithConfig -> RealtimeUpscaler.start() ohne vorheriges stop() (player-integration.js:1764, 2229, 205-235), erreichbar per 'auto-apply'-Button waehrend _active (Menue 1361). _startServer ueberschreibt dann _overlayCanvas (487-492) und _fallbackCheckInterval (504) ohne Cleanup; der Callback cleart RealtimeUpscaler._fallbackCheckInterval (506,511), also die ID des NEUEN Intervalls statt der eigenen, und die alte Canvas bleibt im DOM. Andere Start-Pfade (rt-toggle 1389, rt-switch 1401, Model-Restart 2028) rufen korrekt stop() — daher medium, da ein gezielter 
> **Fix:** start() idempotent machen: am Anfang von RealtimeUpscaler.start() bei _active zuerst this.stop() aufrufen; im Interval-Callback die eigene Interval-ID in einer lokalen Variable capturen und diese clearen.

### 🟡 Sidebar-Panel doppelt tot: nie geladen und alle API-Routen falsch (api/-Praefix)
**`Configuration/sidebar-upscaler.js:288`** · web-player · ✅ bestätigt (2×) _(Erst: high → final: **medium**)_

Die Datei ist nur als PluginPageInfo 'UPSCALERSidebarIntegration' registriert (Plugin.cs:187), aber kein Loader bindet sie ein - die index.html-Injection umfasst nur UPSCALERPlayerIntegration, configurationpage.html laedt sie nicht. Zusaetzlich nutzen alle 11 Server-Aufrufe das Praefix 'api/Upscaler/...', der Controller registriert aber nur [Route("[controller]")] = 'Upscaler/...' - jeder Call wuerde 404 liefern (Status/Hardware/Jobs/Cache/Benchmark/Auto-Optimize saemtlich tot). Das beworbene Sidebar-Feature existiert fuer Nutzer schlicht nicht.
> **Verifikation:** Bestaetigt (doppelt tot): sidebar-upscaler.js ist nur als PluginPageInfo registriert (Plugin.cs:187), doch die index.html-Injektion (Plugin.cs:124) und configurationpage.html (994-996) laden nur UPSCALERPlayerIntegration — kein anderer Verweis laedt die Datei. Zusaetzlich nutzen 10 von 11 Calls api/Upscaler/ (z.B. 288,408,453), waehrend der Controller [Route('[controller]')]=/Upscaler ist (UpscalerController.cs:35) und die 40+ funktionierenden Aufrufe in configurationpage.html Upscaler/ ohne api/ verwenden -> 404; die fruehere Entlastung via ENDPOINT-AUDIT.md:5 beschreibt nur die Scanner-Suchm
> **Fix:** Entweder Datei entfernen (samt PluginPageInfo) oder korrekt einbinden UND alle Routen auf 'Upscaler/...' umstellen; Ausnahme Z. 354 ist bereits korrekt.

### ❌ 413 in /upscale und /upscale-hdr wird zu 500 und oeffnet den Circuit-Breaker
**`docker-ai-service/app/main.py:4546`** · python-main-b · ❌ widerlegt _(Erst: high → final: **none**)_

Der Groessen-Check (Zeile 4527) wirft HTTPException(413) im try-Block, aber es fehlt ein 'except HTTPException: raise' (das /upscale-frame Zeile 4699 hat) — die 413 faellt in 'except Exception', wird als 500 gemeldet und via _record_failure als Modell-Fehler gezaehlt. Sendet der Client fuenfmal in Folge ein zu grosses Bild (realistisch: 4K-16bit-HDR-PNGs des Plugins ueber MAX_UPLOAD_BYTES=50MB an /upscale-hdr, identischer Bug Zeile 4597/4617), oeffnet der Circuit-Breaker (threshold=5) und der gesamte Service liefert 503. Der bestehende Test akzeptiert 400 ODER 413 und deckt den Pfad mit geladenem Modell nicht ab.
> **Verifikation:** Die globale Middleware limit_body_size (Zeilen 1954-1963) liefert bei vorhandenem Content-Length > MAX_UPLOAD_BYTES bereits ein sauberes 413-JSONResponse VOR dem Handler, ohne _record_failure/Circuit-Breaker. Da bei Multipart content-length >= der gelesenen Dateigroesse ist, ist der In-Handler-Check (4527/4597) genau dann tot, wenn content-length gesetzt ist - der C#-Plugin-Upload (MultipartFormDataContent) setzt content-length, also wird der behauptete realistische Trigger gerade abgefangen. Der In-Handler-413-Pfad ist nur ohne Content-Length (chunked) erreichbar (untypischer Client, zusaetzl
> **Fix:** In /upscale und /upscale-hdr 'except HTTPException: raise' vor 'except Exception' einfuegen (oder den Groessen-Check vor den try-Block ziehen) und Client-Fehler nicht als _record_failure zaehlen.

## 🟡 Medium & ⚪ Low — vollständig pro Bereich

Enthält auch die C/H-Befunde, die von der Verifikation auf medium herabgestuft wurden. Nicht einzeln adversarial geprüft (außer den herabgestuften).

<details><summary><b>C# – Kern (Plugin, Config, Registries)</b> — 4 Befunde</summary>

| Sev | Ort | Titel | Kurz |
|---|---|---|---|
| 🟡 | `ModelScale.cs:33` | ScalePatterns verfehlen drei Katalog-IDs (x4v3-Suffix und spanx2-Praefix) | Die vier Regex-Patterns erkennen "realesr-general-x4v3" und "realesr-general-wdn-x4v3" (beide 4x laut models-fallback.json, ersteres als "Best modern  |
| 🟡 | `PluginConfiguration.cs:551` | PluginVersion veraltet nach Plugin-Updates bis zum naechsten manuellen Save | Der Property-Initializer "1.8.3.21" greift nur bei frischer Config; bei Bestandsinstallationen ueberschreibt der XmlSerializer ihn mit dem persistiert |
| ⚪ | `PluginConfiguration.cs:219` | Mehrere XML-Doku-Kommentare widersprechen dem tatsaechlichen Verhalten | Die Doku von EnableAutoModelSelection behauptet "Default false - user must opt in", der Code setzt aber = true (Z. 221). Weitere Drifts: QualityLevel- |
| ⚪ | `Plugin.cs:147` | InjectPlayerScript meldet Erfolg auch ohne erfolgte Injektion und kann alten Tag entfernen | Findet headEndRegex kein </head> (Replace ohne Treffer), wird die Datei trotzdem zurueckgeschrieben und true geliefert; Zeile 85 loggt dann faelschlic |

</details>

<details><summary><b>C# – Controller (REST-API)</b> — 10 Befunde</summary>

| Sev | Ort | Titel | Kurz |
|---|---|---|---|
| 🟡 | `UpscalerController.cs:2289` | Route POST /Upscaler/face-restore/frame fehlt - Face-Restore-Preview der Config-Seite ist tot _(↓von high)_ | Die Config-Seite (configurationpage.html:3161, Button #btn-face-restore-preview) postet den extrahierten Frame an 'Upscaler/face-restore/frame', der C |
| 🟡 | `UpscalerController.cs:1123` | Item-Endpoints ignorieren Per-User-Bibliotheksrechte (Parental Controls umgehbar) | GetComparisonData (Z. 1123), GetFilterPreviewFrame (Z. 2707), UpscaleItemImages (Z. 1212) und ProcessItem (Z. 1382) laden Items via _libraryManager.Ge |
| 🟡 | `UpscalerController.cs:1545` | Library-Allowlist per StartsWith ohne Verzeichnis-Separator - Nachbarordner-Bypass | EnqueueJob (Z. 1543-1545) und PreProcessVideo (Z. 1741-1743) pruefen die Bibliothekszugehoerigkeit mit inputPath.StartsWith(Path.GetFullPath(loc)) ohn |
| 🟡 | `UpscalerController.cs:1886` | ImportSettings: FormatException entkommt TryApply - 500 mit teilweise mutierter Live-Config | TryApply faengt nur InvalidOperationException; JsonElement.GetInt32()/GetInt64() werfen bei nicht darstellbaren Zahlen (z.B. "ScaleFactor": 2.5 oder 2 |
| 🟡 | `UpscalerController.cs:292` | GET /libraries gibt Server-Dateisystempfade an Nicht-Admins preis | GetLibraries liefert fuer jede Bibliothek die physischen locations (Serverpfade) an JEDEN authentifizierten User; Jellyfin selbst gated das Pendant /L |
| 🟡 | `UpscalerController.cs:1112` | compare/{itemId}: scale unvalidiert und Response meldet den angefragten statt den echten Modell-Scale | GetComparisonData uebernimmt scale ohne Bereichspruefung (UpscaleImage erlaubt nur {2,3,4,8}); negative/riesige Werte laufen bis in UpscalerCore.Fallb |
| 🟡 | `UpscalerController.cs:573` | ImportModel puffert den kompletten Download im Jellyfin-Heap, Groessencheck erst danach | Der lokale Importpfad liest die Datei mit ReadAsByteArrayAsync vollstaendig in den Speicher; der 500-MB-Check via Content-Length greift bei chunked Re |
| ⚪ | `UpscalerController.cs:2655` | filter-preview-Endpoints als '(admin only)' dokumentiert, aber fuer alle User offen und ohne Rate-Limit | Die XML-Docs von FilterPreview (Z. 2655) und GetFilterPreviewFrame (Z. 2686) behaupten '(admin only)', tatsaechlich gilt nur das Klassen-[Authorize] - |
| ⚪ | `UpscalerController.cs:961` | GET /hardware-info liefert statische Fantasiewerte statt Erkennung | GetHardwareInfo meldet fest FFmpegAvailable=true und OnnxRuntime="Available" und setzt GpuAvailable auf das Config-Flag HardwareAcceleration statt auf |
| ⚪ | `UpscalerController.cs:2004` | Settings-Roundtrip verliert AiServiceApiToken still | ImportSettings kann AiServiceApiToken setzen (Z. 2004), ExportSettings exportiert das Feld aber nie (bewusst als Secret ausgelassen, ohne Platzhalter  |

</details>

<details><summary><b>C# – Processing (Queue, Auto-Model, Hardware-Cap)</b> — 13 Befunde</summary>

| Sev | Ort | Titel | Kurz |
|---|---|---|---|
| 🟡 | `ProcessingMethodExecutor.cs:92` | RealTime-Pfad: Space-Split zerlegt gequotete FFmpeg-Argumente | BuildFFmpegCommand baut Strings mit eingebetteten Anfuehrungszeichen (-i "path", -vf "...", -y "out"), ProcessRealTimeAsync splittet sie an Leerzeiche |
| 🟡 | `ProcessingStrategySelector.cs:42` | Model "auto" wird zu Leerstring aufgeloest und laesst den Job abbrechen | OptimizeProcessingOptions ersetzt Model "auto"/leer durch hardwareProfile.RecommendedModel - aber das von VideoProcessor.ProcessVideoAsync verwendete  |
| 🟡 | `ProcessingMethodExecutor.cs:202` | Cancellation wird als Failure gemeldet (inkl. Failure-Webhook) | ProcessFrameByFrameAsync (Z.202), ProcessFrameByFrameOverlappedAsync (Z.368), ProcessBatchAsync (Z.418) und ProcessRealTimeAIAsync (Z.909) fangen alle |
| 🟡 | `ProcessingMethodExecutor.cs:328` | Original-Frame-Fallback erzeugt gemischte Aufloesungen, die die Rekonstruktion sprengen | Bei einem fehlgeschlagenen AI-Upscale wird das ORIGINAL-Frame (Eingangsaufloesung) nach processedDir kopiert (hier, in ProcessMultiFrameAsync Z.572-59 |
| 🟡 | `HardwareBudget.cs:170` | Weak-CPU + 4K-Quelle: Light-Ladder ohne 1x-Eintrag fuehrt zu 8K-Output | Der 1x-Branch (UpscalerCore Z.535-539) bevorzugt fuer bereits-4K-Quellen die Medium-schweren 1x-Restaurationsmodelle. Auf tier weak-cpu (max Light) fa |
| 🟡 | `ProcessingMethodExecutor.cs:647` | RealTimeAI leitet Output-Dimensionen aus dem Config-Scale statt dem Modell-Scale ab | outputWidth/Height = input * OptimizedOptions.ScaleFactor (Config-Wert), aber der AI-Service skaliert mit dem nativen Faktor des geladenen Modells. Be |
| ⚪ | `UpscalerCore.cs:435` | Hardware-Tier wird nur von Controller-Endpoints refresht - Headless-Batch laeuft ungecappt | _hardwareTier wird ausschliesslich in UpscalerController (RefreshHardwareTierAsync im recommend-model-Endpoint, CacheHardwareTier beim proxied /recomm |
| ⚪ | `ProcessingQueue.cs:317` | Persistenz verliert den aktiven Job beim Neustart | PersistDebouncedAsync snapshotet nur _queue (Pending); DequeueAsync entfernt den Job vor der Verarbeitung daraus. Stirbt der Server mitten in einem Jo |
| ⚪ | `ProcessingMethodExecutor.cs:340` | Overlapped-Pfad meldet bei unbekannter Dauer total=processed statt -1-Sentinel | Bei estTotalFrames==0 wird SendFrameProgress mit total=processed aufgerufen - der Frame-Anteil ist damit ab dem ersten Frame 100% und CalculateJobProg |
| ⚪ | `UpscalerCore.cs:542` | Multi-Frame-Branch uebergeht PreferredAnimeModel/PreferredLiveActionModel-Override stillschweigend | Der Branch isBatch && inputFrames>1 steht VOR den Override-Checks (Z.560, Z.584). Hat der Nutzer ein Preferred-Model gesetzt und die Service-Instanz m |
| ⚪ | `UpscalerCore.cs:176` | Exception-Filter in UpscaleImageAsync schluckt Cancellation und beantwortet sie mit Fallback-Resize | Der Filter faengt fuer Nicht-letzte Chain-Modelle jede Exception inkl. OperationCanceledException und probiert weitere Modelle (weitere HTTP-Calls nac |
| ⚪ | `ProcessingQueue.cs:308` | Debounced-Persist kann den letzten Zustand verlieren | Laeuft bereits ein Writer, kehrt PersistDebouncedAsync ohne Schreiben zurueck. Hat dieser Writer seinen Snapshot schon VOR der juengsten Mutation geno |
| ⚪ | `ProcessingMethodExecutor.cs:148` | Disk-Space-Schaetzung mit 500KB/Frame unterschaetzt grosse Quellen massiv | Die Schaetzung Dauer*25fps*500KB passt fuer SD/HD-PNGs, aber 4K-Extraktionsframes liegen bei 10-30MB und upscaled 8K-Frames noch darueber - die Pruefu |

</details>

<details><summary><b>C# – Video-Pipeline (ffmpeg, Frames, VMAF)</b> — 16 Befunde</summary>

| Sev | Ort | Titel | Kurz |
|---|---|---|---|
| 🟡 | `VideoFrameProcessor.cs:430` | Temp-Audiodatei leakt bei Cancel/Exception waehrend der Rekonstruktion _(↓von high)_ | temp_audio_{Guid}.mka wird direkt in Path.GetTempPath() angelegt (nicht im Job-tempDir, das der Executor im finally loescht). Das Delete steht erst NA |
| 🟡 | `VideoFrameProcessor.cs:172` | -ss Position mit CurrentCulture formatiert - Frame-Preview bricht auf Komma-Locales | position.TotalSeconds.ToString("F2") ohne InvariantCulture erzeugt auf de-DE z.B. "300,00"; ffmpeg lehnt das als "Invalid duration specification" ab,  |
| 🟡 | `VideoAnalyzer.cs:44` | AnalyzeVideoAsync ignoriert Cancellation komplett - haengendes ffprobe blockiert die Pipeline | Die Methode hat keinen CancellationToken-Parameter; FFProbe.AnalyseAsync (Zeile 48) sowie beide CliWrap-ffprobe-Aufrufe (ExecuteAsync() in Zeile 150 u |
| 🟡 | `VideoFrameProcessor.cs:437` | Rekonstruktion verliert zusaetzliche Audiospuren, alle Untertitel und Kapitel | Die Audio-Extraktion nutzt "-vn -acodec copy" ohne -map: ffmpeg kopiert dann nur EINEN Audiostream (den mit den meisten Kanaelen, nicht zwingend die D |
| 🟡 | `VideoFrameProcessor.cs:368` | 50%-Fehler-Abbruch bricht den Job nicht ab - Exception wird erst bei Task.WhenAll beobachtet | Die InvalidOperationException "Too many frame failures" wird innerhalb eines gequeueten Task geworfen; der for-Loop merkt davon nichts und enqueued we |
| 🟡 | `VideoJobManager.cs:150` | History-Trim kann KeyNotFoundException werfen und einen erfolgreichen Job als Failed melden | Beim Trimmen auf 100 Eintraege wird ueber einen Keys-Snapshot sortiert und dabei _performanceHistory[k] per Indexer gelesen. Beenden zwei Jobs gleichz |
| 🟡 | `VideoJobManager.cs:137` | PerformanceHistory meldet Output-Aufloesung/Scale aus dem Config-Wert statt dem Modell-Scale | OutputResolution und Scale werden aus OptimizedOptions.ScaleFactor berechnet - genau die im Repo dokumentierte Bug-Klasse: der AI-Service nutzt den na |
| 🟡 | `VideoFilterService.cs:62` | nlmeans-Staerke unter 1.0 liegt ausserhalb des ffmpeg-Wertebereichs und laesst jeden Job scheitern | Der ffmpeg-nlmeans-Parameter s hat den Range [1.0, 30.0]; der UI-Slider (DenoisePrefilterStrength, min=0 max=10 step=0.5) erlaubt aber 0.5. Damit erze |
| 🟡 | `VideoFilterService.cs:151` | Vignette-Slider-Semantik invertiert: kleine Werte ergeben maximale, grosse Werte schwache Vignette | Der Slider-Wert (0-5, dokumentiert als "0.0 off to 5.0 heavy") wird als Divisor benutzt: vignette=PI/x. Damit ergibt 5 die schwaechste und 0.1 die sta |
| 🟡 | `UpscalerProgressHub.cs:114` | Progress-Broadcast missbraucht SessionMessageType.UserDataChanged mit fremdem Payload | Alle ~2s pro laufendem Job wird ein eigenes Progress-Objekt als UserDataChanged an alle Admin-Sessions gesendet. Offizielle Clients erwarten dort ein  |
| 🟡 | `VideoProcessor.cs:318` | Bei Cancel/Exception wird kein SendJobCompleted gesendet und keine History geschrieben | SendJobCompleted laeuft nur im Erfolgs-Durchlauf (Zeile 293) und im modelLoaded-Fehlpfad (Zeile 269). Wirft AnalyzeVideoAsync/DetectHardwareAsync oder |
| ⚪ | `VideoFrameProcessor.cs:301` | Semaphore wird bei Cancel disposed, waehrend laufende Tasks noch Release() aufrufen | Wirft await semaphore.WaitAsync(cancellationToken) im Loop eine OperationCanceledException, verlaesst die Methode sofort und `using var semaphore` dis |
| ⚪ | `UpscalerProgressHub.cs:176` | Negatives EstimatedTimeRemaining bei unbekanntem Frame-Total | Bei totalFrames <= 0 (Pipe-Pfad) wird framesRemaining negativ und secondsRemaining damit ebenfalls; die WebSocket-Message traegt dann ein negatives Es |
| ⚪ | `UpscalerService.cs:159` | Task.Delay im generischen catch wirft bei Shutdown und beendet den Worker unsauber | Im catch-Block wird await Task.Delay(2000, ct) mit dem Worker-Token aufgerufen. Faellt eine Job-Exception mit einem gleichzeitigen Shutdown zusammen,  |
| ⚪ | `UpscalerProgressHub.cs:131` | SendJobStarted hat keinen einzigen Aufrufer | Die Methode wird nirgends im Repo aufgerufen (repo-weiter Grep); das "Starting"-Event erreicht Clients nie. Entweder toter Code oder ein vergessener A |
| ⚪ | `VideoAnalyzer.cs:73` | EstimatedQuality wird berechnet, aber nirgends verwendet - und waere bei MKV meist falsch | info.EstimatedQuality wird gesetzt, aber kein Produktionscode liest das Feld (Strategy-Selector, Controller: keine Treffer). Zudem liefert FFprobe fue |

</details>

<details><summary><b>C# – I/O (HTTP-Client, Cache, Scheduled Tasks)</b> — 14 Befunde</summary>

| Sev | Ort | Titel | Kurz |
|---|---|---|---|
| 🟡 | `HttpUpscalerService.cs:167` | _currentlyLoadedModel wird nie invalidiert - stiller Falsch-Modell-Betrieb _(↓von high)_ | Stimmt der gecachte Wert mit dem angefragten Modell ueberein, kehrt EnsureModelLoadedAsync ohne jeden Service-Kontakt mit true zurueck. Der Cache wird |
| 🟡 | `CacheManager.cs:266` | Abgelaufene Cache-Entries hinterlassen verwaiste Dateien auf Disk _(↓von high)_ | GetCachedContentAsync entfernt einen abgelaufenen Entry (IsEntryExpired nach MaxCacheAgeDays) nur aus dem Index, loescht aber die Datei nicht und dekr |
| 🟡 | `HttpUpscalerService.cs:284` | HttpClient-Timeout wird als Nutzer-Cancellation fehlinterpretiert | HttpClient meldet Timeouts als TaskCanceledException; UpscaleImageAsync (und ebenso DownloadModelAsync Z.328, LoadModelAsync Z.370) behandelt das als  |
| 🟡 | `CacheManager.cs:396` | Temp-Dateien von fehlgeschlagenem Pre-Processing werden nie aufgeraeumt | PreProcessContentAsync legt temp/<guid>.mp4 an und loescht die Datei nur im Erfolgsfall (Z.421-424). Schlaegt ProcessVideoAsync fehl oder wirft (auch  |
| 🟡 | `ImageUpscaleScanTask.cs:230` | 2x/4x-Scale-Entscheidung ist wirkungslos (Service ignoriert scale) | Der berechnete scale wird an UpscalerCore.UpscaleImageAsync uebergeben, aber ResolveAutoModel() beruecksichtigt ihn nicht (liefert config.Model, Defau |
| 🟡 | `ImageUpscaleScanTask.cs:240` | PNG-Daten werden unter der Original-Extension (.jpg) gespeichert | Der /upscale-Endpunkt liefert immer image/png (main.py:4536) und auch FallbackResizeAsync speichert per SaveAsPngAsync als PNG; der Task schreibt dies |
| 🟡 | `ImageUpscaleScanTask.cs:239` | Upscalte Bilder werden Jellyfin nie zugeordnet und bleiben unsichtbar | Der Task schreibt <name>_upscaled.<ext> neben das Original, registriert das Ergebnis aber nirgends am Item (kein SetImage, kein RefreshMetadata) - und |
| 🟡 | `LibraryUpscaleScanTask.cs:372` | Kein Abbruch bei Serienfehlern nach Service-Ausfall mitten im Batch | Die Service-Erreichbarkeit wird nur einmal vor dem Scan geprueft. Stirbt der AI-Service danach, durchlaeuft jedes verbleibende Item die volle Kaskade  |
| 🟡 | `LibraryUpscaleScanTask.cs:403` | Nutzerabbruch wird als Fehlschlag gezaehlt und feuert Failure-Webhook | VideoProcessor.ProcessVideoAsync faengt OperationCanceledException intern und liefert Success=false mit Error="Processing cancelled" (VideoProcessor.c |
| 🟡 | `CacheManager.cs:335` | Doppel-Store desselben Keys orphant alte Cache-Datei und zaehlt Groesse doppelt | Bei zwei parallelen PreProcess-Aufrufen fuer dasselbe Video (Doppelklick auf /Upscaler/preprocess) speichern beide: Der Index-Overwrite ersetzt den En |
| ⚪ | `HardwareBenchmarkService.cs:64` | Auto-Benchmark-Timer verwirft die Ergebnisse - Feature ohne Wirkung | RunBenchmarkCallback ignoriert den Rueckgabewert von RunHardwareBenchmark, und die Methode hat keinerlei Seiteneffekte (nichts wird persistiert oder i |
| ⚪ | `HardwareBenchmarkService.cs:317` | GetFallbackStatusAsync fragt Status auch bei bekannt totem Service ab | Selbst wenn IsServiceAvailableAsync gerade false geliefert hat, wird GetServiceStatusAsync aufgerufen - das blockiert bei nicht erreichbarem Service b |
| ⚪ | `LibraryScanHelper.cs:54` | Library-Zuordnung: nur erste Location, ungesicherter Praefix-Match, Leer-Location matcht alles | Der Match prueft nur Locations.FirstOrDefault() (Libraries koennen mehrere Ordner haben), vergleicht ohne Verzeichnistrenner-Grenze ("/media/mov" matc |
| ⚪ | `CacheManager.cs:153` | Index-Save nicht serialisiert; Dispose schreibt non-atomic | Parallele SaveCacheIndexAsync-Aufrufe (z.B. zwei gleichzeitige Stores) schreiben dieselbe feste .tmp-Datei und kollidieren mit IOException, wodurch ei |

</details>

<details><summary><b>C# – Testsuite (xUnit)</b> — 8 Befunde</summary>

| Sev | Ort | Titel | Kurz |
|---|---|---|---|
| 🟡 | `RegistryDriftLockTests.cs:22` | DenoisePrefilterMethod-Dropdown fehlt im RegistryDriftLock | RegistryDropdownPairs deckt 5 Registries ab, aber nicht das <select id="DenoisePrefilterMethod"> (configurationpage.html Z.933-936, Optionen hqdn3d/nl |
| 🟡 | `ProcessingQueueTests.cs:60` | Coalesce-Test prueft das Coalescing gar nicht | MultipleEnqueues_WithinDebounceWindow_CoalesceIntoOneFinalWrite assertet nur, dass alle 5 Job-IDs in der finalen Datei stehen, aber nie, dass genau EI |
| 🟡 | `ProcessingMethodExecutor.cs:707` | Kern-Pipeline und Auth-Handler komplett ohne Tests | Zentrale Services haben keinerlei Unit-Tests: ProcessingMethodExecutor (Codec-Allowlist-Pfade Z.707/1088, Ursprung des CodecRegistry-Bugs), VideoProce |
| 🟡 | `HardwareBudgetTests.cs:24` | Tier-Map-Test sichert keinen Python-Drift trotz Kommentar | MaxWeightFor_maps_every_tier prueft die 5 Tier-Strings nur gegen hartkodierte C#-Erwartungen. Der Kommentar behauptet, ein Rename im Service (main.py  |
| ⚪ | `ModelAvailabilityTests.cs:13` | Behaupteter Cross-Check mit HardwareBenchmarkService existiert nicht | Der Klassenkommentar nennt als Zweck den Cross-Check gegen UpscalerCore.PickAvailable UND HardwareBenchmarkService.EnsureModelAvailable; letzteres (Se |
| ⚪ | `EndpointDeprecationTests.cs:58` | EndpointDeprecationTests zerschneidet Quelltext fragil | The_alias_delegates_instead_of_duplicating_the_body grenzt den Alias-Body via IndexOf("\n        }") mit exakt 8 Leerzeichen ab. Das trifft heute nur  |
| ⚪ | `UpscalerCoreAutoModelTests.cs:30` | Auto-Model/Pick-Tests setzen statischen HardwareTier nicht zurueck | UpscalerCoreAutoModelTests und UpscalerCoreAutoPickTests lesen ueber ResolveModelForVideoDetailed den prozessweiten statischen UpscalerCore.HardwareTi |
| ⚪ | `CacheManagerTests.cs:88` | CacheManagerTests spiegelt die Produktions-Hash-Logik | ExpectedKey reimplementiert die Produktionsberechnung 1:1 (SHA256 ueber "inputPath\|model\|scale\|quality", Hex, lower); GenerateCacheKey_MatchesExpec |

</details>

<details><summary><b>Web – Konfigurationsseite (configurationpage.html)</b> — 15 Befunde</summary>

| Sev | Ort | Titel | Kurz |
|---|---|---|---|
| 🟡 | `configurationpage.html:1191` | XSS: esc() escapet keine Anfuehrungszeichen, wird aber in HTML-Attributen verwendet _(↓von high)_ | esc() (Z. 1007) nutzt den textContent/innerHTML-Trick, der nur &, < und > escapet - Anfuehrungszeichen bleiben erhalten. In refreshJobs wird esc(j.inp |
| 🟡 | `configurationpage.html:3425` | Listener-Akkumulation bei SPA-Revisit: jeder Button feuert ab dem zweiten Besuch mehrfach _(↓von high)_ | viewbeforehide setzt _initialized = false, wodurch onPageShow beim naechsten Anzeigen initNav/attachEvents/attachImportEvents usw. erneut anhaengt; zu |
| 🟡 | `configurationpage.html:3161` | Face-Restore-Preview ruft nicht existierenden Endpoint auf - Button liefert immer HTTP 404 _(↓von high)_ | 'Preview on Selected Media' POSTet an ApiClient.getUrl('Upscaler/face-restore/frame'), aber der UpscalerController kennt nur face-restore/load, /statu |
| 🟡 | `configurationpage.html:1035` | ReferenceError bei jedem Klick auf den Models-Tab: refreshFaceRestoreStatus ausserhalb des Scopes | initNav referenziert refreshFaceRestoreStatus, die Funktion ist aber nur lokal innerhalb von attachEvents deklariert (Z. 3039) und im Scope von initNa |
| 🟡 | `configurationpage.html:1129` | Dashboard-Kacheln 'Completed'/'Failed' stehen konstruktionsbedingt immer auf 0 | refreshDashboard zaehlt Completed/Failed aus data.jobs, aber /Upscaler/jobs liefert nur LAUFENDE Jobs (VideoProcessor entfernt Jobs im finally auf jed |
| 🟡 | `configurationpage.html:2154` | Scale-Dropdown ist mit den Shipped-Defaults leer (ScaleFactor 2 vs. realesrgan-x4 nativ 4x) | loadConfig ruft updateScaleOptions auf (baut Optionen aus dem nativen Scale-Array des Modells, fuer das Default-Modell realesrgan-x4 nur [4]) und setz |
| 🟡 | `configurationpage.html:2234` | Save loescht gespeicherte Model-Auswahl still, wenn die ID nicht in der aktuellen Optionsliste ist | Ist das gespeicherte Modell (z.B. ein OMDB-Import wie omdb-4x-...) nicht in der Optionsliste - etwa weil der AI-Service offline ist und der Fallback-K |
| 🟡 | `configurationpage.html:603` | Auto-Mode-Beschreibung behauptet 'Default: off.', tatsaechlicher Default ist seit v1.8.3.12 true | Die fieldDescription zu EnableAutoModelSelection sagt fett 'Default: off.', aber PluginConfiguration.cs:221 initialisiert EnableAutoModelSelection = t |
| 🟡 | `configurationpage.html:1495` | Poll-Intervall wird nach Verlassen der Seite durch _setStripLive-Race wiederbelebt | viewbeforehide raeumt refreshInterval auf, aber eine zu dem Zeitpunkt noch laufende /jobs-Antwort ruft renderActivityStrip -> _setStripLive auf; aende |
| ⚪ | `configurationpage.html:1213` | Pause/Resume/Cancel-POST ohne catch: Fehler bleiben stumm | Der delegierte Job-Control-Handler schickt den POST ohne .catch. Schlaegt die Aktion fehl (Job inzwischen beendet -> 404, Service-Fehler -> 500), gibt |
| ⚪ | `configurationpage.html:1293` | updateRangeLabels/attachRangeLabels sind toter Code (kein Element matcht .range-val[data-rv]) | Beide Funktionen selektieren '.range-val[data-rv]' bzw. spiegeln in solche Spans, aber im gesamten Markup existiert kein Element mit der Klasse range- |
| ⚪ | `configurationpage.html:2796` | Token-Copy-Button meldet 'Copied' auch ohne Clipboard (HTTP-Setups) | navigator.clipboard existiert nur in Secure Contexts; auf den bei Jellyfin ueblichen HTTP-LAN-Installationen ist es undefined, der Schreibvorgang wird |
| ⚪ | `configurationpage.html:238` | Tabs als <div> statt <button type="button"> - nicht per Tastatur bedienbar | Die Projektregel verlangt fuer In-Page-Tabs <button type="button">. Die aktuellen <div class="upscaler-tab">-Tabs routen zwar nicht weg (das eigentlic |
| ⚪ | `configurationpage.html:2449` | Perf-Monitor pollt alle 5s weiter, auch wenn ein anderer Tab aktiv ist | startPerfMonitor wird bei pageshow und jedem Dashboard-Tab-Klick gestartet, aber nur bei viewbeforehide gestoppt - beim Wechsel auf Settings/Models/et |
| ⚪ | `configurationpage.html:1084` | Banner-Klasse 'service-banner standalone' existiert im CSS nicht | Der Standalone-Zweig von checkServiceHealth setzt die Klasse 'standalone', das Stylesheet definiert aber nur .online/.offline/.checking (Z. 109-111).  |

</details>

<details><summary><b>Web – Player-Integration & Sidebar</b> — 12 Befunde</summary>

| Sev | Ort | Titel | Kurz |
|---|---|---|---|
| 🟡 | `player-integration.js:2194` | Master-Schalter EnablePlugin hat keinerlei Wirkung auf Realtime-Upscaling _(↓von high)_ | startRealtimeUpscaling prueft nur EnableRealtimeUpscaling; EnablePlugin (das Feld des grossen Menue-Schalters, toggleUpscaling Z. 2063) wird clientsei |
| 🟡 | `player-integration.js:1764` | RealtimeUpscaler.start() ohne stop() leakt Overlay-Canvas und Intervalle _(↓von high)_ | _applyAutoNow ('Re-apply to this video') und gestapelte once-'playing'-Listener (Z. 800) rufen _startRtWithConfig/start() waehrend _active===true. _st |
| 🟡 | `sidebar-upscaler.js:288` | Sidebar-Panel doppelt tot: nie geladen und alle API-Routen falsch (api/-Praefix) _(↓von high)_ | Die Datei ist nur als PluginPageInfo 'UPSCALERSidebarIntegration' registriert (Plugin.cs:187), aber kein Loader bindet sie ein - die index.html-Inject |
| 🟡 | `player-integration.js:978` | Status-Zeile prueft nicht existierendes Feld EnableUpscaling | _refreshStatusRow prueft cfg.EnableUpscaling === false, dieses Feld existiert weder in PluginConfiguration.cs noch sonst im Repo - das Master-Feld hei |
| 🟡 | `player-integration.js:1057` | Config-Strings landen unescaped in innerHTML (Model, FavoriteModels) | _renderModelCard interpoliert m.id/m.name/title unescaped; fuer Favoriten (Z. 1126-1137) stammen id und name direkt aus dem freien Config-String Favor |
| 🟡 | `player-integration.js:2106` | _getPlayingItemId erwartet '#/video?id=...' - Genre-Signal des Auto-Modus laeuft leer | Der Parser sucht einen id-Parameter im Hash, jellyfin-webs Video-OSD-Route ist aber schlicht '#/video' ohne Query (der Playback-State liegt im playbac |
| 🟡 | `player-integration.js:913` | Player-Feature haengt an admin-only getPluginConfiguration - fuer normale Nutzer stumm defekt | Das Script wird global fuer alle Nutzer injiziert, liest die Config aber ueber /Plugins/{id}/Configuration, das in Jellyfin die RequiresElevation-Poli |
| ⚪ | `sidebar-upscaler.js:394` | Erneutes Oeffnen des Panels leakt das Live-Monitoring-Intervall | showUpscalerPanel entfernt ein vorhandenes Panel (Z. 59-62) ohne stopLiveMonitoring(); startLiveMonitoring ueberschreibt dann _monitorInterval ohne cl |
| ⚪ | `quick-menu.js:322` | Netzwerk-Test nutzt nicht existierende Route und ignoriert den HTTP-Status | testNetworkConnectivity ruft '/api/system/info' auf - diese Route existiert in Jellyfin nicht (richtig: '/System/Info', zudem ignoriert der absolute P |
| ⚪ | `player-integration.js:510` | Fallback-Log widerspricht der tatsaechlichen Schwelle (5s vs. 10s) | Der Watchdog schaltet nach 10000 ms ohne erfolgreichen Frame auf WebGL um, das Log meldet aber 'No frames for 5s'. Zusammen mit der Notification 'serv |
| ⚪ | `player-integration.js:1119` | Doppelklick auf den Player-Button erzeugt zwei Menues, erstes wird unschliessbar per Outside-Click | toggleUpscalerMenu prueft nur VOR dem async Config-Fetch auf ein vorhandenes Menue; _buildMenu prueft nie. Zwei schnelle Klicks vor Fetch-Ende appende |
| ⚪ | `sidebar-upscaler.js:38` | Veralteter Selektor 'a[href="#/dashboard.html"]' matcht in Jellyfin 10.9+ nicht mehr | Die Routen von jellyfin-web haben seit 10.9 kein '.html'-Suffix mehr (Dashboard = '#/dashboard'), der Selektor findet daher nie einen Treffer und der  |

</details>

<details><summary><b>Web – WebGL/WebGPU/Anime4K</b> — 12 Befunde</summary>

| Sev | Ort | Titel | Kurz |
|---|---|---|---|
| 🟡 | `webgpu-ai-realtime.js:98` | stop() waehrend async start() geht verloren - verwaister Inferenz-Loop _(↓von high)_ | start() hat mehrere await-Luecken (requestAdapter, ORT-CDN-Script, Modell-Download von HuggingFace, Session-Erzeugung), prueft danach aber nie, ob inz |
| 🟡 | `webgl-upscaler.js:96` | CAS-Sharpening: Adaptivitaet tot und Sharpness-Regler invertiert | d = 1.0/(maxRGB-minRGB+0.05) liegt in [0.95, 20], nach Multiplikation mit -0.125 in [-2.5, -0.119]; clamp(x, -0.1, 0.0) saturiert daher fuer JEDEN Kon |
| 🟡 | `webgpu-ai-realtime.js:200` | Dauerhaft werfende Inferenz erzeugt endlosen 60-Hz-Fehlerloop ohne Fallback | Wirft _processFrame eine Exception (z.B. WebGPU device lost nach Treiber-Reset, wonach session.run bei jedem Aufruf wirft; ebenso denkbar bei Input-Ty |
| 🟡 | `webgpu-ai-realtime.js:207` | Kein Aufloesungs-Cap: Volle Videoaufloesung als Inferenz-Input | _processFrame baut den Input-Tensor immer aus der nativen Videoaufloesung (bei 4K: 3840x2160x3 floats ca. 100 MB pro Frame) und laesst Real-ESRGAN dar |
| 🟡 | `anime4k.js:50` | attachVideo-Fehlschlag ist nicht erkennbar - anime4k-Modus wird stiller No-Op | VideoUpscaler.attachVideo verlangt zusaetzlich zur isSupported()-Pruefung (nur OES_texture_float/_linear) die Extension EXT_color_buffer_half_float un |
| 🟡 | `webgl-upscaler.js:170` | webglcontextrestored-Handler laesst Upscaler deaktiviert und erzeugt Textur doppelt | Bei Context-Loss wird disable() gerufen (korrekt), aber der restored-Handler baut nur Shader/Geometrie neu auf und ruft nie enable() - der Upscaler bl |
| 🟡 | `webgl-upscaler.js:394` | destroy() gibt den WebGL-Kontext nicht explizit frei (kein loseContext) | Jeder Start-Zyklus erzeugt in init() ein frisches Canvas mit neuem WebGL-Kontext; destroy() entfernt nur das Canvas und nullt Referenzen, ruft aber ni |
| ⚪ | `webgpu-ai-realtime.js:211` | Neues Canvas plus 2D-Kontext pro Frame im Hot-Loop | _processFrame erzeugt bei jedem Frame ein neues srcCanvas samt getContext('2d') fuer den Video-Readback und setzt zudem _canvas.width/height jedes Mal |
| ⚪ | `webgpu-ai-realtime.js:28` | Modell-Fallback-URL nutzt jsdelivr gh-CDN fuer ein HuggingFace-Repo | Die zweite URL fuer realesrgan-compact-x2 zeigt auf cdn.jsdelivr.net/gh/onnx-community/Real-ESRGAN-Anime/... - der gh-Prefix von jsdelivr bedient auss |
| ⚪ | `webgl-upscaler.js:401` | destroy() nicht idempotent: ungeguardete deleteBuffer-Aufrufe und nicht genullte Handles | Die deleteBuffer-Aufrufe fuer _positionBuffer/_texCoordBuffer pruefen im Gegensatz zu den nachfolgenden Deletes nicht auf this.gl, und destroy() nullt |
| ⚪ | `webgl-upscaler.js:307` | Render-Loop laeuft bei pausiertem Video mit voller Rate weiter | render() prueft weder video.paused noch readyState und laedt daher auch im Pausen-/Idle-Zustand mit Display-Refresh-Rate denselben Frame per texImage2 |
| ⚪ | `webgl-upscaler.js:347` | Unbehandelte Exception im Render-Loop hinterlaesst Schwarzbild (Video bleibt opacity:0) | enable() blendet das Video-Element aus (opacity '0') und verlaesst sich darauf, dass der Canvas-Loop rendert. render() hat aber kein try/catch: wirft  |

</details>

<details><summary><b>Python – main.py (Z. 1–3400)</b> — 15 Befunde</summary>

| Sev | Ort | Titel | Kurz |
|---|---|---|---|
| 🟡 | `main.py:466` | realesrgan-x4-256 (fixed-shape 256) ist mit Default-ONNX_TILE_SIZE=512 unbenutzbar | Der dynamische Tiler (_run_onnx_tiled/upscale_with_onnx) schneidet Kacheln der Groesse ONNX_TILE_SIZE (Default 512) bzw. der Bildgroesse; ein Modell m |
| 🟡 | `main.py:914` | ncnn-Katalogeintraege referenzieren Modellnamen, die das installierte realsr-Paket nicht buendelt | Dockerfile.vulkan installiert realsr-ncnn-vulkan-python, das nur die RealSR-DF2K-Modelle (Namen 'models-DF2K'/'models-DF2K_JPEG') mitbringt. Der Katal |
| 🟡 | `main.py:2371` | Blockierende ort.InferenceSession/session.run direkt im Event-Loop von load_onnx_model | load_onnx_model ist async, erzeugt die Sessions, die GPU-Verifikations-Inferenz (Z. 2393) und den TensorRT-Reload (Z. 2454, Engine-Build kann Minuten  |
| 🟡 | `main.py:2649` | cv2.imdecode laeuft vor der MAX_IMAGE_PIXELS-Pruefung (Decompression-Bomb-OOM) | upscale_image (und upscale_image_hdr, Z. 2763-2776) dekodiert das Bild vollstaendig, bevor das eigene 256-MP-Limit greift; massgeblich ist bis dahin n |
| 🟡 | `main.py:2032` | load_model ohne Kategorie-Guard: Interpolations-/Face-Restore-Modelle als Haupt-Upscaler ladbar | Anders als load_rife_model (Z. 3112-3114) und load_face_restore_model (Z. 3277-3278) prueft load_model die Kategorie nicht. Ueber POST /models/load la |
| 🟡 | `main.py:2011` | load_opencv_model setzt current_model_input_frames nicht zurueck | load_onnx_model und load_ncnn_model setzen state.current_model_input_frames aus model_info, load_opencv_model nicht. Wurde zuvor ein ONNX-Modell mit i |
| 🟡 | `main.py:1388` | nomos8k-hat-l-x4 aktiv, obwohl HAT-S wegen CPU-EP-Inkompatibilitaet deaktiviert wurde | nomos8k-hat-x4 steht mit dem Kommentar 'HAT transformer uses ops (LayerNorm with dynamic shape) that fail on CPUExecutionProvider' auf available:False |
| 🟡 | `main.py:3426` | restore_faces_in_frame meldet fehlgeschlagene Crops als restauriert | Wenn _restore_face_crop fuer einen Crop eine Exception wirft, wird nur gewarnt und continue ausgefuehrt, der Rueckgabewert bleibt aber len(faces). Der |
| ⚪ | `main.py:1955` | Body-Size-Limit nur Content-Length-basiert - Chunked-Requests umgehen es | Die Middleware prueft ausschliesslich den Content-Length-Header; Requests mit Transfer-Encoding: chunked passieren ungeprueft. Die len()-Checks der En |
| ⚪ | `main.py:3282` | Fehlermeldung verweist auf nicht existenten Endpoint /download-model | load_face_restore_model raet bei fehlender Modelldatei zu 'POST /download-model?model_name=...' - diesen Endpoint gibt es nicht; tatsaechlich heisst e |
| ⚪ | `main.py:1932` | DEFAULT_MODEL: kein Alias-Resolve und unbekannte Namen gelten als 'available' | Anders als die Endpoints (Z. 4327/4390/4433) schickt lifespan den DEFAULT_MODEL-Wert nicht durch _resolve_model_key, Legacy-Keys wie 'rife-v4.6' schei |
| ⚪ | `main.py:1842` | Prefix-Pfadcheck ohne Trennzeichen und ignoriertes Sidecar-filename | str(model_path).startswith(str(models_dir.resolve())) akzeptiert Geschwisterpfade wie /app/models-x/... (via filename '../models-x/f.onnx' im Sidecar) |
| ⚪ | `main.py:1629` | rocm-smi-VRAM-Parsing interpretiert Bytes als MB | Neuere rocm-smi-Versionen geben 'VRAM Total Memory (B): 17163091968' aus; der erste isdigit-Token (der Byte-Wert) wird unveraendert als MB uebernommen |
| ⚪ | `main.py:3053` | Zero-Padding statt Reflect bei Multiframe-Randkacheln | upscale_multiframe padded Randkacheln mit Schwarz, wenn eine Bilddimension kleiner als tile_size ist; das VSR-Modell sieht dadurch harte schwarze Kant |
| ⚪ | `main.py:2179` | Raw-ncnn-Fallback: non-contiguous Numpy-Slices und hartkodierte Blob-Namen | img[y_start:y_end, x_start:x_end] ist bei x-Teilschnitten nicht C-contiguous; ncnn.Mat.from_pixels erwartet einen zusammenhaengenden Pixel-Puffer, sod |

</details>

<details><summary><b>Python – main.py (Z. 3401–6628)</b> — 15 Befunde</summary>

| Sev | Ort | Titel | Kurz |
|---|---|---|---|
| 🟡 | `main.py:5813` | /quality-metrics blockiert das Event-Loop mit voller KI-Inferenz _(↓von high)_ | Der async-Handler ruft upscale_image_array und compute_quality_metrics synchron auf statt via run_in_executor — auf einer CPU-Box blockiert eine einzi |
| 🟡 | `main.py:5884` | /process-grain blockiert das Event-Loop (fastNlMeans + Inferenz) _(↓von high)_ | remove_grain (cv2.fastNlMeansDenoisingColored — bei HD-Bildern viele Sekunden CPU) und im 'both'-Pfad zusaetzlich upscale_image_array laufen synchron  |
| 🟡 | `main.py:3934` | subprocess.run und getaddrinfo blockieren das Event-Loop in Diagnose-Endpoints | /gpu-verify (clinfo + nvidia-smi, je timeout=10), /gpus (Zeile 3866), /doctor (Zeile 4070) und /connections/register (socket.getaddrinfo, Zeile 4251)  |
| 🟡 | `main.py:5375` | /models/cleanup dry_run=false scheitert mit managed Tokens und API_TOKEN=disable | Der destruktive Double-Check vergleicht den Header nur gegen den env-API_TOKEN: Mit API_TOKEN=disable ist expected_token="disable" (truthy) und jeder  |
| 🟡 | `main.py:3622` | /logs/recent, /logs/stream und /connections ohne API-Token abrufbar | Die kompletten Server-Logs (inkl. uvicorn-Access-Logs, Fehlerdetails, Modell- und Jellyfin-URLs) sowie die registrierten Plugin-Verbindungen (/connect |
| 🟡 | `main.py:6157` | _ingest_onnx_bytes blockiert das Event-Loop (Session-Load, 500-MB-IO, sha256) | Validierung via ort.InferenceSession auf bis zu 500 MB, Temp-Write und shutil.move laufen synchron, und die Funktion wird direkt aus async-Kontexten a |
| 🟡 | `main.py:6053` | /enhance-faces blockiert das Event-Loop | enhance_faces_in_image (Haar-Cascade-Detection auf bis zu 256-MP-Bildern plus pro Gesicht eine 512x512-ONNX-Inferenz bzw. bilateralFilter) laeuft sync |
| 🟡 | `main.py:5436` | /interpolate-frames und /face-restore/frame ohne Concurrency-Limit | Anders als alle Upscale-Endpoints holen /interpolate-frames und /face-restore/frame (Zeile 5675, dort fehlt zusaetzlich _check_circuit_breaker) keine  |
| 🟡 | `main.py:4409` | Background-Tasks ohne gehaltene Referenz; Job-Registries wachsen unbegrenzt | asyncio.create_task-Rueckgaben fuer Download- (4409) und Import-Jobs (6461) werden verworfen; der Event-Loop haelt nur schwache Referenzen, sodass ein |
| 🟡 | `main.py:6218` | Upload/Import ueberschreibt Built-in-Modelle bei Namenskollision | _ingest_onnx_bytes prueft nicht, ob model_name bereits ein Built-in-Katalogeintrag ist: Ein Upload namens z.B. "fsrcnn-x2" ersetzt Modelldatei und Reg |
| 🟡 | `main.py:4984` | /upscale-stream: Fehlerpfad dropt Frames still, Kommentar behauptet Marker-Frame | Bei einem Inferenzfehler wird nur 'continue' ausgefuehrt — es wird kein Marker-Frame geyieldet, obwohl der Kommentar es behauptet und der adaptive Dro |
| ⚪ | `main.py:4542` | /upscale meldet Decode-Fehler als 413 | upscale_image wirft ValueError sowohl fuer "Image too large" als auch fuer "Failed to decode image"; der Handler mappt beides pauschal auf 413, sodass |
| ⚪ | `main.py:4455` | /models/load: globale GPU-State-Mutation und Rollback ohne Serialisierung | Parallele /models/load-Requests mutieren state.use_gpu/gpu_device_id global vor dem Laden; schlaegt einer fehl, stellt sein Rollback unter Umstaenden  |
| ⚪ | `main.py:5510` | /interpolate-frames: check-then-act beim RIFE-Modell-Load | Zwischen dem needs_load-Check, dem Laden im Executor und dem erneuten Auslesen von state.rife_session liegt kein gemeinsamer Lock: Zwei parallele Requ |
| ⚪ | `main.py:4928` | /upscale-stream: globales _realtime_stats.reset() bei jedem Stream-Start | Das globale Stats-Objekt wird bei jedem neuen Stream zurueckgesetzt; bei parallelen Streams loeschen sich die Sessions gegenseitig die Zaehler, /realt |

</details>

<details><summary><b>Python – token_store / model_import / Tests</b> — 9 Befunde</summary>

| Sev | Ort | Titel | Kurz |
|---|---|---|---|
| 🟡 | `model_import.py:164` | Download-Groessencap greift erst nach vollstaendigem Puffern im RAM _(↓von high)_ | _download_capped laedt mit client.get() den kompletten Response-Body in den Speicher (resp.content) und prueft MAX_MODEL_UPLOAD_BYTES erst danach. _im |
| 🟡 | `token_store.py:86` | Tz-naives expires_at crasht verify() mit TypeError statt fail-closed | _is_expired faengt nur ValueError; ein parsebares, aber timezone-naives expires_at (z.B. "2030-01-01T00:00:00" aus einer Hand-Editierung von tokens.js |
| 🟡 | `test_validation.py:33` | Drei Validierungstests sind wirkungslos (falsches Form-Feld, Auth-403 als Erfolg gewertet) | test_model_name_path_traversal_rejected und test_model_name_with_special_chars_rejected posten das Feld "model", der Endpoint erwartet aber model_name |
| 🟡 | `convert_to_onnx.py:115` | strict=False plus Erfolgsmeldung maskiert komplett unpassende Checkpoints | load_state_dict(strict=False) ignoriert saemtliche fehlenden/unerwarteten Keys und danach wird bedingungslos "Loaded pretrained ... weights" gedruckt  |
| 🟡 | `test_catalog_import.py:1` | Import-Download-Pfad (Cap, sha-Pin, Async-Job) komplett ungetestet | Getestet sind nur _import_gate, _extract_pinned_onnx_from_zip und die Job-Fehlercodes; _download_capped (Cap-Durchsetzung), _download_pinned (502 bei  |
| ⚪ | `convert_to_onnx.py:40` | Abgebrochener Weight-Download vergiftet den Cache dauerhaft | urllib.request.urlretrieve laesst bei einem Abbruch (Verbindungsreset, Ctrl-C) eine partielle Datei unter weights/ liegen; der naechste Lauf sieht os. |
| ⚪ | `token_store.py:112` | Korrupte tokens.json wird still als leerer Store behandelt | _load degradiert OSError/JSONDecodeError kommentarlos zu _empty(): alle Managed Tokens hoeren schlagartig auf zu funktionieren (403), ohne dass irgend |
| ⚪ | `main.py:5099` | Endpoint-Doku verspricht expires_days=0 als 'nie ablaufend', Store lehnt 0 ab | Der Docstring von POST /auth/tokens sagt "Omit expires_days (or 0/null) for a token that never expires", token_store.create_token (token_store.py:171) |
| ⚪ | `test_token_store.py:1` | Kein Test fuer die zentrale Concurrency-Zusicherung des Token-Stores | Der Moduldocstring von token_store verspricht, dass parallele Requests die Datei nicht korrumpieren und frisch erzeugte Tokens nicht ueberschrieben we |

</details>

<details><summary><b>Infrastruktur – Dockerfiles, requirements, CI</b> — 18 Befunde</summary>

| Sev | Ort | Titel | Kurz |
|---|---|---|---|
| 🟡 | `cleanup-dockerhub-tags.ps1:27` | DockerHub-Cleanup wuerde alle v1.8.x-Tags loeschen und :latest auf v1.7.8 zurueckdrehen _(↓von high)_ | Der von dockerhub-cleanup.yml (execute=true) ausgefuehrte Script hat $CurrentNvidiaTag='v1.7.8' hartkodiert und behaelt nur v1.7.7*/v1.7.8*-Pins; aktu |
| 🟡 | `Dockerfile.amd:80` | --force-reinstall ohne --no-deps kann die bewusste numpy<2-Kappe aushebeln | pip install --force-reinstall "onnxruntime-rocm<=1.22.99" reinstalliert auch alle Dependencies und waehlt dabei die NEUESTE Version, die nur die Const |
| 🟡 | `docker-publish.yml:146` | Dry-Run (push=false) scheitert am Registry-Cache-Export ohne Login | Bei workflow_dispatch mit push='false' wird der Docker-Hub-Login uebersprungen (Zeile 104), aber cache-to: type=registry versucht trotzdem unauthentif |
| 🟡 | `build-and-release.yml:51` | workflow_dispatch-Lauf schlaegt immer fehl: VERSION wird 'refs/heads/main' | Bei workflow_dispatch ist GITHUB_REF=refs/heads/main; die Prefix-Entfernung ${GITHUB_REF#refs/tags/v} greift nicht und laesst den vollen String stehen |
| 🟡 | `build.yml:105` | AMD-Dependency-Drift wird entgegen dem Kommentar von KEINEM Workflow abgedeckt | Der Kommentar behauptet, die AMD-Variante werde 'by the weekly lock-requirements workflow' abgedeckt - aber lock-requirements.yml hat keinen amd-Eintr |
| 🟡 | `import-catalog-refresh.yml:28` | Woechentlicher Katalog-Commit deployt die Live-Site nie (GITHUB_TOKEN triggert pages.yml nicht) | Der Push von site/models-import.json erfolgt mit dem Default-GITHUB_TOKEN, und von GITHUB_TOKEN erzeugte Push-Events starten keine weiteren Workflows  |
| 🟡 | `Dockerfile.converter:43` | requirements-converter.lock wird woechentlich erzeugt und committet, aber nie installiert | lock-requirements.yml generiert einen (bewusst hashlosen, aber versions-gepinnten) Converter-Lock, doch Dockerfile.converter installiert weiterhin die |
| 🟡 | `docker-publish.yml:28` | Kein concurrency-Guard: parallele Runs racen :latest und die Rolling-Tags | CLAUDE.md dokumentiert als bekannte Falle, dass zwei gleichzeitige docker-publish-Runs den :latest- und docker7-Rolling-Tag racen und der Operator den |
| 🟡 | `docker-publish.yml:23` | Stales Dispatch-Default '1.6.1.13' ueberschreibt gepinnte Versions-Tags mit aktuellem Code | Das workflow_dispatch-Input version hat den veralteten Default 1.6.1.13 (aktuell: 1.8.3.21). Wer beim Dispatch das vorbefuellte Feld nicht aendert, pu |
| 🟡 | `build.yml:80` | pip-audit prueft die Range-Datei statt der tatsaechlich installierten Locks | pip-audit -r requirements-cpu.txt aufloest die Ranges frisch und prueft damit die NEUESTEN Versionen - die Images installieren aber die bis zu eine Wo |
| 🟡 | `docker-publish.yml:170` | Fehlendes SARIF nach Trivy-Timeout faerbt den Job trotzdem rot | Trivy hat continue-on-error:true ('never let a scan hiccup paint a shipped build red'), aber wenn der Scan am 20-GB-ROCm-Image timeoutet, existiert tr |
| 🟡 | `build.yml:17` | contents:write auf Build-only-Workflows, die per Repo-Regel nie releasen duerfen | build.yml und build-and-release.yml (Zeilen 19-20) fordern permissions: contents: write, obwohl beide nur bauen/testen/Artefakte hochladen - vermutlic |
| ⚪ | `v1.7.1-audit-checks.yml:87` | zip-version-check kollidiert mit der dokumentierten 3-Part-Konvention bei X.Y.Z.0-Releases | Laut Release-Prozess (CLAUDE.md) ist meta.json bei einer .0-Version 3-teilig (z.B. 1.9.0), waehrend die Feeds immer 4-teilig sind (1.9.0.0). Der Strin |
| ⚪ | `lock-requirements.yml:70` | Action-Pinning inkonsistent: 6 von 8 Workflows nutzen mutable Tags statt SHAs | build.yml und build-and-release.yml pinnen alle Actions vorbildlich per Commit-SHA, aber lock-requirements.yml, docker-publish.yml, pages.yml, dockerh |
| ⚪ | `Dockerfile.vulkan:83` | Ungepinnte pip-Installs nach dem hash-gelockten Layer unterlaufen das Lock-Konzept (vulkan) | Nach dem --require-hashes-Install folgen ungepinnte, hashlose Installs: realsr-ncnn-vulkan-python/ncnn (breite Ranges, stderr unterdrueckt) und pybind |
| ⚪ | `Dockerfile.vulkan:56` | Build-Toolchain (cmake, build-essential, ninja, git) verbleibt im finalen Vulkan-Image | Die Compiler-Toolchain wird nur fuer den Source-Build-Fallback von ncnn gebraucht, bleibt aber auch dann im Image (mehrere hundert MB plus groessere A |
| ⚪ | `lock-requirements.yml:17` | variants-Input ist wirkungslos und der numpy<2-Assert kann nie greifen | Das workflow_dispatch-Input 'variants' wird im Workflow-Body nirgends referenziert - die Matrix ist hartkodiert, ein Dispatch mit variants='cpu' lockt |
| ⚪ | `Dockerfile.amd:90` | CPU-Fallback des AMD-Images ist nur eine Logzeile in einem gruenen Publish-Run | Faellt der onnxruntime-rocm-Install auf plain onnxruntime zurueck, druckt der Sichtbarkeits-RUN lediglich ein WARNING in das Build-Log - docker-publis |

</details>

<details><summary><b>Release – Feeds, Versions-Stamping, Scripts</b> — 10 Befunde</summary>

| Sev | Ort | Titel | Kurz |
|---|---|---|---|
| 🟡 | `meta.json:11` | meta.json-Changelog ist 8 Releases veraltet (neuester Eintrag v1.8.3.13 bei Version 1.8.3.21) | Das changelog-Feld ist eine kumulierte Historie, deren juengster Eintrag v1.8.3.13 ist, waehrend version 1.8.3.21 traegt - seit acht Releases wurde ke |
| 🟡 | `manifest.json:440` | v1.5.6.0-Eintrag traegt SHA256 statt MD5 als checksum - Version aus dem Katalog nicht installierbar | Der checksum-Wert von v1.5.6.0 ist 64 Hex-Zeichen (SHA256), alle anderen 79 Eintraege sind 32-Zeichen-MD5; Jellyfin berechnet MD5 des ZIPs und lehnt b |
| 🟡 | `sync-fallback-models.ps1:11` | Katalog-Sync-Gate prueft nur Model-IDs; available/scale-Drift bleibt unentdeckt, beschriebener Diff-Gate existiert nicht | Der Header verspricht 'CI gate: no-op on clean tree (git diff --exit-code)' - dieser Gate existiert nicht und koennte so auch nie bestehen, weil gener |
| ⚪ | `JellyfinUpscalerPlugin.csproj:54` | Tote FFmpeg-Wrapper-Scripts werden weiterhin in den Build-Output kopiert (Release-ZIP-Falle) | Das Wrapper-Feature wurde in v1.8.3.2 komplett entfernt; upscale-ffmpeg.sh/.bat haben keinerlei Code-Referenzen mehr, werden aber per CopyToOutputDire |
| ⚪ | `upscale-ffmpeg.bat:38` | Batch-Wrapper-Logik ist mehrfach kaputt (findstr-Literal, fehlende Delayed Expansion, stale API-Route) | findstr /C: sucht den LITERALEN Text 'SupportsCUDA.*true' (Regex braeuchte /R), CUDA wuerde also nie erkannt; %errorlevel%, %ARGS% und %UPSCALE_FILTER |
| ⚪ | `manifest.json:261` | Changelog-Texte von 19 Alt-Versionen weichen zwischen manifest.json und den beiden Repository-Feeds ab | Fuer die Versionen 1.6.1.14 bis 1.7.7.0 traegt manifest.json laengere Changelog-Texte als repository-jellyfin.json/repository-simple.json (letztere si |
| ⚪ | `verify-release.ps1:122` | targetAbi der Feed-Eintraege wird nur geloggt, nie validiert | Der Triple-Feed-Check asserted checksum-Format/-Gleichheit und sourceUrl, gibt targetAbi aber nur aus; ein Feed-Eintrag mit falschem oder 3-teiligem t |
| ⚪ | `bump-version.py:4` | Docstring behauptet '16 sites', das Script stampt 13 (und spiegelt verify-release nicht 'exactly') | Die sites-Liste hat 13 Eintraege (CLAUDE.md dokumentiert korrekt 13); verify-release.ps1 prueft 16 Stellen, weil die drei Feed-Dateien dort zusaetzlic |
| ⚪ | `check_ui_field_consistency.py:37` | ID-Definitions-Regex maskiert Phantom-Referenzen (data-id, JS-Variable 'id', Backtick-Selektoren ungeprueft) | ID_DEF_RE matcht wegen \bid auch data-id="..." (Wortgrenze nach '-') und jede JS-Zuweisung id = '...'; solche Treffer registrieren Werte als 'definier |
| ⚪ | `verify-release.ps1:74` | Script bricht unter Linux/macOS sofort ab ($env:TEMP ist dort nicht gesetzt) | Join-Path $env:TEMP wirft bei nicht gesetzter TEMP-Variable (Standard auf Linux-pwsh) einen Binding-Fehler, und mit ErrorActionPreference=Stop stirbt  |

</details>

## Empfohlene Fix-Reihenfolge

1. **🟠 `POST /process` absichern** (`controller#1`): Library-Allowlist wie in `EnqueueJob` + Überschreibschutz (`File.Exists(outputPath)`). Schwerwiegendster Befund.
2. **🟠 Auto-Mode-Wurzel** (`core#1`/`processing#2`/`io#1`): `Model`-Default auf `"auto"`/leer ODER Batch-Gate auf `EnableAutoModelSelection` allein — ein Fix, drei Symptome, reaktiviert Hardware-Cap & 8K-Vermeidung im Scan.
3. **🟠 Queue-Busy-Spin** (`processing#1`): Resume-`Release()` weg + Leerzweig-Permit nicht restaurieren.
4. **🟠 Locale-Bugs** (`video#1`/`#4`): `InvariantCulture` bei `fps=`/`-ss`.
5. **🟠 Stille Fehl-Erfolge** (`io#5`, `video#3`): Nicht-AI-Fallbacks nicht als Erfolg speichern/melden.
6. **🟠 Supply-Chain & Release-Tooling**: `trivy-action` auf SHA pinnen (`infra#2`), `cleanup-dockerhub-tags.ps1` Keep-Regeln aktualisieren (`infra#1`), `verify-release.ps1` .0-Tag-Normalisierung (`release#2`).
7. **🟠 Python-Service**: `/models/cleanup` Restart-Schutz (`main-b#3`), Upload-Body-Cap für Modelle (`main-a#1`), Streaming-Semaphore-Leak (`main-b#1`), rife-v4.25 Kanalzahl (`main-a#2`).
8. Danach die restlichen High + die kalibrierten Medium-Befunde.

## Offene Punkte

- **Alle 14 Bereiche + volle Verifikation abgeschlossen** — keine Coverage-Lücke mehr.
- Die xUnit-Suite ist solide (beide Scale-Konventionen, Hardware-Cap, 8K/CPU, Override-Defaults getestet); Hauptlücken: `DenoisePrefilterMethod` fehlt im `RegistryDriftLock` (gleiche Silent-Fallback-Klasse wie der Codec-Bug), Kern-Services `ProcessingMethodExecutor`/`VideoProcessor`/`VideoFrameProcessor` ohne Tests, `HardwareTier`-Statik wird zwischen Tests nicht zurückgesetzt.
- Medium/Low-Befunde sind **nicht** adversarial verifiziert — vor einem Fix jeweils kurz gegenlesen.

---
_Multi-Agent Code-Review · 14/14 Bereiche · 192 Findings · 36 C/H doppelt verifiziert (35 real, 1 widerlegt) · 2026-08-04_