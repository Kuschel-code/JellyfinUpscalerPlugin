# v1.8.3.31 — reguläre Veröffentlichung

Stand: 17.09.2026. Der Nutzer hat die Server-Abnahme ausdrücklich übersprungen. Zielhardware, echter Jellyfin-Player und HDR-Referenzclips sind nicht abgenommen.

## Geprüfter Stand

- Release-Code und Testfix: `73116679bda94b54943a6d27d3d20dc9c0b27f43`.
- Docker-Quellen: `b5f6e3192fab1c4d148c10909d16fffff981a61f`. Die nachfolgende Änderung betrifft ausschließlich einen isolierten C#-Test und Dokumentation; der Produkt-/Service-Code ist unverändert.
- Lokal: 443 C#-Tests, 209 Python-Tests, 36 Node-Verhaltenstests; TRX, UI-Konsistenz, Publish und Paketprüfung bestanden. Python: zwei Deprecation-Warnungen.
- [Finale CI](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/actions/runs/35227968034): erfolgreich.
- [Regulärer Docker-Workflow](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/actions/runs/35227393972): sieben Jobs erfolgreich, keine konkurrierende Veröffentlichung.
- Docker-Hub-Prüfung: alle sieben Versions-Pins stimmen mit den zugehörigen `docker7`- und `docker7-v1.8.3.31`-Tags überein; NVIDIA auch mit `latest`. Zehn Plattform-Builds mit Version 1.8.3.31 und Revision b5f6e31; NVIDIA setzt standardmäßig SKIP_TENSORRT=true. Dies ist kein GPU-Laufzeittest oder Beleg für CVE-Freiheit.

## Docker-Registry-Digests

| Backend | Version-Pin | Index-Digest |
|---|---|---|
| nvidia | `v1.8.3.31` | `sha256:c7eba9faaa1bb6a945575e1987eb1a0991c7b02bfd1f50a094e440b87fbc50e2` |
| amd | `v1.8.3.31-amd` | `sha256:6c7954f64a24869a28b59ecf8021ab4d41f9153012e8b8a37694160a06e0071b` |
| intel | `v1.8.3.31-intel` | `sha256:f5e11c8284ed5f9c2e9eb35b26aeecb2155a77fceb3a26839ae1dbd87a8a7425` |
| apple | `v1.8.3.31-apple` | `sha256:8461981104eaba9cf3fc2fff8794fb06a086e61bdc857c5c8f65f94e7c058192` |
| vulkan | `v1.8.3.31-vulkan` | `sha256:80b5eedd3bdf28065cb1711624f1b6160437ff33266ec04d07f8c140cef405f5` |
| cpu | `v1.8.3.31-cpu` | `sha256:a24f8d8ffde779636d1e5153d59ad5d14efbc6e67b18fd08395c0fec59958638` |
| converter | `v1.8.3.31-converter` | `sha256:2fa2fd015f97d1ccfd023f0495923d78db2c3a3a4e2540df1c85934d65229280` |

## ZIP

Aus `dotnet publish`, genau fünf Runtime-DLLs und meta.json. Assembly-Identitäten und Version 1.8.3.31 geprüft. MD5: `d9c090dbbb85aba857650156204ec420`. Keine PDBs, Test-DLLs, deps.json oder lokalen Testartefakte.

Das veröffentlichte GitHub-ZIP wurde am 22.09.2026 erneut heruntergeladen, bytegleich verglichen und mit derselben MD5 bestätigt. Release: https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/tag/v1.8.3.31. Die identischen Feed-Einträge sind auf dem Arbeitsbranch vorbereitet; Main-Merge/Feed-Auslieferung und die abschließende Online-Verifikation stehen bis zur konkreten Merge-Freigabe aus. Issue #79 bleibt bis dahin offen.
