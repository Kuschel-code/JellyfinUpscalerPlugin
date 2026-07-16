# Plan v1.9 — Settings-Redesign · Import-/Face-Restore-Bugs · Erweiterungs-Roadmap

**Stand: 2026-07-16 · Basis: v1.8.3.9 (Plugin + alle 7 Docker-Images im Lockstep) · Status: NUR PLAN, nichts umgesetzt**
*Diagnose-Befunde in diesem Dokument sind live auf dem Testserver (TrueNAS, docker7-converter) erhoben.*

---

## Teil 1 — Settings-Redesign: neue Aufstellung, besser sortiert, gleiches Design

### 1.1 Ist-Analyse (gemessen an `Configuration/configurationpage.html`)

- 7 Tabs: `Dashboard · Settings · Jobs · Cache · System · Filters · API Tokens`
- Der **Settings-Tab ist ein Monster**: 11 zugeklappte `<details>`-Blöcke (Docker AI Service, Upscaling, Hardware, Player Integration, Auto Model Selection & Fallback, Library Scan, Processing Queue, Webhook Notifications, Real-Time Upscaling, Features, Backup & Restore) **plus** 5 große Modell-Karten (Model Catalog, Import Community Model, ★ Favorites, AI Comparison View, Live Model Benchmark) — zwei völlig verschiedene Themenwelten in einem Tab.
- **19 Checkboxen** (soll laut Vorgabe: keine Häkchen), **10 Zahlen-Inputs** (Kandidaten für Slider).
- Veralteter Hardcode: Kartentitel **„Model Catalog (35 Models)"** — der Katalog hat 76 Einträge (Titel dynamisch machen).
- Face-Restore-Konfiguration hängt im Filters-Tab, die Face-*Modelle* aber logisch zur Modellverwaltung.

### 1.2 Design-Prinzipien (Vorgaben)

1. **Gleiches Design** — das bestehende Operator-Console-Theme (Karten, Chips, KPI-Tiles, Blau-Akzent) bleibt 1:1; nur Struktur + Controls ändern sich.
2. **Slider und Toggles statt Häkchen** (explizite Vorgabe):
   - Alle 19 Checkboxen → **Toggle-Switches** (iOS-Stil, CSS-only): das darunterliegende `<input type="checkbox">` mit **unveränderter ID** bleibt bestehen, nur visuell als Switch gerendert → Save/Load-JS und der UI-Konsistenz-Check bleiben unberührt.
   - Die 10 Zahlenfelder → **Slider** (`<input type="range">` mit Live-Wertanzeige + kleinem Zahlenfeld daneben für Präzision), sinnvolle min/max/step je Feld (z. B. ScaleFactor 1–8, CacheSizeMB 512–65536 step 512, CpuThreads 1–32, MaxConcurrentStreams 1–8).
3. **Ein Thema = ein Ort**: Modellverwaltung raus aus „Settings".

### 1.3 Neue Tab-Aufstellung (Soll)

