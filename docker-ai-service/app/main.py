"""
AI Upscaler Service - FastAPI Application
Jellyfin AI Upscaler Plugin - Microservice Component v1.1.2
Supports OpenCV DNN (.pb) and ONNX Runtime models with GPU detection
"""

import os
import io
import time
import logging
import asyncio
import platform
import subprocess
from pathlib import Path
from typing import Optional, Any
from contextlib import asynccontextmanager

import numpy as np
import cv2
import httpx
from fastapi import FastAPI, File, UploadFile, Form, HTTPException, BackgroundTasks
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
VERSION = "1.1.2"

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
    
    # Hardware info
    gpu_name: str = "Unknown"
    gpu_memory: str = "Unknown"
    cpu_name: str = "Unknown"
    cpu_cores: int = 0
    
    # Plugin connections
    plugin_connections: list = []
    
    # Benchmark results
    last_benchmark: dict = {}

state = AppState()

# Available models with download URLs from PUBLIC sources
# Note: Real-ESRGAN ONNX models need to be converted, using pre-converted from community
AVAILABLE_MODELS = {
    # ============================================================
    # === REAL-ESRGAN Models (Best Quality - Anime & Photo) ===
    # Note: These require ONNX Runtime. Models from community repos.
    # ============================================================
    "realesrgan-x4plus": {
        "name": "Real-ESRGAN x4+ (Best Quality)",
        "url": "https://huggingface.co/ai-forever/Real-ESRGAN/resolve/main/RealESRGAN_x4.pth",
        "scale": 4,
        "description": "⚠️ Requires conversion - Use OpenCV models for now",
        "type": "pth",
        "category": "realesrgan",
        "model_type": "realesrgan",
        "available": False
    },
    "realesrgan-x4plus-anime": {
        "name": "Real-ESRGAN x4+ Anime",
        "url": "https://huggingface.co/ai-forever/Real-ESRGAN/resolve/main/RealESRGAN_x4.pth",
        "scale": 4,
        "description": "⚠️ Coming soon - Use EDSR for quality upscaling",
        "type": "pth",
        "category": "realesrgan",
        "model_type": "realesrgan",
        "available": False
    },
    "realesrnet-x4plus": {
        "name": "RealESRNet x4+ (Faster)",
        "url": "https://huggingface.co/ai-forever/Real-ESRGAN/resolve/main/RealESRGAN_x4.pth",
        "scale": 4,
        "description": "⚠️ Coming soon - Use LapSRN for fast quality",
        "type": "pth",
        "category": "realesrgan",
        "model_type": "realesrgan",
        "available": False
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
    }
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
    except:
        pass
    
    # Detect GPU using nvidia-smi
    try:
        result = subprocess.run(
            ["nvidia-smi", "--query-gpu=name,memory.total", "--format=csv,noheader,nounits"],
            capture_output=True, text=True, timeout=5
        )
        if result.returncode == 0:
            parts = result.stdout.strip().split(",")
            if len(parts) >= 2:
                state.gpu_name = parts[0].strip()
                state.gpu_memory = f"{int(parts[1].strip())} MB"
            else:
                state.gpu_name = result.stdout.strip()
    except Exception as e:
        logger.warning(f"Could not detect GPU: {e}")
        state.gpu_name = "No NVIDIA GPU detected"
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
        
        state.cv_model = sr
        state.cv_model_name = model_name
        state.cv_model_scale = scale
        state.current_model = model_name
        state.current_model_type = "opencv"
        state.onnx_session = None
        
        logger.info(f"OpenCV model {model_name} loaded successfully (scale={scale})")
        return True
        
    except Exception as e:
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
    else:
        logger.error(f"Model type {model_type} not yet supported")
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
            
            with open(model_path, "wb") as f:
                f.write(response.content)
        
        size_mb = model_path.stat().st_size / 1024 / 1024
        logger.info(f"Model {model_name} downloaded ({size_mb:.1f} MB)")
        return True
        
    except Exception as e:
        logger.error(f"Failed to download model {model_name}: {e}")
        if model_path.exists():
            model_path.unlink()
        return False


def upscale_image(image_bytes: bytes) -> bytes:
    """Upscale an image using the loaded model."""
    if state.cv_model is None:
        raise ValueError("No model loaded")
    
    # Decode image
    nparr = np.frombuffer(image_bytes, np.uint8)
    img = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
    
    if img is None:
        raise ValueError("Failed to decode image")
    
    # Upscale using OpenCV DNN Super Resolution
    result = state.cv_model.upsample(img)
    
    # Encode as PNG
    _, buffer = cv2.imencode('.png', result)
    return buffer.tobytes()


def run_benchmark(test_size: int = 256) -> dict:
    """Run a quick benchmark on the loaded model."""
    if state.current_model is None:
        return {"error": "No model loaded"}
    
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
    
    scale = state.cv_model_scale
    
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
    return {"status": "healthy", "model_loaded": state.current_model is not None}


@app.get("/status")
async def status():
    """Get service status."""
    scale = state.cv_model_scale if state.cv_model else None
    
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
            "tensorrt_available": any("Tensorrt" in p for p in state.providers)
        },
        "cpu": {
            "name": state.cpu_name,
            "cores": state.cpu_cores
        },
        "providers": state.providers,
        "using_gpu": state.use_gpu
    }


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
async def load_model_endpoint(model_name: str = Form(...), use_gpu: bool = Form(True)):
    """Load a model into memory."""
    if model_name not in AVAILABLE_MODELS:
        raise HTTPException(status_code=404, detail=f"Model {model_name} not found")
    
    model_info = AVAILABLE_MODELS[model_name]
    if not model_info.get("available", True):
        raise HTTPException(status_code=400, detail=f"Model {model_name} is not yet available")
    
    model_path = get_model_path(model_name)
    
    if not model_path.exists():
        raise HTTPException(status_code=404, detail=f"Model {model_name} not downloaded")
    
    state.use_gpu = use_gpu
    success = await load_model(model_name)
    
    if not success:
        raise HTTPException(status_code=500, detail="Failed to load model")
    
    return {
        "status": "success", 
        "model": model_name, 
        "model_type": state.current_model_type,
        "using_gpu": use_gpu
    }


