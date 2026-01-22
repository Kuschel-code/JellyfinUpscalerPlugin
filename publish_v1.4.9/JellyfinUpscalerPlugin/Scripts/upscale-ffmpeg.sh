#!/bin/bash
###############################################################################
# AI Upscaler FFmpeg Wrapper
# Intercepts Jellyfin transcoding commands and injects upscaling filters
#
# INSTALLATION:
# 1. chmod +x upscale-ffmpeg.sh
# 2. Set this script as FFmpeg path in Jellyfin Admin Dashboard
# 3. Configure REAL_FFMPEG_PATH to point to actual ffmpeg binary
###############################################################################

# Configuration
REAL_FFMPEG_PATH="/usr/bin/ffmpeg"
UPSCALER_PLUGIN_API="http://localhost:8096/api/Upscaler"
ENABLE_UPSCALING=true
UPSCALE_FACTOR=2
LOG_FILE="/var/log/jellyfin/upscaler-wrapper.log"

# Logging function
log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" >> "$LOG_FILE"
}

# Check if real FFmpeg exists
if [ ! -f "$REAL_FFMPEG_PATH" ]; then
    log "ERROR: Real FFmpeg not found at $REAL_FFMPEG_PATH"
    exit 1
fi

# Parse FFmpeg arguments to detect transcoding
ARGS=("$@")
IS_TRANSCODING=false
HAS_VIDEO_FILTER=false
VIDEO_FILTER_INDEX=-1

for i in "${!ARGS[@]}"; do
    if [[ "${ARGS[$i]}" == "-vf" ]] || [[ "${ARGS[$i]}" == "-filter:v" ]]; then
        HAS_VIDEO_FILTER=true
        VIDEO_FILTER_INDEX=$((i + 1))
    fi
    
    if [[ "${ARGS[$i]}" == *".m3u8"* ]] || [[ "${ARGS[$i]}" == *".ts"* ]]; then
        IS_TRANSCODING=true
    fi
done

# Inject upscaling filter if enabled and transcoding detected
if [ "$ENABLE_UPSCALING" = true ] && [ "$IS_TRANSCODING" = true ]; then
    log "Detected transcoding, injecting AI upscaling filter..."
    
    # Get hardware capabilities from plugin API
    HARDWARE_INFO=$(curl -s "$UPSCALER_PLUGIN_API/hardware" || echo '{"SupportsCUDA":false}')
    SUPPORTS_CUDA=$(echo "$HARDWARE_INFO" | grep -o '"SupportsCUDA":[^,}]*' | grep -o '[^:]*$')
    
    # Build upscaling filter based on hardware
    if [[ "$SUPPORTS_CUDA" == *"true"* ]]; then
        UPSCALE_FILTER="hwupload_cuda,scale_cuda=${UPSCALE_FACTOR}*iw:${UPSCALE_FACTOR}*ih:interp_algo=lanczos,unsharp_cuda=luma_amount=1.5,hwdownload,format=nv12"
        log "Using NVIDIA CUDA upscaling filter"
    else
        UPSCALE_FILTER="libplacebo=w=${UPSCALE_FACTOR}*iw:h=${UPSCALE_FACTOR}*ih:upscaler=ewa_lanczos"
        log "Using software libplacebo upscaling filter"
    fi
    
    # Inject or append to existing video filter
    if [ "$HAS_VIDEO_FILTER" = true ]; then
        EXISTING_FILTER="${ARGS[$VIDEO_FILTER_INDEX]}"
        ARGS[$VIDEO_FILTER_INDEX]="${EXISTING_FILTER},${UPSCALE_FILTER}"
        log "Appended to existing filter: ${ARGS[$VIDEO_FILTER_INDEX]}"
    else
        # Insert before output file (usually last 2 args)
        INSERT_POS=$((${#ARGS[@]} - 2))
        ARGS=("${ARGS[@]:0:$INSERT_POS}" "-vf" "$UPSCALE_FILTER" "${ARGS[@]:$INSERT_POS}")
        log "Inserted new upscaling filter"
    fi
fi

# Log final command
log "Executing: $REAL_FFMPEG_PATH ${ARGS[*]}"

# Execute real FFmpeg with modified arguments
exec "$REAL_FFMPEG_PATH" "${ARGS[@]}"
