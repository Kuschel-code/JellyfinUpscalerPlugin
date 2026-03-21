"""
AI Upscaler Service - FastAPI Application
Jellyfin AI Upscaler Plugin - Microservice Component v1.5.5
Supports OpenCV DNN (.pb) and ONNX Runtime models with GPU detection
Multi-GPU selection, robust TensorRT/CUDA/OpenVINO fallback
"""

import os
import io
import time
import logging
import asyncio
import platform
import subprocess
import threading
import multiprocessing
from pathlib import Path
from typing import Optional, Any, List
from contextlib import asynccontextmanager

import numpy as np
import cv2
import httpx
from fastapi import FastAPI, File, UploadFile, Form, HTTPException, BackgroundTasks, Request
from fastapi.responses import Response, HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel

# Try to import ONNX Runtime (optional)
try:
    import onnxruntime as ort
    ONNX_AVAILABLE = True
except ImportError:
    ONNX_AVAILABLE = False
    ort = None

# Configure logging
logging.basicConfig(
    level=os.getenv("LOG_LEVEL", "INFO"),
    format="%(asctime)s | %(levelname)s | %(message)s"
)
logger = logging.getLogger(__name__)

# Paths
MODELS_DIR = Path("/app/models")
CACHE_DIR = Path("/app/cache")
STATIC_DIR = Path("/app/static")

# Version
VERSION = "1.5.5"

# Global state
class AppState:
    # OpenCV DNN model
    cv_model: Any = None
    cv_model_name: Optional[str] = None
    cv_model_scale: int = 2
    
    # ONNX model
    onnx_session = None
    onnx_model_name: Optional[str] = None
    onnx_model_scale: int = 4
    
    current_model: Optional[str] = None
    current_model_type: str = "opencv"  # "opencv" or "onnx"
    providers: list = []
    use_gpu: bool = True
    processing_count: int = 0
    max_concurrent: int = 4
    gpu_device_id: int = 0  # GPU device index for multi-GPU systems

    # Hardware info
    gpu_name: str = "Unknown"
    gpu_memory: str = "Unknown"
    gpu_list: list = []  # List of detected GPUs for multi-GPU selection
    cpu_name: str = "Unknown"
    cpu_cores: int = 0
    
    # Plugin connections
    plugin_connections: list = []
    
    # Benchmark results
    last_benchmark: dict = {}

    # Last model load error (for surfacing in API responses)
    last_load_error: Optional[str] = None

state = AppState()

# Concurrency semaphore for upscaling requests (thread-safe)
import asyncio as _asyncio
_upscale_semaphore = _asyncio.Semaphore(state.max_concurrent)

# Threading lock to prevent model-swap data races between load and inference
_model_lock = threading.Lock()

# Tile size for ONNX inference (prevents OOM on large images)
ONNX_TILE_SIZE = int(os.getenv("ONNX_TILE_SIZE", "512"))

# Available models with download URLs from PUBLIC sources
# Note: Real-ESRGAN ONNX models need to be converted, using pre-converted from community
AVAILABLE_MODELS = {
    # ============================================================
    # === REAL-ESRGAN Models (Best Quality - Anime & Photo) ===
    # Note: These require ONNX Runtime. Models from community repos.
    # ============================================================
    "realesrgan-x4": {
        "name": "Real-ESRGAN x4 (Best Quality)",
        "url": "https://huggingface.co/AXERA-TECH/Real-ESRGAN/resolve/main/onnx/realesrgan-x4.onnx",
        "scale": 4,
        "description": "Best quality 4x for photos & anime (67MB ONNX)",
        "type": "onnx",
        "category": "realesrgan",
        "model_type": "realesrgan",
        "available": True
    },
    "realesrgan-x4-256": {
        "name": "Real-ESRGAN x4 (256px optimized)",
        "url": "https://huggingface.co/AXERA-TECH/Real-ESRGAN/resolve/main/onnx/realesrgan-x4-256.onnx",
        "scale": 4,
        "description": "Optimized for 256px tiles, better for low VRAM",
        "type": "onnx",
        "category": "realesrgan",
        "model_type": "realesrgan",
        "available": True
    },
    
    # ============================================================
    # === Fast Models (Recommended for Real-Time / CPU) ===
    # ============================================================
    "fsrcnn-x2": {
        "name": "FSRCNN x2 (Fast)",
        "url": "https://raw.githubusercontent.com/Saafke/FSRCNN_Tensorflow/master/models/FSRCNN_x2.pb",
        "scale": 2,
        "description": "Very fast 2x upscaling, good for real-time",
        "type": "pb",
        "category": "fast",
        "model_type": "fsrcnn",
        "available": True
    },
    "fsrcnn-x3": {
        "name": "FSRCNN x3 (Fast)",
        "url": "https://raw.githubusercontent.com/Saafke/FSRCNN_Tensorflow/master/models/FSRCNN_x3.pb",
        "scale": 3,
        "description": "Fast 3x upscaling",
        "type": "pb",
        "category": "fast",
        "model_type": "fsrcnn",
        "available": True
    },
    "fsrcnn-x4": {
        "name": "FSRCNN x4 (Fast)",
        "url": "https://raw.githubusercontent.com/Saafke/FSRCNN_Tensorflow/master/models/FSRCNN_x4.pb",
        "scale": 4,
        "description": "Fast 4x upscaling, lower quality but quick",
        "type": "pb",
        "category": "fast",
        "model_type": "fsrcnn",
        "available": True
    },
    "espcn-x2": {
        "name": "ESPCN x2 (Fastest)",
        "url": "https://raw.githubusercontent.com/fannymonori/TF-ESPCN/master/export/ESPCN_x2.pb",
        "scale": 2,
        "description": "Fastest model, minimal quality improvement",
        "type": "pb",
        "category": "fast",
        "model_type": "espcn",
        "available": True
    },
    "espcn-x3": {
        "name": "ESPCN x3 (Fastest)",
        "url": "https://raw.githubusercontent.com/fannymonori/TF-ESPCN/master/export/ESPCN_x3.pb",
        "scale": 3,
        "description": "Fastest 3x model",
        "type": "pb",
        "category": "fast",
        "model_type": "espcn",
        "available": True
    },
    "espcn-x4": {
        "name": "ESPCN x4 (Fastest)",
        "url": "https://raw.githubusercontent.com/fannymonori/TF-ESPCN/master/export/ESPCN_x4.pb",
        "scale": 4,
        "description": "Fastest 4x model",
        "type": "pb",
        "category": "fast",
        "model_type": "espcn",
        "available": True
    },
    
    # ============================================================
    # === Quality Models - LapSRN ===
    # ============================================================
    "lapsrn-x2": {
        "name": "LapSRN x2 (Quality)",
        "url": "https://raw.githubusercontent.com/fannymonori/TF-LapSRN/master/export/LapSRN_x2.pb",
        "scale": 2,
        "description": "Good quality 2x upscaling",
        "type": "pb",
        "category": "quality",
        "model_type": "lapsrn",
        "available": True
    },
    "lapsrn-x4": {
        "name": "LapSRN x4 (Quality)",
        "url": "https://raw.githubusercontent.com/fannymonori/TF-LapSRN/master/export/LapSRN_x4.pb",
        "scale": 4,
        "description": "Good quality 4x upscaling",
        "type": "pb",
        "category": "quality",
        "model_type": "lapsrn",
        "available": True
    },
    "lapsrn-x8": {
        "name": "LapSRN x8 (Quality)",
        "url": "https://raw.githubusercontent.com/fannymonori/TF-LapSRN/master/export/LapSRN_x8.pb",
        "scale": 8,
        "description": "Extreme 8x upscaling",
        "type": "pb",
        "category": "quality",
        "model_type": "lapsrn",
        "available": True
    },
    
    # ============================================================
    # === EDSR Models (High Quality, Slow) ===
    # ============================================================
    "edsr-x2": {
        "name": "EDSR x2 (Best OpenCV)",
        "url": "https://github.com/Saafke/EDSR_Tensorflow/raw/master/models/EDSR_x2.pb",
        "scale": 2,
        "description": "Best quality 2x with OpenCV, requires more compute",
        "type": "pb",
        "category": "quality",
        "model_type": "edsr",
        "available": True
    },
    "edsr-x3": {
        "name": "EDSR x3 (Best OpenCV)",
        "url": "https://github.com/Saafke/EDSR_Tensorflow/raw/master/models/EDSR_x3.pb",
        "scale": 3,
        "description": "Best quality 3x with OpenCV",
        "type": "pb",
        "category": "quality",
        "model_type": "edsr",
        "available": True
    },
    "edsr-x4": {
        "name": "EDSR x4 (Best OpenCV)",
        "url": "https://github.com/Saafke/EDSR_Tensorflow/raw/master/models/EDSR_x4.pb",
        "scale": 4,
        "description": "Best quality 4x with OpenCV, slowest but best",
        "type": "pb",
        "category": "quality",
        "model_type": "edsr",
        "available": True
    },

    # ============================================================
    # === NEW v1.5.3.4 — Next-Gen Models (ONNX) ===
    # ============================================================
    "span-x2": {
        "name": "SPAN x2 (Fastest Quality)",
        "url": "https://github.com/jcj83429/upscaling/raw/f73a3a02874360ec6ced18f8bdd8e43b5d7bba57/2xLiveActionV1_SPAN/2xLiveActionV1_SPAN_490000.onnx",
        "scale": 2,
        "description": "SPAN — fastest quality model, NTIRE 2023 winner. Best for real-time video.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "span",
        "available": True
    },
    "span-x4": {
        "name": "SPAN x4 (Fastest Quality)",
        "url": "https://huggingface.co/mp3pintyo/upscale/resolve/main/4xSPANkendata_fp32.onnx",
        "scale": 4,
        "description": "SPAN 4x — fast quality upscaling, great speed/quality balance.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "span",
        "available": True
    },
    "realesrgan-x2-plus": {
        "name": "Real-ESRGAN x2+ (General)",
        "url": "https://huggingface.co/tidus2102/Real-ESRGAN/resolve/main/Real-ESRGAN_x2plus.onnx",
        "scale": 2,
        "description": "Real-ESRGAN x2 Plus — high quality 2x for photos and live-action.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "realesrgan",
        "available": True
    },
    "realesrgan-animevideo-x4": {
        "name": "Real-ESRGAN AnimeVideo x4",
        "url": "https://huggingface.co/tidus2102/Real-ESRGAN/resolve/main/RealESR-AnimeVideo-v3_x4.onnx",
        "scale": 4,
        "description": "Real-ESRGAN trained specifically for anime video. Optimized for temporal consistency.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "realesrgan",
        "available": True
    },
    "swinir-x4": {
        "name": "SwinIR x4 (Transformer Quality)",
        "url": "https://huggingface.co/rocca/swin-ir-onnx/resolve/main/003_realSR_BSRGAN_DFO_s64w8_SwinIR-M_x4_GAN.onnx",
        "scale": 4,
        "description": "SwinIR — Swin Transformer for image restoration. Best quality for photos & live-action.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "swinir",
        "available": True
    },

}