@app.post("/upscale")
async def upscale_endpoint(
    file: UploadFile = File(...),
    scale: int = Form(2)
):
    """Upscale an image."""
    if state.cv_model is None:
        raise HTTPException(status_code=400, detail="No model loaded. Please load a model first.")
    
    if state.processing_count >= state.max_concurrent:
        raise HTTPException(status_code=429, detail="Too many concurrent requests")
    
    state.processing_count += 1
    
    try:
        # Read image
        image_bytes = await file.read()
        
        # Upscale in thread pool to not block async
        loop = asyncio.get_event_loop()
        result = await loop.run_in_executor(None, upscale_image, image_bytes)
        
        return Response(content=result, media_type="image/png")
        
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        logger.error(f"Upscale failed: {e}")
        raise HTTPException(status_code=500, detail="Upscaling failed")
    finally:
        state.processing_count -= 1


@app.get("/benchmark")
async def benchmark_endpoint():
    """Run a benchmark on the current model."""
    if state.cv_model is None:
        raise HTTPException(status_code=400, detail="No model loaded")
    
    loop = asyncio.get_event_loop()
    result = await loop.run_in_executor(None, run_benchmark, 256)
    return result


@app.post("/config")
async def update_config(
    use_gpu: Optional[bool] = Form(None),
    max_concurrent: Optional[int] = Form(None)
):
    """Update service configuration."""
    if use_gpu is not None:
        state.use_gpu = use_gpu
    if max_concurrent is not None:
        state.max_concurrent = max_concurrent
    
    return {
        "use_gpu": state.use_gpu,
        "max_concurrent": state.max_concurrent
    }
