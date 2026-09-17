# v1.8.3.31 — Release

Stand: 17.09.2026. Der Nutzer hat die Zielserver-Abnahme ausdrücklich aufgehoben: „Überspring die serverabnahme“. Reguläre Veröffentlichung ist damit freigegeben, sofern die lokalen, CI-, Paket-, Docker- und Feed-Prüfungen bestehen. Es wird keine Zielhardware-Verifikation behauptet. Branch: `update/v1.8.3.31`. Vorhandene Änderungen wurden erhalten; kein Reset/Verwerfen des Worktrees.

## Umfang

- P0: Beide Build-Workflows testen das explizite C#-Testprojekt, erzeugen TRX, laden es auch bei Fehlern hoch und prüfen nichtleere, vollständig erfolgreiche Ergebnisse. Keine GitHub-Release-Schritte in den Build-Workflows.
- P1: CUDA bleibt Standard; `SKIP_TENSORRT=true` auch ohne Compose. TensorRT nur ausdrücklich mit passenden Runtime-Bibliotheken aktivieren.
- #79: Frame-/Chunk-Proxys verbrauchen das Zehn-Bildaktionen-Limit nicht. HTTP 429/503, Fehlerdetail und Retry-After erreichen den Player. Der Dienst kann nach abgewiesener Half-open-Probe wieder gesund werden.
- Player: höchstens ein Capture/Request/Decode gleichzeitig; exponentieller Backoff 250–2000 ms mit Jitter und Retry-After; frische Frames nach Wartezeit; Abbruch und Generationsschutz bei Stop, Navigation, Modell- und Moduswechsel. Pause/Resume setzt keinen falschen Timeout in Gang.
- A1: Treiber-Upscaling verhindert zusätzliches Plugin-Upscaling in allen Modi. Objektmaskierung ersetzt Upscaling, nutzt aber den gemeinsamen Server-Capture-Loop. Auto wählt Server-AI nur bei `benchmark.fps >= videoFps * 0.8`; unbekannte FPS wählen Lanczos/CAS.
- Ehrliche Namen: CUDA-Lanczos, VAAPI-Skalierung und libplacebo EWA Lanczos werden nicht als NVIDIA VSR/AMD FSR ausgegeben.
- H1: ausschließlich PQ/ST.2084, BT.2020, mindestens 10 Bit, einzelne RGB16-PNGs und libx265/yuv420p10le. Statische Mastering-/Content-Light-Metadaten werden transportiert. HLG, unbekannte Transfer/Primaries, dynamisches HDR, HDR-Realtime und HDR-Multi-Frame werden abgelehnt. Keine Originalframe-/SDR-Ersetzung bei Fehlern.
- Die HDR-Inferenz verwendet weiterhin Tone-Mapping, SDR-Inferenz und inverse Rekonstruktion. Ein bestätigter Fehler bei der Mischung linearer Luminanz und PQ-Codewerte wurde korrigiert und synthetisch geprüft. RGB16-Transport und Unit-Tests sind keine HDR-Display- oder Qualitätsabnahme.
- B0: sämtliche 17 derzeitigen `available:false`-Einträge werden aus `Resources/models-fallback.json` gelesen. Interpolation, Face-Restore und Detektoren sind weder normale Upscaler noch normale Benchmark-Kandidaten. Unbekannte importierte Upscaler bleiben zulässig.
- Discussion #80: Batch-Jobs brechen bei AI-Fehlern ab und beenden laufende Frames; keine lokalen Resizes/Originalkopien als Erfolg. Beschädigte Bilder, Dimensionswechsel und Lücken werden abgelehnt. Der Realtime-Dateiencoder übernimmt die native Größe aus dem ersten echten AI-Bild. FFmpeg-Decoding/-Encoding bleibt auf dem Jellyfin-Host.
- Import: tatsächliche Downloadlimits während des Empfangs, endungstreues Safetensors/TorchScript-Laden, Prüfung endlicher und formgleicher Konvertierungsausgaben.
- Manueller Release-Validator: identische vollständige Einträge aller drei Feeds, Plugin-GUID, exakter Sechs-Dateien-Inhalt, nichtleere Runtime-DLLs mit korrekter Identität, Plugin-Assembly/File-Version und Metaversion. Portabler Temp-Pfad unter Linux/Windows.
- Docker-Workflow: Version aus dem gewählten Checkout, abweichende Dispatch-Version wird abgelehnt; globale Serialisierung verhindert konkurrierende Rolling-/latest-Runs. Build-only veröffentlicht keinen Registry-Cache.

Details zu Meldungen und Modellquellen: [Audit vom 16.09.2026](AUDIT-v1.8.3.31-2026-09-16.md). Kein neues, ungeprüftes Modell wurde als verfügbar eingebaut.

## Lokale Prüfnachweise

