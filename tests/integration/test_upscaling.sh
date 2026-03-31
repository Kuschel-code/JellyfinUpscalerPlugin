#!/usr/bin/env bash
# Integration test: build CPU Docker image, start service, verify real upscaling works.
# Usage: bash tests/integration/test_upscaling.sh [--no-build]
set -euo pipefail

IMAGE="jellyfin-upscaler-cpu:integration-test"
CONTAINER="upscaler-integration-test"
PORT=5099

NO_BUILD=false
if [[ "${1:-}" == "--no-build" ]]; then
  NO_BUILD=true
fi

cleanup() {
  echo "--- Cleanup ---"
  docker stop "$CONTAINER" 2>/dev/null || true
  docker rm "$CONTAINER" 2>/dev/null || true
}
trap cleanup EXIT

if [ "$NO_BUILD" = false ]; then
  echo "=== [1/6] Building CPU Docker image ==="
  docker build -f docker-ai-service/Dockerfile.cpu \
    -t "$IMAGE" \
    docker-ai-service/
  echo "Build complete."
fi

echo "=== [2/6] Starting container on port $PORT ==="
docker run -d --name "$CONTAINER" \
  -p ${PORT}:5000 \
  -e DEFAULT_MODEL=espcn-x4 \
  "$IMAGE"

echo "=== [3/6] Waiting for service startup (max 90s) ==="
for i in $(seq 1 45); do
  if curl -sf "http://localhost:${PORT}/health" > /dev/null 2>&1; then
    echo "  Service ready after $((i*2))s"
    break
  fi
  if [ "$i" -eq 45 ]; then
    echo "ERROR: Service did not start within 90s"
    docker logs "$CONTAINER"
    exit 1
  fi
  printf "."
  sleep 2
done
echo ""

echo "=== [4/6] Downloading + loading espcn-x4 model (~100KB) ==="
curl -sf -X POST "http://localhost:${PORT}/models/download" \
  -d "model=espcn-x4" | python3 -c "
import json, sys
d = json.load(sys.stdin)
assert d.get('status') == 'success', f'Model download failed: {d}'
print('  Downloaded:', d.get('model'), f\"({d.get('size_mb', '?')} MB)\")
"

curl -sf -X POST "http://localhost:${PORT}/models/load" \
  -d "model=espcn-x4" | python3 -c "
import json, sys
d = json.load(sys.stdin)
assert d.get('status') == 'loaded', f'Model load failed: {d}'
print('  Loaded:', d.get('model'))
"

echo "=== [5/6] Creating 64x64 test image and calling /upscale ==="
python3 - <<'PYEOF'
from PIL import Image
import numpy as np
img = Image.fromarray(np.random.randint(50, 200, (64, 64, 3), dtype=np.uint8))
img.save("/tmp/test_input_64x64.png")
print(f"  Created /tmp/test_input_64x64.png: {img.size[0]}x{img.size[1]}")
PYEOF

curl -sf -X POST "http://localhost:${PORT}/upscale" \
  -F "file=@/tmp/test_input_64x64.png;type=image/png" \
  -F "scale=4" \
  -o /tmp/test_output.png

echo "=== [6/6] Verifying output dimensions ==="
python3 - <<'PYEOF'
from PIL import Image
inp = Image.open("/tmp/test_input_64x64.png")
out = Image.open("/tmp/test_output.png")
print(f"  Input:  {inp.size[0]}x{inp.size[1]}")
print(f"  Output: {out.size[0]}x{out.size[1]}")
scale_x = out.size[0] / inp.size[0]
scale_y = out.size[1] / inp.size[1]
assert out.size[0] >= inp.size[0] * 2, f"Output width {out.size[0]} is less than 2x input {inp.size[0]}"
assert out.size[1] >= inp.size[1] * 2, f"Output height {out.size[1]} is less than 2x input {inp.size[1]}"
print(f"  Scale achieved: {scale_x:.1f}x width, {scale_y:.1f}x height")
PYEOF

echo ""
echo "============================================"
echo "  ALL INTEGRATION TESTS PASSED"
echo "============================================"
