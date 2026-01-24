"""
AI Upscaler Service - FastAPI Application
Jellyfin AI Upscaler Plugin - Microservice Component v1.2
Supports OpenCV DNN (.pb), ONNX Runtime, and Real-ESRGAN models
"""

import os
import io
import time
import logging
import asyncio
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

# Global state
class AppState:
    # OpenCV DNN model (using Any to avoid import-time dependency)
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
    
    # Benchmark results
    last_benchmark: dict = {}

state = AppState()

# Available models with download URLs from PUBLIC sources
AVAILABLE_MODELS = {
    # ============================================================
    # === REAL-ESRGAN Models (Best Quality - Anime & Photo) ===
    # ============================================================
    "realesrgan-x4plus": {
        "name": "Real-ESRGAN x4+ (Best Quality)",
        "url": "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth",
        "onnx_url": "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-x4plus.onnx",
        "scale": 4,
        "description": "Best quality 4x for real-world photos. Sharp details, noise removal.",
        "type": "onnx",
        "category": "realesrgan",
        "model_type": "realesrgan"
    },
    "realesrgan-x4plus-anime": {
        "name": "Real-ESRGAN x4+ Anime (Best for Anime)",
        "url": "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.2.4/RealESRGAN_x4plus_anime_6B.pth",
        "onnx_url": "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-x4plus-anime.onnx",
        "scale": 4,
        "description": "Optimized for anime/illustrations. Crisp lines, vibrant colors.",
        "type": "onnx",
        "category": "realesrgan",
        "model_type": "realesrgan"
    },
    "realesrnet-x4plus": {
        "name": "RealESRNet x4+ (Faster)",
        "url": "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRNet_x4plus.pth",
        "onnx_url": "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrnet-x4plus.onnx",
        "scale": 4,
        "description": "Faster variant of Real-ESRGAN, good for batch processing.",
        "type": "onnx",
        "category": "realesrgan",
        "model_type": "realesrgan"
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
        "model_type": "fsrcnn"
    },
    "fsrcnn-x3": {
        "name": "FSRCNN x3 (Fast)",
        "url": "https://raw.githubusercontent.com/Saafke/FSRCNN_Tensorflow/master/models/FSRCNN_x3.pb",
        "scale": 3,
        "description": "Fast 3x upscaling",
        "type": "pb",
        "category": "fast",
        "model_type": "fsrcnn"
    },
    "fsrcnn-x4": {
        "name": "FSRCNN x4 (Fast)",
        "url": "https://raw.githubusercontent.com/Saafke/FSRCNN_Tensorflow/master/models/FSRCNN_x4.pb",
        "scale": 4,
        "description": "Fast 4x upscaling, lower quality but quick",
        "type": "pb",
        "category": "fast",
        "model_type": "fsrcnn"
    },
    "espcn-x2": {
        "name": "ESPCN x2 (Fastest)",
        "url": "https://raw.githubusercontent.com/fannymonori/TF-ESPCN/master/export/ESPCN_x2.pb",
        "scale": 2,
        "description": "Fastest model, minimal quality improvement",
        "type": "pb",
        "category": "fast",
        "model_type": "espcn"
    },
    "espcn-x3": {
        "name": "ESPCN x3 (Fastest)",
        "url": "https://raw.githubusercontent.com/fannymonori/TF-ESPCN/master/export/ESPCN_x3.pb",
        "scale": 3,
        "description": "Fastest 3x model",
        "type": "pb",
        "category": "fast",
        "model_type": "espcn"
    },
    "espcn-x4": {
        "name": "ESPCN x4 (Fastest)",
        "url": "https://raw.githubusercontent.com/fannymonori/TF-ESPCN/master/export/ESPCN_x4.pb",
        "scale": 4,
        "description": "Fastest 4x model",
        "type": "pb",
        "category": "fast",
        "model_type": "espcn"
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
        "model_type": "lapsrn"
    },
    "lapsrn-x4": {
        "name": "LapSRN x4 (Quality)",
        "url": "https://raw.githubusercontent.com/fannymonori/TF-LapSRN/master/export/LapSRN_x4.pb",
        "scale": 4,
        "description": "Good quality 4x upscaling",
        "type": "pb",
        "category": "quality",
        "model_type": "lapsrn"
    },
    "lapsrn-x8": {
        "name": "LapSRN x8 (Quality)",
        "url": "https://raw.githubusercontent.com/fannymonori/TF-LapSRN/master/export/LapSRN_x8.pb",
        "scale": 8,
        "description": "Extreme 8x upscaling",
        "type": "pb",
        "category": "quality",
        "model_type": "lapsrn"
    },
    
    # ============================================================
    # === EDSR Models (High Quality, Slow) ===
    # ============================================================
    "edsr-x2": {
        "name": "EDSR x2 (High Quality)",
        "url": "https://github.com/Saafke/EDSR_Tensorflow/raw/master/models/EDSR_x2.pb",
        "scale": 2,
        "description": "High quality 2x, requires more compute",
        "type": "pb",
        "category": "quality",
        "model_type": "edsr"
    },
    "edsr-x3": {
        "name": "EDSR x3 (High Quality)",
        "url": "https://github.com/Saafke/EDSR_Tensorflow/raw/master/models/EDSR_x3.pb",
        "scale": 3,
        "description": "High quality 3x",
        "type": "pb",
        "category": "quality",
        "model_type": "edsr"
    },
    "edsr-x4": {
        "name": "EDSR x4 (High Quality)",
        "url": "https://github.com/Saafke/EDSR_Tensorflow/raw/master/models/EDSR_x4.pb",
        "scale": 4,
        "description": "High quality 4x, slowest OpenCV model",
        "type": "pb",
        "category": "quality",
        "model_type": "edsr"
    }
}


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Application startup and shutdown."""
    logger.info("Starting AI Upscaler Service v1.2 (Real-ESRGAN Edition)...")
    
    # Create directories
    MODELS_DIR.mkdir(parents=True, exist_ok=True)
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    
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
    logger.info(f"Available Models: {len(AVAILABLE_MODELS)} (including Real-ESRGAN)")
    
    # Load default model if specified
    default_model = os.getenv("DEFAULT_MODEL")
    if default_model:
        await load_model(default_model)
    
    yield
    
    logger.info("Shutting down AI Upscaler Service...")


app = FastAPI(
    title="AI Upscaler Service",
    description="Neural network image upscaling service for Jellyfin (Real-ESRGAN Edition)",
    version="1.2.0",
    lifespan=lifespan
)

# Mount static files
if STATIC_DIR.exists():
    app.mount("/static", StaticFiles(directory=str(STATIC_DIR)), name="static")


# Response models
class StatusResponse(BaseModel):
    status: str
    current_model: Optional[str]
    available_providers: list
    using_gpu: bool
    loaded_models: list
    processing_count: int
    max_concurrent: int
    onnx_available: bool


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


async def load_onnx_model(model_name: str, model_info: dict, model_path: Path) -> bool:
    """Load an ONNX model (Real-ESRGAN)."""
    if not ONNX_AVAILABLE:
        logger.error("ONNX Runtime not available. Cannot load ONNX models.")
        return False
    
    try:
        scale = model_info.get("scale", 4)
        
        # Set up ONNX Runtime session options
        sess_options = ort.SessionOptions()
        sess_options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
        
        # Choose providers based on GPU setting
        if state.use_gpu:
            providers = ['CUDAExecutionProvider', 'CPUExecutionProvider']
        else:
            providers = ['CPUExecutionProvider']
        
        # Create session
        session = ort.InferenceSession(str(model_path), sess_options, providers=providers)
        
        state.onnx_session = session
        state.onnx_model_name = model_name
        state.onnx_model_scale = scale
        state.current_model = model_name
        state.current_model_type = "onnx"
        state.cv_model = None
        
        # Log actual providers being used
        actual_providers = session.get_providers()
        logger.info(f"ONNX model {model_name} loaded with providers: {actual_providers}")
        
        return True
        
    except Exception as e:
        logger.error(f"Failed to load ONNX model {model_name}: {e}")
        return False


async def load_model(model_name: str) -> bool:
    """Load a model (OpenCV DNN or ONNX) into memory."""
    if model_name not in AVAILABLE_MODELS:
        logger.error(f"Unknown model: {model_name}")
        return False
    
    model_info = AVAILABLE_MODELS[model_name]
    model_path = get_model_path(model_name)
    
    if not model_path.exists():
        logger.error(f"Model not found: {model_path}")
        return False
    
    model_type = model_info.get("type", "pb")
    
    if model_type == "onnx":
        return await load_onnx_model(model_name, model_info, model_path)
    else:
        return await load_opencv_model(model_name, model_info, model_path)


async def download_model(model_name: str) -> bool:
    """Download a model from the repository."""
    if model_name not in AVAILABLE_MODELS:
        logger.error(f"Unknown model: {model_name}")
        return False
    
    model_info = AVAILABLE_MODELS[model_name]
    model_path = get_model_path(model_name)
    
    if model_path.exists():
        logger.info(f"Model {model_name} already exists")
        return True
    
    try:
        # Use onnx_url for ONNX models if available
        download_url = model_info.get("onnx_url", model_info.get("url"))
        
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


def upscale_with_opencv(image_bytes: bytes) -> bytes:
    """Upscale an image using the loaded OpenCV model."""
    if state.cv_model is None:
        raise ValueError("No OpenCV model loaded")
    
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


def upscale_with_onnx(image_bytes: bytes) -> bytes:
    """Upscale an image using the loaded ONNX model (Real-ESRGAN)."""
    if state.onnx_session is None:
        raise ValueError("No ONNX model loaded")
    
    # Decode image
    nparr = np.frombuffer(image_bytes, np.uint8)
    img = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
    
    if img is None:
        raise ValueError("Failed to decode image")
    
    # Convert BGR to RGB
    img = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
    
    # Normalize to [0, 1] and transpose to NCHW format
    img = img.astype(np.float32) / 255.0
    img = np.transpose(img, (2, 0, 1))  # HWC to CHW
    img = np.expand_dims(img, axis=0)    # Add batch dimension
    
    # Run inference
    input_name = state.onnx_session.get_inputs()[0].name
    output_name = state.onnx_session.get_outputs()[0].name
    result = state.onnx_session.run([output_name], {input_name: img})[0]
    
    # Post-process: remove batch, transpose back to HWC
    result = np.squeeze(result, axis=0)
    result = np.transpose(result, (1, 2, 0))  # CHW to HWC
    
    # Clip and convert back to uint8
    result = np.clip(result * 255.0, 0, 255).astype(np.uint8)
    
    # Convert RGB back to BGR for OpenCV encoding
    result = cv2.cvtColor(result, cv2.COLOR_RGB2BGR)
    
    # Encode as PNG
    _, buffer = cv2.imencode('.png', result)
    return buffer.tobytes()


def upscale_image(image_bytes: bytes) -> bytes:
    """Upscale an image using the appropriate loaded model."""
    if state.current_model_type == "onnx":
        return upscale_with_onnx(image_bytes)
    else:
        return upscale_with_opencv(image_bytes)


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
    return {"status": "healthy", "model_loaded": state.current_model is not None}


@app.get("/status")
async def status():
    """Get service status."""
    scale = None
    if state.current_model_type == "onnx":
        scale = state.onnx_model_scale
    elif state.cv_model:
        scale = state.cv_model_scale
    
    return {
        "status": "running",
        "version": "1.2.0",
        "current_model": state.current_model,
        "model_type": state.current_model_type,
        "available_providers": state.providers,
        "using_gpu": state.use_gpu,
        "loaded_models": [state.current_model] if state.current_model else [],
        "processing_count": state.processing_count,
        "max_concurrent": state.max_concurrent,
        "onnx_available": ONNX_AVAILABLE,
        "model_scale": scale,
        "realesrgan_available": ONNX_AVAILABLE
    }


@app.get("/models")
async def list_models():
    """List available models."""
    models = []
    for model_id, info in AVAILABLE_MODELS.items():
        model_path = get_model_path(model_id)
        models.append({
            "id": model_id,
            "name": info["name"],
            "description": info["description"],
            "scale": info["scale"],
            "category": info.get("category", "general"),
            "type": info.get("type", "pb"),
            "downloaded": model_path.exists(),
            "loaded": state.current_model == model_id
        })
    return {"models": models, "total": len(models)}


@app.post("/models/download")
async def download_model_endpoint(model_name: str = Form(...)):
    """Download a model."""
    if model_name not in AVAILABLE_MODELS:
        raise HTTPException(status_code=404, detail=f"Model {model_name} not found")
    
    success = await download_model(model_name)
    if not success:
        raise HTTPException(status_code=500, detail="Failed to download model")
    
    model_path = get_model_path(model_name)
    size_mb = model_path.stat().st_size / 1024 / 1024 if model_path.exists() else 0
    
    return {"status": "success", "model": model_name, "size_mb": round(size_mb, 2)}


@app.post("/models/load")
async def load_model_endpoint(model_name: str = Form(...), use_gpu: bool = Form(True)):
    """Load a model into memory."""
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
    if state.current_model is None:
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
    if state.current_model is None:
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
