#!/bin/bash
# Native macOS install for Apple Silicon (M1/M2/M3/M4/M5)
# Uses CoreML + Neural Engine for GPU-accelerated AI upscaling
# Recommended over Docker for best performance on macOS
#
# Make executable with: chmod +x install-native-macos.sh

set -e

echo "=== JellyfinUpscalerPlugin AI Service — Native macOS Setup ==="
echo ""

# Check for Apple Silicon
if [[ $(uname -m) != "arm64" ]]; then
    echo "WARNING: This script is optimized for Apple Silicon (M1-M5)."
    echo "Intel Macs will use CPU-only mode."
fi

# Check Python
PYTHON=$(command -v python3.12 || command -v python3.11 || command -v python3)
if [[ -z "$PYTHON" ]]; then
    echo "ERROR: Python 3.11+ required. Install with: brew install python@3.12"
    exit 1
fi
echo "Using Python: $PYTHON"

# Create venv
VENV_DIR="./venv-macos"
echo "Creating virtual environment at $VENV_DIR..."
$PYTHON -m venv "$VENV_DIR"
source "$VENV_DIR/bin/activate"

# Install dependencies
echo "Installing dependencies..."
pip install --upgrade pip
pip install -r requirements-apple.txt

# Create launch script
cat > run-macos.sh << 'LAUNCH_EOF'
#!/bin/bash
source ./venv-macos/bin/activate
export USE_GPU=true
export ONNX_PROVIDERS=CoreMLExecutionProvider,CPUExecutionProvider
export PORT=${PORT:-5000}
echo "Starting AI Upscaler Service on port $PORT..."
echo "Using CoreML + Neural Engine for GPU acceleration"
uvicorn app.main:app --host 0.0.0.0 --port $PORT --workers 1
LAUNCH_EOF
chmod +x run-macos.sh

echo ""
echo "=== Setup Complete ==="
echo "Start the service with: ./run-macos.sh"
echo "Models directory: ./models/"
echo "API will be available at: http://localhost:5000"
