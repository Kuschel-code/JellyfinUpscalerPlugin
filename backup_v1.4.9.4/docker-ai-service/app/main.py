"""
AI Upscaler Service - FastAPI Application
Jellyfin AI Upscaler Plugin - Microservice Component
"""

import os
import io
import logging
import asyncio
from pathlib import Path
from typing import Optional
from contextlib import asynccontextmanager

import numpy as np
import cv2
import httpx
import onnxruntime as ort
from fastapi import FastAPI, File, UploadFile, Form, HTTPException, BackgroundTasks
from fastapi.responses import Response, HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel

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
    session: Optional[ort.InferenceSession] = None
    current_model: Optional[str] = None
    providers: list = []
    use_gpu: bool = True
    processing_count: int = 0
    max_concurrent: int = 4

state = AppState()

# Available models with download URLs
AVAILABLE_MODELS = {
    "realesrgan-x2": {
        "name": "Real-ESRGAN x2",
        "url": "https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/models-v1.0/realesrgan-x2.onnx",
        "scale": 2,
        "description": "High-quality 2x upscaling for real-world images"
    },
    "realesrgan-x4": {
        "name": "Real-ESRGAN x4", 
        "url": "https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/models-v1.0/realesrgan-x4.onnx",
        "scale": 4,
        "description": "High-quality 4x upscaling for real-world images"
    },
    "fsrcnn-x2": {
        "name": "FSRCNN x2 (Fast)",
        "url": "https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/models-v1.0/fsrcnn-x2.onnx",
        "scale": 2,
        "description": "Fast 2x upscaling, lower quality but faster"
    }
}


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Application startup and shutdown."""
    logger.info("Starting AI Upscaler Service...")
    
    # Create directories
    MODELS_DIR.mkdir(parents=True, exist_ok=True)
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    
    # Detect available providers
    state.providers = ort.get_available_providers()
    state.use_gpu = os.getenv("USE_GPU", "true").lower() == "true"
    state.max_concurrent = int(os.getenv("MAX_CONCURRENT_REQUESTS", "4"))
    
    logger.info(f"Available ONNX Providers: {state.providers}")
    logger.info(f"GPU Enabled: {state.use_gpu}")
    
    # Load default model if specified
    default_model = os.getenv("DEFAULT_MODEL")
    if default_model and (MODELS_DIR / f"{default_model}.onnx").exists():
        await load_model(default_model)
    
    yield
    
    logger.info("Shutting down AI Upscaler Service...")


app = FastAPI(
    title="AI Upscaler Service",
    description="Neural network image upscaling service for Jellyfin",
    version="1.0.0",
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


class ModelInfo(BaseModel):
    name: str
    description: str
    scale: int
    downloaded: bool


async def load_model(model_name: str) -> bool:
    """Load an ONNX model into memory."""
    model_path = MODELS_DIR / f"{model_name}.onnx"
    
    if not model_path.exists():
        logger.error(f"Model not found: {model_path}")
        return False
    
    try:
        # Configure session options
        sess_options = ort.SessionOptions()
        sess_options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
        
        # Select providers based on GPU preference
        if state.use_gpu:
            providers = []
            if "TensorrtExecutionProvider" in state.providers:
                providers.append("TensorrtExecutionProvider")
            if "CUDAExecutionProvider" in state.providers:
                providers.append("CUDAExecutionProvider")
            providers.append("CPUExecutionProvider")
        else:
            providers = ["CPUExecutionProvider"]
        
        logger.info(f"Loading model {model_name} with providers: {providers}")
        
        state.session = ort.InferenceSession(
            str(model_path),
            sess_options=sess_options,
            providers=providers
        )
        state.current_model = model_name
        
        logger.info(f"Model {model_name} loaded successfully")
        return True
        
    except Exception as e:
        logger.error(f"Failed to load model {model_name}: {e}")
        return False


async def download_model(model_name: str) -> bool:
    """Download a model from the repository."""
    if model_name not in AVAILABLE_MODELS:
        logger.error(f"Unknown model: {model_name}")
        return False
    
    model_info = AVAILABLE_MODELS[model_name]
    model_path = MODELS_DIR / f"{model_name}.onnx"
    
    if model_path.exists():
        logger.info(f"Model {model_name} already exists")
        return True
    
    try:
        logger.info(f"Downloading model {model_name} from {model_info['url']}")
        
        async with httpx.AsyncClient(timeout=300.0) as client:
            response = await client.get(model_info["url"], follow_redirects=True)
            response.raise_for_status()
            
            with open(model_path, "wb") as f:
                f.write(response.content)
        
        logger.info(f"Model {model_name} downloaded ({model_path.stat().st_size / 1024 / 1024:.1f} MB)")
        return True
        
    except Exception as e:
        logger.error(f"Failed to download model {model_name}: {e}")
        if model_path.exists():
            model_path.unlink()
        return False


def upscale_image(image_bytes: bytes, scale: int = 2) -> bytes:
    """Upscale an image using the loaded ONNX model."""
    if state.session is None:
        raise ValueError("No model loaded")
    
    # Decode image
    nparr = np.frombuffer(image_bytes, np.uint8)
    img = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
    
    if img is None:
        raise ValueError("Failed to decode image")
    
    # Get original dimensions
    h, w = img.shape[:2]
    
    # Preprocess: BGR -> RGB, normalize, transpose to NCHW
    img_rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
    img_normalized = img_rgb.astype(np.float32) / 255.0
    img_input = np.transpose(img_normalized, (2, 0, 1))  # HWC -> CHW
    img_input = np.expand_dims(img_input, axis=0)  # CHW -> NCHW
    
    # Get input/output names
    input_name = state.session.get_inputs()[0].name
    output_name = state.session.get_outputs()[0].name
    
    # Run inference
    result = state.session.run([output_name], {input_name: img_input})[0]
    
    # Postprocess: NCHW -> HWC, denormalize, RGB -> BGR
    result = np.squeeze(result, axis=0)  # NCHW -> CHW
    result = np.transpose(result, (1, 2, 0))  # CHW -> HWC
    result = np.clip(result * 255.0, 0, 255).astype(np.uint8)
    result_bgr = cv2.cvtColor(result, cv2.COLOR_RGB2BGR)
    
    # Encode as PNG
    _, buffer = cv2.imencode('.png', result_bgr)
    return buffer.tobytes()


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


@app.get("/status", response_model=StatusResponse)
async def status():
    """Get service status."""
    return StatusResponse(
        status="running",
        current_model=state.current_model,
        available_providers=state.providers,
        using_gpu=state.use_gpu,
        loaded_models=[state.current_model] if state.current_model else [],
        processing_count=state.processing_count,
        max_concurrent=state.max_concurrent
    )


@app.get("/models")
async def list_models():
    """List available models."""
    models = []
    for model_id, info in AVAILABLE_MODELS.items():
        model_path = MODELS_DIR / f"{model_id}.onnx"
        models.append({
            "id": model_id,
            "name": info["name"],
            "description": info["description"],
            "scale": info["scale"],
            "downloaded": model_path.exists(),
            "loaded": state.current_model == model_id
        })
    return {"models": models}


@app.post("/models/download")
async def download_model_endpoint(model_name: str = Form(...)):
    """Download a model."""
    if model_name not in AVAILABLE_MODELS:
        raise HTTPException(status_code=404, detail=f"Model {model_name} not found")
    
    success = await download_model(model_name)
    if not success:
        raise HTTPException(status_code=500, detail="Failed to download model")
    
    return {"status": "success", "model": model_name}


@app.post("/models/load")
async def load_model_endpoint(model_name: str = Form(...), use_gpu: bool = Form(True)):
    """Load a model into memory."""
    model_path = MODELS_DIR / f"{model_name}.onnx"
    
    if not model_path.exists():
        raise HTTPException(status_code=404, detail=f"Model {model_name} not downloaded")
    
    state.use_gpu = use_gpu
    success = await load_model(model_name)
    
    if not success:
        raise HTTPException(status_code=500, detail="Failed to load model")
    
    return {"status": "success", "model": model_name, "using_gpu": use_gpu}


@app.post("/upscale")
async def upscale_endpoint(
    file: UploadFile = File(...),
    scale: int = Form(2)
):
    """Upscale an image."""
    if state.session is None:
        raise HTTPException(status_code=400, detail="No model loaded. Please load a model first.")
    
    if state.processing_count >= state.max_concurrent:
        raise HTTPException(status_code=429, detail="Too many concurrent requests")
    
    state.processing_count += 1
    
    try:
        # Read image
        image_bytes = await file.read()
        
        # Upscale in thread pool to not block async
        loop = asyncio.get_event_loop()
        result = await loop.run_in_executor(None, upscale_image, image_bytes, scale)
        
        return Response(content=result, media_type="image/png")
        
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        logger.error(f"Upscale failed: {e}")
        raise HTTPException(status_code=500, detail="Upscaling failed")
    finally:
        state.processing_count -= 1


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