def detect_hardware():
    """Detect GPU and CPU hardware information."""
    # Detect CPU
    state.cpu_name = platform.processor() or "Unknown CPU"
    state.cpu_cores = os.cpu_count() or 0
    
    # Try to get better CPU name on Linux
    try:
        if os.path.exists("/proc/cpuinfo"):
            with open("/proc/cpuinfo", "r") as f:
                for line in f:
                    if "model name" in line:
                        state.cpu_name = line.split(":")[1].strip()
                        break
    except Exception as e:
        logger.debug(f"Failed to read /proc/cpuinfo for CPU name: {e}")

    gpu_detected = False
    
    # Try NVIDIA GPU first (nvidia-smi) — enumerate ALL GPUs
    try:
        result = subprocess.run(
            ["nvidia-smi", "--query-gpu=index,name,memory.total", "--format=csv,noheader,nounits"],
            capture_output=True, text=True, timeout=5
        )
        if result.returncode == 0:
            for line in result.stdout.strip().split("\n"):
                parts = [p.strip() for p in line.split(",")]
                if len(parts) >= 3:
                    gpu_entry = {
                        "index": int(parts[0]),
                        "name": parts[1],
                        "memory": f"{int(parts[2])} MB",
                        "type": "nvidia"
                    }
                    state.gpu_list.append(gpu_entry)
            if state.gpu_list:
                # Use the selected device or first GPU
                idx = min(state.gpu_device_id, len(state.gpu_list) - 1)
                state.gpu_name = state.gpu_list[idx]["name"]
                state.gpu_memory = state.gpu_list[idx]["memory"]
                gpu_detected = True
                logger.info(f"Detected {len(state.gpu_list)} NVIDIA GPU(s): {[g['name'] for g in state.gpu_list]}")

            # Detect compute capability for Blackwell identification
            try:
                cc_result = subprocess.run(
                    ["nvidia-smi", "--query-gpu=compute_cap", "--format=csv,noheader"],
                    capture_output=True, text=True, timeout=10
                )
                if cc_result.returncode == 0:
                    for i, cc_line in enumerate(cc_result.stdout.strip().split('\n')):
                        cc = cc_line.strip()
                        if cc and i < len(state.gpu_list):
                            state.gpu_list[i]['compute_capability'] = cc
                            cc_major = int(cc.split('.')[0]) if '.' in cc else 0
                            if cc_major >= 12:
                                logger.info(f"  GPU {i}: Blackwell architecture detected (sm_{cc.replace('.', '')})")
                            elif cc_major >= 8:
                                logger.info(f"  GPU {i}: Ampere/Ada architecture (sm_{cc.replace('.', '')})")
            except Exception as e:
                logger.debug(f"Could not detect compute capability: {e}")

    except Exception as e:
        logger.debug(f"NVIDIA GPU not detected: {e}")
    
    # Try AMD GPU (rocm-smi)
    if not gpu_detected:
        try:
            result = subprocess.run(
                ["rocm-smi", "--showproductname"],
                capture_output=True, text=True, timeout=5
            )
            if result.returncode == 0 and "GPU" in result.stdout:
                # Parse AMD GPU name from rocm-smi output
                for line in result.stdout.split("\n"):
                    if "Card series" in line or "GPU" in line:
                        state.gpu_name = line.split(":")[-1].strip() if ":" in line else "AMD GPU"
                        break
                else:
                    state.gpu_name = "AMD ROCm GPU"
                
                # Try to get VRAM
                try:
                    mem_result = subprocess.run(
                        ["rocm-smi", "--showmeminfo", "vram"],
                        capture_output=True, text=True, timeout=5
                    )
                    if mem_result.returncode == 0:
                        for line in mem_result.stdout.split("\n"):
                            if "Total" in line:
                                # Extract memory size
                                parts = line.split()
                                for i, p in enumerate(parts):
                                    if p.isdigit():
                                        state.gpu_memory = f"{int(p)} MB"
                                        break
                except Exception as e:
                    logger.debug(f"Failed to detect AMD GPU VRAM via rocm-smi: {e}")
                    state.gpu_memory = "Unknown"

                gpu_detected = True
                logger.info(f"Detected AMD GPU: {state.gpu_name}")
        except Exception as e:
            logger.debug(f"AMD GPU not detected: {e}")
    
    # Try Intel GPU (via /dev/dri and lspci)
    if not gpu_detected:
        try:
            # Check if /dev/dri exists (Intel GPU render node)
            dri_path = Path("/dev/dri")
            render_nodes = list(dri_path.glob("renderD*")) if dri_path.exists() else []

            # Log /dev/dri status for diagnostics
            if dri_path.exists():
                try:
                    dri_contents = list(dri_path.iterdir())
                    logger.info(f"Intel GPU: /dev/dri contents: {[str(f) for f in dri_contents]}")
                    # Check permissions
                    for rn in render_nodes:
                        import stat
                        st = rn.stat()
                        logger.info(f"Intel GPU: {rn} permissions: {oct(st.st_mode)}, gid: {st.st_gid}")
                except Exception as perm_err:
                    logger.warning(f"Intel GPU: Cannot read /dev/dri: {perm_err}")
            else:
                logger.info("Intel GPU: /dev/dri not found. Ensure Docker has --device=/dev/dri or privileged mode.")

            # Check clinfo for OpenCL platform diagnostics
            try:
                clinfo_result = subprocess.run(
                    ["clinfo", "--list"],
                    capture_output=True, text=True, timeout=5
                )
                if clinfo_result.returncode == 0:
                    logger.info(f"Intel GPU: clinfo platforms:\n{clinfo_result.stdout.strip()}")
                else:
                    logger.warning(f"Intel GPU: clinfo returned no platforms. "
                                   f"Check that intel-compute-runtime is installed and /dev/dri is mapped.")
            except FileNotFoundError:
                logger.debug("Intel GPU: clinfo not installed")
            except Exception as cl_err:
                logger.debug(f"Intel GPU: clinfo check failed: {cl_err}")

            if render_nodes:
                # Try lspci to get Intel GPU name
                gpu_name = "Intel GPU"
                try:
                    result = subprocess.run(
                        ["lspci"],
                        capture_output=True, text=True, timeout=5
                    )
                    if result.returncode == 0:
                        for line in result.stdout.split("\n"):
                            if ("VGA" in line or "Display" in line or "3D" in line) and "Intel" in line:
                                gpu_name = line.split(":")[-1].strip()
                                break
                except Exception as e:
                    logger.debug(f"Failed to detect Intel GPU name via lspci: {e}")

                # Also check via /sys for device info
                if gpu_name == "Intel GPU":
                    try:
                        for card_dir in Path("/sys/class/drm").glob("card*/device"):
                            vendor_path = card_dir / "vendor"
                            if vendor_path.exists():
                                vendor = vendor_path.read_text().strip()
                                if vendor == "0x8086":  # Intel vendor ID
                                    device_path = card_dir / "device"
                                    if device_path.exists():
                                        device_id = device_path.read_text().strip()
                                        gpu_name = f"Intel GPU (Device {device_id})"
                                    break
                    except Exception as e:
                        logger.debug(f"Failed to detect Intel GPU via /sys/class/drm: {e}")

                state.gpu_name = gpu_name
                state.gpu_memory = "Shared Memory"
                # Enumerate Intel GPUs by render nodes
                for i, rn in enumerate(render_nodes):
                    state.gpu_list.append({
                        "index": i,
                        "name": gpu_name if i == 0 else f"Intel GPU {i}",
                        "memory": "Shared Memory",
                        "type": "intel",
                        "render_node": str(rn)
                    })
                gpu_detected = True
                logger.info(f"Detected Intel GPU: {state.gpu_name} (render nodes: {[str(r) for r in render_nodes]})")

            # If no render nodes but OpenVINO is available, still mark as detected
            elif ONNX_AVAILABLE and 'OpenVINOExecutionProvider' in ort.get_available_providers():
                state.gpu_name = "Intel OpenVINO (CPU inference only)"
                state.gpu_memory = "Shared"
                gpu_detected = True
                logger.warning(
                    "Intel OpenVINO available but no /dev/dri render nodes found. "
                    "Models will run on CPU. To enable GPU acceleration:\n"
                    "  1. Pass --device=/dev/dri to Docker\n"
                    "  2. Ensure intel-compute-runtime is installed in the container\n"
                    "  3. Add user to 'render' and 'video' groups"
                )

        except Exception as e:
            logger.debug(f"Intel GPU not detected: {e}")
    
    # Try Apple Silicon (macOS)
    if not gpu_detected and platform.system() == "Darwin":
        try:
            # Check for Apple Silicon
            result = subprocess.run(
                ["sysctl", "-n", "machdep.cpu.brand_string"],
                capture_output=True, text=True, timeout=5
            )
            if result.returncode == 0:
                cpu_brand = result.stdout.strip()
                if "Apple" in cpu_brand:
                    state.gpu_name = f"Apple Silicon ({cpu_brand})"
                    # Apple Silicon has unified memory
                    try:
                        mem_result = subprocess.run(
                            ["sysctl", "-n", "hw.memsize"],
                            capture_output=True, text=True, timeout=5
                        )
                        if mem_result.returncode == 0:
                            mem_bytes = int(mem_result.stdout.strip())
                            state.gpu_memory = f"{mem_bytes // (1024**3)} GB (Unified)"
                    except Exception as e:
                        logger.debug(f"Failed to detect Apple Silicon memory size: {e}")
                        state.gpu_memory = "Unified Memory"
                    gpu_detected = True
                    logger.info(f"Detected Apple Silicon: {state.gpu_name}")
        except Exception as e:
            logger.debug(f"Apple Silicon not detected: {e}")
    
    # No GPU detected
    if not gpu_detected:
        state.gpu_name = "No GPU detected (CPU-only mode)"
        state.gpu_memory = "N/A"


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Application startup and shutdown."""
    logger.info(f"Starting AI Upscaler Service v{VERSION}...")
    
    # Create directories
    MODELS_DIR.mkdir(parents=True, exist_ok=True)
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    
    # Detect hardware
    detect_hardware()
    logger.info(f"CPU: {state.cpu_name} ({state.cpu_cores} cores)")
    logger.info(f"GPU: {state.gpu_name} ({state.gpu_memory})")
    
    # Detect available ONNX providers
    if ONNX_AVAILABLE:
        state.providers = ort.get_available_providers()
        logger.info(f"ONNX Runtime Providers: {state.providers}")
    else:
        state.providers = ["OpenCV-DNN"]
        logger.warning("ONNX Runtime not available. Install with: pip install onnxruntime-gpu")
    
    state.use_gpu = os.getenv("USE_GPU", "true").lower() == "true"
    state.max_concurrent = int(os.getenv("MAX_CONCURRENT_REQUESTS", "4"))
    state.gpu_device_id = int(os.getenv("GPU_DEVICE_ID", "0"))

    # Re-create semaphore with actual env var value
    global _upscale_semaphore
    _upscale_semaphore = _asyncio.Semaphore(state.max_concurrent)
    
    logger.info(f"GPU Enabled: {state.use_gpu}")
    logger.info(f"ONNX Runtime Available: {ONNX_AVAILABLE}")
    
    # Count available models
    available_count = sum(1 for m in AVAILABLE_MODELS.values() if m.get("available", True))
    logger.info(f"Available Models: {available_count}/{len(AVAILABLE_MODELS)}")
    
    # Load default model if specified
    default_model = os.getenv("DEFAULT_MODEL")
    if default_model and AVAILABLE_MODELS.get(default_model, {}).get("available", True):
        await download_model(default_model)
        await load_model(default_model)
    
    yield
    
    logger.info("Shutting down AI Upscaler Service...")


app = FastAPI(
    title="AI Upscaler Service",
    description="Neural network image upscaling service for Jellyfin",
    version=VERSION,
    lifespan=lifespan
)

# Mount static files
if STATIC_DIR.exists():
    app.mount("/static", StaticFiles(directory=str(STATIC_DIR)), name="static")


def get_model_path(model_name: str) -> Path:
    """Get the file path for a model."""
    model_info = AVAILABLE_MODELS.get(model_name, {})
    ext = model_info.get("type", "pb")
    return MODELS_DIR / f"{model_name}.{ext}"


async def load_opencv_model(model_name: str, model_info: dict, model_path: Path) -> bool:
    """Load an OpenCV DNN Super Resolution model."""
    try:
        model_type = model_info.get("model_type", "fsrcnn")
        scale = model_info.get("scale", 2)
        
        # Create OpenCV DNN Super Resolution object
        sr = cv2.dnn_superres.DnnSuperResImpl_create()
        sr.readModel(str(model_path))
        sr.setModel(model_type, scale)
        
        # Set GPU if available and enabled
        if state.use_gpu:
            try:
                sr.setPreferableBackend(cv2.dnn.DNN_BACKEND_CUDA)
                sr.setPreferableTarget(cv2.dnn.DNN_TARGET_CUDA)
                logger.info("Using CUDA backend for OpenCV DNN")
            except Exception as e:
                logger.warning(f"CUDA not available for OpenCV DNN: {e}")
                sr.setPreferableBackend(cv2.dnn.DNN_BACKEND_DEFAULT)
                sr.setPreferableTarget(cv2.dnn.DNN_TARGET_CPU)
        
        with _model_lock:
            state.cv_model = sr
            state.cv_model_name = model_name
            state.cv_model_scale = scale
            state.current_model = model_name
            state.current_model_type = "opencv"
            state.onnx_session = None
        
        logger.info(f"OpenCV model {model_name} loaded successfully (scale={scale})")
        return True
        
    except Exception as e:
        state.last_load_error = str(e)
        logger.error(f"Failed to load OpenCV model {model_name}: {e}")
        return False


async def load_model(model_name: str) -> bool:
    """Load a model into memory."""
    if model_name not in AVAILABLE_MODELS:
        logger.error(f"Unknown model: {model_name}")
        return False
    
    model_info = AVAILABLE_MODELS[model_name]
    
    if not model_info.get("available", True):
        logger.error(f"Model {model_name} is not yet available")
        return False
    
    model_path = get_model_path(model_name)
    
    if not model_path.exists():
        logger.error(f"Model not found: {model_path}")
        return False
    
    model_type = model_info.get("type", "pb")
    
    if model_type == "pb":
        return await load_opencv_model(model_name, model_info, model_path)
    elif model_type == "onnx":
        return await load_onnx_model(model_name, model_info, model_path)
    else:
        logger.error(f"Model type {model_type} not yet supported")
        return False


def _probe_tensorrt_subprocess(model_path_str: str, device_id: int) -> bool:
    """Test TensorRT in an isolated subprocess to avoid poisoning the CUDA context.
    Returns True if TensorRT works, False otherwise."""
    try:
        probe_code = """
