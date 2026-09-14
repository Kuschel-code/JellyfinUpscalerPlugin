# v1.8.3.31 — lokaler Release Candidate

Status: lokale Gates bestanden am 07.09.2026; RC-Branch-Push vom Nutzer ausdrücklich freigegeben. Zielserver-Zugriff ungeklärt, Release-Veröffentlichung gesperrt.
Arbeitsbranch: `update/v1.8.3.31`. Separater Checkout aus `4ec6a93dbcf099387d0f27a5983339f0c1edbe01` (main; gegenüber dem erwarteten `fa3d0aa190e75b7f590a50e8e69a1dd9b3a6c42f` nur der automatische Katalog-Refresh). Der erwähnte vorhandene RC-Checkout war auf diesem PC nicht auffindbar. Er wurde nicht verändert.

## Umfang und Grenzen

- P0: Beide Build-Workflows testen das explizite C#-Testprojekt und laden TRX auch bei Fehlern hoch. Der Validator lehnt fehlende, ungültige, leere und fehlgeschlagene Läufe ab. CI erstellt keine GitHub-Releases.
- P1: Standard-Compose setzt `SKIP_TENSORRT=true`; CUDA bleibt Standard. TensorRT erst mit den benötigten kompatiblen Bibliotheken im Image explizit einschalten.
- #79: Frame- und Chunk-Proxys verbrauchen das Limit der zehn expliziten Bildaktionen nicht. Status, Fehlerdetail und `Retry-After` werden weitergereicht.
- Aktueller GitHub-Scan vom 15.09.2026: #79 ist das einzige offene Issue. Die neue Discussion #80 meldet CPU-Last bei Bibliotheksjobs und einen untätigen AI-Container; der Batch-/Multi-Frame-Pfad bricht bei Docker-Fallbacks jetzt fail-closed mit Fehlerdetail ab, statt lokale Resizes oder Originalframes als Erfolg zu behandeln. Discussion #11 betrifft bereits implementierte Objektmaskierung/Detektor-Importe und enthält keine weitere Patch-Release-Blockade.
- Player: Eine Anfrage einschließlich Capture/Decode; Backoff 250–2000 ms mit Jitter, längeres `Retry-After` wird eingehalten. Nach der Wartezeit wird neu aufgenommen. Stop/Moduswechsel brechen Requests ab und verwerfen verspätete Antworten. Pause löst keinen Inaktivitäts-Fallback aus.
- A1: Treiber-Upscaling verhindert Plugin-Upscaling in allen Modi, auch vor dem Modell-Warmup. Maskierung ersetzt Upscaling und erhält den gemeinsamen Capture-Loop. Auto benötigt `benchmark.fps >= videoFps * 0.8`; die Bildrate kommt aus den Jellyfin-Mediendaten und berücksichtigt die Wiedergabegeschwindigkeit. Unbekannte FPS wählen Lanczos/CAS.
- Namen: CUDA-Lanczos, VAAPI-Skalierung und libplacebo EWA Lanczos sind keine Implementierungen von NVIDIA VSR oder AMD FSR.
- H1: Nur PQ/ST.2084, BT.2020, mindestens 10 Bit; einzelne echte RGB16-PNGs und `libx265` mit `yuv420p10le`. Statische Mastering-/Content-Light-Metadaten werden übertragen. Dynamische Metadaten werden im Stream und über alle Frames gesucht. HLG, unbekannte HDR-Transferfunktionen, dynamisches HDR, HDR-Realtime und HDR-Multi-Frame werden abgelehnt. Ungeprüfte HDR-Denoise-/Kreativfilter werden ebenfalls abgewiesen. BT.2020 wird bei der Pixelkonvertierung explizit gesetzt. Im Player erfolgt die Ablehnung vor Canvas-Capture; ohne Videofarbmetadaten startet keine Realtime-Verarbeitung. Fehler im HDR-Pfad brechen den Job ab, ohne SDR- oder Originalframe-Ersatz.
- Der bestehende HDR-Algorithmus arbeitet intern mit Tone-Mapping, SDR-Inferenz und inverser Rekonstruktion. RGB16-Transport ist kein Nachweis unveränderter HDR-Pixel oder korrekter Darstellung auf einem HDR-Display. Die Zielhardware-Abnahme bleibt zwingend.
- B0: Interpolation, Face-Restore und Detektoren sind keine normalen Upscaler. Nichtverfügbarkeit kommt vollständig aus `Resources/models-fallback.json` (bei dieser Prüfung 17 Einträge). Unbekannte selbst importierte Upscaler bleiben zulässig.

## Lokale Gates

Alle Testzahlen werden aus dem abschließenden Lauf dokumentiert. Testartefakte liegen außerhalb des Commits oder in ignorierten `TestResults*/`-Verzeichnissen. Mutationen werden jeweils einzeln durchgeführt und bytegleich zurückgenommen.

