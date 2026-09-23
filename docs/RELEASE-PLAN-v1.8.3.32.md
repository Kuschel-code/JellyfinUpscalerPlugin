# v1.8.3.32 — Native Jellyfin 12

Stand: 23.09.2026. Neuer Arbeitsbranch `update/v1.8.3.32`; das veröffentlichte v1.8.3.31-Asset bleibt unverändert. Nutzerauftrag: auf Jellyfin 12 aktualisieren und insbesondere Docker auf konsistenten Update-Stand prüfen.

## Änderungen

- Plugin und Testprojekt: `net10.0`, Jellyfin.Controller `12.0.0`, Metadaten-ABI `12.0.0`. Mindestversion Jellyfin 12.0; der bisherige 10.11-Build bleibt in der veröffentlichten Version 1.8.3.31 verfügbar.
- Alle drei .NET-CI-Workflows verwenden SDK 10. Keine automatischen Plugin-Release-/Upload-Schritte.
- Kein abweichender Logging-Paketpin im Testprojekt; Jellyfin liefert seine passende transitive Version.
- UserManager-Test verwendet die neue `GetUsers()`-API und prüft den tatsächlichen Aufruf. Der vorhandene Adapter bleibt versionsadaptiv.
- Feed-Prüfung verlangt für einen veröffentlichten neuen Build dessen Metadaten-ABI. Ein Kandidat darf die vorige veröffentlichte 10.11-ABI im Feed beibehalten; zwei neue Verhaltenstests sichern den Übergang.
- Paketprüfung verwendet den net10.0-Publish-Pfad. Versionen, 15 Website-Kopfzeilen, Anforderungen und Server-Compose-Beispiel sind aktualisiert; das Beispiel verwendet Jellyfin 12.1.

## Nachweise

.NET SDK 10.0.401 lokal isoliert installiert. Jellyfin-12.0-Build: 443 C#-Tests und TRX-Validator bestanden. Docker-Service: 209 Python-Tests bestanden, zwei Deprecation-Warnungen. Node: 36 Verhaltenstests bestanden. UI: 138 Referenzen in sieben Skripten konsistent. Feed-Prüfung: zehn Tests bestanden. Publish erfolgreich; 17 Paketprüfungen mit echten DLLs bestanden. Das Paket enthält nur Plugin, FFMpegCore, CliWrap, Instances, ImageSharp und meta.json.

Zusätzlicher Build gegen Jellyfin.Controller 12.1.0 in einer getrennten Quellkopie: ebenfalls 443/443 C#-Tests und TRX-Validator bestanden. Zehn Website-Seiten und die mobile Startseite im lokalen Chrome ohne JavaScript-Seitenfehler geprüft. Lokaler Jellyfin-12.1-Plugin-Ladetest erfolgreich: offizielles Image `sha256:78d3ea1207d1322471fcac39a614f004f2ccf7e878f95ab2977d752f07e4dd7e`, neuer Container ohne Netzwerk oder Hostmounts, darin der gegen 12.0 gebaute Publish. Log bestätigt alle fünf Assemblies und „Loaded plugin: AI Upscaler Plugin 1.8.3.32“, erfolgreiche Script-Injektion und Startup complete nach 27,92 s. `/health` meldet Healthy, `/System/Info/Public` Version 12.1.0. FFmpeg 8.1.2 wird nach der initialen Plugin-Initialisierung aufgelöst. Der erwartete Downloadfehler des Jellyfin-Standardkatalogs entsteht durch die absichtliche Netzwerkisolation; kein Plugin-Ladefehler. Testcontainer anschließend gestoppt. Kein Playback-, Konfigurations-Speicher-, GPU- oder HDR-End-to-End-Test. Zielserver-/GPU-/HDR-/Live-Player-Abnahme bleibt übersprungen und darf nicht aus Build- oder Containerstart-Ergebnissen abgeleitet werden.

## Docker

Alle sieben veröffentlichten 1.8.3.31-Varianten und zehn Plattform-Konfigurationen am 22.09.2026 erneut direkt gegen Docker Hub geprüft: Versions-Pins und docker7-Tags stimmen überein, NVIDIA auch latest; Labels 1.8.3.31 / b5f6e31. CUDA bleibt Standard, SKIP_TENSORRT=true. Der Python-HTTP-Dienst enthält keine Jellyfin-/NET-Abhängigkeit; die Migration verlangt keine Änderung von CUDA/ROCm/OpenVINO oder Modellformaten. GPU-Basisimages werden nicht ohne passende Tests auf andere ABI-Generationen umgestellt.

Am 23.09.2026 als [v1.8.3.32 RC1](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/tag/v1.8.3.32-rc.1) veröffentlicht. Alle sieben Docker-Kandidaten und zehn Plattform-Konfigurationen sind geprüft; [Digests](DOCKER-RC-v1.8.3.32.md). Beim RC blieben die regulären latest/docker7-Tags auf 1.8.3.31; sie wurden erst mit dem regulären v1.8.3.32-Release aktualisiert. Die regulären Feeds erhalten keinen Kandidaten-Eintrag. ZIP-MD5: `ad1548ff1f936cdb2dac5be17b2609bb`. PR #81 und #83 wurden nach ausdrücklicher Zustimmung des Nutzers nach main gemergt. Der vollständige v1.8.3.31-Online-Validator bestand; der reguläre v1.8.3.32-Release und alle sieben Docker-Varianten sind veröffentlicht. Die aktuelle Feed-/ZIP-Verifikation ist im [Publikationsnachweis](RELEASE-v1.8.3.32-PUBLICATION.md) dokumentiert.
