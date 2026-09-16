# Docker RC v1.8.3.31 — 16.09.2026

Alle sieben Kandidaten wurden über den manuellen [Docker-Workflow](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/actions/runs/35127642208) gebaut und auf Docker Hub gepusht. Die öffentlichen Tag-Abfragen bestätigen dieselben Digests für den jeweiligen RC-Tag und seinen Commit-Pin. Das ist keine Zielhardware-Abnahme.

Image-Repository: `kuscheltier/jellyfin-ai-upscaler`. Docker-Quellcommit: `5c299d281e754239861d2acf72834578e0ab3980`; der Service-Code entspricht dem lokal geprüften Stand `97c0181`. Nachfolgende Änderungen betreffen Dokumentation und CI, nicht die Container-Anwendung.

| Backend | RC-Tag | Plattformen | Image-Index-Digest |
|---|---|---|---|
| nvidia | `rc-v1.8.3.31` | amd64 | `sha256:2dbd1a6d838ac108645a66a840842347c8e70b5088230b3061ca59e7e7fcbfb4` |
| amd | `rc-v1.8.3.31-amd` | amd64 | `sha256:74dba9040ee299687eeb12d2f7da1f10d9865fc99aade3b8fb69ce0d99e735f9` |
| intel | `rc-v1.8.3.31-intel` | amd64 | `sha256:962fb267600e02eccc860da7fe8c0a5cea827ca600bd67d32064f8a67a6a4381` |
| apple | `rc-v1.8.3.31-apple` | amd64, arm64 | `sha256:67625057cd53dc13ea6d63616f9d1e2c83d3a092c902a7d21b6f9a5212b47515` |
| vulkan | `rc-v1.8.3.31-vulkan` | amd64, arm64 | `sha256:85ac7626f5a7724191960a4406a2a002e7ac95cdbc9c37a587eef3d97ca2a6a7` |
| cpu | `rc-v1.8.3.31-cpu` | amd64, arm64 | `sha256:28dcc9999e984db19bb9ae0bdc1cd91c99ec2ff03198c13326d3eb604a1c0ec0` |
| converter | `rc-v1.8.3.31-converter` | amd64 | `sha256:9b415e84b6e2b374991ea52acea876cd4be8787ea5dd9a6043ce89e453f6d515` |

Commit-Pins: `rc-v1.8.3.31-5c299d2` für NVIDIA; derselbe Präfix plus `-amd`, `-intel`, `-apple`, `-vulkan`, `-cpu` oder `-converter` für die übrigen Backends.

Die regulären Tags `latest`, `docker7` und `docker7-cpu` wurden zusätzlich abgefragt: Sie zeigen weiterhin die Veröffentlichung vom 16.08.2026. Der RC hat keine regulären Update-Tags verschoben. Der AMD-Trivy-Nachlauf war beim Registry-Abgleich noch aktiv; die sieben Image-Pushes waren bereits abgeschlossen. Der verlinkte Workflow enthält den endgültigen Jobstatus.

## Lokal geprüft

- Docker Desktop 4.91.0, Engine 29.8.0, Linux amd64; CPU-Build erfolgreich, `pip check` ohne Konflikte.
- Health, `/health/detailed`, `/gpu-verify`, `/status`, echter FSRCNN-x2-Download und Modellladen erfolgreich.
- 286 echte Frame-Inferenzen in 301,77 Sekunden: HTTP 200 und 256×144 Pixel aus 128×72 Eingabe; auch Frame 11 erfolgreich.
- 48 gleichzeitige Testaufrufe: 43×503 mit `Busy` und `Retry-After: 1`; nach Wartezeit wieder 200. Als HLG deklarierte Realtime-Anfrage: 422.
- Docker-Tag-Logik: vier Tests, 14 Workflow-Kombinationen und zwei erkannte Mutationen. Feed-Audit: acht Tests und drei erkannte Mutationen; echte ZIP-/Assembly-Prüfung mit 17 Fällen.
- Website: zehn geänderte Seiten im Browser geladen, keine JavaScript-Seitenfehler; Desktop-/Mobilansicht kontrolliert.

## Offen vor regulärer Freigabe

Jellyfin-Zielserver `192.168.178.113`: dokumentierte URLs und administrativer Zugang fehlen weiterhin. Deshalb noch keine Installation/Sicherung/Neustart, kein echter Player-Dauertest, keine 429-Proxy-Erholung, Treiber-/Maskierungs-Abnahme oder PQ-/HLG-Referenzclipprüfung auf Zielhardware. Lokale CPU-Inferenz prüft diese Pfade nicht.

Plugin-Release und drei Feeds werden erst nach dieser Abnahme mit dem tatsächlichen veröffentlichten ZIP aktualisiert. Issue #79 bleibt bis dahin offen. Details: [Release-Plan](RELEASE-PLAN-v1.8.3.31.md).
