"""
AI Upscaler Service - FastAPI Application
Jellyfin AI Upscaler Plugin - Microservice Component v1.5.5.3
Supports OpenCV DNN (.pb) and ONNX Runtime models with GPU detection
Multi-GPU selection, robust TensorRT/CUDA/OpenVINO fallback
"""

import os
import time
import logging
import asyncio
import platform
import subprocess
import threading
import hmac
import urllib.parse
import uuid
from pathlib import Path
from typing import Optional, Any
from contextlib import asynccontextmanager

import numpy as np
import cv2
import httpx
from fastapi import FastAPI, File, UploadFile, Form, HTTPException, Request
from fastapi.responses import Response, HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles

# Try to import ONNX Runtime (optional)
try:
    import onnxruntime as ort
    ONNX_AVAILABLE = True
except ImportError:
    ONNX_AVAILABLE = False
    ort = None

# ncnn-Vulkan for GPU super-resolution on AMD pre-RDNA2, Intel, etc.
try:
    from realsr_ncnn_vulkan_python import RealSR
    NCNN_AVAILABLE = True
except ImportError:
    try:
        import ncnn
        NCNN_AVAILABLE = True
        RealSR = None
    except ImportError:
        NCNN_AVAILABLE = False
        RealSR = None
        ncnn = None

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
VERSION = "1.5.5.3"

# Global state
class AppState:
    """Global application state. Uses __init__ to avoid mutable class-level defaults
    being shared across instances (Python gotcha with mutable defaults on class attributes)."""

    def __init__(self):
        # OpenCV DNN model
        self.cv_model: Any = None
        self.cv_model_name: Optional[str] = None
        self.cv_model_scale: int = 2

        # ONNX model
        self.onnx_session = None
        self.onnx_model_name: Optional[str] = None
        self.onnx_model_scale: int = 4

        # ncnn-Vulkan model (for Vulkan GPU acceleration)
        self.ncnn_upscaler: Any = None
        self.ncnn_model_name: Optional[str] = None
        self.ncnn_model_scale: int = 4
        self.ncnn_gpu_id: int = 0

        self.current_model: Optional[str] = None
        self.current_model_type: str = "opencv"  # "opencv", "onnx", or "ncnn"
        self.providers: list = []
        self.use_gpu: bool = True
        self.processing_count: int = 0
        self.max_concurrent: int = 4
        self.gpu_device_id: int = 0  # GPU device index for multi-GPU systems

        # Hardware info
        self.gpu_name: str = "Unknown"
        self.gpu_memory: str = "Unknown"
        self.gpu_list: list = []  # List of detected GPUs for multi-GPU selection
        self.cpu_name: str = "Unknown"
        self.cpu_cores: int = 0

        # Plugin connections
        self.plugin_connections: list = []

        # Benchmark results
        self.last_benchmark: dict = {}

        # Last model load error (for surfacing in API responses)
        self.last_load_error: Optional[str] = None

        # Multi-frame model support (e.g. EDVR uses 5 input frames)
        self.current_model_input_frames: int = 1

        # Metrics tracking
        self.total_jobs: int = 0
        self.total_failures: int = 0
        self.total_frames_processed: int = 0
        self.total_processing_time_ms: float = 0
        self.model_usage_count: dict = {}  # model_name -> count
        self.model_failure_count: dict = {}  # model_name -> count
        self.last_job_time_ms: float = 0
        self.service_start_time: float = 0

        # Health monitoring
        self.consecutive_failures: int = 0
        self.circuit_open: bool = False
        self.circuit_half_open: bool = False
        self.circuit_open_at: float = 0
        self.circuit_breaker_threshold: int = 5
        self.circuit_breaker_reset_seconds: int = 60

        # Model management
        self.model_last_used: dict = {}  # model_name -> timestamp

state = AppState()


class ModelNotReadyError(ValueError):
    """Raised when no model is loaded and upscaling is attempted."""
    pass


# Concurrency semaphore — created lazily in lifespan() to avoid
# asyncio.Semaphore before event loop exists (breaks Python 3.10+)
_upscale_semaphore: Optional[asyncio.Semaphore] = None

# Threading lock to prevent model-swap data races between load and inference
_model_lock = threading.Lock()

# Threading lock for circuit breaker state mutations
_circuit_lock = threading.Lock()

# Threading lock for processing_count (+=/-= is not atomic in CPython)
_processing_count_lock = threading.Lock()

# Per-model download lock — prevents concurrent downloads of the same model
_download_locks: dict[str, asyncio.Lock] = {}
_download_locks_guard = threading.Lock()

# Tile size for ONNX inference (prevents OOM on large images)
def _safe_int_env(name: str, default: int, min_val: Optional[int] = None, max_val: Optional[int] = None) -> int:
    """Parse integer from env var with fallback on invalid values.
    Clamps result to [min_val, max_val] when bounds are provided."""
    try:
        value = int(os.getenv(name, str(default)))
    except ValueError:
        logger.warning(f"Invalid {name} env var, using default {default}")
        value = default
    if min_val is not None and value < min_val:
        logger.warning(f"{name}={value} below minimum {min_val}, clamping")
        value = min_val
    if max_val is not None and value > max_val:
        logger.warning(f"{name}={value} exceeds maximum {max_val}, clamping")
        value = max_val
    return value