Abschließender Lauf nach allen Mutationen: **442 C#-Tests, 209 Python-Tests und 36 Node-Verhaltenstests bestanden**, keine fehlgeschlagenen oder übersprungenen Tests. Python meldet zwei Deprecation-Warnungen aus Starlette/httpx und AnyIO. Logs, TRX, Mutationsergebnisse und ZIP liegen in `../local-validation/` oder ignorierten `TestResults*/`-Verzeichnissen, nicht im Commit.

- C#: .NET SDK 9.0.317, Release-Lauf und TRX-Validator bestätigen 442 tatsächlich ausgeführte, bestandene Ergebnisse.
- Python: vollständige Suite in der projektbezogenen Python-3.12-Umgebung. FastAPI-TestClient benötigt hier eine Sandbox-Ausnahme für lokale IPC. Frühere Hänger unter der Sandbox waren kein nachgewiesener Produktfehler.
- Node: der geforderte `node --test tests/*.test.cjs` besteht. Die lokale Prozessisolation zeigt drei Datei-Aggregate; Node 24 mit `--test-isolation=none` zählt die tatsächlichen Verhaltenstests einzeln.
- UI-Konsistenz: 138 Konfigurationsreferenzen, sieben Standalone-Skripte. TRX-Validator: zwei Testmethoden einschließlich ungültiger Eingabefälle. Docker-Versionsauflösung: vier Fälle. PowerShell-Paketprüfung: 17 Fälle mit echten Publish-Assemblies.
- Vorgeschriebene Mutationen erkannt: Frame-Limiter exakt ab Frame 11, Treiber-/Maskierungs-Guard, Benchmarkregel, Kategorien, HLG in C# und Python, absichtlich fehlgeschlagener echter C#-Test. Validator lehnt dessen TRX und einen tatsächlichen Null-Test-Lauf ab.
- Zusätzliche Mutationen: elf Player-Races/Guards und fünf neue Kategorie-/Download-/Konvertierungsprüfungen erkannt. Alle Mutationen jeweils einzeln, bytegleich entfernt; vollständige Suiten danach erneut grün ausgeführt.

## Docker Desktop: lokaler CPU-Test am 16.09.2026

Docker Desktop 4.91.0 / Engine 29.8.0, Linux amd64, vier VM-CPUs und knapp 2 GB VM-RAM. CPU-Image aus dem geprüften Service-Stand `97c0181` mit `APP_VERSION=1.8.3.31` erfolgreich gebaut. Image-Index-Digest: `sha256:7831bf2970c23e9bc1ac27a6f1a2fe81a7d652aec0382ff7fc906228547579f5`.

- Separater Testcontainer, nur localhost, eigener API-Token; Health healthy. `/health/detailed`, `/gpu-verify`, `/status` und Modellladen erfolgreich. Die Diagnostik meldet CPU, keine verifizierte GPU.
- Echtes FSRCNN-x2-Modell aus dem bestehenden hashgeprüften Katalog geladen; **286 Frame-Anfragen über 301,77 Sekunden**, alle HTTP 200. Jedes JPEG wurde dekodiert und auf 256×144 Pixel bei 128×72 Eingabe geprüft. Frame 11 ebenfalls erfolgreich.
- Anschließend 48 konkurrierende Anfragen: **43× HTTP 503** mit Fehlerdetail `Busy` und `Retry-After: 1`, fünf erfolgreiche Antworten. Nach einer Sekunde Wartezeit wieder HTTP 200.
- Ein als HLG deklarierter Realtime-Aufruf wird verständlich mit HTTP 422 abgelehnt. Dies war kein echter HLG-Referenzclip.
- Kein Jellyfin-Player, kein C#-Proxy und keine Zielhardware in diesem Test: damit insbesondere kein Beleg für die reale 429-Weitergabe oder die vollständige Abnahme von #79. Die End-to-End-Abnahme auf dem Zielserver wurde später ausdrücklich vom Nutzer übersprungen.

Logs und Antworten liegen außerhalb des Repositories in `../local-validation/docker-2026-09-16/`. Alle sieben RC-Varianten sind inzwischen auf Docker Hub veröffentlicht; Tags, Plattformen und Commit-Pins wurden über die öffentliche Registry-API bestätigt. [Digests und Docker-Prüfnachweise](DOCKER-RC-v1.8.3.31.md). Der Docker-Workflow ist inzwischen mit allen sieben Jobs erfolgreich abgeschlossen (am 17.09.2026 bestätigt).

## Paket

Ausschließlich aus `dotnet publish JellyfinUpscalerPlugin.csproj -c Release`. Das ZIP enthält genau `JellyfinUpscalerPlugin.dll`, `FFMpegCore.dll`, `CliWrap.dll`, `Instances.dll`, `SixLabors.ImageSharp.dll` und `meta.json`. Keine PDBs, Test-DLLs, deps.json, Scripts oder Testartefakte. Inhalt, CRC, Publish-Bytegleichheit, Assembly/File-Version und Metadaten werden vor Upload geprüft.