import onnxruntime as ort
import sys
try:
    model_path = sys.argv[1]
    dev_id = sys.argv[2]
    opts = ort.SessionOptions()
    opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    providers = ['TensorrtExecutionProvider', 'CUDAExecutionProvider', 'CPUExecutionProvider']
    provider_options = [
        {'device_id': dev_id, 'trt_max_workspace_size': '2147483648'},
        {'device_id': dev_id},
        {}
    ]
    sess = ort.InferenceSession(model_path, opts, providers=providers, provider_options=provider_options)
    actual = sess.get_providers()
    if 'TensorrtExecutionProvider' in actual:
        print('OK')
        sys.exit(0)
    else:
        print('NO_TRT')
        sys.exit(1)
except Exception as e:
    print(f'FAIL:{e}')
    sys.exit(1)
"""
        result = subprocess.run(
            ["python3", "-c", probe_code, model_path_str, str(device_id)],
            capture_output=True, text=True, timeout=60
        )
        if result.returncode == 0 and 'OK' in result.stdout:
            return True
        logger.debug(f"TensorRT probe result: {result.stdout.strip()} {result.stderr.strip()}")
    except Exception as e:
        logger.debug(f"TensorRT subprocess probe failed: {e}")
    return False


async def load_onnx_model(model_name: str, model_info: dict, model_path: Path) -> bool:
    """Load an ONNX model (Real-ESRGAN) into memory with robust GPU fallback.

    Chain order (CUDA first to avoid TensorRT poisoning the CUDA context):
    1. CUDA + CPU (reliable, fast to test)
    2. OpenVINO GPU + CPU (Intel GPUs)
    3. CPU only (always works)

    After CUDA succeeds, optionally probe TensorRT in a subprocess.
    If TensorRT works, reload with TensorRT for better performance.
    """
    if not ONNX_AVAILABLE:
        logger.error("ONNX Runtime not available. Cannot load ONNX models.")
        return False

    try:
        scale = model_info.get("scale", 4)
        skip_tensorrt = os.getenv("SKIP_TENSORRT", "false").lower() == "true"
        device_id = state.gpu_device_id

        # Check for provider override via environment variable
        override_providers = os.getenv("ONNX_PROVIDERS")
        if override_providers:
            providers = [p.strip() for p in override_providers.split(",")]
            logger.info(f"Using override providers: {providers}")
            try:
                session = ort.InferenceSession(str(model_path), providers=providers)
                with _model_lock:
                    state.onnx_session = session
                    state.current_model = model_name
                    state.current_model_type = "onnx"
                    state.onnx_model_scale = model_info.get("scale", 4)
                    state.onnx_model_name = model_name
                    state.cv_model = None
                state.providers = session.get_providers()
                logger.info(f"ONNX model {model_name} loaded with override providers: {state.providers}")
                return True
            except Exception as e:
                logger.warning(f"Override providers failed: {e}, falling back to auto-detection")

        # Detect available providers
        available_providers = ort.get_available_providers()
        logger.info(f"Available ONNX Runtime providers: {available_providers}")
        logger.info(f"GPU device ID: {device_id}, SKIP_TENSORRT: {skip_tensorrt}")

        # Build provider chains — CUDA first (safe), TensorRT probed separately
        provider_chains = []

        if state.use_gpu:
            # Chain 1: CUDA + CPU (most reliable GPU path — try FIRST)
            if 'CUDAExecutionProvider' in available_providers:
                provider_chains.append({
                    'providers': ['CUDAExecutionProvider', 'CPUExecutionProvider'],
                    'options': [{'device_id': str(device_id)}, {}],
                    'name': 'CUDA'
                })

            # Chain 2: OpenVINO GPU (Intel GPUs)
            if 'OpenVINOExecutionProvider' in available_providers:
                openvino_device = f'GPU.{device_id}' if device_id > 0 else 'GPU'
                provider_chains.append({
                    'providers': ['OpenVINOExecutionProvider', 'CPUExecutionProvider'],
                    'options': [{'device_type': openvino_device, 'precision': 'FP32'}, {}],
                    'name': 'OpenVINO GPU'
                })

        # Chain: CoreML + CPU (macOS with Apple Silicon — M1/M2/M3/M4/M5)
        if platform.system() == "Darwin" and platform.machine() == "arm64":
            try:
                if "CoreMLExecutionProvider" in available_providers:
                    logger.info("Attempting CoreML (Apple Neural Engine) provider chain...")
                    coreml_options = {
                        "coreml_flags": 0  # 0 = default, uses Neural Engine when available
                    }
                    providers_to_try = [
                        ("CoreMLExecutionProvider", coreml_options),
                        "CPUExecutionProvider"
                    ]
                    try:
                        session = ort.InferenceSession(str(model_path), providers=providers_to_try)
                        active = session.get_providers()
                        if "CoreMLExecutionProvider" in active:
                            logger.info("CoreML provider active — using Apple Neural Engine")
                            with _model_lock:
                                state.onnx_session = session
                                state.current_model = model_name
                                state.current_model_type = "onnx"
                                state.onnx_model_scale = model_info.get("scale", 4)
                                state.onnx_model_name = model_name
                                state.use_gpu = True
                                state.gpu_name = f"Apple Neural Engine ({platform.processor() or 'Apple Silicon'})"
                                state.cv_model = None
                            state.providers = active
                            logger.info(f"ONNX model {model_name} loaded with CoreML: {active}")
                            return True
                    except Exception as e:
                        logger.warning(f"CoreML provider failed: {e}, falling back...")
            except Exception as e:
                logger.debug(f"CoreML detection error: {e}")

        # Chain 3: CPU only (always works)
        provider_chains.append({
            'providers': ['CPUExecutionProvider'],
            'options': [{}],
            'name': 'CPU'
        })

        # Try each chain
        session = None
        last_error = None

        for chain_idx, chain in enumerate(provider_chains):
            providers = chain['providers']
            provider_options = chain['options']
            chain_name = chain['name']

            try:
                logger.info(f"Trying chain {chain_idx + 1}/{len(provider_chains)}: {chain_name} ({providers})")

                chain_sess_options = ort.SessionOptions()
                chain_sess_options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL

                session = ort.InferenceSession(
                    str(model_path), chain_sess_options,
                    providers=providers, provider_options=provider_options
                )

                actual_providers = session.get_providers()
                logger.info(f"Session created with providers: {actual_providers}")

                # Verify GPU is actually active
                gpu_providers = [p for p in actual_providers if p != 'CPUExecutionProvider']

                if state.use_gpu and gpu_providers:
                    # GPU is active — verify with a real inference test
                    try:
                        test_input = np.random.rand(1, 3, 16, 16).astype(np.float32)
                        input_name = session.get_inputs()[0].name
                        session.run(None, {input_name: test_input})
                        logger.info(f"GPU inference verification passed ({gpu_providers[0]})")
                    except Exception as verify_err:
                        logger.warning(f"GPU inference verification failed: {verify_err}")
                        session = None
                        continue

                    logger.info(f"GPU acceleration active: {gpu_providers[0]}")
                    break

                elif state.use_gpu and chain_idx < len(provider_chains) - 1:
                    logger.warning(f"Session created but only CPU active, trying next chain...")
                    session = None
                    continue
                else:
                    # CPU chain or last resort
                    break

            except Exception as e:
                last_error = e
                logger.warning(f"Chain {chain_idx + 1} ({chain_name}) failed: {e}")

                # OpenVINO GPU failed — try CPU device before giving up
                if 'OpenVINOExecutionProvider' in providers and 'GPU' in str(e):
                    try:
                        logger.info("OpenVINO GPU failed, trying OpenVINO CPU device...")
                        cpu_options = [{'device_type': 'CPU'}, {}]
                        session = ort.InferenceSession(
                            str(model_path), chain_sess_options,
                            providers=providers, provider_options=cpu_options
                        )
                        actual_providers = session.get_providers()
                        logger.warning("OpenVINO running on CPU (GPU compute runtime not available)")
                        state.use_gpu = False
                        break
                    except Exception as e2:
                        logger.warning(f"OpenVINO CPU also failed: {e2}")

                session = None
                continue

        if session is None:
            logger.error(f"All provider chains failed for {model_name}. Last error: {last_error}")
            return False

        # If we got CUDA and TensorRT is available, probe TensorRT in subprocess
        actual_providers = session.get_providers()
        if (not skip_tensorrt
                and 'CUDAExecutionProvider' in actual_providers
                and 'TensorrtExecutionProvider' in available_providers):
            logger.info("Probing TensorRT in isolated subprocess...")
            loop = asyncio.get_running_loop()
            trt_ok = await loop.run_in_executor(
                None, _probe_tensorrt_subprocess, str(model_path), device_id
            )
            if trt_ok:
                logger.info("TensorRT probe succeeded — reloading with TensorRT...")
                try:
                    trt_opts = ort.SessionOptions()
                    trt_opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
                    trt_session = ort.InferenceSession(
                        str(model_path), trt_opts,
                        providers=['TensorrtExecutionProvider', 'CUDAExecutionProvider', 'CPUExecutionProvider'],
                        provider_options=[
                            {'device_id': str(device_id), 'trt_max_workspace_size': '2147483648'},
                            {'device_id': str(device_id)},
                            {}
                        ]
                    )
                    trt_actual = trt_session.get_providers()
                    if 'TensorrtExecutionProvider' in trt_actual:
                        session = trt_session
                        actual_providers = trt_actual
                        logger.info(f"TensorRT active: {actual_providers}")
                    else:
                        logger.info("TensorRT not in active providers after reload, keeping CUDA")
                except Exception as trt_err:
                    logger.warning(f"TensorRT reload failed (keeping CUDA): {trt_err}")
            else:
                logger.info("TensorRT probe failed — keeping CUDA (no context poisoning)")

        with _model_lock:
            state.onnx_session = session
            state.onnx_model_name = model_name
            state.onnx_model_scale = scale
            state.current_model = model_name
            state.current_model_type = "onnx"
            state.cv_model = None

        # Update providers list with actual active providers
        actual_providers = session.get_providers()
        state.providers = actual_providers
        logger.info(f"ONNX model {model_name} loaded successfully with: {actual_providers}")

        return True

    except Exception as e:
        state.last_load_error = str(e)
        logger.error(f"Failed to load ONNX model {model_name}: {e}")
        return False


async def download_model(model_name: str) -> bool:
    """Download a model from the repository."""
    if model_name not in AVAILABLE_MODELS:
        logger.error(f"Unknown model: {model_name}")
        return False
    
    model_info = AVAILABLE_MODELS[model_name]
    
    if not model_info.get("available", True):
        logger.error(f"Model {model_name} is not yet available for download")
        return False
    
    model_path = get_model_path(model_name)
    
    if model_path.exists():
        logger.info(f"Model {model_name} already exists")
        return True
    
    try:
        download_url = model_info.get("url")
        
        logger.info(f"Downloading model {model_name} from {download_url}")
        
        async with httpx.AsyncClient(timeout=600.0) as client:
            response = await client.get(download_url, follow_redirects=True)
            response.raise_for_status()

            # Use run_in_executor to avoid blocking the event loop on large file writes
            loop = asyncio.get_running_loop()
            await loop.run_in_executor(None, model_path.write_bytes, response.content)
        
        size_mb = model_path.stat().st_size / 1024 / 1024
        logger.info(f"Model {model_name} downloaded ({size_mb:.1f} MB)")
        return True
        
    except Exception as e:
        logger.error(f"Failed to download model {model_name}: {e}")
        if model_path.exists():
            model_path.unlink()
        return False


def upscale_image(image_bytes: bytes) -> bytes:
    """Upscale an image using the loaded model (OpenCV or ONNX)."""
    if state.current_model is None:
        raise ValueError("No model loaded")
    
    # Decode image
    nparr = np.frombuffer(image_bytes, np.uint8)
    img = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
    
    if img is None:
        raise ValueError("Failed to decode image")
    
    if state.current_model_type == "opencv" and state.cv_model is not None:
        # Upscale using OpenCV DNN Super Resolution
        result = state.cv_model.upsample(img)
    elif state.current_model_type == "onnx" and state.onnx_session is not None:
        # Upscale using ONNX Runtime (Real-ESRGAN)
        result = upscale_with_onnx(img)
    else:
        raise ValueError("No model loaded")
    
    # Encode as PNG
    _, buffer = cv2.imencode('.png', result)
    return buffer.tobytes()


def _onnx_infer_tile(img_rgb_float: np.ndarray, session, input_name: str, output_name: str) -> np.ndarray:
    """Run ONNX inference on a single tile (HWC float32 [0,1] RGB). Returns HWC float32 RGB."""
    img_nchw = np.transpose(img_rgb_float, (2, 0, 1))  # HWC to CHW
    img_batch = np.expand_dims(img_nchw, axis=0)  # Add batch dimension
    result = session.run([output_name], {input_name: img_batch})[0]
    result = np.squeeze(result, axis=0)
    result = np.transpose(result, (1, 2, 0))  # CHW to HWC
    return result


def upscale_with_onnx(img: np.ndarray) -> np.ndarray:
    """Upscale an image using the loaded ONNX model (Real-ESRGAN).

    Uses tile-based processing for large images to prevent GPU OOM.
    Tile size controlled by ONNX_TILE_SIZE env var (default 512).
    Overlap of 32px between tiles prevents seam artifacts.
    """
    tile_size = ONNX_TILE_SIZE
    overlap = 32
    h, w = img.shape[:2]

    # Convert BGR to RGB and normalize to [0, 1]
    img_rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0

    # Acquire model lock to prevent reading a half-swapped model
    with _model_lock:
        session = state.onnx_session
        if session is None:
            raise ValueError("ONNX session not loaded")
        input_name = session.get_inputs()[0].name
        output_name = session.get_outputs()[0].name
        scale = state.onnx_model_scale

    # Small image: skip tiling, process directly
    if w <= tile_size and h <= tile_size:
        with _model_lock:
            session = state.onnx_session
            if session is None:
                raise ValueError("ONNX session not loaded")
        result = _onnx_infer_tile(img_rgb, session, input_name, output_name)
        result = np.clip(result * 255.0, 0, 255).astype(np.uint8)
        return cv2.cvtColor(result, cv2.COLOR_RGB2BGR)

    # Tile-based processing for large images
    step = tile_size - overlap
    out_h, out_w = h * scale, w * scale
    output = np.zeros((out_h, out_w, 3), dtype=np.float64)
    weight = np.zeros((out_h, out_w, 3), dtype=np.float64)

    # Build a per-tile blending mask (linear ramp on overlap borders)
    def _make_blend_weight(th: int, tw: int) -> np.ndarray:
        """Create a 2D blending weight array that tapers at edges for seamless stitching."""
        wy = np.ones(th, dtype=np.float64)
        wx = np.ones(tw, dtype=np.float64)
        if overlap > 0:
            ramp = np.linspace(0, 1, overlap, dtype=np.float64)
            # Top / left ramp
            wy[:overlap] = ramp
            wx[:overlap] = ramp
            # Bottom / right ramp
            wy[-overlap:] = ramp[::-1]
            wx[-overlap:] = ramp[::-1]
        return wy[:, None] * wx[None, :]  # (th, tw)

    for y in range(0, h, step):
        for x in range(0, w, step):
            # Clamp tile to image boundaries
            y_end = min(y + tile_size, h)
            x_end = min(x + tile_size, w)
            y_start = max(y_end - tile_size, 0)
            x_start = max(x_end - tile_size, 0)

            tile = img_rgb[y_start:y_end, x_start:x_end]

            # Inference (lock protects against model swap during run)
            with _model_lock:
                session = state.onnx_session
                if session is None:
                    raise ValueError("ONNX session not loaded")
            out_tile = _onnx_infer_tile(tile, session, input_name, output_name)

            # Compute output coordinates
            oy_start = y_start * scale
            ox_start = x_start * scale
            oy_end = y_end * scale
            ox_end = x_end * scale
            actual_oh = oy_end - oy_start
            actual_ow = ox_end - ox_start

            # Trim inference output to match expected size (model may round)
            out_tile = out_tile[:actual_oh, :actual_ow]

            blend_w = _make_blend_weight(actual_oh, actual_ow)
            blend_w3 = blend_w[:, :, None]  # broadcast to 3 channels

            output[oy_start:oy_end, ox_start:ox_end] += out_tile.astype(np.float64) * blend_w3
            weight[oy_start:oy_end, ox_start:ox_end] += blend_w3

    # Normalize by accumulated weight
    weight = np.maximum(weight, 1e-8)
    output = output / weight

    result = np.clip(output * 255.0, 0, 255).astype(np.uint8)
    return cv2.cvtColor(result, cv2.COLOR_RGB2BGR)


def upscale_image_array(img: np.ndarray) -> np.ndarray:
    """Upscale a numpy image array using the loaded model. Avoids double encode/decode for frame pipeline."""
    with _model_lock:
        if state.current_model is None:
            raise ValueError("No model loaded")
        model_type = state.current_model_type
        cv_model = state.cv_model
        onnx_session = state.onnx_session

    if model_type == "opencv" and cv_model is not None:
        return cv_model.upsample(img)
    elif model_type == "onnx" and onnx_session is not None:
        return upscale_with_onnx(img)
    else:
        raise ValueError("No model loaded")


def run_benchmark(test_size: int = 256) -> dict:
    """Run a quick benchmark on the loaded model."""
    if state.current_model is None:
        return {"error": "No model loaded"}
    
    # Adjust test_size for models that require specific input dimensions
    # Real-ESRGAN x4 models effectively work on tiles, but for benchmarking we keep it small and standard.
    # 64x64 input -> 256x256 output (x4)
    if state.current_model_type == "onnx" and "realesrgan" in state.current_model:
        test_size = 64

    # Create test image
    test_img = np.random.randint(0, 255, (test_size, test_size, 3), dtype=np.uint8)
    _, test_bytes = cv2.imencode('.png', test_img)
    test_bytes = test_bytes.tobytes()
    
    # Warmup
    try:
        _ = upscale_image(test_bytes)
    except Exception as e:
        return {"error": f"Warmup failed: {e}"}
    
    # Benchmark
    times = []
    for _ in range(5):
        start = time.time()
        _ = upscale_image(test_bytes)
        times.append(time.time() - start)
    
    avg_time = sum(times) / len(times)
    fps = 1.0 / avg_time if avg_time > 0 else 0
    
    scale = state.onnx_model_scale if state.current_model_type == "onnx" else state.cv_model_scale
    
    result = {
        "model": state.current_model,
        "model_type": state.current_model_type,
        "scale": scale,
        "input_size": f"{test_size}x{test_size}",
        "output_size": f"{test_size * scale}x{test_size * scale}",
        "avg_time_ms": round(avg_time * 1000, 2),
        "fps": round(fps, 2),
        "using_gpu": state.use_gpu
    }
    
    state.last_benchmark = result
    return result


# API Endpoints

@app.get("/", response_class=HTMLResponse)
async def root():
    """Serve the web UI."""
    html_path = STATIC_DIR / "index.html"
    if html_path.exists():
        return HTMLResponse(content=html_path.read_text())
    return HTMLResponse(content="<h1>AI Upscaler Service</h1><p>UI not found</p>")


@app.get("/health")
async def health():
    """Health check endpoint."""
    return {
        "status": "healthy",
        "model_loaded": state.current_model is not None,
        "model_name": state.current_model,
        "model_type": state.current_model_type,
        "providers": state.providers,
        "using_gpu": state.use_gpu,
        "gpu_name": state.gpu_name
    }


@app.get("/status")
async def status():
    """Get service status."""
    scale = state.onnx_model_scale if state.current_model_type == "onnx" else (state.cv_model_scale if state.cv_model else None)
    
    # Check if CUDA is active
    has_cuda = any("CUDA" in p for p in state.providers)
    has_tensorrt = any("Tensorrt" in p for p in state.providers)
    
    return {
        "status": "running",
        "version": VERSION,
        "current_model": state.current_model,
        "model_type": state.current_model_type,
        "available_providers": state.providers,
        "using_gpu": state.use_gpu,
        "loaded_models": [state.current_model] if state.current_model else [],
        "processing_count": state.processing_count,
        "max_concurrent": state.max_concurrent,
        "onnx_available": ONNX_AVAILABLE,
        "model_scale": scale,
        "cuda_available": has_cuda,
        "tensorrt_available": has_tensorrt
    }


@app.get("/hardware")
async def hardware_info():
    """Get hardware information."""
    has_cuda = any("CUDA" in p for p in state.providers)

    return {
        "gpu": {
            "name": state.gpu_name,
            "memory": state.gpu_memory,
            "cuda_available": has_cuda,
            "tensorrt_available": any("Tensorrt" in p for p in state.providers),
            "device_id": state.gpu_device_id
        },
        "cpu": {
            "name": state.cpu_name,
            "cores": state.cpu_cores
        },
        "providers": state.providers,
        "using_gpu": state.use_gpu,
        "gpu_list": state.gpu_list
    }


@app.get("/gpus")
async def list_gpus():
    """Enumerate available GPUs for multi-GPU selection."""
    gpus = []

    # Try NVIDIA GPUs via nvidia-smi
    try:
        result = subprocess.run(
            ["nvidia-smi", "--query-gpu=index,name,memory.total,memory.free,driver_version",
             "--format=csv,noheader,nounits"],
            capture_output=True, text=True, timeout=10
        )
        if result.returncode == 0:
            for line in result.stdout.strip().split("\n"):
                parts = [p.strip() for p in line.split(",")]
                if len(parts) >= 4:
                    gpus.append({
                        "index": int(parts[0]),
                        "name": parts[1],
                        "memory_total_mb": int(float(parts[2])),
                        "memory_free_mb": int(float(parts[3])),
                        "driver": parts[4] if len(parts) > 4 else "unknown",
                        "type": "nvidia"
                    })
    except Exception:
        pass

    # Try Intel GPUs via render nodes
    try:
        drm_path = Path("/sys/class/drm")
        if drm_path.exists():
            for render_node in sorted(drm_path.glob("renderD*")):
                device_path = render_node / "device"
                vendor_path = device_path / "vendor"
                if vendor_path.exists():
                    vendor = vendor_path.read_text().strip()
                    if vendor == "0x8086":  # Intel
                        device_name = "Intel GPU"
                        device_file = device_path / "device"
                        if device_file.exists():
                            device_id_hex = device_file.read_text().strip()
                            device_name = f"Intel GPU ({device_id_hex})"
                        idx = len(gpus)
                        gpus.append({
                            "index": idx,
                            "name": device_name,
                            "memory_total_mb": 0,
                            "memory_free_mb": 0,
                            "driver": "i915/xe",
                            "type": "intel"
                        })
    except Exception:
        pass

    return {
        "gpus": gpus,
        "total": len(gpus),
        "current_device_id": state.gpu_device_id
    }


@app.get("/gpu-verify")
async def gpu_verify():
    """Run GPU diagnostics — clinfo, nvidia-smi, ONNX providers."""
    diagnostics = {
        "onnx_providers": ort.get_available_providers() if ONNX_AVAILABLE else [],
        "active_providers": state.providers,
        "using_gpu": state.use_gpu,
        "gpu_device_id": state.gpu_device_id,
        "gpu_list": state.gpu_list
    }

    # clinfo for OpenCL/Intel
    try:
        result = subprocess.run(["clinfo", "--list"], capture_output=True, text=True, timeout=10)
        diagnostics["clinfo"] = result.stdout.strip() if result.returncode == 0 else f"error: {result.stderr.strip()}"
    except Exception as e:
        diagnostics["clinfo"] = f"not available: {e}"

    # nvidia-smi
    try:
        result = subprocess.run(["nvidia-smi", "--query-gpu=name,driver_version,memory.total",
                                 "--format=csv,noheader"], capture_output=True, text=True, timeout=10)
        diagnostics["nvidia_smi"] = result.stdout.strip() if result.returncode == 0 else f"error: {result.stderr.strip()}"
    except Exception as e:
        diagnostics["nvidia_smi"] = f"not available: {e}"

    # /dev/dri status (Intel GPU passthrough check)
    dri_path = Path("/dev/dri")
    if dri_path.exists():
        try:
            dri_contents = [str(f.name) for f in dri_path.iterdir()]
            diagnostics["dev_dri"] = {"exists": True, "contents": dri_contents}
        except Exception as e:
            diagnostics["dev_dri"] = {"exists": True, "error": str(e)}
    else:
        diagnostics["dev_dri"] = {"exists": False, "hint": "Pass --device=/dev/dri to Docker for Intel GPU access"}

    # SKIP_TENSORRT setting
    diagnostics["skip_tensorrt"] = os.getenv("SKIP_TENSORRT", "false").lower() == "true"

    # ONNX inference test
    if ONNX_AVAILABLE and state.onnx_session is not None:
        try:
            test_input = np.random.rand(1, 3, 16, 16).astype(np.float32)
            input_name = state.onnx_session.get_inputs()[0].name
            start = time.time()
            state.onnx_session.run(None, {input_name: test_input})
            elapsed = time.time() - start
            diagnostics["inference_test"] = {"status": "ok", "time_ms": round(elapsed * 1000, 2)}
        except Exception as e:
            diagnostics["inference_test"] = {"status": "failed", "error": str(e)}
    else:
        diagnostics["inference_test"] = {"status": "no_model_loaded"}

    # Troubleshooting tips based on detected issues
    tips = []
    if not state.use_gpu:
        tips.append("GPU disabled. Set USE_GPU=true environment variable to enable.")
    if diagnostics.get("clinfo", "").startswith("error") or "not available" in diagnostics.get("clinfo", ""):
        tips.append("OpenCL not working. For Intel GPUs: ensure intel-compute-runtime is installed and /dev/dri is mapped.")
    if "nvidia_smi" in diagnostics and "not available" in diagnostics["nvidia_smi"]:
        tips.append("NVIDIA GPU not detected. Ensure Docker has --gpus all or --runtime=nvidia.")
    if diagnostics.get("skip_tensorrt"):
        tips.append("TensorRT is skipped (SKIP_TENSORRT=true). Set to false if your GPU supports TensorRT.")
    diagnostics["troubleshooting_tips"] = tips

    return diagnostics


@app.get("/connections")
async def plugin_connections():
    """Get plugin connection status."""
    return {
        "connections": state.plugin_connections,
        "total": len(state.plugin_connections)
    }


@app.post("/connections/register")
async def register_connection(
    plugin_id: str = Form(...),
    jellyfin_url: str = Form(...)
):
    """Register a plugin connection."""
    connection = {
        "plugin_id": plugin_id,
        "jellyfin_url": jellyfin_url,
        "connected_at": time.strftime("%Y-%m-%d %H:%M:%S"),
        "last_ping": time.strftime("%Y-%m-%d %H:%M:%S")
    }
    
    # Update or add connection
    for i, conn in enumerate(state.plugin_connections):
        if conn["plugin_id"] == plugin_id:
            state.plugin_connections[i] = connection
            return {"status": "updated", "connection": connection}
    
    state.plugin_connections.append(connection)
    return {"status": "registered", "connection": connection}


@app.get("/models")
async def list_models():
    """List available models."""
    models = []
    for model_id, info in AVAILABLE_MODELS.items():
        model_path = get_model_path(model_id)
        is_available = info.get("available", True)
        models.append({
            "id": model_id,
            "name": info["name"],
            "description": info["description"],
            "scale": info["scale"],
            "category": info.get("category", "general"),
            "type": info.get("type", "pb"),
            "downloaded": model_path.exists() if is_available else False,
            "loaded": state.current_model == model_id,
            "available": is_available
        })
    return {"models": models, "total": len(models)}


@app.post("/models/download")
async def download_model_endpoint(model_name: str = Form(...)):
    """Download a model."""
    if model_name not in AVAILABLE_MODELS:
        raise HTTPException(status_code=404, detail=f"Model {model_name} not found")
    
    model_info = AVAILABLE_MODELS[model_name]
    if not model_info.get("available", True):
        raise HTTPException(status_code=400, detail=f"Model {model_name} is not yet available. Real-ESRGAN ONNX support coming soon!")
    
    success = await download_model(model_name)
    if not success:
        raise HTTPException(status_code=500, detail="Failed to download model")
    
    model_path = get_model_path(model_name)
    size_mb = model_path.stat().st_size / 1024 / 1024 if model_path.exists() else 0
    
    return {"status": "success", "model": model_name, "size_mb": round(size_mb, 2)}


@app.post("/models/load")
async def load_model_endpoint(
    model_name: str = Form(...),
    use_gpu: bool = Form(True),
    gpu_device_id: Optional[int] = Form(None)
):
    """Load a model into memory. Optionally specify GPU device index."""
    if model_name not in AVAILABLE_MODELS:
        raise HTTPException(status_code=404, detail=f"Model {model_name} not found")

    model_info = AVAILABLE_MODELS[model_name]
    if not model_info.get("available", True):
        raise HTTPException(status_code=400, detail=f"Model {model_name} is not yet available")

    model_path = get_model_path(model_name)

    if not model_path.exists():
        raise HTTPException(status_code=404, detail=f"Model {model_name} not downloaded")

    state.use_gpu = use_gpu
    if gpu_device_id is not None:
        state.gpu_device_id = gpu_device_id
        logger.info(f"GPU device ID set to {gpu_device_id}")

    success = await load_model(model_name)

    if not success:
        raise HTTPException(status_code=500, detail=f"Failed to load model: {state.last_load_error or 'unknown'}")

    return {
        "status": "success",
        "model": model_name,
        "model_type": state.current_model_type,
        "using_gpu": state.use_gpu,
        "gpu_device_id": state.gpu_device_id,
        "providers": state.providers
    }


@app.post("/upscale")
async def upscale_endpoint(
    file: UploadFile = File(...),
    scale: int = Form(2)
):
    """Upscale an image. Scale is determined by the loaded model; the scale parameter is validated for consistency."""
    if state.cv_model is None and state.onnx_session is None:
        raise HTTPException(status_code=400, detail="No model loaded. Please load a model first.")

    # Validate scale against loaded model's native scale
    model_scale = state.onnx_model_scale if state.current_model_type == "onnx" else state.cv_model_scale
    if scale != model_scale:
        logger.warning(f"Requested scale={scale} differs from loaded model scale={model_scale}. Using model's native scale={model_scale}.")

    # Atomic try-acquire to avoid TOCTOU race (same pattern as /upscale-frame)
    acquired = False
    try:
        await asyncio.wait_for(_upscale_semaphore.acquire(), timeout=0)
        acquired = True
    except asyncio.TimeoutError:
        raise HTTPException(status_code=429, detail="Too many concurrent requests")

    try:
        state.processing_count += 1
        try:
            # Read image
            image_bytes = await file.read()

            # Upscale in thread pool to not block async
            loop = asyncio.get_running_loop()
            result = await loop.run_in_executor(None, upscale_image, image_bytes)

            return Response(content=result, media_type="image/png")

        except ValueError as e:
            raise HTTPException(status_code=400, detail=str(e))
        except Exception as e:
            logger.error(f"Upscale failed: {e}")
            raise HTTPException(status_code=500, detail="Upscaling failed")
        finally:
            state.processing_count -= 1
    finally:
        if acquired:
            _upscale_semaphore.release()


@app.get("/benchmark")
async def benchmark_endpoint():
    """Run a benchmark on the current model."""
    if state.cv_model is None and state.onnx_session is None:
        raise HTTPException(status_code=400, detail="No model loaded")
    
    loop = asyncio.get_running_loop()
    result = await loop.run_in_executor(None, run_benchmark, 256)
    return result


@app.post("/upscale-frame")
async def upscale_frame_endpoint(request: Request):
    """Fast frame upscaling for real-time playback. Raw JPEG in, JPEG out. Returns 503 when busy."""
    if state.cv_model is None and state.onnx_session is None:
        raise HTTPException(status_code=400, detail="No model loaded")

    # Return 503 immediately if busy — client skips this frame (try-acquire)
    acquired = False
    try:
        await asyncio.wait_for(_upscale_semaphore.acquire(), timeout=0)
        acquired = True
    except asyncio.TimeoutError:
        raise HTTPException(status_code=503, detail="Busy")

    try:
        state.processing_count += 1
        try:
            body = await request.body()
            if not body:
                raise HTTPException(status_code=400, detail="Empty body")

            # Decode JPEG to numpy array
            nparr = np.frombuffer(body, np.uint8)
            img = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
            if img is None:
                raise HTTPException(status_code=400, detail="Failed to decode image")

            # Upscale using array helper (no double encode/decode)
            loop = asyncio.get_running_loop()
            result = await loop.run_in_executor(None, upscale_image_array, img)

            # Encode as JPEG quality 85 (much faster than PNG)
            _, buffer = cv2.imencode('.jpg', result, [cv2.IMWRITE_JPEG_QUALITY, 85])

            return Response(content=buffer.tobytes(), media_type="image/jpeg")

        except HTTPException:
            raise
        except Exception as e:
            logger.error(f"Frame upscale failed: {e}")
            raise HTTPException(status_code=500, detail="Frame upscaling failed")
        finally:
            state.processing_count -= 1
    finally:
        if acquired:
            _upscale_semaphore.release()


def _run_frame_benchmark(width: int, height: int) -> dict:
    """Benchmark at actual capture resolution with JPEG encode/decode."""
    if state.current_model is None:
        return {"error": "No model loaded"}

    # Create test image at specified capture resolution
    test_img = np.random.randint(0, 255, (height, width, 3), dtype=np.uint8)

    # Warm up
    try:
        upscale_image_array(test_img)
    except Exception as e:
        return {"error": f"Warmup failed: {str(e)}"}

    # Benchmark 5 iterations (JPEG decode → upscale → JPEG encode)
    times = []
    iterations = 5
    for _ in range(iterations):
        # Simulate full pipeline: JPEG encode → decode → upscale → JPEG encode
        _, jpeg_buf = cv2.imencode('.jpg', test_img, [cv2.IMWRITE_JPEG_QUALITY, 85])
        jpeg_bytes = jpeg_buf.tobytes()

        start = time.perf_counter()
        nparr = np.frombuffer(jpeg_bytes, np.uint8)
        img = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
        result = upscale_image_array(img)
        cv2.imencode('.jpg', result, [cv2.IMWRITE_JPEG_QUALITY, 85])
        elapsed = time.perf_counter() - start
        times.append(elapsed)

    avg_time = sum(times) / len(times)
    fps = 1.0 / avg_time if avg_time > 0 else 0

    return {
        "fps": round(fps, 1),
        "avg_time_ms": round(avg_time * 1000, 1),
        "model": state.current_model,
        "using_gpu": state.use_gpu,
        "capture_width": width,
        "capture_height": height,
        "output_width": width * (state.onnx_model_scale if state.current_model_type == "onnx" else state.cv_model_scale),
        "output_height": height * (state.onnx_model_scale if state.current_model_type == "onnx" else state.cv_model_scale)
    }


@app.get("/benchmark-frame")
async def benchmark_frame_endpoint(width: int = 480, height: int = 270):
    """Benchmark at actual capture resolution for real-time upscaling feasibility."""
    if state.cv_model is None and state.onnx_session is None:
        raise HTTPException(status_code=400, detail="No model loaded")

    if state.current_model is None:
        raise HTTPException(status_code=400, detail="No model loaded. Load a model first via POST /models/load")

    # Clamp dimensions to reasonable range
    width = max(64, min(width, 1920))
    height = max(64, min(height, 1080))

    loop = asyncio.get_running_loop()
    result = await loop.run_in_executor(None, _run_frame_benchmark, width, height)
    return result


@app.post("/config")
async def update_config(
    use_gpu: Optional[bool] = Form(None),
    max_concurrent: Optional[int] = Form(None),
    gpu_device_id: Optional[int] = Form(None)
):
    """Update service configuration."""
    if use_gpu is not None:
        state.use_gpu = use_gpu
    if max_concurrent is not None:
        state.max_concurrent = max_concurrent
    if gpu_device_id is not None:
        state.gpu_device_id = gpu_device_id
        logger.info(f"GPU device ID updated to {gpu_device_id}")

    return {
        "use_gpu": state.use_gpu,
        "max_concurrent": state.max_concurrent,
        "gpu_device_id": state.gpu_device_id
    }
