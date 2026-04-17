#!/bin/bash
# Jellyfin AI Upscaler — container entrypoint
# Detects available GPU backend, prints a startup banner, warns on mismatches,
# then execs the CMD (uvicorn by default).
set -e

log() { printf '[entrypoint] %s\n' "$*"; }

detect_backend() {
    if command -v nvidia-smi >/dev/null 2>&1 && nvidia-smi -L >/dev/null 2>&1; then
        echo "nvidia"
        return
    fi
    if command -v rocm-smi >/dev/null 2>&1 && rocm-smi --showid >/dev/null 2>&1; then
        echo "rocm"
        return
    fi
    if [ -e /dev/dri/renderD128 ]; then
        if command -v vainfo >/dev/null 2>&1 && vainfo >/dev/null 2>&1; then
            echo "intel-vaapi"
            return
        fi
        echo "vulkan-or-intel"
        return
    fi
    echo "cpu"
}

BACKEND="$(detect_backend || echo cpu)"
USE_GPU_VAL="${USE_GPU:-false}"

log "======================================================"
log " Jellyfin AI Upscaler"
log " Version:     ${APP_VERSION:-unknown}"
log " Commit:      ${APP_COMMIT:-unknown}"
log " Backend:     ${BACKEND}"
log " USE_GPU:     ${USE_GPU_VAL}"
log " Model:       ${DEFAULT_MODEL:-realesrgan-x4}"
log " Concurrency: ${MAX_CONCURRENT_REQUESTS:-4}"
log "======================================================"

if [ "${USE_GPU_VAL}" = "true" ] && [ "${BACKEND}" = "cpu" ]; then
    log "WARNING: USE_GPU=true but no GPU detected in the container."
    log "  - nvidia: pass --gpus all (or deploy.resources in compose)"
    log "  - amd/rocm: pass --device=/dev/kfd --device=/dev/dri"
    log "  - intel/vulkan: pass --device=/dev/dri"
    log "  Falling back to CPU inference (slow)."
fi

exec "$@"