Die tatsächliche lokale ZIP-MD5 steht im lokalen `../local-validation/package.json` und im Abschlussbericht. Sie ist keine Prüfsumme eines veröffentlichten Assets. Alle drei Feeds bleiben bis zum tatsächlichen Upload unverändert.

## Server-Abnahme: auf ausdrücklichen Nutzerwunsch übersprungen

Die Brain-Pläne wurden über die vorhandene Netzwerkfreigabe gelesen. Für den Zielserver `192.168.178.113` fehlt weiterhin eine konkrete dokumentierte Jellyfin-/AI-Service-URL und ein verwendbarer administrativer Zugang. Die einmalige Nachfrage wurde bereits gestellt. Keine Portscans, geratenen Ports oder Credentials.

Nicht durchgeführt: Erreichbarkeits-/Health-Prüfung über eine dokumentierte Ziel-URL, recoverable Sicherung, Deployment, Jellyfin-Neustart, harter Webclient-Reload, `/health/detailed`, `/gpu-verify`, echtes Modellladen/Inferenz, mindestens fünf Minuten Player-Wiedergabe, echte 429/503-Erholung, Treiber-Guard und Maskierung im realen Client sowie PQ-/HLG-Referenzclip auf Zielhardware.

Ein früherer lokaler synthetischer FFmpeg-Transporttest erhielt zehn RGB16-Frames/1,000 s, PQ/BT.2020/yuv420p10le und statische HDR-Metadaten. Er enthielt keine echte AI-Inferenz und ersetzt keine Zielhardware-Abnahme.

## GitHub-Auslieferung: technische Gates und dokumentierter Abnahmeverzicht

Der Nutzer hat Commits und Push des RC-Branches vorab ausdrücklich freigegeben. Die GitHub-CLI ist inzwischen erfolgreich angemeldet; der RC-Branch wurde am 16.09.2026 bis `5c299d2` gepusht. Der frühere HTTP-403-Blocker der GitHub-App besteht damit für den CLI-Push nicht mehr. Keine Secrets ausgegeben.

Am 16.09.2026: Issue #79 weiterhin offen; Tag und Release `v1.8.3.31` nicht vorhanden. Die Feeds enthalten noch die veröffentlichte Vorgängerversion. Der neueste Main-Commit `9602d5d17110d4fc9bc53c0ba21149daadc0f684` ändert ausschließlich das Generierungsdatum des Importkatalogs.

Der Docker-Workflow kann vor der Hardware-Abnahme mit `channel=candidate` alle sieben Varianten unter getrennten `rc-v1.8.3.31[-backend]`-Tags und commitgebundenen RC-Tags veröffentlichen. Vier Verhaltenstests, 14 Kombinationen des tatsächlichen Workflow-Tag-Schritts und zwei erkannte Mutationen prüfen, dass Kandidaten keine finalen Pins oder Rolling-Tags ändern. Dies ist keine Freigabe als reguläres Release.

Nach dem ausdrücklichen Abnahmeverzicht vom 17.09.2026: Branch/Review abschließen; Tag-/Release-Existenz erneut prüfen; Docker-Workflow für 1.8.3.31 mit `channel=release` ohne konkurrierende latest-Runs ausführen; geprüftes Plugin-ZIP manuell veröffentlichen. Veröffentlichtes ZIP herunterladen, echte MD5 identisch in alle drei Feeds übernehmen, Feeds committen/pushen und `pwsh Scripts/verify-release.ps1 -Tag v1.8.3.31` vollständig gegen GitHub bestehen lassen. Erst dann #79 mit Version, Umgebung, Testdauer und Ergebnissen kommentieren und schließen.

Der bisherige CI-Audit erzwang fälschlich einen vorgezogenen Feed-Eintrag. Er prüft jetzt die tatsächlichen Publish-Assemblies/ZIP-Metadaten und identische veröffentlichte Feed-Einträge, lässt aber einen noch unveröffentlichten Kandidaten korrekt außerhalb der Feeds. Acht Verhaltenstests und drei erkannte Mutationen sichern dies ab. Die strenge Online-Prüfung nach dem Release bleibt unverändert.

## Bewusst offen

A3/A2, B1/B2, H2/H3, C1–C3 und D1/D2 bleiben Brain-Tracks in ihrer dokumentierten Abhängigkeitsreihenfolge. Keine unvollständige RIFE-, ArtCNN-, HDR-Realtime- oder Temporal-VSR-Funktion im Patch. GPU-/HDR-Qualität, Timeline und Pixelintegrität auf echten Zielclips bleiben eigenständige Gates.
