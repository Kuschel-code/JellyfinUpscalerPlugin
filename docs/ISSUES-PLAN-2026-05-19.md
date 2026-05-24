# Issues-Triage & Fix-Plan — 2026-05-19

**Scan-Datum:** 2026-05-19
**Repo-Stand:** `main` @ commit `e470849` (v1.7.3.1 + repository-simple.json hotfix)
**Offene Issues:** 6 (sortiert nach Priorität)

---

## Executive Summary

| # | Status | Prio | Action | Aufwand |
|---|---|---|---|---|
| **#67** | OPEN, **heute** | **P0** | Code-Fix (FP16-Type-Detection) | ~15 LoC |
| **#66** | OPEN, 2d | P1 | Code-Fix (WSL2 `/dev/dxg` Detection) + Doku | ~40 LoC + Doku |
| **#62** | OPEN, 5w | P2 | Comment + Close (v1.5.x obsolet) | 2 min |
| **#63** | OPEN, 5w | P2 | Comment + Close (v1.5.x obsolet, gleicher Cluster wie #62) | 2 min |
| **#64** | OPEN, 4w | P3 | Close (seit v1.6.1.14 implementiert) | 1 min |
| **#49** | OPEN, 7w | P3 | Close (seit v1.5.x als `docker7-vulkan` Image) | 1 min |

**Gesamt-Aufwand für Docker-Patches dieser Session:** ~60 LoC Code + Doku-Update.

---

## P0 — Issue #67: ONNX models failing because of wrong input type