Erforderlich: explizites `dotnet test`, TRX-Validator, Python-Suite, Node-Verhaltenstests, UI-Konsistenz, `git diff --check`, `dotnet publish`. Mutationen: Frame-Limiter (Fehler bei Frame 11), Treiber-/Maskierungs-Guard, Benchmarkgrenze, Kategorien, HLG-Akzeptanz, echter C#-Fehler und Null-Test-TRX.

Das Paket kommt aus `dotnet publish`. `Scripts/package-plugin.py` nimmt ausschließlich Plugin, FFMpegCore, CliWrap, Instances, ImageSharp und `meta.json` auf und prüft ZIP-Inhalt/CRC/Bytegleichheit. Assembly- und Metadatenversion sind vor Upload gesondert zu prüfen. Eine lokale MD5 ist noch keine MD5 eines veröffentlichten Assets.

## Server-Abnahme und Auslieferung

Brain wurde über den bereits dokumentierten SSH-Zugang zum NAS gelesen. Für `192.168.178.113` wurde keine konkrete dokumentierte Brain-/Jellyfin-/AI-Service-URL gefunden. Die einmalige Nachfrage nach URL und ursprünglichem RC-Checkout ist offen. Keine Ports oder Zugangsdaten werden geraten.

Vor jeder Änderung am Ziel: Konfiguration und Installation recoverable sichern; Service/Plugin kontrolliert aktualisieren; Jellyfin neu starten und Webclient hart neu laden. Danach `/health/detailed`, `/gpu-verify`, Modellladen und echte Inferenz; mindestens fünf Minuten Server-AI ohne Frame-11-Sperre; 429/503 mit Erholung; Treiber-Guard und Maskierung im echten Player. PQ-Referenzclip: Transfer, Primaries, Bit-Tiefe, statische Metadaten, Framezahl, Laufzeit und sichtbares Bild; HLG verständlich abweisen.

Der Nutzer hat das Committen und Pushen des geprüften RC-Branches vor der Server-Abnahme ausdrücklich freigegeben. Die übrige Auslieferung bleibt an die Server-Gates gebunden: bestehende Tags/Releases prüfen; Docker-Workflow mit Version `1.8.3.31` gemäß `CLAUDE.md` manuell ausführen (keine konkurrierenden latest-Runs); geprüftes ZIP manuell veröffentlichen. Veröffentlichtes ZIP herunterladen und dessen reale MD5 identisch in alle drei Feeds übernehmen; Feeds committen/pushen; `pwsh Scripts/verify-release.ps1` vollständig gegen GitHub. Erst danach Issue #79 mit Version, Umgebung, Dauer und Ergebnissen kommentieren und schließen.

## Nachprüfung nach aktuellem GitHub-Scan — 15.09.2026

