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
    # WSL2 / Docker Desktop on Windows exposes the GPU via /dev/dxg, not
    # /dev/dri/renderD128 (issue #66/#69). main.py already detects this since
    # v1.7.4 - keep the startup banner consistent with the real detection so it
    # doesn't print "Backend: cpu" (and fire a false GPU-missing warning) when an
    # Intel/AMD GPU is actually present through the DirectX bridge.
    if [ -e /dev/dxg ]; then
        echo "intel-wsl2"
        return
    fi
    echo "cpu"
}

# Issue #90: a render node that exists but cannot be opened makes OpenVINO/OpenCL report
# "Device GPU is not available" and the service silently runs on the CPU. `group_add: render`
# does not fix it: the group NAME resolves inside the container, to a different GID than the
# HOST group that owns /dev/dri/renderD128.
check_render_access() {
    local node denied="" gid
    for node in /dev/dri/renderD*; do
        [ -e "$node" ] || continue
        if [ -r "$node" ] && [ -w "$node" ]; then
            return 0
        fi
        denied="$node"
    done
    [ -n "$denied" ] || return 0
    gid="$(stat -c '%g' "$denied" 2>/dev/null || echo '?')"
    log "WARNING: $denied exists but this container user cannot open it (uid=$(id -u), groups=$(id -G))."
    log "  The device belongs to GID ${gid}. Give the container the HOST's numeric GID:"
    log "    compose:    group_add: [\"${gid}\"]"
    log "    docker run: --group-add ${gid}"
    log "  (find it on the host: stat -c '%g' /dev/dri/renderD128)"
    log "  'group_add: render' does NOT work - the name resolves to a different GID in the container."
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

check_render_access

exec "$@"