**Reporter:** [@eparrish64](https://github.com/eparrish64), 2026-05-19 (heute)
**Hardware:** NVIDIA, `docker7` tag, frisches Setup
**Symptom:** ALLE ONNX-Models schlagen beim Warmup fehl mit:
```
Warmup failed: [ONNXRuntimeError] : 2 : INVALID_ARGUMENT :
  Unexpected input data type. Actual: (tensor(float16)), expected: (tensor(float))
```

### Root Cause

`docker-ai-service/app/main.py` Zeilen 2322-2326 und 2337-2342:

```python
# Single-frame path (Z.2322-2326)
if state.use_fp16:
    img_batch = img_batch.astype(np.float16)   # blind cast

# Multi-frame path (Z.2337-2342)
if state.use_fp16:
    batch = batch.astype(np.float16)            # blind cast
```

`_resolve_fp16_setting()` (Z.1137-1175) aktiviert FP16 automatisch für jede NVIDIA mit Compute Capability >= 7.0 (Volta+, also alle modernen RTX). Aber:

- Die meisten ONNX-Models im Katalog (Real-ESRGAN, SwinIR, etc.) sind **als FP32 exportiert** und haben Input-Type `tensor(float)`.
- Der blinde FP16-Cast erzeugt einen Type-Mismatch beim ersten `session.run()`.

Bug ist **systemisch** — jeder Warmup auf NVIDIA-Hardware mit nicht-FP16-Model schlaegt fehl.

### Fix

Vor jedem `astype(np.float16)` den Model-Input-Type pruefen:

```python
def _model_expects_fp16() -> bool:
    """Check if loaded ONNX model expects float16 input."""
    if state.current_session is None:
        return False
    try:
        input_type = state.current_session.get_inputs()[0].type
        return input_type == 'tensor(float16)'
    except (IndexError, AttributeError):
        return False

# Then in upscale_image() (single-frame):
if state.use_fp16 and _model_expects_fp16():
    img_batch = img_batch.astype(np.float16)

# And in upscale_video_chunk() (multi-frame):
if state.use_fp16 and _model_expects_fp16():
    batch = batch.astype(np.float16)
```

**Net effect:** FP16-Path bleibt erhalten fuer kuenftige FP16-exported Models. Aktuelle FP32-Models laufen wieder fehlerfrei.

### Tests

- **Regression-Test in `docker-ai-service/tests/test_inference.py`** (neu): Mock ONNX-Session mit `inputs[0].type = 'tensor(float)'`, `state.use_fp16=True`, pruefe dass kein `np.float16`-Cast passiert.
- **Manuell:** nach Patch `gh container run` mit docker7-Tag, Warmup auf real-esrgan-x4 muss durchgehen.

### User-Comm

Sofort-Antwort an @eparrish64 mit Hinweis: Fix in v1.7.4 — bis dahin Workaround `USE_FP16=false` als Env-Var im Docker-Compose.

---

## P1 — Issue #66: Docker image and WSL2 subsystem linux in windows 11

**Reporter:** [@FrRene06](https://github.com/FrRene06), 2026-05-17
**Hardware:** Windows 11 + Docker Desktop + WSL2 + Intel Arc A380
**Symptom:** Dashboard durchgehend "No GPU detected (CPU-only mode)" trotz `intel`/`vulkan`-Image.

### Root Cause

`docker-ai-service/app/main.py:1307-1404` — Intel-GPU-Detection sucht ausschliesslich `/dev/dri/renderD*`. Bei WSL2 ist der Pfad `/dev/dxg` (DirectX-Bridge). Aktuelle Code-Pfade:

- `if render_nodes:` -> `/dev/dri` da -> GPU detected
- `elif ONNX_AVAILABLE and 'OpenVINOExecutionProvider' in ort.get_available_providers():` -> markiert als "Intel OpenVINO (CPU inference only)" — irrefuehrend, weil GPU via `/dev/dxg` tatsaechlich nutzbar waere

`clinfo` wird **bereits aufgerufen** (Z.1330-1342), aber nur als `logger.info(...)`. Output wird nicht als Decision-Input verwendet.

### Fix

Detection-Pfad fuer `/dev/dxg` einfuegen, der existierenden `clinfo`-Output wiederverwendet:

```python
# Inside Intel detection block, BEFORE the `elif ONNX_AVAILABLE ...` branch:
elif Path("/dev/dxg").exists() and clinfo_result and clinfo_result.returncode == 0 \
     and "Intel" in clinfo_result.stdout:
    state.gpu_name = _parse_clinfo_intel_name(clinfo_result.stdout) or "Intel GPU (WSL2)"
    state.gpu_memory = "Shared (DXG)"
    state.gpu_list.append({
        "index": 0,
        "name": state.gpu_name,
        "memory": "Shared (DXG)",
        "type": "intel-wsl2",
        "render_node": "/dev/dxg"
    })
    gpu_detected = True
    logger.info(f"Detected Intel GPU via WSL2 /dev/dxg: {state.gpu_name}")
```

Plus Helper:

```python
def _parse_clinfo_intel_name(clinfo_output: str) -> str | None:
    """Extract 'Intel(R) Arc(TM) A380 Graphics' from `clinfo --list` output."""
    for line in clinfo_output.splitlines():
        if "Intel" in line and ("Arc" in line or "Iris" in line or "Graphics" in line):
            return line.strip().split(":")[-1].strip()
    return None
```

### docker-compose.yml: WSL2-Variant Section

Neue kommentierte Section unterhalb von `ai-upscaler-intel`:

```yaml
  # ============================================================
  # WSL2 / Docker Desktop variant (Windows 11 + Intel/AMD GPU)
  # ============================================================
  # ai-upscaler-wsl2:
  #   image: kuscheltier/jellyfin-ai-upscaler:docker7-intel
  #   container_name: jellyfin-ai-upscaler-wsl2
  #   ports:
  #     - "5000:5000"
  #   volumes:
  #     - ai-models:/app/models
  #     - ai-cache:/app/cache
  #     - /usr/lib/wsl:/usr/lib/wsl:ro
  #   devices:
  #     - /dev/dxg:/dev/dxg
  #   environment:
  #     - USE_GPU=true
  #     - OPENVINO_DEVICE=GPU
  #     - LD_LIBRARY_PATH=/usr/lib/wsl/lib
  #   restart: unless-stopped
```

### README Korrektur

Zeile 1048 aendern von:
```
- **Windows Docker Desktop**: GPU passthrough not supported — use `:docker7-cpu`
```
zu:
```
- **Windows Docker Desktop (WSL2)**: Intel/AMD GPUs via `/dev/dxg` + WSL2-driver mount (see docker-compose.yml WSL2 section). NVIDIA: use NVIDIA Container Toolkit.
```

### `/gpu-verify` Endpoint erweitern

```python
diagnostics["wsl2"] = {
    "is_wsl2_environment": Path("/dev/dxg").exists(),
    "wsl_lib_mounted": Path("/usr/lib/wsl/lib").exists(),
    "ld_library_path": os.environ.get("LD_LIBRARY_PATH", ""),
}
```

### Tests

Mock-basierter Test in `test_detection.py` (neu): `Path.exists` mock so dass `/dev/dxg` da ist und `/dev/dri/renderD*` leer, `subprocess.run` mock liefert clinfo-Intel-Output -> assert `gpu_detected == True` und `state.gpu_name` enthaelt "Intel".

---

## P2 — Issues #62 + #63 (Installation/Checksum-Cluster aus v1.5.x)

**Reporter:** Multiple (TechnicalMacaroon, Sabmurai, ChinchillaMan2, Ozimandias20, lhusemann, TheYang)
**Dates:** 2026-04-12 ... 2026-04-17
**Common Symptom:** Plugin v1.5.5.4+ kann nicht installiert werden ("Checksum Validation Mismatch"), v1.5.5.0 installiert aber UI nicht funktional.

### Root Cause

Alles **veraltete Probleme aus dem v1.5.x-Zeitraum**:
- Damals Checksum-Bugs (#51, #57 closed)
- API_TOKEN-Pflicht-Bug in v1.5.5.x (TheYang's Beobachtung)
- Outdated Plugin-Repo-Feed (siehe #65 -> in dieser Session bereits gefixt mit `repository-simple.json` Sync)

Seit v1.7.3.1 + Repo-Feed-Hotfix ist all das obsolet.

### Action

**Sammel-Comment auf beiden Issues** (gleicher Inhalt), dann close:

> The issues reported here all originate from v1.5.x (Apr 2026). Since then:
>
> 1. Plugin updated to v1.7.3.1 (May 2026) — Docker container architecture stabilized, checksum-bugs fixed.
> 2. Repo-feed URL synchronized in e470849 — both `repository-jellyfin.json` and the legacy `repository-simple.json` now serve v1.7.3.1.
>
> Please retry with:
> - Plugin URL: `https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/repository-jellyfin.json`
> - Docker tag: `kuscheltier/jellyfin-ai-upscaler:docker7` (rolling latest)
>
> If you still hit problems with v1.7.3.1, please open a fresh issue with logs — happy to investigate. Closing as v1.5.x-era.

---

## P3 — Issue #64: Feature: Select Library (already implemented)

**Reporter:** [@southsko](https://github.com/southsko), 2026-04-18
**Request:** "Can we select a specific library to monitor?"

### Status

**Already shipped in v1.6.1.14 (2026-04-19, one day after this issue):**
- New `EnabledLibraryIds` config field
- Chip-based library picker on Settings tab
- New `GET /Upscaler/libraries` endpoint enumerates virtual folders
- Empty list = legacy scan-all behavior (backwards-compat)

### Action

Comment + close:

> Implemented in v1.7.3.1 (originally v1.6.1.14, Apr 2026).
>
> Go to Settings -> AI Upscaler -> Library Scan section — there's now a chip-picker showing all your Jellyfin libraries. Pick the ones you want scanned; leave empty for scan-all behavior.

---

## P3 — Issue #49: Vulkan support? (already implemented)

**Reporter:** [@Zippy-boy](https://github.com/Zippy-boy), 2026-03-25
**Request:** "Can Vulkan support be added to this. This would greatly improve compatibility."

### Status

**Already supported since v1.5.x:**
- Docker tag `:docker7-vulkan` ships with ncnn-Vulkan backend
- Works on AMD pre-RDNA2, Intel iGPU, any Vulkan-capable GPU
- Listed in `README.md:283-287` as official supported variant

### Action

Comment + close:

> Already supported — pull the `docker7-vulkan` image:
> ```
> docker pull kuscheltier/jellyfin-ai-upscaler:docker7-vulkan
> ```
> Uses ncnn-Vulkan backend. Works on AMD pre-RDNA2, Intel iGPU, and any Vulkan-capable hardware.

---

## Reihenfolge dieser Session

### Phase 1 — Docker-Code-Patches (jetzt)

1. **#67 FP16-Type-Detection** in `docker-ai-service/app/main.py`:
   - Helper `_model_expects_fp16()` einfuegen
   - 2x Cast-Guard in `upscale_image()` und `upscale_video_chunk()`

2. **#66 WSL2 Detection** in `docker-ai-service/app/main.py`:
   - Helper `_parse_clinfo_intel_name()` einfuegen
   - 3. Branch in Intel-GPU-Block einfuegen (zwischen `if render_nodes:` und `elif ONNX_AVAILABLE`)
   - `/gpu-verify` endpoint mit WSL2-Diagnostics erweitern

3. **#66 Doku** — `docker-compose.yml` WSL2-Section + `README.md:1048` Korrektur

### Phase 2 — Issue-Triage (Quick-Wins, je 1-2 min)

4. #64 + #49 mit Comment schliessen
5. #62 + #63 mit Sammel-Comment schliessen
6. #67 sofort-Antwort mit Workaround (`USE_FP16=false`)
7. #66 sofort-Antwort mit erwartetem Fix in v1.7.4

### Phase 3 — Tests & Verification (vor Release)

8. `docker-ai-service/tests/test_inference.py` (neu) — FP16-Type-Detection regression-guard
9. `docker-ai-service/tests/test_detection.py` (neu) — WSL2-Detection-Path mock-test
10. `docker build` + `gh container run` Smoke-Test mit `:latest` Tag

### Phase 4 — Release v1.7.4

- meta.json + manifest.json + repository-*.json bump
- RELEASE-NOTES-v1.7.4.md
- ZIP build + Quad-MD5 verify
- Push, ghcr upload, repo-feed sync

---

## Backlog: Architektur-Verbesserungen (nicht fuer diese Session)

- **Detection-Strategy-Refactor:** `detect_hardware()` ist 242 LoC if/elif. Refactoring zu `DETECTION_STRATEGIES: list[DetectionStrategy]` Klassen-Liste wuerde kuenftige Platforms sauberer hinzufuegbar machen.
- **CI-Job fuer FP16-Model-Compatibility:** Vor jedem Release alle Models in beiden Precision-Modi durch-warmen, broken-pairs flaggen.
- **Auto-FP16-Detection enhancement:** `_resolve_fp16_setting()` koennte nicht nur GPU-Capability checken, sondern auch ob das geladene Model FP16 unterstuetzt — vermeidet User-Confusion komplett.

---

**Plan-Autor:** Maintainer-Session 2026-05-19
**Naechster Schritt:** Phase 1 starten (Docker-Code-Patches).
