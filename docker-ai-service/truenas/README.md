# Running the AI Upscaler on TrueNAS SCALE

TrueNAS SCALE 24.10 ("Electric Eel") moved from K3s (Helm charts) to docker
for app orchestration. The simplest way to deploy this service on TrueNAS is
via the built-in **Custom App** feature — which accepts a standard
docker-compose file.

## Quick install

1. **Create the dataset for models + cache** (web UI):
   - Storage → Datasets → apps → Add Dataset → name `jellyfin-ai-upscaler`
   - Inside it, add two children: `models` and `cache`
   - Set the ACL to permit UID **568** (TrueNAS `apps` user) read+write

2. **Create the Custom App**:
   - Apps → Discover Apps → **Custom App** (top-right)
   - Application Name: `jellyfin-ai-upscaler`
   - Switch to the **YAML** tab
   - Paste the contents of [`docker-compose.truenas.yml`](./docker-compose.truenas.yml)
   - Replace `/mnt/tank/apps/...` with your actual pool path

3. **GPU allocation** (optional, NVIDIA only):
   - In the same wizard, scroll to **Resources → GPU**
   - Allocate `1` NVIDIA GPU
   - TrueNAS wires the runtime automatically (no need for `deploy.resources`)

4. **Intel / AMD GPU** (Vulkan or OpenVINO image):
   - Change image tag to `:docker7-intel`, `:docker7-amd`, or `:docker7-vulkan`
   - In **Storage → Host Paths**, add `/dev/dri` → `/dev/dri`
   - For AMD ROCm also add `/dev/kfd` → `/dev/kfd`

5. **Verify**:
   ```bash
   curl http://<truenas-ip>:5000/health
   ```
   The Jellyfin plugin then points at `http://<truenas-ip>:5000` in
   Dashboard → AI Upscaler → Docker Service URL.

## Why a Custom App and not a full Catalog?

A full TrueNAS catalog entry requires a HelmChart repo with its own CI, schema
validation, and release machinery — overkill for a single-service appliance.
The Custom App path uses standard docker-compose syntax (same file that
powers `docker compose up -d` on any Linux box), which keeps parity with the
bare-docker quickstart in the [main README](../../README.md).

## Auto-updates

The compose file tags `com.centurylinklabs.watchtower.enable=true`. If you run
[Watchtower](https://containrrr.dev/watchtower/) alongside (as a separate
TrueNAS Custom App), new `docker7` builds pushed to Docker Hub will be pulled
and restarted automatically — no manual intervention.
