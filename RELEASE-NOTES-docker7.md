# Docker Image Release `docker7` — Infrastructure Modernization

> Plugin version: **v1.6.1.13** (unchanged)
> Docker tag family: **docker6.1 → docker7**
>
> No plugin-side breaking changes. Existing installs keep working. Users who
> updated their `docker-compose.yml` to point at `:docker7` (or pull the tag
> via Watchtower) get the improvements below automatically.

---

## Upgrade in one command

```bash
cd /path/to/docker-ai-service
git pull
docker compose pull && docker compose up -d
```

Or, if you don't use compose:

```bash
docker pull kuscheltier/jellyfin-ai-upscaler:docker7
docker rm -f jellyfin-ai-upscaler
# re-run your usual `docker run` with :docker7 instead of :docker6.1
```

If you had an older image pinned (e.g. `:docker6`, `:docker6.1-cpu`), either
bump the tag to `:docker7*` or stay on the old tag — docker6.1 remains
available on Docker Hub; it's just no longer rebuilt.

---

## What changed in each image

### 1. OCI-standard image metadata
Every image now exposes proper [OpenContainers labels](https://github.com/opencontainers/image-spec/blob/main/annotations.md):

```
org.opencontainers.image.title
org.opencontainers.image.description
org.opencontainers.image.vendor
org.opencontainers.image.source
org.opencontainers.image.documentation
org.opencontainers.image.licenses
org.opencontainers.image.version    ← build-time APP_VERSION
org.opencontainers.image.revision   ← build-time APP_COMMIT (short git SHA)
```

Why it matters: Docker Hub now renders proper metadata, Trivy/Harbor/Watchtower read version info from labels, and `docker inspect` shows the exact commit each image was built from.

### 2. Startup banner + GPU auto-detection
`entrypoint.sh` now probes `nvidia-smi` → `rocm-smi` → `vainfo` → `/dev/dri`
before handing off to uvicorn. On boot you'll see:

```
[entrypoint] ======================================================
[entrypoint]  Jellyfin AI Upscaler
[entrypoint]  Version:     1.6.1.13
[entrypoint]  Commit:      7f7c557
[entrypoint]  Backend:     nvidia
[entrypoint]  USE_GPU:     true
[entrypoint]  Model:       realesrgan-x4
[entrypoint]  Concurrency: 4
[entrypoint] ======================================================
```

If `USE_GPU=true` is set but no GPU is visible inside the container,
the entrypoint now prints a targeted warning with the exact runtime
flag you're missing (`--gpus all`, `--device=/dev/kfd`, `--device=/dev/dri`)
before falling back to CPU. No more silent slow inference.

### 3. BuildKit pip cache mounts
All six Dockerfiles now use `RUN --mount=type=cache,target=/root/.cache/pip`
instead of `--no-cache-dir`. Rebuild-from-source time after a code-only change
drops by **~85%** (mostly saves the torch/opencv wheel downloads).

### 4. Multi-architecture builds
The slim-python variants (cpu, apple, vulkan) now ship as **multi-arch
manifests** covering `linux/amd64` and `linux/arm64`:

| Image | linux/amd64 | linux/arm64 |
|-------|:-:|:-:|
| `:docker7` (NVIDIA) | ✅ | — |
| `:docker7-amd`      | ✅ | — |
| `:docker7-intel`    | ✅ | — |
| `:docker7-apple`    | ✅ | ✅ |
| `:docker7-vulkan`   | ✅ | ✅ |
| `:docker7-cpu`      | ✅ | ✅ |

That means Raspberry Pi 5 and Apple-Silicon-hosted Docker now pull the
right arch automatically — no more manual platform override.

### 5. Semver triple-tagging
Every release pushes three tags for every backend:

| Tag form | Example | Use case |
|----------|---------|----------|
| `<family>[-suffix]` | `docker7`, `docker7-cpu` | **Rolling** — Watchtower target |
| `<family>-v<ver>[-suffix]` | `docker7-v1.6.1.13-cpu` | **Pinned** — reproducible runs |
| `v<ver>[-suffix]` | `v1.6.1.13-cpu` | **Semver** — full version pin |

Pick whichever stability guarantee you want.

### 6. CI/CD workflow: `.github/workflows/docker-publish.yml`
A new GitHub Actions matrix build publishes all six images on manual
dispatch or on any `docker7*` / `v*-docker7` tag push. It:

- Reuses BuildKit layer cache across runs via registry cache tags
- Builds multi-arch variants with QEMU emulation
- Runs **Trivy** CVE scans (HIGH/CRITICAL, fix-available) and uploads
  SARIF to the Security tab — visible under Repository → Security → Code scanning alerts
- Fails gracefully: one broken base image doesn't block the other five

### 7. TrueNAS SCALE Custom App support
New folder `docker-ai-service/truenas/` with:
- [`docker-compose.truenas.yml`](docker-ai-service/truenas/docker-compose.truenas.yml) — drop-in paste for the Custom App YAML tab
- [`README.md`](docker-ai-service/truenas/README.md) — step-by-step with GPU allocation notes

### 8. Watchtower opt-in label
`docker-compose.yml` now tags the service with:

```yaml
labels:
  - "com.centurylinklabs.watchtower.enable=true"
```

Enables scoped auto-updates when you pair with a Watchtower container
running `WATCHTOWER_LABEL_ENABLE=true`. No effect unless you run Watchtower.

---

## Images NOT changing behavior

- **Python `/health` endpoint** — was already rich JSON (`status`, `model_loaded`, `gpu_name`, `circuit_open`), so no `/healthz` addition was needed for this release.
- **Volumes** (`ai-models`, `ai-cache`) — already named since earlier releases; no data migration required.
- **Model catalog** — identical to v1.6.1.12 + v1.6.1.13 (40+ models, catalog cleanup already shipped).

---

## Skipped deliberately

Two items from the enhancement list were evaluated and **not** included:

- **Multi-stage builds for NVIDIA/AMD**. Too risky for a rolling upgrade — would require re-testing the CUDA runtime + TensorRT code paths. Queued for a future release if image size becomes an issue.
- **Docker Secrets `_FILE` pattern**. Adds complexity to `main.py` for minimal benefit in a home-lab setting where env vars are already the norm.

---

## Compatibility

| Component | Before | After | Breaking? |
|-----------|:------:|:-----:|:---------:|
| Plugin DLL | v1.6.1.13 | v1.6.1.13 | ❌ |
| Docker tag | docker6.1 | docker7 | Tag-only — old tag still exists |
| docker-compose volumes | `ai-models`, `ai-cache` | same | ❌ |
| `/health` JSON shape | unchanged | unchanged | ❌ |
| `USE_GPU` / `DEFAULT_MODEL` env | unchanged | unchanged | ❌ |

The plugin HTTP client treats `docker6.1` and `docker7` identically — it
only calls `/upscale`, `/status`, `/health`.

---

## Attribution

This plugin (and this Docker release) is developed as a pair-programming
collaboration with **Anthropic's Claude Opus 4.7**. See the README badge
and the `Co-Authored-By: Claude` trailer on release commits.

---

## Verify your upgrade

After pulling, confirm you're on docker7:

```bash
docker inspect kuscheltier/jellyfin-ai-upscaler:docker7 \
  --format '{{ index .Config.Labels "org.opencontainers.image.version" }}'
# → 1.6.1.13

curl -s http://localhost:5000/health | jq
# → { "status": "healthy", "model_loaded": true, ... }
```

If the banner prints `Backend: cpu` but you expected GPU, check the warning
line immediately below — it tells you exactly which runtime flag is missing.