| Tab | Inhalt (Karten in dieser Reihenfolge) | Herkunft |
|---|---|---|
| **Dashboard** | unverändert (Status, Jobs, Performance) | wie heute |
| **Models** *(NEU)* | 1. Model Catalog (Titel dynamisch „(N models)") · 2. ★ Favorites · 3. Import Community Model (inkl. Suche + File-Install) · 4. Face-Restore-Modelle (Download/Load) · 5. Live Model Benchmark · 6. AI Comparison View | aus Settings + Filters gezogen |
| **Settings** *(verschlankt, 5 Gruppen statt 11+5)* | 1. **Connection** (Docker AI Service + Hardware/GPU) · 2. **Upscaling** (Model/Scale/Quality + Auto-Selection & Fallback) · 3. **Playback** (Player Integration + Real-Time Upscaling) · 4. **Library Scan** (inkl. Bibliotheks-Picker, Thresholds, Codec) · 5. **Advanced** (Queue · Webhooks · Features · Backup & Restore) | Umgruppierung |
| **Jobs / Cache / System / Filters / API Tokens** | unverändert; Filters behält die *Anwendungs*-Einstellungen von Face Restore (Enable, Strength), die *Modell*-Verwaltung wandert zu Models | minimal |

Bonus (klein, hoher Nutzen): **Einstellungs-Suchfeld** oben im Settings-Tab, das Gruppen/Zeilen live filtert (gleiche Mechanik wie die Import-Suche).

### 1.4 Leitplanken für die Umsetzung (Pflicht)

- **Keine ID ändert sich** — nur Elemente verschieben/umhüllen. Gates: `check_ui_field_consistency.py` + `node --check` + `dotnet build`.
- Save-/Load-Handler (`saveConfig`/`loadConfig`) bleiben unangetastet; Toggle/Slider sind reine Darstellungsschicht über den bestehenden Inputs.
- Jellyfin-Fragment-Regel beachten: alles CSS/JS bleibt im `data-role="page"`-Div; in-page Tabs bleiben `<button type="button">`.
- Abschluss-Gate: Playwright-Live-Test auf dem Testserver (jeden Tab öffnen, Save-Roundtrip, Feld-Zählung gegen Vorher-Snapshot).

**Aufwand:** ~1 Tag (0,5 Umgruppierung + Models-Tab, 0,25 Toggle/Slider-CSS+Wiring, 0,25 Tests/Live-Verifikation). Release als **v1.9.0** (plugin-only möglich, Docker-Lockstep-Dispatch trotzdem).

---

## Teil 2 — Bug-Paket: „Import zeigt dauerhafte rote Meldungen"

### 2.1 Live-Diagnose-Befund (heute, Testserver)

| Test | Ergebnis |
|---|---|
| Picker-Zustand | 670 Optionen, **286 installierbar** (46 direct + 240 convertible), kein Fehler im Leerlauf |
| One-Click-Import (direct) über die UI | ✅ grün („sha256 verified — pinned to ★ Favorites") |
| Convert (.pth) über die UI | ✅ grün (HurrDeblur, ~10 s) |
| **NC-Modell auswählen** | ⚠️ **Dauerhaft ROTE Meldung** `⚠ CC-BY-NC-SA-4.0 - NON-COMMERCIAL license…` (Farbe `#f87171` = identisch mit echten Fehlern) |

**Kernbefund:** Der Import-Mechanismus funktioniert. Die „dauerhaften roten Meldungen" sind mit hoher Wahrscheinlichkeit die **NC-Lizenz-Warnung** — sie erscheint bei fast der Hälfte der Anime-Modelle, bleibt stehen, ist rot und damit von einem Fehler nicht unterscheidbar. Ein Design-Bug, kein Funktions-Bug.

### 2.2 Maßnahmen

1. **NC-Warnung entrot-en**: amber/gelb (`#fbbf24`), Wortlaut ergänzen: *„Import trotzdem möglich — nur nicht für kommerzielle/öffentliche Dienste."* Rot bleibt exklusiv für echte Fehler.
2. **Fehlertexte mit nächstem Schritt**: jede rote Meldung nennt Ursache + Handlung („Service nicht erreichbar → URL in Connection prüfen", „Timeout → Modell ist groß, erneut versuchen oder File-Install nutzen").
3. **Import/Convert asynchron machen** (größter echter Rot-Kandidat): große ESRGAN-Converts (60+ MB .pth, CPU) können die 570-s-Proxy-Kette reißen → Umbau auf Job-Muster (`import-async` + Status-Poll, analog `/models/download-async`), UI zeigt Fortschritt (Download % → Convert → Validate) statt eines langen gelben Freeze.
4. **Favorites-Karte Refresh-Reihenfolge**: direkt nach Import zeigt die Karte kurz „not on the service" (Models-Liste war beim Render noch alt) → Render erst nach abgeschlossenem `loadModels()`.
5. **Rückfrage an den Betreiber** (falls die rote Meldung *nicht* die NC-Warnung war): exakten Wortlaut + Modellname notieren — Feld dafür ist in der Diagnose-Checkliste unten.

**Aufwand:** Punkte 1/2/4 ≈ 2 h · Punkt 3 ≈ 0,5–1 Tag (Service-Job + UI-Poll). Punkte 1/2/4 können als schneller v1.8.3.10-Hotfix vorgezogen werden.

---

## Teil 3 — Bug-Paket: „Face-Restore-Modelle lassen sich nicht herunterladen/laden"

### 3.1 Diagnose-Befund

- **Alle 4 Download-URLs live OK** (HEAD-geprüft): gfpgan-v1.4 = 325 MB, codeformer = 359 MB, gpen-512 = 271 MB, restoreformer++ = 281 MB → **keine toten Links**.
- Wahrscheinlichste Ursachen auf der CPU-Box: (a) 325–359-MB-Download + ONNX-Session-Load reißt die Timeout-Kette (570 s Proxy / 600 s UI) auf langsamer Leitung/Disk; (b) **RAM**: GFPGAN-Session auf einem schwachen Server kann beim Load OOM-sterben — die UI zeigt dann nur „Load failed".

### 3.2 Maßnahmen

1. **Diagnose zuerst** (30 min): Load auf dem Testserver auslösen und Container-Logs mitschneiden (`docker logs`) — Timeout vs. OOM vs. anderes unterscheiden.
2. **Async-Download auch für Face-Modelle**: der seit v1.8.2 existierende Hintergrund-Downloader (`/models/download-async` + Status-Poll) wird für `face_restore`-Kategorie verdrahtet; UI bekommt Fortschrittsanzeige statt Minuten-Freeze.
3. **Klare Fehlerdifferenzierung**: „Download-Timeout" / „zu wenig RAM (Modell braucht ~1–2 GB frei)" / Service-Fehlertext durchreichen.
4. Optional: kleinere Alternative evaluieren (GFPGAN-onnx-lite/kleinere Auflösungsvariante) für schwache Boxen.

**Aufwand:** Diagnose 0,5 h · Umsetzung ~0,5 Tag (gemeinsam mit Teil 2 Punkt 3 — gleiches Job-Muster).

---

## Teil 4 — Erweiterungs- & Verbesserungsplan (Roadmap)

### v1.9.0 — „Aufgeräumt & robust" (dieses Paket)
1. **Settings-Redesign** (Teil 1: Models-Tab, 5 Settings-Gruppen, Toggles + Slider, Suchfeld)
2. **Import-UX-Fixes** (Teil 2: NC-Warnung amber, Fehlertexte, Favorites-Refresh)
3. **Async Import/Convert/Face-Download mit Fortschritt** (Teil 2.3 + Teil 3)
4. **Quick-Menu-Favoriten im Player** (★-Gruppe/Chip — bei v1.8.3.7 bewusst zurückgestellt)
5. **Importierte Modelle über die UI löschen** (`DELETE /models/upload/{name}` existiert — Unpin+Delete-Knopf in der Favorites-/Models-Karte)
6. Kleinkram: „Model Catalog (35 Models)"-Titel dynamisch; Import-Picker merkt sich Suchtext pro Session

### v1.9.x — Kandidaten (einzeln shipbar)
- **IFRNet-ONNX-Export** (eigener reproduzierbarer Export, Opset ≥17, PSNR-Gate → erste echte RIFE-Alternative; Prompt liegt bereit)
- **Pipeline-Parallelismus GPU-Messung** (Durchsatz-Beweis auf schneller GPU-Box → ggf. default-on via `/recommend`)
- **Adore + Archivist evaluieren** (hauseigenes VMAF-Gate; Archivist nur wenn als Pre-Pass ≤ ~20 Zeilen anbindbar)
- **Benchmark für importierte Modelle** (omdb-Modelle im Live-Benchmark zulassen → Nutzer sieht, was seine Box schafft)

### v2.0 / Trigger-gesteuert
- **Jellyfin 12.0 final** (aktuell RC2): Testmatrix fahren → Kompat-Banner + ggf. zweiter Feed-Eintrag `targetAbi 12.0.0.0`
- **SeedVR2 / FlashVSR** (Video-SR der nächsten Generation) — Trigger: ONNX/TensorRT-Export ODER stabiler 12-GB-Consumer-GPU-Betrieb
- **NPU-Backend** (Ryzen AI / VitisAI-EP) — Trigger: erster Nutzer oder stabiles EP in ORT-Releases
- **main.py-Modularisierung Phase 1** (6.700+ Zeilen; Import/Convert/Katalog nach token_store-Muster extrahieren) — Wartbarkeit
- **CVE-OS-Sweep** (Debian-Paket-Alerts aus den jetzt vollständig laufenden Trivy-Scans auditieren — seit 2026-07-16 scannt auch das 20-GB-AMD-Image komplett)

### Bewusst NICHT (bestehende Entscheidungen, gelten weiter)
DLSS/FSR/XeSS · RTX/AMD VSR einbauen (client-seitig, dokumentiert) · Diffusion-Upscaler (SUPIR-Klasse, img2img-Drift) · sshd zurück ins Image · freie Import-URLs ohne sha256-Pin · gdrive/mega-Download-Automatisierung (AGB/Interaktiv — File-Install deckt es ab) · Recommendation-Endpoint-Konsolidierung (drei verschiedene Funktionen, keine Aliase — dokumentiert)

---

## Umsetzungs-Reihenfolge (Empfehlung)

1. **Hotfix v1.8.3.10** *(2–3 h)*: NC-Warnung amber + Fehlertexte + Favorites-Refresh + Katalog-Titel dynamisch → nimmt sofort die „roten Meldungen" weg.
2. **Face-Restore-Diagnose** *(0,5 h, Testserver)*: Logs beim Load → Ursache festnageln, fließt in Schritt 3.
3. **v1.9.0** *(~2–3 Tage)*: Settings-Redesign + async Import/Convert/Face-Download + Quick-Menu-Favoriten + UI-Delete.
4. v1.9.x-Kandidaten danach einzeln, v2.0 nach Triggern.

**Validierung je Schritt (Standard-Gates):** pytest + xUnit + UI-Konsistenz + `node --check` + Playwright-Live-Test auf dem Testserver + kompletter Release-Zyklus mit `verify-release.ps1` + Docker-Lockstep.

### Offene Diagnose-Checkliste (bitte ergänzen)
- [ ] Exakter Wortlaut der roten Import-Meldung(en), falls es NICHT die NC-Lizenz-Warnung war + betroffene Modellnamen
- [ ] Welche Face-Restore-Modelle wurden probiert (gfpgan / codeformer / gpen / restoreformer++), und erschien die Meldung sofort oder nach ~10 min?