ONNX_TILE_SIZE = _safe_int_env("ONNX_TILE_SIZE", 512, min_val=64, max_val=2048)
ONNX_TILE_SIZE_MULTIFRAME = _safe_int_env("ONNX_TILE_SIZE_MULTIFRAME", 256, min_val=64, max_val=1024)
MAX_UPLOAD_BYTES = _safe_int_env("MAX_UPLOAD_BYTES", 50 * 1024 * 1024, min_val=1024, max_val=500 * 1024 * 1024)
MAX_INPUT_FRAMES = 10  # Safety cap for multi-frame endpoints

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

    # ============================================================
    # === v1.5.3.6 — Community Video-Optimized Models ===
    # ============================================================

    # --- Video Real-Time (Ultra-Fast, <3MB) ---
    "clearreality-x4": {
        "name": "ClearReality x4 (Ultra-Fast Video)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/4x-ClearRealityV1.onnx",
        "scale": 4,
        "description": "SPAN architecture, only 1.7MB. Real-time 4x for clean video. Best for faces, nature, hair.",
        "type": "onnx",
        "category": "video-fast",
        "model_type": "span",
        "available": True
    },
    "nomosuni-compact-x2": {
        "name": "NomosUni Compact x2 (Real-Time Video)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/2xNomosUni_compact_otf_medium.onnx",
        "scale": 2,
        "description": "Compact 2x with medium degradation handling. 2.4MB, ideal for real-time video playback.",
        "type": "onnx",
        "category": "video-fast",
        "model_type": "compact",
        "available": True
    },
    "lsdir-compact-x4": {
        "name": "LSDIR Compact x4 (Fast Video)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/4xLSDIRCompact.onnx",
        "scale": 4,
        "description": "Compact 4x trained on 85k images. 2.5MB, fast enough for near-real-time video.",
        "type": "onnx",
        "category": "video-fast",
        "model_type": "compact",
        "available": True
    },
    "swinir-small-x2": {
        "name": "SwinIR-S x2 (Lightweight Transformer)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/002_lightweightSR_DIV2K_s64w8_SwinIR-S_x2.onnx",
        "scale": 2,
        "description": "Lightweight SwinIR 2x, only 7.9MB. Good quality/speed balance for video.",
        "type": "onnx",
        "category": "video-fast",
        "model_type": "swinir",
        "available": True
    },
    "swinir-small-x4": {
        "name": "SwinIR-S x4 (Lightweight Transformer)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/002_lightweightSR_DIV2K_s64w8_SwinIR-S_x4.onnx",
        "scale": 4,
        "description": "Lightweight SwinIR 4x, only 8MB. Best quality in the fast category.",
        "type": "onnx",
        "category": "video-fast",
        "model_type": "swinir",
        "available": True
    },

    # --- Video Quality (Best single-frame for video content) ---
    "ultrasharp-v2-x4": {
        "name": "UltraSharp V2 x4 (Best Photo/Video)",
        "url": "https://huggingface.co/Kim2091/UltraSharpV2/resolve/main/4x-UltraSharpV2_fp32_op17.onnx",
        "scale": 4,
        "description": "DAT2 Transformer — best overall quality for photos and video. 49MB.",
        "type": "onnx",
        "category": "video-quality",
        "model_type": "dat2",
        "available": True
    },
    "nomos2-dat2-x4": {
        "name": "Nomos2 DAT2 x4 (Photo/Video HQ)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/4xNomos2_hq_dat2_fp32.onnx",
        "scale": 4,
        "description": "DAT2 trained on Nomos v2 dataset. Fixes noise, compression, blur. 53MB.",
        "type": "onnx",
        "category": "video-quality",
        "model_type": "dat2",
        "available": True
    },
    "nomos2-realplksr-x4": {
        "name": "Nomos2 RealPLKSR x4 (Efficient Quality)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/4xNomos2_realplksr_dysample_256_fp32_fullyoptimized.onnx",
        "scale": 4,
        "description": "Modern RealPLKSR architecture. 30MB — best quality-to-size ratio for video.",
        "type": "onnx",
        "category": "video-quality",
        "model_type": "realplksr",
        "available": True
    },

    # --- Film Restoration (Old Movies, DVDs, VHS) ---
    "fsdedither-x4": {
        "name": "FSDedither x4 (DVD/VHS Restore)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/4xFSDedither.onnx",
        "scale": 4,
        "description": "Removes dithering artifacts from old DVDs and digitized VHS tapes. 67MB.",
        "type": "onnx",
        "category": "film-restore",
        "model_type": "esrgan",
        "available": True
    },
    "nomos8k-hat-x4": {
        "name": "Nomos8k HAT-S x4 (Film Restoration)",
        "url": "https://github.com/Phhofm/models/raw/main/4xNomos8kSCHAT-S/4xNomos8kSCHAT-S.onnx",
        "scale": 4,
        "description": "HAT Transformer trained on 8k dataset. Handles JPG compression + blur. 57MB.",
        "type": "onnx",
        "category": "film-restore",
        "model_type": "hat",
        "available": True
    },

    # --- Anime Video Specialist ---
    "anime-compact-x4": {
        "name": "Real-ESRGAN Anime Compact x4 (Fast Anime)",
        "url": "https://huggingface.co/xiongjie/lightweight-real-ESRGAN-anime/resolve/main/RealESRGAN_x4plus_anime_4B32F.onnx",
        "scale": 4,
        "description": "Ultra-lightweight anime 4x, only 5MB. Perfect for anime real-time playback.",
        "type": "onnx",
        "category": "anime",
        "model_type": "compact",
        "available": True
    },
    "apisr-anime-x2": {
        "name": "APISR x2 (Anime Production Quality)",
        "url": "https://huggingface.co/Xenova/2x_APISR_RRDB_GAN_generator-onnx/resolve/main/onnx/model.onnx",
        "scale": 2,
        "description": "CVPR 2024 — trained on anime production pipeline. Best anime 2x quality. 18MB.",
        "type": "onnx",
        "category": "anime",
        "model_type": "rrdb",
        "available": True
    },
    "apisr-x3": {
        "name": "APISR x3 (General Quality)",
        "url": "https://huggingface.co/Xenova/3x_APISR_RRDB_GAN_generator-onnx/resolve/main/onnx/model.onnx",
        "scale": 3,
        "description": "CVPR 2024 — general 3x for photos & video. Ideal for 720p to 1080p. ~25MB.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "rrdb",
        "available": True
    },

    # ============================================================
    # === VIDEO SR Models (Multi-Frame) ===
    # ============================================================
    "edvr-m-x4": {
        "name": "EDVR-M x4 (Video SR - 5 Frame)",
        "url": "https://huggingface.co/kuscheltier/jellyfin-vsr-models/resolve/main/edvr_m_x4.onnx",
        "scale": 4,
        "description": "EDVR-M — Multi-frame video super-resolution. Uses 5 frames for temporal consistency. Best batch quality.",
        "type": "onnx",
        "category": "video-sr",
        "model_type": "edvr",
        "input_frames": 5,
        "available": True
    },
    "realbasicvsr-x4": {
        "name": "RealBasicVSR x4 (Video SR - 5 Frame)",
        "url": "https://huggingface.co/kuscheltier/jellyfin-vsr-models/resolve/main/realbasicvsr_x4.onnx",
        "scale": 4,
        "description": "RealBasicVSR — Recurrent VSR with optical flow (CVPR 2022). Best for real-world degraded video (VHS, DVD, streaming). ~50MB.",
        "type": "onnx",
        "category": "video-sr",
        "model_type": "realbasicvsr",
        "input_frames": 5,
        "available": True
    },
    "animesr-v2-x4": {
        "name": "AnimeSR v2 x4 (Anime Video SR - 5 Frame)",
        "url": "https://huggingface.co/kuscheltier/jellyfin-vsr-models/resolve/main/animesr_v2_x4.onnx",
        "scale": 4,
        "description": "AnimeSR v2 — Anime-specialized multi-frame VSR (NeurIPS 2022). Preserves line art and flat colors. ~30MB.",
        "type": "onnx",
        "category": "video-sr",
        "model_type": "animesr",
        "input_frames": 5,
        "available": True
    },

    # ============================================================
    # === VULKAN Models (ncnn — for AMD pre-RDNA2, Intel iGPU) ===
    # ============================================================
    "ncnn-realesrgan-x4": {
        "name": "Real-ESRGAN x4 (Vulkan GPU)",
        "url": "",
        "scale": 4,
        "description": "Real-ESRGAN x4 via ncnn-Vulkan. Works on any Vulkan GPU (AMD RX 5700, Intel Arc, etc.). Bundled with realsr-ncnn-vulkan.",
        "type": "ncnn",
        "category": "vulkan",
        "model_type": "realesrgan",
        "ncnn_model": "realesrgan-x4plus",
        "available": NCNN_AVAILABLE
    },
    "ncnn-realesrgan-anime-x4": {
        "name": "Real-ESRGAN Anime x4 (Vulkan GPU)",
        "url": "",
        "scale": 4,
        "description": "Real-ESRGAN Anime optimized via ncnn-Vulkan. Best for anime content on Vulkan GPUs.",
        "type": "ncnn",
        "category": "vulkan",
        "model_type": "realesrgan",
        "ncnn_model": "realesrgan-x4plus-anime",
        "available": NCNN_AVAILABLE
    },
    "ncnn-realsr-x4": {
        "name": "RealSR x4 (Vulkan GPU)",
        "url": "",
        "scale": 4,
        "description": "RealSR DF2K x4 via ncnn-Vulkan. Photo-realistic super-resolution on Vulkan GPUs.",
        "type": "ncnn",
        "category": "vulkan",
        "model_type": "realsr",
        "ncnn_model": "realsr-df2k-x4",
        "available": NCNN_AVAILABLE
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
    
    # Detect hardware (run in executor to avoid blocking event loop with subprocess calls)
    loop = asyncio.get_event_loop()
    await loop.run_in_executor(None, detect_hardware)
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
    try:
        state.max_concurrent = max(1, int(os.getenv("MAX_CONCURRENT_REQUESTS", "4")))
    except ValueError:
        logger.warning("Invalid MAX_CONCURRENT_REQUESTS env var, using default 4")
        state.max_concurrent = 4
    try:
        state.gpu_device_id = int(os.getenv("GPU_DEVICE_ID", "0"))
    except ValueError:
        logger.warning("Invalid GPU_DEVICE_ID env var, using default 0")
        state.gpu_device_id = 0

    # Re-create semaphore with actual env var value (inside event loop)
    global _upscale_semaphore
    _upscale_semaphore = asyncio.Semaphore(state.max_concurrent)

    # Track service uptime
    state.service_start_time = time.time()

    logger.info(f"GPU Enabled: {state.use_gpu}")
    logger.info(f"ONNX Runtime Available: {ONNX_AVAILABLE}")
    logger.info(f"ncnn-Vulkan Available: {NCNN_AVAILABLE}")

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

# Middleware: reject oversized requests before reading body into memory
@app.middleware("http")
async def limit_body_size(request: Request, call_next):
    content_length = request.headers.get("content-length")
    if content_length:
        try:
            if int(content_length) > MAX_UPLOAD_BYTES:
                return JSONResponse(status_code=413, content={"detail": f"Request too large ({content_length} bytes, max {MAX_UPLOAD_BYTES})"})
        except ValueError:
            return JSONResponse(status_code=400, content={"detail": "Invalid Content-Length header"})
    return await call_next(request)

# Mount static files
if STATIC_DIR.exists():
    app.mount("/static", StaticFiles(directory=str(STATIC_DIR)), name="static")


def get_model_path(model_name: str) -> Path:
    """Get the file path for a model, with path traversal protection."""
    model_info = AVAILABLE_MODELS.get(model_name, {})
    ext = model_info.get("type", "pb")
    safe_name = Path(model_name).name  # strip directory components
    path = (MODELS_DIR / f"{safe_name}.{ext}").resolve()
    if not str(path).startswith(str(MODELS_DIR.resolve())):
        raise ValueError(f"Invalid model path for: {model_name}")
    return path


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
            # Clear competing backends to prevent stale references
            state.onnx_session = None
            state.ncnn_upscaler = None
            state.last_load_error = None

        state.model_last_used[model_name] = time.time()
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
    model_type = model_info.get("type", "pb")

    # ncnn models are bundled with the realsr-ncnn-vulkan package — no file on disk needed
    if model_type != "ncnn" and not model_path.exists():
        logger.error(f"Model not found: {model_path}")
        return False

    if model_type == "pb":
        return await load_opencv_model(model_name, model_info, model_path)
    elif model_type == "onnx":
        return await load_onnx_model(model_name, model_info, model_path)
    elif model_type == "ncnn":
        return await load_ncnn_model(model_name, model_info, model_path)
    else:
        logger.error(f"Model type {model_type} not yet supported")
        return False


async def load_ncnn_model(model_name: str, model_info: dict, model_path: Path) -> bool:
    """Load a model using ncnn-Vulkan backend for GPU inference on Vulkan-capable GPUs.
    Supports pre-RDNA2 AMD (RX 5700 etc.), Intel iGPUs, and any Vulkan device.
    Uses realsr-ncnn-vulkan-python wrapper or raw ncnn bindings."""
    if not NCNN_AVAILABLE:
        logger.error("ncnn/Vulkan not available — install realsr-ncnn-vulkan-python or ncnn-vulkan")
        state.last_load_error = "ncnn-Vulkan not installed"
        return False

    scale = model_info.get("scale", 4)
    ncnn_model_name = model_info.get("ncnn_model", "realesrgan-x4plus")
    gpu_id = state.gpu_device_id

    try:
        if RealSR is not None:
            # Use the realsr-ncnn-vulkan-python high-level wrapper
            upscaler = RealSR(gpuid=gpu_id, model=ncnn_model_name, scale=scale)
            logger.info(f"ncnn-Vulkan: Loaded {model_name} via RealSR wrapper (GPU {gpu_id})")
        else:
            # Fallback: raw ncnn bindings (requires .param + .bin files)
            param_path = model_path.with_suffix('.param')
            bin_path = model_path.with_suffix('.bin')
            if not param_path.exists() or not bin_path.exists():
                logger.error(f"ncnn model files not found: {param_path}, {bin_path}")
                state.last_load_error = f"Missing .param/.bin files for {model_name}"
                return False

            net = ncnn.Net()
            net.opt.use_vulkan_compute = True
            net.opt.num_threads = 4
            net.load_param(str(param_path))
            net.load_model(str(bin_path))
            upscaler = net
            logger.info(f"ncnn-Vulkan: Loaded {model_name} via raw ncnn (GPU {gpu_id})")

        with _model_lock:
            state.ncnn_upscaler = upscaler
            state.ncnn_model_name = model_name
            state.ncnn_model_scale = scale
            state.ncnn_gpu_id = gpu_id
            state.current_model = model_name
            state.current_model_type = "ncnn"
            state.current_model_input_frames = model_info.get("input_frames", 1)
            state.onnx_model_scale = scale  # For compatibility with benchmark
            state.providers = ["VulkanComputeProvider"]
            # Clear competing backends to prevent stale references
            state.cv_model = None
            state.onnx_session = None
            state.last_load_error = None

        # Update model usage tracking
        state.model_last_used[model_name] = time.time()
        return True

    except Exception as e:
        logger.error(f"Failed to load ncnn model {model_name}: {e}")
        state.last_load_error = str(e)
        return False


def upscale_with_ncnn(img: np.ndarray) -> np.ndarray:
    """Upscale image using ncnn-Vulkan backend.
    Uses RealSR wrapper (tile-based internally) or raw ncnn inference."""
    with _model_lock:
        upscaler = state.ncnn_upscaler
        scale = state.ncnn_model_scale

    if upscaler is None:
        raise ValueError("No ncnn model loaded")

    if RealSR is not None and isinstance(upscaler, RealSR):
        # RealSR wrapper handles tiling internally
        # Convert BGR (OpenCV) to PIL Image for the wrapper
        from PIL import Image as PILImage
        img_rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
        pil_img = PILImage.fromarray(img_rgb)
        result_pil = upscaler.process(pil_img)
        result_rgb = np.array(result_pil)
        return cv2.cvtColor(result_rgb, cv2.COLOR_RGB2BGR)
    else:
        # Raw ncnn — manual tile-based inference with weighted blending
        h, w = img.shape[:2]
        tile_size = ONNX_TILE_SIZE
        overlap = 32
        step = tile_size - overlap
        out_h, out_w = h * scale, w * scale
        output = np.zeros((out_h, out_w, 3), dtype=np.float32)
        weight = np.zeros((out_h, out_w, 3), dtype=np.float32)

        def _ncnn_blend_weight(th: int, tw: int) -> np.ndarray:
            wy = np.ones(th, dtype=np.float32)
            wx = np.ones(tw, dtype=np.float32)
            if overlap > 0:
                ramp = np.linspace(0, 1, min(overlap * scale, th // 2) if th > 1 else 0, dtype=np.float32)
                wy[:len(ramp)] = ramp
                wy[-len(ramp):] = ramp[::-1]
                ramp_x = np.linspace(0, 1, min(overlap * scale, tw // 2) if tw > 1 else 0, dtype=np.float32)
                wx[:len(ramp_x)] = ramp_x
                wx[-len(ramp_x):] = ramp_x[::-1]
            return wy[:, None] * wx[None, :]

        for y in range(0, h, step):
            for x in range(0, w, step):
                # Clamp tile to image boundaries
                y_end = min(y + tile_size, h)
                x_end = min(x + tile_size, w)
                y_start = max(y_end - tile_size, 0)
                x_start = max(x_end - tile_size, 0)
                tile = img[y_start:y_end, x_start:x_end]
                th, tw = tile.shape[:2]

                # ncnn inference
                mat_in = ncnn.Mat.from_pixels(tile, ncnn.Mat.PixelType.PIXEL_BGR, tw, th)
                ex = upscaler.create_extractor()
                ex.input("data", mat_in)
                _, mat_out = ex.extract("output")
                # ncnn outputs CHW planar layout — reshape to CHW then transpose to HWC
                raw = np.array(mat_out)
                result_tile = raw.reshape(3, th * scale, tw * scale).transpose(1, 2, 0).astype(np.float32)

                oy, ox = y_start * scale, x_start * scale
                oth, otw = th * scale, tw * scale
                bw = _ncnn_blend_weight(oth, otw)[:, :, None]

                output[oy:oy+oth, ox:ox+otw] += result_tile * bw
                weight[oy:oy+oth, ox:ox+otw] += bw

        weight = np.maximum(weight, 1e-8)
        output = np.clip(output / weight, 0, 255).astype(np.uint8)
        return output


def _probe_tensorrt_subprocess(model_path_str: str, device_id: int) -> bool:
    """Test TensorRT in an isolated subprocess to avoid poisoning the CUDA context.
    Returns True if TensorRT works, False otherwise."""
    if not isinstance(device_id, int) or device_id < 0 or device_id > 99:
        logger.warning(f"Invalid device_id {device_id}, using 0")
        device_id = 0
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
        {'device_id': int(dev_id), 'trt_max_workspace_size': 2147483648},
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
                    state.current_model_input_frames = model_info.get("input_frames", 1)
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
                                state.current_model_input_frames = model_info.get("input_frames", 1)
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
                    # Build test tensor from actual model input shape (handles both
                    # single-frame (1,3,H,W) and multi-frame (1,5,3,H,W) models)
                    try:
                        model_input = session.get_inputs()[0]
                        input_name = model_input.name
                        input_shape = model_input.shape  # e.g. [1, 3, 'height', 'width'] or [1, 5, 3, 'h', 'w']
                        # Replace dynamic dims (strings/None) with small test size 16
                        test_shape = [d if isinstance(d, int) and d > 0 else 16 for d in input_shape]
                        test_input = np.random.rand(*test_shape).astype(np.float32)
                        session.run(None, {input_name: test_input})
                        logger.info(f"GPU inference verification passed ({gpu_providers[0]}) input_shape={input_shape}")
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
                            {'device_id': int(device_id), 'trt_max_workspace_size': 2147483648},
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
            state.current_model_input_frames = model_info.get("input_frames", 1)
            state.current_model = model_name
            state.current_model_type = "onnx"
            # Clear competing backends to prevent stale references
            state.cv_model = None
            state.ncnn_upscaler = None
            state.last_load_error = None
            # Update providers list inside lock for thread safety
            state.providers = session.get_providers()

        state.model_last_used[model_name] = time.time()
        logger.info(f"ONNX model {model_name} loaded successfully with: {state.providers}")

        return True

    except Exception as e:
        state.last_load_error = str(e)
        logger.error(f"Failed to load ONNX model {model_name}: {e}")
        return False


async def download_model(model_name: str) -> bool:
    """Download a model from the repository. Thread-safe per model name."""
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

    # Per-model async lock prevents concurrent downloads of the same model
    with _download_locks_guard:
        if model_name not in _download_locks:
            _download_locks[model_name] = asyncio.Lock()
        dl_lock = _download_locks[model_name]

    async with dl_lock:
        # Re-check after acquiring lock (another coroutine may have completed download)
        if model_path.exists():
            logger.info(f"Model {model_name} already downloaded (by concurrent request)")
            return True

        # Unique temp file avoids collisions with any lingering partial downloads
        temp_path = model_path.with_suffix(f".tmp.{uuid.uuid4().hex[:8]}")
        try:
            download_url = model_info.get("url")

            # ncnn models are bundled with the realsr-ncnn-vulkan package — no download needed
            if not download_url:
                if model_info.get("type") == "ncnn":
                    logger.info(f"Model {model_name} is bundled (ncnn) — no download needed")
                    return True
                raise ValueError(f"No download URL for model {model_name}")

            # Validate download URL against allowlist (hostname-based to prevent SSRF bypass)
            _ALLOWED_DOWNLOAD_HOSTS = (
                "huggingface.co",
                "github.com",
                "raw.githubusercontent.com",
            )
            parsed_url = urllib.parse.urlparse(download_url)
            if parsed_url.scheme != "https" or parsed_url.hostname not in _ALLOWED_DOWNLOAD_HOSTS:
                raise ValueError(f"Download URL not from allowed domain: {parsed_url.hostname}")

            logger.info(f"Downloading model {model_name} from {download_url}")

            async with httpx.AsyncClient(timeout=600.0, follow_redirects=True) as dl_client:
                async with dl_client.stream("GET", download_url) as response:
                    response.raise_for_status()
                    with open(temp_path, "wb") as f:
                        async for chunk in response.aiter_bytes(chunk_size=65536):
                            f.write(chunk)

            # Atomic rename — prevents partial files surviving crashes
            temp_path.rename(model_path)

            size_mb = model_path.stat().st_size / 1024 / 1024
            logger.info(f"Model {model_name} downloaded ({size_mb:.1f} MB)")
            return True

        except Exception as e:
            logger.error(f"Failed to download model {model_name}: {e}")
            # Only clean up temp file — never delete a pre-existing valid model
            if temp_path.exists():
                temp_path.unlink()
            return False


def upscale_image(image_bytes: bytes) -> bytes:
    """Upscale an image using the loaded model (OpenCV or ONNX)."""
    # Snapshot model state under lock for thread-safe dispatch
    with _model_lock:
        model_type = state.current_model_type
        cv_model = state.cv_model
        has_onnx = state.onnx_session is not None
        has_ncnn = state.ncnn_upscaler is not None
        if state.current_model is None:
            raise ModelNotReadyError("No model loaded")

    # Decode image
    nparr = np.frombuffer(image_bytes, np.uint8)
    img = cv2.imdecode(nparr, cv2.IMREAD_COLOR)

    if img is None:
        raise ValueError("Failed to decode image")

    if model_type == "opencv" and cv_model is not None:
        # Upscale using OpenCV DNN Super Resolution
        result = cv_model.upsample(img)
    elif model_type == "onnx" and has_onnx:
        # Upscale using ONNX Runtime (Real-ESRGAN)
        result = upscale_with_onnx(img)
    elif model_type == "ncnn" and has_ncnn:
        # Upscale using ncnn-Vulkan
        result = upscale_with_ncnn(img)
    else:
        raise ModelNotReadyError("No model loaded")

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


def _onnx_infer_multiframe_tile(tiles: list, session, input_name: str, output_name: str, num_frames: int) -> np.ndarray:
    """Infer a multi-frame tile stack. tiles = list of num_frames arrays, each (H,W,3) float32 [0,1]."""
    stacked = np.stack(tiles, axis=0)                    # (T, H, W, 3)
    stacked = np.transpose(stacked, (0, 3, 1, 2))       # (T, 3, H, W)
    batch = np.expand_dims(stacked, axis=0).astype(np.float32)  # (1, T, 3, H, W)

    result = session.run([output_name], {input_name: batch})[0]

    # Handle models that output all T frames: (1, T, 3, H*s, W*s)
    if result.ndim == 5:
        result = result[:, num_frames // 2, :, :, :]     # center frame only

    result = np.squeeze(result, axis=0)                  # (3, H*s, W*s)
    result = np.transpose(result, (1, 2, 0))             # (H*s, W*s, 3)
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

    # Acquire model lock ONCE to capture consistent session/config snapshot
    with _model_lock:
        session = state.onnx_session
        if session is None:
            raise ValueError("ONNX session not loaded")
        input_name = session.get_inputs()[0].name
        output_name = session.get_outputs()[0].name
        scale = state.onnx_model_scale or 4

    # Small image: skip tiling, process directly
    if w <= tile_size and h <= tile_size:
        result = _onnx_infer_tile(img_rgb, session, input_name, output_name)
        result = np.clip(result * 255.0, 0, 255).astype(np.uint8)
        return cv2.cvtColor(result, cv2.COLOR_RGB2BGR)

    # Tile-based processing for large images
    step = tile_size - overlap
    out_h, out_w = h * scale, w * scale
    output = np.zeros((out_h, out_w, 3), dtype=np.float32)
    weight = np.zeros((out_h, out_w, 3), dtype=np.float32)

    # Build a per-tile blending mask (linear ramp on overlap borders)
    def _make_blend_weight(th: int, tw: int) -> np.ndarray:
        """Create a 2D blending weight array that tapers at edges for seamless stitching."""
        wy = np.ones(th, dtype=np.float32)
        wx = np.ones(tw, dtype=np.float32)
        if overlap > 0:
            # Clamp ramp length to prevent negative index wrap on tiles smaller than overlap
            ramp_y = min(overlap, th // 2) if th > 1 else 0
            ramp_x = min(overlap, tw // 2) if tw > 1 else 0
            if ramp_y > 0:
                ramp = np.linspace(0, 1, ramp_y, dtype=np.float32)
                wy[:ramp_y] = ramp
                wy[-ramp_y:] = ramp[::-1]
            if ramp_x > 0:
                ramp = np.linspace(0, 1, ramp_x, dtype=np.float32)
                wx[:ramp_x] = ramp
                wx[-ramp_x:] = ramp[::-1]
        return wy[:, None] * wx[None, :]  # (th, tw)

    for y in range(0, h, step):
        for x in range(0, w, step):
            # Clamp tile to image boundaries
            y_end = min(y + tile_size, h)
            x_end = min(x + tile_size, w)
            y_start = max(y_end - tile_size, 0)
            x_start = max(x_end - tile_size, 0)

            tile = img_rgb[y_start:y_end, x_start:x_end]

            # Inference using captured session (no lock needed — snapshot is consistent)
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

            output[oy_start:oy_end, ox_start:ox_end] += out_tile.astype(np.float32) * blend_w3
            weight[oy_start:oy_end, ox_start:ox_end] += blend_w3

    # Normalize by accumulated weight
    weight = np.maximum(weight, 1e-8)
    output = output / weight

    result = np.clip(output * 255.0, 0, 255).astype(np.uint8)
    return cv2.cvtColor(result, cv2.COLOR_RGB2BGR)


def upscale_multiframe(frames: list) -> np.ndarray:
    """Upscale using multi-frame model. frames = list of np.ndarray BGR images. Returns upscaled center frame BGR."""
    num_frames = len(frames)

    # Acquire model references once under lock
    with _model_lock:
        session = state.onnx_session
        if session is None:
            raise ValueError("No ONNX model loaded")
        input_name = session.get_inputs()[0].name
        output_name = session.get_outputs()[0].name
        scale = state.onnx_model_scale or 4

    # Convert all frames BGR -> RGB float32
    frames_rgb = []
    for f in frames:
        rgb = cv2.cvtColor(f, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
        frames_rgb.append(rgb)

    h, w, _ = frames_rgb[0].shape
    tile_size = ONNX_TILE_SIZE_MULTIFRAME
    overlap = 32

    # Small image fast path: no tiling needed
    if w <= tile_size and h <= tile_size:
        result_rgb = _onnx_infer_multiframe_tile(frames_rgb, session, input_name, output_name, num_frames)
        result_rgb = np.clip(result_rgb * 255.0, 0, 255).astype(np.uint8)
        return cv2.cvtColor(result_rgb, cv2.COLOR_RGB2BGR)

    # Tiled processing — same grid for all frames
    out_h, out_w = h * scale, w * scale
    output = np.zeros((out_h, out_w, 3), dtype=np.float32)
    weight = np.zeros((out_h, out_w, 3), dtype=np.float32)

    step = tile_size - overlap
    y_tiles = list(range(0, max(h - tile_size, 0) + 1, step))
    if not y_tiles or y_tiles[-1] + tile_size < h:
        y_tiles.append(max(h - tile_size, 0))
    x_tiles = list(range(0, max(w - tile_size, 0) + 1, step))
    if not x_tiles or x_tiles[-1] + tile_size < w:
        x_tiles.append(max(w - tile_size, 0))

    for y in y_tiles:
        for x in x_tiles:
            # Extract same tile position from all frames, pad if edge tile is smaller than tile_size
            tile_list = []
            for frame_rgb in frames_rgb:
                tile = frame_rgb[y:y+tile_size, x:x+tile_size, :]
                # Pad undersized edge tiles to tile_size (e.g. when only one dimension > tile_size)
                th_actual, tw_actual = tile.shape[:2]
                if th_actual < tile_size or tw_actual < tile_size:
                    padded = np.zeros((tile_size, tile_size, 3), dtype=tile.dtype)
                    padded[:th_actual, :tw_actual, :] = tile
                    tile = padded
                tile_list.append(tile)

            out_tile = _onnx_infer_multiframe_tile(tile_list, session, input_name, output_name, num_frames)

            # Compute actual content dimensions (clip to canvas, crop padded regions)
            oy, ox = y * scale, x * scale
            oy_end = min(oy + out_tile.shape[0], out_h)
            ox_end = min(ox + out_tile.shape[1], out_w)
            actual_th = oy_end - oy
            actual_tw = ox_end - ox

            # Build blend weights using ACTUAL content size, not padded tile size
            # This prevents brightness artifacts at edges where tiles were zero-padded
            blend_y = np.ones(actual_th, dtype=np.float32)
            blend_x = np.ones(actual_tw, dtype=np.float32)
            ramp = overlap * scale
            if ramp > 0:
                # Clamp ramp to half tile size to prevent overlapping writes at center
                ramp_y = min(ramp, actual_th // 2) if actual_th > 1 else 0
                ramp_x = min(ramp, actual_tw // 2) if actual_tw > 1 else 0
                for i in range(ramp_y):
                    blend_y[i] = i / ramp
                    blend_y[actual_th - 1 - i] = i / ramp
                for i in range(ramp_x):
                    blend_x[i] = i / ramp
                    blend_x[actual_tw - 1 - i] = i / ramp
            blend_w = blend_y[:, None] * blend_x[None, :]
            blend_w3 = blend_w[:, :, None]

            output[oy:oy_end, ox:ox_end] += out_tile[:actual_th, :actual_tw].astype(np.float32) * blend_w3
            weight[oy:oy_end, ox:ox_end] += blend_w3

    weight = np.maximum(weight, 1e-8)
    output = output / weight
    output = np.clip(output * 255.0, 0, 255).astype(np.uint8)
    return cv2.cvtColor(output, cv2.COLOR_RGB2BGR)


def upscale_image_array(img: np.ndarray) -> np.ndarray:
    """Upscale a numpy image array using the loaded model. Avoids double encode/decode for frame pipeline."""
    with _model_lock:
        if state.current_model is None:
            raise ModelNotReadyError("No model loaded")
        model_type = state.current_model_type
        cv_model = state.cv_model
        has_onnx = state.onnx_session is not None
        has_ncnn = state.ncnn_upscaler is not None

    if model_type == "opencv" and cv_model is not None:
        return cv_model.upsample(img)
    elif model_type == "onnx" and has_onnx:
        return upscale_with_onnx(img)
    elif model_type == "ncnn" and has_ncnn:
        return upscale_with_ncnn(img)
    else:
        raise ModelNotReadyError("No model loaded")


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
    
    if state.current_model_type == "onnx":
        scale = state.onnx_model_scale
    elif state.current_model_type == "ncnn":
        scale = state.ncnn_model_scale
    else:
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
    status_data = {
        "status": "degraded" if state.circuit_open else "healthy",
        "model_loaded": state.current_model is not None,
        "model_name": state.current_model,
        "model_type": state.current_model_type,
        "providers": state.providers,
        "using_gpu": state.use_gpu,
        "gpu_name": state.gpu_name,
        "circuit_open": state.circuit_open
    }
    if state.circuit_open:
        return JSONResponse(status_code=503, content=status_data)
    return status_data


@app.get("/status")
async def status():
    """Get service status."""
    if state.current_model_type == "onnx":
        scale = state.onnx_model_scale
    elif state.current_model_type == "ncnn":
        scale = state.ncnn_model_scale
    else:
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
        "tensorrt_available": has_tensorrt,
        "input_frames": state.current_model_input_frames
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
    except Exception as e:
        logger.debug(f"NVIDIA GPU enumeration skipped: {e}")

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
    except Exception as e:
        logger.debug(f"Intel GPU enumeration skipped: {e}")

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

    # ONNX inference test — build test tensor from actual model input shape
    if ONNX_AVAILABLE and state.onnx_session is not None:
        try:
            model_input = state.onnx_session.get_inputs()[0]
            input_name = model_input.name
            input_shape = model_input.shape
            test_shape = [d if isinstance(d, int) and d > 0 else 16 for d in input_shape]
            test_input = np.random.rand(*test_shape).astype(np.float32)
            start = time.time()
            state.onnx_session.run(None, {input_name: test_input})
            elapsed = time.time() - start
            diagnostics["inference_test"] = {"status": "ok", "time_ms": round(elapsed * 1000, 2), "input_shape": str(input_shape)}
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
    # Validate jellyfin_url scheme
    parsed = urllib.parse.urlparse(jellyfin_url)
    if parsed.scheme not in ("http", "https"):
        raise HTTPException(status_code=400, detail="Invalid URL scheme. Only http:// and https:// are allowed.")

    # Block SSRF: reject private/loopback IP addresses
    import ipaddress
    hostname = parsed.hostname
    if hostname:
        # Reject well-known loopback/private hostnames
        _blocked_hosts = {"localhost", "localhost.localdomain", "ip6-localhost", "ip6-loopback"}
        if hostname.lower() in _blocked_hosts:
            raise HTTPException(status_code=400, detail="Private/loopback URLs are not allowed.")
        try:
            addr = ipaddress.ip_address(hostname)
            if addr.is_private or addr.is_loopback or addr.is_link_local or addr.is_reserved:
                raise HTTPException(status_code=400, detail="Private/loopback URLs are not allowed.")
        except ValueError:
            # hostname is a DNS name, not a raw IP — allow it (DNS may resolve to anything,
            # but blocking raw private IPs covers the most exploitable SSRF vectors)
            pass

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
    if gpu_device_id is not None and (gpu_device_id < 0 or gpu_device_id > 99):
        raise HTTPException(status_code=400, detail="gpu_device_id must be 0-99")

    if model_name not in AVAILABLE_MODELS:
        raise HTTPException(status_code=404, detail=f"Model {model_name} not found")

    model_info = AVAILABLE_MODELS[model_name]
    if not model_info.get("available", True):
        raise HTTPException(status_code=400, detail=f"Model {model_name} is not yet available")

    model_path = get_model_path(model_name)

    # Auto-download model if not present (ncnn models are bundled, skip path check)
    model_type = model_info.get("type", "pb")
    if model_type != "ncnn" and not model_path.exists():
        logger.info(f"Model {model_name} not downloaded — auto-downloading...")
        dl_success = await download_model(model_name)
        if not dl_success:
            raise HTTPException(status_code=500, detail=f"Failed to auto-download model {model_name}")

    # Save previous GPU state for rollback on failure
    prev_use_gpu = state.use_gpu
    prev_device_id = state.gpu_device_id

    state.use_gpu = use_gpu
    if gpu_device_id is not None:
        state.gpu_device_id = gpu_device_id
        logger.info(f"GPU device ID set to {gpu_device_id}")

    success = await load_model(model_name)

    if not success:
        # Rollback GPU state on failure
        state.use_gpu = prev_use_gpu
        state.gpu_device_id = prev_device_id
        logger.error(f"Model load failed: {state.last_load_error}")
        raise HTTPException(status_code=500, detail="Failed to load model. Check server logs for details.")

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
    _check_circuit_breaker()

    if state.cv_model is None and state.onnx_session is None and state.ncnn_upscaler is None:
        raise HTTPException(status_code=400, detail="No model loaded. Please load a model first.")

    # Validate scale against loaded model's native scale
    if state.current_model_type == "onnx":
        model_scale = state.onnx_model_scale
    elif state.current_model_type == "ncnn":
        model_scale = state.ncnn_model_scale
    else:
        model_scale = state.cv_model_scale
    if scale != model_scale:
        logger.warning(f"Requested scale={scale} differs from loaded model scale={model_scale}. Using model's native scale={model_scale}.")

    # Capture semaphore reference so release always targets the same instance
    # (protects against /config recreating the semaphore mid-request)
    sem = _upscale_semaphore
    acquired = False
    try:
        await asyncio.wait_for(sem.acquire(), timeout=0)
        acquired = True
        with _processing_count_lock:
            state.processing_count += 1
    except asyncio.TimeoutError:
        raise HTTPException(status_code=429, detail="Too many concurrent requests")

    start_time = time.time()
    model_name = state.current_model or "unknown"
    try:
        # Read image
        image_bytes = await file.read()
        if len(image_bytes) > MAX_UPLOAD_BYTES:
            raise HTTPException(status_code=413, detail=f"Image too large ({len(image_bytes)} bytes, max {MAX_UPLOAD_BYTES})")

        # Upscale in thread pool to not block async
        loop = asyncio.get_running_loop()
        result = await loop.run_in_executor(None, upscale_image, image_bytes)

        duration_ms = (time.time() - start_time) * 1000
        _record_success(model_name, duration_ms)
        return Response(content=result, media_type="image/png")

    except ModelNotReadyError as e:
        _record_failure(model_name)
        logger.warning(f"Upscale input error: {e}")
        raise HTTPException(status_code=400, detail="Invalid input or model not ready")
    except Exception as e:
        _record_failure(model_name)
        logger.error(f"Upscale failed: {e}")
        raise HTTPException(status_code=500, detail="Upscaling failed")
    finally:
        if acquired:
            with _processing_count_lock:
                state.processing_count -= 1
            sem.release()


@app.get("/benchmark")
async def benchmark_endpoint():
    """Run a benchmark on the current model."""
    if state.cv_model is None and state.onnx_session is None and state.ncnn_upscaler is None:
        raise HTTPException(status_code=400, detail="No model loaded")

    loop = asyncio.get_running_loop()
    result = await loop.run_in_executor(None, run_benchmark, 256)
    return result


@app.post("/upscale-frame")
async def upscale_frame_endpoint(request: Request):
    """Fast frame upscaling for real-time playback. Raw JPEG in, JPEG out. Returns 503 when busy."""
    _check_circuit_breaker()

    if state.cv_model is None and state.onnx_session is None and state.ncnn_upscaler is None:
        raise HTTPException(status_code=400, detail="No model loaded")

    # Capture semaphore reference for safe release
    sem = _upscale_semaphore
    acquired = False
    try:
        await asyncio.wait_for(sem.acquire(), timeout=0)
        acquired = True
        with _processing_count_lock:
            state.processing_count += 1
    except asyncio.TimeoutError:
        raise HTTPException(status_code=503, detail="Busy")

    start_time = time.time()
    model_name = state.current_model or "unknown"
    try:
        body = await request.body()
        if not body:
            raise HTTPException(status_code=400, detail="Empty body")
        if len(body) > MAX_UPLOAD_BYTES:
            raise HTTPException(status_code=413, detail=f"Image too large ({len(body)} bytes, max {MAX_UPLOAD_BYTES})")

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

        duration_ms = (time.time() - start_time) * 1000
        _record_success(model_name, duration_ms)
        with _processing_count_lock:
            state.total_frames_processed += 1
        return Response(content=buffer.tobytes(), media_type="image/jpeg")

    except HTTPException:
        raise
    except Exception as e:
        _record_failure(model_name)
        logger.error(f"Frame upscale failed: {e}")
        raise HTTPException(status_code=500, detail="Frame upscaling failed")
    finally:
        if acquired:
            with _processing_count_lock:
                state.processing_count -= 1
            sem.release()


@app.post("/upscale-video-chunk")
async def upscale_video_chunk(request: Request):
    """Multi-frame upscaling: receives N PNG frames, returns upscaled center frame."""
    _check_circuit_breaker()

    if state.current_model is None:
        raise HTTPException(status_code=400, detail="No model loaded")

    # Read expected_frames under model lock for consistency with the loaded model
    with _model_lock:
        expected_frames = min(state.current_model_input_frames, MAX_INPUT_FRAMES)

    # Capture semaphore reference for safe release
    sem = _upscale_semaphore
    acquired = False
    try:
        await asyncio.wait_for(sem.acquire(), timeout=0)
        acquired = True
        with _processing_count_lock:
            state.processing_count += 1
    except asyncio.TimeoutError:
        raise HTTPException(status_code=503, detail="Busy")

    start_time = time.time()
    model_name = state.current_model or "unknown"
    try:
        # Parse multipart form
        form = await request.form()
        frame_files = []
        for i in range(expected_frames):
            key = f"frame_{i}"
            if key not in form:
                raise HTTPException(status_code=400, detail=f"Missing {key}. Expected {expected_frames} frames.")
            frame_files.append(form[key])

        # Read all frames
        frames = []
        for ff in frame_files:
            data = await ff.read()
            img = cv2.imdecode(np.frombuffer(data, np.uint8), cv2.IMREAD_COLOR)
            if img is None:
                raise HTTPException(status_code=400, detail="Invalid image data")
            frames.append(img)

        # If single-frame model loaded, transparent fallback: upscale center frame only
        if expected_frames == 1:
            center = frames[len(frames) // 2]
            loop = asyncio.get_running_loop()
            result = await loop.run_in_executor(None, upscale_image_array, center)
        else:
            loop = asyncio.get_running_loop()
            result = await loop.run_in_executor(None, upscale_multiframe, frames)

        # Encode as PNG
        _, buffer = cv2.imencode('.png', result)

        duration_ms = (time.time() - start_time) * 1000
        _record_success(model_name, duration_ms)
        with _processing_count_lock:
            state.total_frames_processed += expected_frames
        return Response(content=buffer.tobytes(), media_type="image/png")

    except HTTPException:
        raise
    except Exception as e:
        _record_failure(model_name)
        logger.error(f"Multi-frame upscale error: {e}")
        raise HTTPException(status_code=500, detail="Multi-frame upscaling failed")
    finally:
        if acquired:
            with _processing_count_lock:
                state.processing_count -= 1
            sem.release()


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
        logger.error(f"Benchmark warmup failed: {e}")
        return {"error": "Warmup failed. Check server logs for details."}

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
        "output_width": width * (state.onnx_model_scale if state.current_model_type == "onnx" else (state.ncnn_model_scale if state.current_model_type == "ncnn" else state.cv_model_scale)),
        "output_height": height * (state.onnx_model_scale if state.current_model_type == "onnx" else (state.ncnn_model_scale if state.current_model_type == "ncnn" else state.cv_model_scale))
    }


@app.get("/benchmark-frame")
async def benchmark_frame_endpoint(width: int = 480, height: int = 270):
    """Benchmark at actual capture resolution for real-time upscaling feasibility."""
    if state.cv_model is None and state.onnx_session is None and state.ncnn_upscaler is None:
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
        if max_concurrent < 1 or max_concurrent > 256:
            raise HTTPException(status_code=400, detail="max_concurrent must be 1-256")
        state.max_concurrent = max_concurrent
        global _upscale_semaphore
        # Replace semaphore — new requests will use the new limit.
        # In-flight requests still hold the old semaphore reference from their
        # local 'acquired' variable, so they'll release correctly on completion.
        _upscale_semaphore = asyncio.Semaphore(max_concurrent)
        logger.info(f"max_concurrent changed to {max_concurrent}, new semaphore active (in-flight requests unaffected)")
    if gpu_device_id is not None:
        if gpu_device_id < 0 or gpu_device_id > 99:
            raise HTTPException(status_code=400, detail="gpu_device_id must be 0-99")
        state.gpu_device_id = gpu_device_id
        logger.info(f"GPU device ID updated to {gpu_device_id}")

    return {
        "use_gpu": state.use_gpu,
        "max_concurrent": state.max_concurrent,
        "gpu_device_id": state.gpu_device_id
    }


# ============================================================
# === Prometheus-Style Metrics Endpoint ===
# ============================================================

@app.get("/metrics")
async def prometheus_metrics():
    """Prometheus-compatible metrics endpoint."""
    uptime = time.time() - state.service_start_time if state.service_start_time > 0 else 0
    avg_time = state.total_processing_time_ms / max(state.total_jobs, 1)

    lines = [
        "# HELP upscaler_jobs_total Total upscaling jobs processed",
        "# TYPE upscaler_jobs_total counter",
        f"upscaler_jobs_total {state.total_jobs}",
        "",
        "# HELP upscaler_failures_total Total failed upscaling jobs",
        "# TYPE upscaler_failures_total counter",
        f"upscaler_failures_total {state.total_failures}",
        "",
        "# HELP upscaler_frames_total Total frames processed",
        "# TYPE upscaler_frames_total counter",
        f"upscaler_frames_total {state.total_frames_processed}",
        "",
        "# HELP upscaler_processing_time_avg_ms Average processing time per job",
        "# TYPE upscaler_processing_time_avg_ms gauge",
        f"upscaler_processing_time_avg_ms {avg_time:.1f}",
        "",
        "# HELP upscaler_last_job_time_ms Last job processing time",
        "# TYPE upscaler_last_job_time_ms gauge",
        f"upscaler_last_job_time_ms {state.last_job_time_ms:.1f}",
        "",
        "# HELP upscaler_active_jobs Currently processing jobs",
        "# TYPE upscaler_active_jobs gauge",
        f"upscaler_active_jobs {state.processing_count}",
        "",
        "# HELP upscaler_uptime_seconds Service uptime in seconds",
        "# TYPE upscaler_uptime_seconds gauge",
        f"upscaler_uptime_seconds {uptime:.0f}",
        "",
        "# HELP upscaler_circuit_open Circuit breaker state (1=open, 0=closed)",
        "# TYPE upscaler_circuit_open gauge",
        f"upscaler_circuit_open {1 if state.circuit_open else 0}",
        "",
        "# HELP upscaler_consecutive_failures Consecutive failures count",
        "# TYPE upscaler_consecutive_failures gauge",
        f"upscaler_consecutive_failures {state.consecutive_failures}",
    ]

    # Per-model usage metrics
    for model_name, count in state.model_usage_count.items():
        safe_name = model_name.replace("-", "_").replace(".", "_").replace('"', '_')
        lines.append(f'upscaler_model_usage_total{{model="{safe_name}"}} {count}')

    for model_name, count in state.model_failure_count.items():
        safe_name = model_name.replace("-", "_").replace(".", "_").replace('"', '_')
        lines.append(f'upscaler_model_failures_total{{model="{safe_name}"}} {count}')

    return Response(content="\n".join(lines) + "\n", media_type="text/plain; charset=utf-8")


# ============================================================
# === Health Check & Circuit Breaker ===
# ============================================================

def _record_success(model_name: str, duration_ms: float):
    """Record a successful job for metrics and health tracking.
    Note: total_frames_processed is NOT incremented here — each endpoint
    controls its own frame count (1 for single-frame, N for multi-frame)."""
    with _circuit_lock:
        state.total_jobs += 1
        state.total_processing_time_ms += duration_ms
        state.last_job_time_ms = duration_ms
        state.consecutive_failures = 0
        state.model_usage_count[model_name] = state.model_usage_count.get(model_name, 0) + 1
        state.model_last_used[model_name] = time.time()

        # Close circuit breaker on success (handles both half-open probe and normal)
        was_half_open = state.circuit_half_open
        state.circuit_half_open = False
        if state.circuit_open or was_half_open:
            state.circuit_open = False
            logger.info("Circuit breaker CLOSED after successful job")


def _record_failure(model_name: str):
    """Record a failed job for metrics and health tracking."""
    with _circuit_lock:
        state.total_jobs += 1
        state.total_failures += 1
        state.consecutive_failures += 1
        state.model_failure_count[model_name] = state.model_failure_count.get(model_name, 0) + 1

        # If this was a half-open probe that failed, re-open the circuit
        if state.circuit_half_open:
            state.circuit_half_open = False
            state.circuit_open = True
            state.circuit_open_at = time.time()
            logger.warning("Circuit breaker RE-OPENED after half-open probe failed")
            return

        # Open circuit breaker after threshold failures
        if state.consecutive_failures >= state.circuit_breaker_threshold and not state.circuit_open:
            state.circuit_open = True
            state.circuit_open_at = time.time()
            logger.warning(f"Circuit breaker OPEN after {state.consecutive_failures} consecutive failures")


def _check_circuit_breaker():
    """Check if circuit breaker allows processing. Raises 503 if open."""
    exc = None
    with _circuit_lock:
        # If circuit is closed AND not in half-open probe, allow request
        if not state.circuit_open and not state.circuit_half_open:
            return

        # If circuit is closed but half-open probe is in progress,
        # block all non-probe requests until probe completes
        if not state.circuit_open and state.circuit_half_open:
            exc = HTTPException(
                status_code=503,
                detail="Circuit breaker half-open — probe in progress, retry shortly"
            )
        elif state.circuit_open:
            elapsed = time.time() - state.circuit_open_at
            if elapsed >= state.circuit_breaker_reset_seconds:
                if state.circuit_half_open:
                    # Another request is already probing — block this one
                    exc = HTTPException(
                        status_code=503,
                        detail="Circuit breaker half-open — probe in progress, retry shortly"
                    )
                else:
                    # Half-open: allow exactly one request through as probe
                    state.circuit_half_open = True
                    logger.info("Circuit breaker HALF-OPEN, allowing one probe request")
                    return
            else:
                exc = HTTPException(
                    status_code=503,
                    detail=f"Circuit breaker open — {state.consecutive_failures} consecutive failures. "
                           f"Retry in {int(state.circuit_breaker_reset_seconds - elapsed)}s"
                )
    # Raise outside the lock to prevent deadlock if exception handlers acquire _circuit_lock
    if exc:
        raise exc


@app.get("/health/detailed")
async def health_detailed():
    """Detailed health status including circuit breaker, GPU, and model state."""
    gpu_healthy = None  # None = not applicable (GPU disabled)
    gpu_error = None

    # Quick GPU check — covers ONNX, ncnn, and OpenVINO backends
    if not state.use_gpu:
        gpu_healthy = None
        gpu_error = "GPU disabled (CPU mode)"
    elif state.use_gpu:
        if state.current_model_type == "ncnn":
            gpu_healthy = state.ncnn_upscaler is not None
            if not gpu_healthy:
                gpu_error = "ncnn-Vulkan upscaler not loaded"
        elif state.onnx_session:
            try:
                providers = state.onnx_session.get_providers()
                gpu_healthy = any(
                    "CUDA" in p or "Tensorrt" in p or "CoreML" in p
                    or "DirectML" in p or "OpenVINO" in p
                    for p in providers
                )
                if not gpu_healthy:
                    gpu_error = f"Only CPU providers active: {providers}"
            except Exception as e:
                gpu_healthy = False
                gpu_error = str(e)
        elif state.cv_model is not None:
            # OpenCV DNN — GPU status is best-effort (no provider list)
            gpu_healthy = True
        else:
            gpu_healthy = False
            gpu_error = "No model loaded"

    return {
        "status": "healthy" if not state.circuit_open else "degraded",
        "circuit_breaker": {
            "open": state.circuit_open,
            "half_open": state.circuit_half_open,
            "consecutive_failures": state.consecutive_failures,
            "threshold": state.circuit_breaker_threshold,
            "reset_seconds": state.circuit_breaker_reset_seconds
        },
        "gpu": {
            "healthy": gpu_healthy,
            "name": state.gpu_name,
            "memory": state.gpu_memory,
            "error": gpu_error
        },
        "model": {
            "loaded": state.current_model is not None,
            "name": state.current_model,
            "type": state.current_model_type,
            "input_frames": state.current_model_input_frames,
            "last_error": bool(state.last_load_error)
        },
        "metrics": {
            "total_jobs": state.total_jobs,
            "total_failures": state.total_failures,
            "avg_time_ms": state.total_processing_time_ms / max(state.total_jobs, 1),
            "uptime_seconds": time.time() - state.service_start_time if state.service_start_time > 0 else 0
        }
    }


# ============================================================
# === Model Auto-Management ===
# ============================================================

@app.get("/models/disk-usage")
async def models_disk_usage():
    """Show disk usage of downloaded models."""
    models_dir = str(MODELS_DIR)
    if not os.path.isdir(models_dir):
        return {"models_dir": models_dir, "total_size_mb": 0, "models": []}

    model_files = []
    total_size = 0
    for f in os.listdir(models_dir):
        fpath = os.path.join(models_dir, f)
        if os.path.isfile(fpath):
            size = os.path.getsize(fpath)
            total_size += size
            last_used = state.model_last_used.get(f.replace(".onnx", "").replace(".pb", ""), 0)
            model_files.append({
                "name": f,
                "size_mb": round(size / 1024 / 1024, 1),
                "last_used": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(last_used)) if last_used > 0 else "never"
            })

    model_files.sort(key=lambda x: x["size_mb"], reverse=True)
    return {
        "models_dir": models_dir,
        "total_size_mb": round(total_size / 1024 / 1024, 1),
        "model_count": len(model_files),
        "models": model_files
    }


@app.post("/models/cleanup")
async def models_cleanup(max_age_days: int = 30, dry_run: bool = True, request: Request = None):
    """Clean up models not used within max_age_days. Use dry_run=false to actually delete.
    Requires X-Api-Token header matching API_TOKEN env var for destructive operations."""
    if max_age_days < 0 or max_age_days > 36500:
        raise HTTPException(status_code=400, detail="max_age_days must be 0-36500")
    # Require API token for non-dry-run (destructive) operations
    if not dry_run:
        expected_token = os.getenv("API_TOKEN", "")
        provided_token = request.headers.get("x-api-token", "") if request else ""
        if not expected_token or not hmac.compare_digest(provided_token, expected_token):
            return JSONResponse(status_code=403, content={"error": "API token required for destructive cleanup operations"})
    models_dir = str(MODELS_DIR)
    if not os.path.isdir(models_dir):
        return {"cleaned": 0, "message": "Models directory not found"}

    cutoff = time.time() - (max_age_days * 86400)
    to_delete = []
    kept = []
    actually_freed_mb = 0.0

    with _model_lock:
        current = state.current_model

    for f in os.listdir(models_dir):
        fpath = os.path.join(models_dir, f)
        if not os.path.isfile(fpath):
            continue

        model_key = f.replace(".onnx", "").replace(".pb", "")
        last_used = state.model_last_used.get(model_key, 0)

        # Don't delete the currently loaded model (read under lock above)
        if current and model_key == current:
            kept.append({"name": f, "reason": "currently loaded"})
            continue

        if last_used < cutoff:
            try:
                size_mb = round(os.path.getsize(fpath) / 1024 / 1024, 1)
            except OSError:
                continue
            to_delete.append({"name": f, "size_mb": size_mb, "last_used_days_ago": int((time.time() - last_used) / 86400) if last_used > 0 else -1})
            if not dry_run:
                try:
                    os.remove(fpath)
                    actually_freed_mb += size_mb
                    logger.info(f"Deleted unused model: {f} ({size_mb}MB)")
                except Exception as e:
                    logger.warning(f"Failed to delete {f}: {e}")
        else:
            kept.append({"name": f, "reason": "recently used"})

    return {
        "dry_run": dry_run,
        "to_delete": to_delete,
        "kept": kept,
        "freed_mb": round(actually_freed_mb, 1) if not dry_run else sum(m["size_mb"] for m in to_delete),
        "deleted_count": len(to_delete) if not dry_run else 0
    }


# service_start_time is set in lifespan() — no deprecated on_event("startup") needed
