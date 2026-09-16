# Code-, Issue- und Modellprüfung — 16.09.2026

Arbeitsstand: Release Candidate 1.8.3.31 auf `update/v1.8.3.31`. Dieser Bericht dokumentiert einen Review und lokale Regressionen; er ist keine Bestätigung aller historischen Hardwaremeldungen.

## Öffentliche Meldungen

Die GitHub-API liefert 56 Issues (ohne Pull Requests); nur [#79](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/79) ist offen. Die zehn Discussions wurden einschließlich ihrer verfügbaren Kommentare erfasst und am 16.09.2026 erneut vollständig über GraphQL abgeglichen (keine weiteren Seiten, keine neue Discussion; letzte Änderung #80 am 14.09.2026). Geschlossene Meldungen wurden als Regressionsthemen berücksichtigt, nicht pauschal als auf dieser Maschine reproduziert bezeichnet.

- #79: Frame-/Chunk-Aufrufe außerhalb des Bildaktionslimits; Player-Single-Flight, Backoff, Retry-After, Abbruch und Wiederanlauf werden lokal getestet.
- [Discussion #80](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions/80): bestätigte Codefehler bei verstecktem CPU-/Originalframe-Fallback und fortlaufender Batch-Verarbeitung nach einem AI-Fehler korrigiert. FFmpeg-Decoding und Encoding laufen weiterhin auf dem Jellyfin-Host; CPU-Last allein beweist deshalb keinen Fehler. Die Umgebung des Melders wurde nicht reproduziert.
- #74 und frühere Installations-/Prüfsummenmeldungen: beide Build-Workflows, Publish-Paket und manueller Release-Validator geprüft. Der Validator prüft nun exakten ZIP-Inhalt, tatsächliche Assembly-/File-Version, Plugin-GUID und identische Feed-Einträge.
- #44/#46 und #71: TensorRT ist ausdrücklich optional; Provider-Verfügbarkeit ist kein Nachweis aktiver GPU-Inferenz. Hosttreiber, Bibliotheken und Durchreichung bleiben Zielhardware-Gates.
- #67: vorhandene Tests für den tatsächlichen ONNX-Eingabetyp behalten.
- #75/#76/#77: veröffentlichte Rückmeldungen beschreiben Webroot-Rechte, Repository-Cache und bereits korrigierte Toast-Stapelung. Keine erneute Zielclient-Abnahme behauptet.
- Discussions [#10](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions/10), [#13](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions/13): Setup/Client-/Remote-Fragen; heutiger AI-Dienst benutzt HTTP, keinen SSH-Transcoder.
- Discussion [#11](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions/11): Detektor-/Maskierungsfunktionen getrennt von Upscalern halten.
- Discussions [#12](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions/12), [#16](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions/16), [#19](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions/19): historische Ankündigung/Nutzbarkeit/Kompatibilität; aktuelles Ziel bleibt Jellyfin 10.11.8/net9.
- Discussions [#35](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions/35), [#38](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions/38): AMD-Support/Leistung erfordert echte AMD-Abnahme.
- Discussion [#68](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions/68): Unraid-Paketierung ist ein eigener Wunsch, kein bestätigter Defekt im RC.

## Zusätzliche bestätigte Defekte

- Player: verspätetes Modellladen und verzögerter Autostart konnten nach Stop oder Navigation neu starten; Modellwechsel umging HDR-/Benchmark-Prüfungen; suspendierte Pausentimer verursachten falschen Timeout.
- Batch: eine Fehlantwort konnte weitere CPU-Fallbacks, Originalkopien oder ausgelassene Frames erzeugen. Abbruch propagiert nun zu laufenden Requests; leere/beschädigte Bilder, wechselnde Maße und unvollständige Sequenzen werden abgelehnt.
- Native Modellskalierung: der Realtime-Dateiencoder vertraute der konfigurierten Skala statt der tatsächlich gelieferten Bildgröße. Er wird jetzt erst nach der ersten dekodierten AI-Antwort angelegt. Bekannte Skalen werden auch nach Modell-Fallback aktualisiert.
- Modelle: Loader und Benchmark-Auswahl konnten RIFE/Face-/Detektormodelle als Upscaler verwenden. Eine inkompatible gespeicherte Skala konnte den HTML-Select leeren. Unbekannte importierte Upscaler bleiben zulässig.
- HDR: fehlende Transfer-/Primaries-Angaben werden abgelehnt. Die Rekonstruktion vermischte PQ-Codewerte und lineare Luminanz; neutrale synthetische Testpixel zeigen den Fehler. Die Korrektur ist keine HDR-Displayfreigabe. Schwarzer AI-Output wird nicht mit hellen Originalpixeln ersetzt.
- Dienst: ein eigener Uploadfehler 413 wurde zu 500 umgewandelt. Abgewiesene Half-open-Proben konnten die Circuit-Breaker-Erholung sperren; 503 erhält Retry-After und Probe-Besitz wird an den Request gebunden.
- Import: Python und C# lasen Downloads vollständig ein, bevor sie das Größenlimit prüften. Begrenzung erfolgt nun während des Empfangs. Konvertierungsprüfung lehnt NaN/Inf und abweichende Tensorformen ab; Dateiendungen für Safetensors/TorchScript bleiben erhalten.
- Docker-Veröffentlichung: veralteter Versionsdefault entfernt, Versionsangabe muss zum Checkout passen, globale Serialisierung der Rolling-Tags; Build-only exportiert keinen Registry-Cache.

## Aktuelle Modellquellen

Der bestehende Importkatalog enthält **62 direkte ONNX-Einträge und 609 Einträge zur Konvertierung**. Das sind Katalogeinträge, keine auf der Zielhardware freigegebenen Modelle.

| Quelle | Befund | Entscheidung für 1.8.3.31 |
|---|---|---|
| [OpenModelDB, letzte Änderung der Modelldaten](https://github.com/OpenModelDB/open-model-database/commit/233259dbb522ad8841331a8995fef884c9bf7ef9) | Letzte ermittelte Änderung vom 10.08.2026 korrigiert Downloadlinks/Hashes; Sonic-Modelle vom 01.08., Adore, Fallin und Archiver sind bereits im vorhandenen Katalog. | Keine neuen Einträge durch Namensraten oder unüberprüfte Gewichte hinzufügen. Der Main-Refresh vom 14.09. ändert nur das Generierungsdatum. |
| [mpv-AnimeJaNai 3.5.0](https://github.com/the-database/mpv-AnimeJaNai/releases/tag/3.5.0) | Veröffentlichung vom 18.06.2026 betrifft den Player samt Backend-/RIFE-Komponenten. | Kein direkt übertragbarer Beleg für ein neues normales Jellyfin-Upscaler-Modell. |
| [ArtCNN](https://github.com/Artoriuz/ArtCNN) | Neue Deinterlacer-/Restorer-/Dehalo-Experimente im September. ArtCNN unterscheidet Luma- und Chroma-Verarbeitung mit eigenen Farbraumannahmen. | Bleibt im dokumentierten Brain-Track; keine unvollständige RGB-/HDR-Integration im Patch. |
| [Spandrel-Loader](https://github.com/chaiNNer-org/spandrel/blob/main/libs/spandrel/spandrel/__helpers/loader.py) | Dateiendung bestimmt PTH-, TorchScript- oder Safetensors-Parser. | Bestehenden Import reparieren; neue Architekturunterstützung nicht aus der Modellbezeichnung ableiten. |

B0 verwendet weiterhin sämtliche `available:false`-Einträge aus `Resources/models-fallback.json` (aktuell 17). RIFE, Face-Restore und Detektoren gehören zu ihren eigenen Verarbeitungspfaden.

## Issue-Inventar

Der Status stammt aus GitHub zum Scanzeitpunkt. „Geschlossen“ ist der Repository-Status, kein zusätzlicher Live-Test durch diesen Review.

| Issue | Titel | GitHub-Status |
|---|---|---|
| [#79](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/79) | Server AI upscaling hits hard-coded 10 requests/minute limit | offen |
| [#77](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/77) | UI notifications are stacked incorrectly | geschlossen |
| [#76](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/76) | Repository not working in Jellyfin | geschlossen |
| [#75](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/75) | The player button is not being injected | geschlossen |
| [#74](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/74) | Updating from the Plugin catalog not working | geschlossen |
| [#73](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/73) | SSH remote transcoding wiki appears outdated for docker7 image | geschlossen |
| [#72](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/72) | Active Jobs stick at 95% | geschlossen |
| [#71](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/71) | Api token | geschlossen |
| [#70](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/70) | Linux Subsystem wsl2 | geschlossen |
| [#69](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/69) | Intel Arc A310, Aspect Ratio & Non-admin users | geschlossen |
| [#67](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/67) | ONNX models failing because of wrong input type | geschlossen |
| [#66](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/66) | Docker image and Wsl2 subsystem linux in windows 11 | geschlossen |
| [#65](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/65) | No newer version available | geschlossen |
| [#64](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/64) | Feature:  Select Library | geschlossen |
| [#63](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/63) | Cannot install plugin | geschlossen |
| [#62](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/62) | Plugin ver 1.5.5.4 Unable to connect to Docker AI Service | geschlossen |
| [#59](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/59) | docker5-amd missing? | geschlossen |
| [#58](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/58) | Upscaler ver 1.5.5.8 Not Supported on Jellyfin ver 10.11.8 | geschlossen |
| [#57](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/57) | Checksum Validation Mismatch During Jellyfin Plugin Installation | geschlossen |
| [#54](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/54) | Unable to install latest plugin version/old plugin version unable to connect to docker | geschlossen |
| [#52](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/52) | An error occurred while installing the plugin. | geschlossen |
| [#51](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/51) | Checksum Error for v1.5.5.0 | geschlossen |
| [#49](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/49) | Vulkan support? | geschlossen |
| [#48](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/48) | Plugin is very far from being usable | geschlossen |
| [#47](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/47) | plugin not supported | geschlossen |
| [#46](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/46) | RTX A2000 ERROR random | geschlossen |
| [#45](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/45) | Intel ARC A380 | geschlossen |
| [#44](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/44) | Running docker plugin on truenas with nvidia | geschlossen |
| [#43](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/43) | 1.5.1.0 - Checksum doesn't match | geschlossen |
| [#42](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/42) | 1.5.0.8 - FFMPEG crash when trying to play & No AI Models listed | geschlossen |
| [#39](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/39) | 1.5.0.2 - "Save configuration" in JellyfinUpscalerPlugin does not work, unable to test functionality | geschlossen |
| [#37](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/37) | 1.5.0.2 - Checksum mismatch while trying to install via Jellyfin | geschlossen |
| [#36](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/36) | Unable to connect jellyfin plugin to container | geschlossen |
| [#34](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/34) | Unable to Enable Plugin | geschlossen |
| [#33](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/33) | 1.5.0.0 fails to install | geschlossen |
| [#32](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/32) | Is OpenVINO support planned? | geschlossen |
| [#30](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/30) | Status: v1.4.9/v1.5.0 Docker Test / Info/Patches | geschlossen |
| [#29](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/29) | 1.4.9 fails install | geschlossen |
| [#28](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/28) | 1.4.8 Release zip file not on release page | geschlossen |
| [#27](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/27) | Still not seeing the settings option on plugin page | geschlossen |
| [#26](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/26) | Activ but still not working? | geschlossen |
| [#25](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/25) | Still an Error on Version 1.4.4 | geschlossen |
| [#24](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/24) | 1.4.1 will not install | geschlossen |
| [#18](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/18) | Hello? Is anyone there? | geschlossen |
| [#17](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/17) | Community | geschlossen |
| [#15](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/15) | Catalog doesnt load when repository is pasted in | geschlossen |
| [#14](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/14) | Catalog infinite loop | geschlossen |
| [#9](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/9) | Checksum mismatch v1.6.3.2 | geschlossen |
| [#8](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/8) | Unable to save plugin settings? | geschlossen |
| [#7](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/7) | Install by Adding Repo URL fails | geschlossen |
| [#6](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/6) | Plugin Not Installable from Catalog + Malfunctioned After Manual Install | geschlossen |
| [#5](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/5) | mb | geschlossen |
| [#4](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/4) | error with latest version | geschlossen |
| [#3](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/3) | Cannot Add Plugin | geschlossen |
| [#2](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/2) | Invalid checksums from repo? | geschlossen |
| [#1](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues/1) | Repo don't work | geschlossen |