- Der öffentliche Scan enthält weiterhin genau ein offenes Issue: [#79](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/79). Die neue [Discussion #80](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions/80) wurde als Integrationsfehler behandelt: Docker-Fallbacks in Bibliotheks-/Multi-Frame-Jobs werden nicht mehr als lokale Verarbeitung oder Erfolg verborgen, sondern mit Status und Fehlerdetail abgebrochen. [Discussion #11](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions/11) beschreibt bereits vorhandene Objektmaskierungs-/Detektor-Funktionen.
- Der zusätzliche C#-Regressionstest läuft mit **403 bestanden, 0 fehlgeschlagen, 0 übersprungen**; TRX-Validator: 403 ausgeführt und bestanden. `dotnet publish` und Paketprüfung liefen erneut erfolgreich. Das aktuelle lokale ZIP ist **1.857.981 Bytes**, MD5 **`2de2e430c77d127e84538b215f3c0fec`**.
- Node-Verhaltenstests: **2 Testdateien bestanden**. Der erneute Python-Lauf mit der projektbezogenen Umgebung hing bereits beim ersten FastAPI-Testfixture; nach 120 Sekunden wurde er abgebrochen. Der frühere vollständige Lauf vom 07.09.2026 bleibt mit 177 bestandenen Tests dokumentiert; ein neuer grüner Python-Lauf ist vor Veröffentlichung erneut erforderlich.

## Bewusst außerhalb dieses Patch-Releases

A3/A2, B1/B2, H2/H3, C1–C3 und D1/D2 bleiben Brain-Tracks in dokumentierter Abhängigkeitsreihenfolge. Keine unvollständige RIFE-, ArtCNN-, HDR-Realtime- oder Temporal-VSR-Funktion. Container-/Timeline-Integrität und reale GPU-/HDR-Qualität werden nicht aus Unit-Tests abgeleitet.

## Abschluss der lokalen Prüfung — 07.09.2026

- .NET SDK 9.0.317, C# Release: **402 bestanden, 0 fehlgeschlagen, 0 übersprungen**; TRX mit 402 tatsächlich ausgeführten Ergebnissen akzeptiert.
- Python 3.12 im projektbezogenen venv: **177 bestanden**, zwei Deprecation-Warnungen aus Starlette/httpx und AnyIO; keine übersprungenen Tests.
- Node 20.18.1: **25 bestanden**, keine Fehler/Skips. Die Tests führen die echte Player-Implementierung in einer kontrollierten Browser-Umgebung aus.
- TRX-Validator: zwei Python-Testmethoden samt ungültigen Eingabefällen grün. UI-Konsistenz: 138 Konfigurationsreferenzen und sieben Skripte. `git diff --check` grün.
- Alle acht Source-/Testmutationen erkannt; zusätzlich tatsächlicher C#-Fehler-TRX und Null-Test-TRX vom Validator abgelehnt. Frame-Limiter-Mutation: Ablehnung exakt bei Frame 11. Mutationen bytegleich entfernt und alle Suiten danach erneut grün.
- `dotnet publish JellyfinUpscalerPlugin.csproj -c Release` erfolgreich. Tatsächliche Assembly- und File-Version der Plugin-DLL: **1.8.3.31**; `meta.json` stimmt überein. Alle fünf Runtime-Assemblies aus den Publish-Abhängigkeiten im ZIP vorhanden, dazu ausschließlich `meta.json`.
- Lokales ZIP: **1.858.482 Bytes**, MD5 **`2de8fd1142e1c741a7534393dbe90dfb`**. CRC und Bytegleichheit gegenüber Publish geprüft. Zusätzliche Paketprüfung weist fehlendes Instances.dll und falsche Metaversion ab; unerwünschte PDB-/Testdateien werden nicht aufgenommen. Dies ist die MD5 des lokalen RC, kein veröffentlichter Asset-Hash.
- Synthetischer lokaler FFmpeg-Transporttest: 10 RGB16-PNGs, rekonstruierter Clip 10 Frames/1,000 s, `yuv420p10le`, `smpte2084`, `bt2020`, Mastering- und Content-Light-Metadaten erhalten. Keine AI-Inferenz und keine HDR-Display-Abnahme in diesem Test.
- Bestehende Auth-Tests luden bei erfolgreicher Authentifizierung echte Modelle herunter. Nur dieser Download wurde isoliert; die Tests verlangen jetzt HTTP 200 und den tatsächlichen Aufruf der Downloadfunktion. Hardware-Erkennungstests isolieren den Fall „keine GPU“ auch gegenüber vorhandenen sysfs-Geräten.
- GitHub live: nur #79 offen; Tag und Release `v1.8.3.31` nicht vorhanden. Commits und RC-Branch-Push sind nun ausdrücklich freigegeben. Docker-Publish, Release und Feed-Update bleiben ausstehend, solange die vorgegebenen Server-Gates offen sind. Issue #79 bleibt offen.

Testergebnisse liegen im Schwesterverzeichnis `../local-validation/` und im ignorierten `TestResults/`. Das lokale Paket liegt unter `../local-validation/JellyfinUpscalerPlugin-v1.8.3.31.zip`. Diese Artefakte gehören nicht in einen Commit.

**Serverergebnisse:** Brain-Dateien über dokumentiertes NAS-SSH gelesen. Auf `192.168.178.113` wurden mangels dokumentierter URLs keine HTTP-/Health-/GPU-Anfragen gestellt und keine Installation verändert. Backup, Deployment, Neustart, harter Client-Reload, Modellladen, echte Inferenz, fünf Minuten Wiedergabe, 429/503-Erholung, Treiber-Guard, Maskierung und PQ/HLG-Clipprüfung sind auf dem Zielserver **nicht durchgeführt**. Weder Erreichbarkeit noch GPU/HDR-Funktion sind damit verifiziert. Die konkrete Brain-/Jellyfin-/AI-Service-URL und der passende dokumentierte Zugang fehlen; die einmalige Nachfrage bleibt unbeantwortet.

## Freigegebener Branch-Push — Zugriffsblocker

Der Nutzer hat den Push von `update/v1.8.3.31` ausdrücklich beauftragt. Der RC wird in vier logischen lokalen Commits gesichert. Die verbundene GitHub-App authentifiziert den Repository-Eigentümer, verweigert das Erstellen des Git-Trees jedoch mit HTTP 403 (`Resource not accessible by integration`). Lokal ist `gh` nicht angemeldet; ein nichtinteraktiver HTTPS-Push findet keine Anmeldung. Die vorhandenen SSH-Schlüssel werden ebenfalls nicht akzeptiert. Es wurden keine Zugangsdaten ausgegeben oder Änderungen auf dem Remote-Branch veröffentlicht. Für den Push muss ein vorhandener GitHub-Zugang mit Schreibberechtigung bereitgestellt werden.
