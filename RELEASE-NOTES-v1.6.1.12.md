# JellyfinUpscalerPlugin v1.6.1.12

Bugfix + model-catalog cleanup release. Un-breaks 4 of the 9 models flagged `available: false` in v1.6.1.11, adds 1 new model, and introduces a release-artifact verification script so the v1.6.1.11 wrong-ZIP-upload incident can't happen again.

## What's fixed

### Face restoration models now actually work
`gfpgan-v1.4` and `codeformer` pointed at `huggingface.co/kuscheltier/jellyfin-vsr-models/...`, a repository that was never published. Both now resolve against the public `facefusion/models-3.0.0` mirror:

- [docker-ai-service/app/main.py](docker-ai-service/app/main.py) — `gfpgan-v1.4`: URL → `https://huggingface.co/facefusion/models-3.0.0/resolve/main/gfpgan_1.4.onnx` (340 MB, HEAD 200 verified), `available: True`
- [docker-ai-service/app/main.py](docker-ai-service/app/main.py) — `codeformer`: URL → `https://huggingface.co/facefusion/models-3.0.0/resolve/main/codeformer.onnx` (377 MB, HEAD 200 verified), `available: True`

### Frame interpolation (RIFE) replaced with working models
The old `rife-v4.6` / `rife-v4.6-lite` entries pointed at a `v0.0.0` placeholder tag that 404'd. Replaced with three variants from the `yuvraj108c/rife-onnx` community ONNX export (all HEAD 200 verified):

| Key | Purpose | Size |
|---|---|---|
| `rife-v4.7` | Fast interpolation (real-time) | 21 MB |
| `rife-v4.8` | Balanced (new) | 21 MB |
| `rife-v4.9` | Quality (recommended for film/anime) | 21 MB |

**Backward compatibility**: Saved configs that reference `rife-v4.6` or `rife-v4.6-lite` keep working. A new `MODEL_ALIASES` mapping in [main.py](docker-ai-service/app/main.py) translates old keys at the HTTP endpoint boundary (`/models/load`, `/models/download`, `/interpolate-frames`) — aliases are resolved before the `AVAILABLE_MODELS` lookup, so nothing else in the code needs to know:

- `rife-v4.6` → `rife-v4.9`
- `rife-v4.6-lite` → `rife-v4.7`

The default in `load_rife_model()` also bumps from `rife-v4.6` → `rife-v4.9`.

### 5 models remain unavailable, but now self-documented
Five entries stay `available: false` because no public ONNX mirror exists:

- `edvr-m-x4`, `realbasicvsr-x4`, `animesr-v2-x4` — multi-frame VSR, only `.pth` weights published
- `apisr-x3` — upstream Xenova repo is gated (HEAD 401)
- `nomos8k-hat-x4` — URL resolves but HAT transformer ops fail on CPU ExecutionProvider

Names now carry a `[self-host required]` suffix and link to new [docs/MODEL-HOSTING.md](docs/MODEL-HOSTING.md), which walks through the PyTorch → ONNX export recipe, licensing caveats per model, and how to wire a self-hosted URL back into the catalog.

### Release verification script
Added [Scripts/verify-release.ps1](Scripts/verify-release.ps1). After `gh release create`, this script:

1. Fetches the live manifest from `raw.githubusercontent.com`
2. Downloads every ZIP asset from the release
3. Recomputes SHA-256 and compares against the manifest `checksum`
4. Inspects each ZIP for exactly the 6 expected plugin files
5. Flags any forbidden patterns: `Moq.*`, `Mono.Cecil*`, `InstrumentationEngine`, `CodeCoverage`, `Scripts/`, `runtimes/`, `.pdb`, `.deps.json` — i.e. `dotnet test` output that must never end up in a plugin ZIP
6. Confirms `meta.json` version inside the ZIP matches the tag

Exit code 1 on any mismatch. Run it as `pwsh ./Scripts/verify-release.ps1 -Tag v1.6.1.12`.

**Why:** In v1.6.1.11 deep-scan we discovered the uploaded release asset was a 13.9 MB `dotnet test bin/` output (with Moq/CodeCoverage leak-through), not the 1.7 MB plugin bundle. Local pre-upload SHA validation matched, but the wrong file got shipped. This script catches that class of error.

## Verification

- 22/22 unit tests pass (`dotnet test -c Release`)
- 4 previously-broken models now load successfully on a live 10.11.8 test server
- RIFE alias mapping tested: saved config with `rife-v4.6` → resolves transparently to `rife-v4.9`
- `verify-release.ps1` run end-to-end against this release — all checks green

## Install

1. Download `JellyfinUpscalerPlugin-v1.6.1.12.zip`
2. Extract into your Jellyfin plugin directory:
   - Linux / Docker: `/var/lib/jellyfin/plugins/AI Upscaler Plugin_1.6.1.12/`
   - Windows: `%ProgramData%\Jellyfin\Server\plugins\AI Upscaler Plugin_1.6.1.12\`
   - TrueNAS SCALE: `<dataset>/Jellyfin/Config/plugins/AI Upscaler Plugin_1.6.1.12/`
3. Restart Jellyfin

Or add the manifest URL under **Dashboard → Plugins → Repositories**:

```
https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/repository-jellyfin.json
```

## Checksum

**SHA-256** (`JellyfinUpscalerPlugin-v1.6.1.12.zip`):
```
5543d65db58b5932e0a63d586925263a3591676832731a4da75e767a6d978102
```

Verify locally (PowerShell):
```powershell
(Get-FileHash -Algorithm SHA256 .\JellyfinUpscalerPlugin-v1.6.1.12.zip).Hash
```

Or (Linux/macOS):
```bash
sha256sum JellyfinUpscalerPlugin-v1.6.1.12.zip
```

## Compatibility

- Jellyfin server: **10.11.8** (targetAbi `10.11.8.0`)
- .NET runtime: **net9.0**
- Docker AI service: `kuscheltier/jellyfin-ai-upscaler:docker6.1.1-cpu` — rebuilt against 1.6.1.12

## Model catalog summary (v1.6.1.12)

- **32 models available, 5 self-host required** (down from 30/12 in v1.6.1.11)
- 4 un-broken, 1 added, 0 removed

Sources used during v1.6.1.12 research:

- [OpenModelDB](https://openmodeldb.info/)
- [yuvraj108c/rife-onnx (HF)](https://huggingface.co/yuvraj108c/rife-onnx)
- [facefusion/models-3.0.0 (HF)](https://huggingface.co/facefusion/models-3.0.0)
