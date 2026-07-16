"""
AI Upscaler Service - FastAPI Application
Jellyfin AI Upscaler Plugin - Microservice Component v1.5.5.8
Supports OpenCV DNN (.pb) and ONNX Runtime models with GPU detection
Multi-GPU selection, robust TensorRT/CUDA/OpenVINO fallback
"""

import os
import re
import time
import json
import logging
import asyncio
import collections
import platform
import shutil
import subprocess
import tempfile
import threading
import hashlib
import hmac
import socket
import urllib.parse
import uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Optional, Any
from contextlib import asynccontextmanager

import numpy as np
import cv2
import httpx
from fastapi import FastAPI, File, UploadFile, Form, HTTPException, Request, Body
from fastapi.responses import Response, HTMLResponse, JSONResponse, StreamingResponse
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

# Live log streaming — in-memory ring buffer + asyncio event bus for SSE
LOG_BUFFER: "collections.deque[dict]" = collections.deque(maxlen=1000)
_LOG_SEQ = 0
_LOG_LOCK = threading.Lock()
_LOG_SUBSCRIBERS: "set[asyncio.Queue[dict]]" = set()
_LOG_LOOP: "Optional[asyncio.AbstractEventLoop]" = None

class _BufferHandler(logging.Handler):
    """Captures every log record into LOG_BUFFER and fans out to SSE subscribers."""
    def emit(self, record: logging.LogRecord) -> None:
        global _LOG_SEQ
        try:
            msg = self.format(record)
        except Exception:
            msg = record.getMessage()
        with _LOG_LOCK:
            _LOG_SEQ += 1
            entry = {
                "seq": _LOG_SEQ,
                "ts": record.created,
                "level": record.levelname,
                "logger": record.name,
                "msg": msg,
            }
            LOG_BUFFER.append(entry)
            subs = list(_LOG_SUBSCRIBERS)
        if _LOG_LOOP is not None and subs:
            for q in subs:
                try:
                    _LOG_LOOP.call_soon_threadsafe(q.put_nowait, entry)
                except Exception:
                    pass

_buffer_handler = _BufferHandler()
_buffer_handler.setFormatter(logging.Formatter("%(message)s"))
_buffer_handler.setLevel(logging.DEBUG)
logging.getLogger().addHandler(_buffer_handler)

def _attach_buffer_to_uvicorn():
    """Uvicorn configures its own loggers with propagate=False AFTER import,
    so we call this from lifespan() (post-uvicorn-config) to capture access logs."""
    for _name in ("uvicorn", "uvicorn.error", "uvicorn.access", "fastapi"):
        _lg = logging.getLogger(_name)
        if _buffer_handler not in _lg.handlers:
            _lg.addHandler(_buffer_handler)
        if _lg.level == logging.NOTSET:
            _lg.setLevel(logging.INFO)

# Paths — env-var overrides allow running outside Docker (e.g. CI unit tests)
MODELS_DIR = Path(os.getenv("MODELS_DIR", "/app/models"))
CACHE_DIR = Path(os.getenv("CACHE_DIR", "/app/cache"))
STATIC_DIR = Path(os.getenv("STATIC_DIR", "/app/static"))
# CONFIG_DIR — PERSISTENT auth/config state (managed API tokens). Deliberately
# separate from the wipe-able CACHE_DIR and from MODELS_DIR; mount as its own volume.
CONFIG_DIR = Path(os.getenv("CONFIG_DIR", "/app/config"))

from . import token_store  # hashed, persistent multi-token store (lazy expiry)

# Version — single source of truth is the APP_VERSION build arg the
# Dockerfiles pass; fall back to a literal only for bare local runs.
# (FIX-1: the hardcoded literal had drifted to 1.6.1.21 while the image
# entrypoint banner correctly reported 1.7.7 — issue #69 screenshots.)
VERSION = os.getenv("APP_VERSION", "1.8.2")

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

        # Interpolation (RIFE) model
        self.rife_session = None
        self.rife_model_name: Optional[str] = None
        self.rife_loaded: bool = False

        # Face-restore model (v1.6.1.7: GFPGAN / CodeFormer)
        self.face_restore_session = None
        self.face_restore_model_name: Optional[str] = None
        self.face_restore_loaded: bool = False
        self.face_restore_input_size: int = 512   # Models standardised on 512x512 face crops
        self.face_detector = None                  # Lazy-loaded OpenCV Haar cascade

        # FP16 mixed precision inference
        self.use_fp16: bool = False

        # Model management
        self.model_last_used: dict = {}  # model_name -> timestamp

state = AppState()


class RealtimeStats:
    """Track real-time upscaling performance.

    All accumulators use bounded rolling windows to prevent unbounded memory
    growth in long-running deployments.  frames_processed / total_time are
    capped at _ACCUM_WINDOW_SIZE frames — avg_ms reflects the most recent
    window rather than a lifetime average, which is more useful in practice.
    """

    _FPS_WINDOW_SIZE = 60    # Rolling window for FPS calculation
    _ACCUM_WINDOW_SIZE = 500  # Max frames kept for avg_ms — prevents OOM

    def __init__(self):
        self.current_fps: float = 0.0
        self.dropped_frames: int = 0
        self._lock = threading.Lock()
        self._timestamps: collections.deque = collections.deque(maxlen=RealtimeStats._FPS_WINDOW_SIZE)
        # Bounded deque for avg latency — oldest entries dropped automatically
        self._durations: collections.deque = collections.deque(maxlen=RealtimeStats._ACCUM_WINDOW_SIZE)

    @property
    def frames_processed(self) -> int:
        return len(self._durations)

    @property
    def total_time(self) -> float:
        return sum(self._durations)

    def record_frame(self, duration: float) -> None:
        """Record a processed frame and update rolling FPS via sliding window."""
        with self._lock:
            self._durations.append(duration)

            now = time.time()
            self._timestamps.append(now)

            # Compute FPS from the sliding window
            if len(self._timestamps) >= 2:
                window_elapsed = self._timestamps[-1] - self._timestamps[0]
                if window_elapsed > 0:
                    self.current_fps = (len(self._timestamps) - 1) / window_elapsed

    def record_drop(self) -> None:
        """Record a dropped frame."""
        with self._lock:
            self.dropped_frames += 1

    def snapshot(self) -> dict:
        """Return a copy of current stats."""
        with self._lock:
            durations = list(self._durations)
            total = sum(durations)
            count = len(durations)
            avg_ms = (total / count * 1000) if count > 0 else 0.0
            return {
                "frames_processed": count,
                "total_time_s": round(total, 3),
                "current_fps": round(self.current_fps, 2),
                "avg_frame_ms": round(avg_ms, 2),
                "dropped_frames": self.dropped_frames,
            }

    def reset(self) -> None:
        """Reset all counters."""
        with self._lock:
            self.current_fps = 0.0
            self.dropped_frames = 0
            self._timestamps.clear()
            self._durations.clear()


_realtime_stats = RealtimeStats()


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

# Threading lock for plugin_connections list mutations
_connections_lock = threading.Lock()

# Per-model download lock — prevents concurrent downloads of the same model
_download_locks: dict[str, asyncio.Lock] = {}
_download_locks_guard = threading.Lock()

# v1.8.2 — async download-job registry. The synchronous /models/download blocks the
# HTTP request until a (possibly multi-GB) download finishes, which trips client/proxy
# timeouts on big models. /models/download-async starts the download in the background
# and returns a job id the caller polls via /models/download-status/{id}.
_download_jobs: dict[str, dict] = {}
_download_jobs_guard = threading.Lock()

# AVAILABLE_MODELS mutations (upload, delete) are serialised by _model_lock.
# The /models list endpoint takes a snapshot under _model_lock to prevent
# "RuntimeError: dictionary changed size during iteration" if a custom model
# is uploaded or deleted concurrently.

# Benchmark lock — ensures only one benchmark runs at a time (created in lifespan)
_benchmark_lock: Optional[asyncio.Lock] = None

# /models list cache — avoids 40 Path.exists() filesystem checks on every call
_models_cache: Optional[dict] = None
_models_cache_expiry: float = 0.0
_MODELS_CACHE_TTL: float = 30.0  # seconds


def _invalidate_models_cache() -> None:
    """Expire the /models list cache immediately after any model mutation."""
    global _models_cache_expiry
    _models_cache_expiry = 0.0

# Bounded thread pool for CPU-bound upscaling work.
# Using None in run_in_executor relies on the default pool which Python sizes to
# min(32, cpu_count+4) — fine but uncontrolled. An explicit executor lets us cap
# workers at MAX_CPU_WORKERS (default: cpu_count) so we don't over-subscribe the
# CPU when multiple concurrent requests arrive simultaneously.
_cpu_executor = ThreadPoolExecutor(
    max_workers=int(os.getenv("MAX_CPU_WORKERS", str(os.cpu_count() or 4))),
    thread_name_prefix="upscaler"
)


def _require_api_token(request: Request) -> None:
    """Authenticate a request via the X-Api-Token header.

    Two credential sources, both timing-safe:
      1. the env API_TOKEN  (bootstrap / legacy single token)
      2. any active, non-expired managed token  (token_store)
    API_TOKEN=disable opts out entirely. Secure-by-default: if neither an env
    token nor any managed token exists, requests are rejected.
    Raises HTTPException(403) on mismatch or missing token."""
    expected_token = os.getenv("API_TOKEN", "")
    if expected_token == "disable":
        return  # Explicitly opted out of auth
    provided_token = request.headers.get("x-api-token", "") if request else ""

    # 1) bootstrap env token
    if expected_token and provided_token and hmac.compare_digest(provided_token, expected_token):
        return
    # 2) managed tokens (hashed, persistent, lazy-expiry)
    if provided_token and token_store.verify(provided_token):
        return

    if not expected_token and not token_store.has_any():
        logger.warning("No API auth configured (no API_TOKEN env var and no managed tokens) — rejecting request. "
                       "Create a token in the plugin, set API_TOKEN, or set API_TOKEN=disable for a trusted LAN.")
        raise HTTPException(status_code=403, detail="API_TOKEN not configured. Set API_TOKEN env var or create a managed token to secure this service.")
    raise HTTPException(status_code=403, detail="Invalid or missing API token")


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
MAX_IMAGE_PIXELS = 16000 * 16000  # ~256 MP — prevent OOM from decompression bombs
MAX_INPUT_FRAMES = 10  # Safety cap for multi-frame endpoints

# FP16 mixed precision for ONNX inference.
# "auto" = enable on CUDA/TensorRT GPUs with compute capability >= 7.0 (Volta+)
# "true" = force FP16, "false" = force FP32
USE_FP16 = os.getenv("USE_FP16", "auto").lower().strip()

# Scene-change detection threshold for multi-frame VSR models.
# When a scene change is detected within the frame window, the service
# falls back to single-frame upscaling to avoid ghosting artifacts.
# Range 0.0 (never detect) to 1.0 (always detect). Default 0.35.
try:
    SCENE_CHANGE_THRESHOLD = max(0.0, min(1.0, float(os.getenv("SCENE_CHANGE_THRESHOLD", "0.35"))))
except (ValueError, TypeError):
    SCENE_CHANGE_THRESHOLD = 0.35

# ── Feature toggles (all configurable via env vars) ──────────────────────

# Quality metrics: compute PSNR/SSIM after upscaling
ENABLE_QUALITY_METRICS = os.getenv("ENABLE_QUALITY_METRICS", "true").lower() == "true"

# Face enhancement: GFPGAN/CodeFormer post-processing
ENABLE_FACE_ENHANCE = os.getenv("ENABLE_FACE_ENHANCE", "true").lower() == "true"
try:
    FACE_ENHANCE_STRENGTH = max(0.0, min(1.0, float(os.getenv("FACE_ENHANCE_STRENGTH", "0.7"))))
except (ValueError, TypeError):
    FACE_ENHANCE_STRENGTH = 0.7

# Film grain management: denoise before upscaling, optional re-grain after
ENABLE_GRAIN_MANAGEMENT = os.getenv("ENABLE_GRAIN_MANAGEMENT", "true").lower() == "true"
GRAIN_DENOISE_STRENGTH = max(1, min(30, _safe_int_env("GRAIN_DENOISE_STRENGTH", 5, 1, 30)))
try:
    GRAIN_READD_INTENSITY = max(0.0, min(50.0, float(os.getenv("GRAIN_READD_INTENSITY", "0.0"))))
except (ValueError, TypeError):
    GRAIN_READD_INTENSITY = 0.0

# Custom model upload
ENABLE_MODEL_UPLOAD = os.getenv("ENABLE_MODEL_UPLOAD", "true").lower() == "true"
MAX_MODEL_UPLOAD_BYTES = _safe_int_env("MAX_MODEL_UPLOAD_BYTES", 500 * 1024 * 1024, 1024 * 1024, 2 * 1024 * 1024 * 1024)

# OpenAPI / Swagger docs visibility
ENABLE_API_DOCS = os.getenv("ENABLE_API_DOCS", "true").lower() == "true"

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
        "url": "https://huggingface.co/huggingworld/onnx-image-models/resolve/main/4x-ClearRealityV1-fp32-opset17.onnx",
        "scale": 4,
        "description": "SPAN architecture, only 1.7MB. Real-time 4x for clean video. Best for faces, nature, hair. Measured 32x faster than realesrgan-x4 on CPU (4ms vs 129ms per 64px tile).",
        "type": "onnx",
        "category": "video-fast",
        "model_type": "span",
        "license": "Apache-2.0",
        "attribution": "Kim2091 (huggingface.co/Kim2091/ClearRealityV1)",
        "sha256": "bbce9d5a653281cfae07788d620bd4ec45712709bdf6349a06a893159efd97ce",
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
        "name": "UltraSharp V2 x4 (Best Photo/Video) [non-commercial license]",
        "url": "https://huggingface.co/Kim2091/UltraSharpV2/resolve/main/4x-UltraSharpV2_fp32_op17.onnx",
        "scale": 4,
        "description": "DAT2 Transformer — best overall quality for photos and video. 49MB. License CC-BY-NC-SA-4.0: personal/home use only, no commercial deployments.",
        "type": "onnx",
        "category": "video-quality",
        "model_type": "dat2",
        "license": "CC-BY-NC-SA-4.0",
        "attribution": "Kim2091 (huggingface.co/Kim2091/UltraSharpV2)",
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
        # HAT transformer uses ops (LayerNorm with dynamic shape) that fail on CPUExecutionProvider
        # in this ONNX Runtime build. Re-enable once CUDA/ROCm EP is available or model is re-exported.
        "available": False
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
        "name": "APISR x3 (General Quality) [self-host required]",
        "url": "https://huggingface.co/Xenova/3x_APISR_RRDB_GAN_generator-onnx/resolve/main/onnx/model.onnx",
        "scale": 3,
        "description": "CVPR 2024 — general 3x for photos & video. Ideal for 720p to 1080p. ~25MB. [Upstream Xenova repo gated — see docs/MODEL-HOSTING.md to self-host.]",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "rrdb",
        # Xenova repo returns 401 anonymously — gated or removed upstream. See docs/MODEL-HOSTING.md for self-hosting instructions.
        "available": False
    },

    # ============================================================
    # === NEW v1.8.3.4 — License-checked additions (sha256-pinned) ===
    # Each entry was download-verified, ONNX-sanity-checked (dynamic H/W,
    # correct output scale) and CPU-benchmarked before adoption.
    # NC-licensed candidates (UltraSharp V2 stays flagged, Adore 2x) were
    # rejected or flagged; see docs/MODEL-EVAL-2026-07.md.
    # ============================================================
    "purephoto-realplksr-x4": {
        "name": "PurePhoto RealPLKSR x4 (Photo/Portrait)",
        "url": "https://huggingface.co/huggingworld/onnx-image-models/resolve/main/4xPurePhoto-RealPLSKR.onnx",
        "scale": 4,
        "description": "RealPLKSR tuned for realistic photos and portraits. 30MB, 67ms/64px-tile CPU. ('RealPLSKR' in the URL is the author's original file name.)",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "realplksr",
        "license": "CC-BY-SA-4.0",
        "attribution": "asterixcool (openmodeldb.info/models/4x-PurePhoto-RealPLSKR)",
        "sha256": "c03da555c9c7ff58425fb9e6f812b7c23c40e06efc31c13145fab78e44b8aad8",
        "available": True
    },
    "nomos8kdat-x4": {
        "name": "Nomos8k DAT x4 (JPEG Restoration)",
        "url": "https://huggingface.co/huggingworld/onnx-image-models/resolve/main/4xNomos8kDAT.onnx",
        "scale": 4,
        "description": "DAT trained on Nomos8k — restores heavily JPEG-compressed sources (old rips). 86MB, heavy on CPU (486ms/64px-tile), GPU recommended.",
        "type": "onnx",
        "category": "film-restore",
        "model_type": "dat",
        "license": "CC-BY-4.0",
        "attribution": "Philip Hofmann / Helaman (openmodeldb.info/models/4x-Nomos8kDAT)",
        "sha256": "e5c10de92a14544764ca4e4dc0269f7de3cd2e4975b1d149ad687fd195eb9de1",
        "available": True
    },
    "fallin-soft-x2": {
        "name": "Fallin Soft x2 (Anime Real-Time)",
        "url": "https://github.com/renarchi/Re-SISR/releases/download/Fallin/2x_Fallin_soft_renarchi_fp32.onnx",
        "scale": 2,
        "description": "Real-CUGAN-arch anime 2x by the Adore author, permissively licensed. 5.7MB, 17ms/64px-tile CPU — real-time 1080p anime.",
        "type": "onnx",
        "category": "anime",
        "model_type": "cugan",
        "license": "CC-BY-4.0",
        "attribution": "renarchi (openmodeldb.info/models/2x-Fallin-Soft)",
        "sha256": "487792c65406a6851a6bd48d5cbdcee75317fdc3e241f02fe7054936b9914d15",
        "available": True
    },

    # ============================================================
    # === VIDEO SR Models (Multi-Frame) ===
    # ============================================================
    "edvr-m-x4": {
        "name": "EDVR-M x4 (Video SR - 5 Frame) [self-host required]",
        "url": "https://huggingface.co/kuscheltier/jellyfin-vsr-models/resolve/main/edvr_m_x4.onnx",
        "scale": 4,
        "description": "EDVR-M — Multi-frame video super-resolution. Uses 5 frames for temporal consistency. Best batch quality. [Requires self-hosted ONNX export — see docs/MODEL-HOSTING.md]",
        "type": "onnx",
        "category": "video-sr",
        "model_type": "edvr",
        "input_frames": 5,
        "self_host": True,
        # Multi-frame VSR has no public ONNX mirror. User must export from official PyTorch weights — see docs/MODEL-HOSTING.md.
        "available": False
    },
    "realbasicvsr-x4": {
        "name": "RealBasicVSR x4 (Video SR - 5 Frame) [self-host required]",
        "url": "https://huggingface.co/kuscheltier/jellyfin-vsr-models/resolve/main/realbasicvsr_x4.onnx",
        "scale": 4,
        "description": "RealBasicVSR — Recurrent VSR with optical flow (CVPR 2022). Best for real-world degraded video (VHS, DVD, streaming). ~50MB. [Requires self-hosted ONNX — see docs/MODEL-HOSTING.md]",
        "type": "onnx",
        "category": "video-sr",
        "model_type": "realbasicvsr",
        "input_frames": 5,
        "self_host": True,
        # Multi-frame VSR has no public ONNX mirror. User must export from official PyTorch weights — see docs/MODEL-HOSTING.md.
        "available": False
    },
    "animesr-v2-x4": {
        "name": "AnimeSR v2 x4 (Anime Video SR - 5 Frame) [self-host required]",
        "url": "https://huggingface.co/kuscheltier/jellyfin-vsr-models/resolve/main/animesr_v2_x4.onnx",
        "scale": 4,
        "description": "AnimeSR v2 — Anime-specialized multi-frame VSR (NeurIPS 2022). Preserves line art and flat colors. ~30MB. [Requires self-hosted ONNX — see docs/MODEL-HOSTING.md]",
        "type": "onnx",
        "category": "video-sr",
        "model_type": "animesr",
        "input_frames": 5,
        "self_host": True,
        # Multi-frame VSR has no public ONNX mirror. User must export from official PyTorch weights — see docs/MODEL-HOSTING.md.
        "available": False
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

    # ============================================================
    # === RIFE Models (Frame Interpolation) ===
    # URLs point to yuvraj108c/rife-onnx — community ONNX exports of Practical-RIFE.
    # All three variants verified live (HEAD 200) as of v1.6.1.12 release.
    # ============================================================
    "rife-v4.7": {
        "name": "RIFE v4.7 (Fast Frame Interpolation)",
        "url": "https://huggingface.co/yuvraj108c/rife-onnx/resolve/main/rife47_ensemble_True_scale_1_sim.onnx",
        "scale": 1,
        "description": "RIFE v4.7 — Faster Frame Interpolation (2x FPS). Ensemble enabled, scale=1. Lighter model for real-time use. 21MB.",
        "type": "onnx",
        "category": "interpolation",
        "model_type": "rife",
        "input_frames": 2,
        "available": True
    },
    "rife-v4.8": {
        "name": "RIFE v4.8 (Balanced Frame Interpolation)",
        "url": "https://huggingface.co/yuvraj108c/rife-onnx/resolve/main/rife48_ensemble_True_scale_1_sim.onnx",
        "scale": 1,
        "description": "RIFE v4.8 — Balanced Frame Interpolation (2x FPS). Middle ground between v4.7 (fast) and v4.9 (quality). 21MB.",
        "type": "onnx",
        "category": "interpolation",
        "model_type": "rife",
        "input_frames": 2,
        "available": True
    },
    "rife-v4.9": {
        "name": "RIFE v4.9 (Quality Frame Interpolation)",
        "url": "https://huggingface.co/yuvraj108c/rife-onnx/resolve/main/rife49_ensemble_True_scale_1_sim.onnx",
        "scale": 1,
        "description": "RIFE v4.9 — Best-quality Real-Time Frame Interpolation (2x FPS). Ensemble enabled, scale=1. Recommended for film/anime. 21MB.",
        "type": "onnx",
        "category": "interpolation",
        "model_type": "rife",
        "arch": "rife",
        "input_frames": 2,
        "available": True
    },

    # ============================================================
    # === Frame-Interpolation — second architectures (v1.8.2) ===
    # RIFE (above) is the only self-hosted interpolation arch. IFRNet and CAIN
    # are wired into the (architecture-adaptive) interpolation engine but are
    # experimental + available:False: there is no checksum-verified public ONNX
    # export to auto-download, so they are "bring-your-own / self-host" until we
    # host one. The engine runs them correctly once the ONNX is in the models dir
    # (IFRNet = 3-input incl. timestep; CAIN = 2-input, fixed midpoint).
    # ============================================================
    "ifrnet": {
        "name": "IFRNet (Frame Interpolation — experimental)",
        "url": "https://github.com/ltkong218/IFRNet",
        "scale": 1,
        "description": "IFRNet intermediate-flow interpolation (2x FPS). Second interpolation architecture beside RIFE — arbitrary-timestep capable. Experimental: self-host an ONNX export in the models dir (no verified public export yet).",
        "type": "onnx",
        "category": "interpolation",
        "model_type": "ifrnet",
        "arch": "ifrnet",
        "input_frames": 2,
        "experimental": True,
        "self_host": True,
        "available": False
    },
    "cain": {
        "name": "CAIN (Frame Interpolation — experimental)",
        "url": "https://github.com/myungsub/CAIN",
        "scale": 1,
        "description": "CAIN channel-attention interpolation (2x FPS, fixed midpoint). Second interpolation architecture beside RIFE — 2-input, no timestep. Experimental: self-host an ONNX export in the models dir (no verified public export yet).",
        "type": "onnx",
        "category": "interpolation",
        "model_type": "cain",
        "arch": "cain",
        "input_frames": 2,
        "experimental": True,
        "self_host": True,
        "available": False
    },

    # ============================================================
    # === Face-Restore Models (v1.6.1.7) ===
    # ============================================================
    # Restore faces in low-quality / old video. Works independently of the
    # main upscaler — detect faces via OpenCV Haar cascade, run face model on
    # each 512x512 crop, paste back with feathered-edge alpha blending.
    "gfpgan-v1.4": {
        "name": "GFPGAN v1.4 (Face Restore)",
        "url": "https://huggingface.co/facefusion/models-3.0.0/resolve/main/gfpgan_1.4.onnx",
        "scale": 1,
        "description": "GFPGAN v1.4 — Tencent ARC's face restoration GAN. Restores heavily degraded faces. 512x512 crops. Apache 2.0. Mirrored via facefusion/models-3.0.0. ~340MB.",
        "type": "onnx",
        "category": "face_restore",
        "model_type": "face_restore",
        "input_size": 512,
        "available": True
    },
    "codeformer": {
        "name": "CodeFormer (Face Restore)",
        "url": "https://huggingface.co/facefusion/models-3.0.0/resolve/main/codeformer.onnx",
        "scale": 1,
        "description": "CodeFormer - Robust face restoration with transformer codebook. Good for severely degraded faces. 512x512. S-Lab License. Mirrored via facefusion/models-3.0.0. ~377MB.",
        "type": "onnx",
        "category": "face_restore",
        "model_type": "face_restore",
        "input_size": 512,
        "available": True
    },

    # ============================================================
    # v1.6.1.17 - New SOTA models (2025/2026 releases)
    # ============================================================

    # Real-CUGAN-Pro (Bilibili AI Lab) - Cascaded U-Net.
    # Cleaner anime line-art than Real-ESRGAN-AnimeVideo, sharper than waifu2x.
    # ONNX mirror: mayhug/Real-CUGAN. Closes the long-standing gap users complained about.
    "real-cugan-x2": {
        "name": "Real-CUGAN x2 (Anime Quality) [self-host required]",
        "url": "https://github.com/styler00dollar/VSGAN-tensorrt-docker/releases/download/models/cugan_up2x-latest-conservative_op18_clamp.onnx",
        "scale": 2,
        "description": "Real-CUGAN x2 - Bilibili AI Lab. Original HF mirror is gone; the only public ONNX exports (styler00dollar op18) declare opset 18 but keep the pre-18 ReduceMean 'axes' attribute, so onnxruntime rejects them (INVALID_GRAPH; they target TensorRT). Use fallin-soft-x2 (same cugan architecture, verified) or self-host: docs/MODEL-HOSTING.md.",
        "type": "onnx",
        "category": "anime",
        "model_type": "cugan",
        "license": "MIT",
        "attribution": "bilibili/ailab Real-CUGAN",
        # 2026-07: mayhug HF repo gone; styler00dollar op18 exports fail ORT load (verified locally: InvalidGraph ReduceMean axes).
        "available": False
    },
    "real-cugan-x4": {
        "name": "Real-CUGAN x4 (Anime Quality) [self-host required]",
        "url": "https://github.com/styler00dollar/VSGAN-tensorrt-docker/releases/download/models/cugan_up4x-latest-conservative_op18_clamp.onnx",
        "scale": 4,
        "description": "Real-CUGAN x4 - Bilibili AI Lab. Original HF mirror is gone; the only public ONNX exports (styler00dollar op18) fail onnxruntime load (INVALID_GRAPH, TensorRT-oriented export). Use fallin-soft-x2 (cugan arch) or realesrgan-animevideo-x4, or self-host: docs/MODEL-HOSTING.md.",
        "type": "onnx",
        "category": "anime",
        "model_type": "cugan",
        "license": "MIT",
        "attribution": "bilibili/ailab Real-CUGAN",
        # 2026-07: mayhug HF repo gone; styler00dollar op18 exports fail ORT load (verified locally: InvalidGraph ReduceMean axes).
        "available": False
    },

    # DRCT-L (Phips/aaronespasa) - Dense-Residual Connected Transformer.
    # Sharper than DAT2/UltraSharp on real-world photo content, same VRAM class.
    # Trained on 4xRealWebPhoto_v4 dataset - robust against streaming JPG/WebP artefacts.
    "drct-l-x4": {
        "name": "DRCT-L x4 (SOTA Photo) [self-host required]",
        "url": "https://github.com/Phhofm/models/releases/download/4xRealWebPhoto_v4_drct-l/4xRealWebPhoto_v4_drct-l_fp32.onnx",
        "scale": 4,
        "description": "DRCT-L x4 - Dense-Residual Connected Transformer. The only verified public ONNX (Phhofm RealWebPhoto v4 DRCT-L) is a FIXED 1x3x64x64-input export, which this pipeline's dynamic tiler cannot feed (edge tiles are smaller than 64px). Needs a dynamic-axes re-export - see docs/MODEL-HOSTING.md.",
        "type": "onnx",
        "category": "video-quality",
        "model_type": "drct",
        "license": "CC-BY-4.0",
        "attribution": "Philip Hofmann / Helaman (4xRealWebPhoto_v4_drct-l)",
        # 2026-07: original URL had no ONNX; Phhofm export verified to LOAD but has a fixed 64px input shape — incompatible with dynamic tiling. Candidate alternative (unverified): huggingworld 4xNomos2_hq_drct-l.onnx (184MB).
        "available": False
    },

    # BHI-RealPLKSR (Phhofm) - RealPLKSR architecture trained on BHI dataset.
    # ~2x throughput vs DAT2 at comparable quality. Sweet spot for mid-tier GPU
    # library batch processing. Fast enough for near-realtime on RTX 3060+.
    "bhi-realplksr-x4": {
        "name": "BHI-RealPLKSR x4 (Speed Champion) [self-host required]",
        "url": "https://github.com/Phhofm/models/releases/download/4xbhi_realplksr/4xBHI_realplksr.onnx",
        "scale": 4,
        "description": "BHI-RealPLKSR x4 - 2x faster than DAT2 at comparable quality. Release asset renamed upstream; no stable ONNX URL. See docs/MODEL-HOSTING.md. Alternative: purephoto-realplksr-x4 (same architecture).",
        "type": "onnx",
        "category": "video-fast",
        "model_type": "realplksr",
        # 2026-07 URL sweep: GitHub release asset 404 (assets renamed upstream).
        "available": False
    },

    # RIFE 4.25 (hzwer/Practical-RIFE) - current SOTA real-life frame interpolation.
    # Replaces v4.7-4.9 as Auto-Mode default. Better scene-cut detection (less ghosting).
    "rife-v4.25": {
        "name": "RIFE v4.25 (Frame Interpolation - Latest)",
        "url": "https://github.com/NevermindNilas/TAS-Models-Host/releases/download/main/rife425_fp32_op21_slim.onnx",
        "scale": 1,
        "description": "RIFE v4.25 - current SOTA frame interpolation, better scene-bleeding handling than v4.7-4.9. Recommended new default for 24-60fps. ~22MB (fp32 op21 slim export; needs onnxruntime >= 1.20).",
        "type": "onnx",
        "category": "interpolation",
        "model_type": "rife",
        "license": "MIT",
        "attribution": "hzwer/Practical-RIFE; TAS-Models-Host ONNX export",
        "sha256": "7fa9a1aee51299fa6b3b92da4fe0c6c3dc74a9cdb3cf956e2702d401fe5ca87d",
        # 2026-07: yuvraj108c repo only hosts rife 4.7-4.9 — repointed to the TAS op21-slim export (ORT load verified; 1-input 7-channel signature).
        "available": True
    },

    # ============================================================
    # === v1.7.1 Wishlist Models (first catalog additions in 9 releases) ===
    # ============================================================

    # OmniSR (CVPR 2023) - omni-axis self-attention for compact-but-strong SR.
    "omnisr-x2": {
        "name": "OmniSR x2 (Compact SOTA)",
        "url": "https://github.com/Phhofm/models/releases/download/2xHFA2kOmniSR/2xHFA2kOmniSR_fp32_opset17.onnx",
        "scale": 2,
        "description": "OmniSR x2 - CVPR 2023, omni-axis self-attention. HFA2k training set (anime-leaning), ~4.6MB. Faster than SwinIR at comparable PSNR; good for 720p->1440p.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "omnisr",
        "license": "CC-BY-4.0",
        "attribution": "Philip Hofmann / Helaman (2xHFA2kOmniSR)",
        "sha256": "54a53c3af07620222eeda969468415d76c19a4b1b9f209e8803f5575e5b87bbe",
        # 2026-07: Phhofm/models-omnisr HF repo gone — repointed to the author's own GitHub release export (ORT load+infer verified).
        "available": True
    },
    "omnisr-x4": {
        "name": "OmniSR x4 (Compact SOTA)",
        "url": "https://huggingface.co/huggingworld/onnx-image-models/resolve/main/epoch895_OmniSR.onnx",
        "scale": 4,
        "description": "OmniSR x4 - CVPR 2023 official weights (epoch895 export). ~5.6MB. Sweet spot between SwinIR and FSRCNN for 480p->1920p.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "omnisr",
        "license": "Apache-2.0",
        "attribution": "Francis0625/Omni-SR official weights; huggingworld ONNX export",
        "sha256": "c82256a5b8ac7543ed5ddbbc4ff531233d0b42ef159ea6c31060c5ed5924fd1e",
        # 2026-07: Phhofm/models-omnisr HF repo gone — repointed to the official-weights export.
        "available": True
    },

    # DAT-light (2023) - smaller and faster than DAT2 at slightly lower PSNR.
    "dat-light-x2": {
        "name": "DAT-light x2 (Production Transformer) [self-host required]",
        "url": "https://huggingface.co/zhengchen1999/DAT/resolve/main/dat-light-x2.onnx",
        "scale": 2,
        "description": "DAT-light x2 - smaller/faster sibling of DAT2. Upstream HF repo removed; no ONNX mirror found. See docs/MODEL-HOSTING.md.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "dat",
        # 2026-07 URL sweep: HF repo gone (401), no ONNX mirror found.
        "available": False
    },
    "dat-light-x4": {
        "name": "DAT-light x4 (Production Transformer) [self-host required]",
        "url": "https://huggingface.co/zhengchen1999/DAT/resolve/main/dat-light-x4.onnx",
        "scale": 4,
        "description": "DAT-light x4 - smaller/faster sibling of DAT2. Upstream HF repo removed; no ONNX mirror found. See docs/MODEL-HOSTING.md. Alternative: nomos8kdat-x4 (DAT) or nomos2-dat2-x4.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "dat",
        # 2026-07 URL sweep: HF repo gone (401), no ONNX mirror found.
        "available": False
    },

    # RestoreFormer++ (MM 2023) - face restore for severely degraded faces.
    "restoreformer-plus-plus": {
        "name": "RestoreFormer++ (Face Restoration)",
        "url": "https://huggingface.co/facefusion/models-3.0.0/resolve/main/restoreformer_plus_plus.onnx",
        "scale": 1,
        "description": "RestoreFormer++ - state-of-the-art face restoration, better than GFPGAN/CodeFormer for severely degraded faces. ~294MB (fp32).",
        "type": "onnx",
        "category": "face_restore",
        "model_type": "face_restore",
        "license": "Apache-2.0",
        "attribution": "wzhouxiff/RestoreFormerPlusPlus; FaceFusion models mirror",
        "sha256": "1aba559333b60fce0270e3436699ebf56bbc602e8fefe9502f027b1b5fe4eead",
        # 2026-07: upstream HF repo gone — repointed to the FaceFusion assets mirror. Size corrected (~294MB, not 50MB).
        "available": True
    },

    # ============================================================
    # === v1.7.2 Wishlist Models (Round 2: MAN, CRAFT, GPEN, NAFNet) ===
    # ============================================================

    "man-x2": {
        "name": "MAN x2 (Multi-scale Attention) [self-host required]",
        "url": "https://huggingface.co/icandle/MAN/resolve/main/man-x2.onnx",
        "scale": 2,
        "description": "MAN x2 - Multi-scale Attention Network, ICME 2023. Upstream HF repo removed; no ONNX mirror found. See docs/MODEL-HOSTING.md.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "man",
        # 2026-07 URL sweep: HF repo gone (401), no ONNX mirror found.
        "available": False
    },
    "man-x4": {
        "name": "MAN x4 (Multi-scale Attention) [self-host required]",
        "url": "https://huggingface.co/icandle/MAN/resolve/main/man-x4.onnx",
        "scale": 4,
        "description": "MAN x4 - Multi-scale Attention Network, ICME 2023. Upstream HF repo removed; no ONNX mirror found. See docs/MODEL-HOSTING.md.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "man",
        # 2026-07 URL sweep: HF repo gone (401), no ONNX mirror found.
        "available": False
    },
    "craft-x2": {
        "name": "CRAFT x2 (Compositional Refinement) [self-host required]",
        "url": "https://huggingface.co/AVC2-UESTC/CRAFT-SR/resolve/main/craft-x2.onnx",
        "scale": 2,
        "description": "CRAFT x2 - Compositional Refinement texture-aware SR, 2023. Upstream ships only .pth via Google Drive; no public ONNX export exists. See docs/MODEL-HOSTING.md.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "craft",
        # 2026-07 URL sweep: HF repo gone (401), no ONNX mirror found anywhere.
        "available": False
    },
    "craft-x4": {
        "name": "CRAFT x4 (Compositional Refinement) [self-host required]",
        "url": "https://huggingface.co/AVC2-UESTC/CRAFT-SR/resolve/main/craft-x4.onnx",
        "scale": 4,
        "description": "CRAFT x4 - Compositional Refinement, 2023. Upstream ships only .pth via Google Drive; no public ONNX export exists. See docs/MODEL-HOSTING.md.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "craft",
        # 2026-07 URL sweep: HF repo gone (401), no ONNX mirror found anywhere.
        "available": False
    },
    "gpen-512": {
        "name": "GPEN-512 (Face Restoration Alternative)",
        "url": "https://huggingface.co/facefusion/models-3.0.0/resolve/main/gpen_bfr_512.onnx",
        "scale": 1,
        "description": "GPEN-512 - face restoration with GAN prior. Different visual style than GFPGAN (more conservative). ~284MB.",
        "type": "onnx",
        "category": "face_restore",
        "model_type": "face_restore",
        "license": "Apache-2.0",
        "attribution": "Alibaba DAMO GPEN; FaceFusion models mirror",
        "sha256": "c6dd20daa7dd4313b83cb5bfb2a50a534b2f217afcd383b77862859d829d7f1a",
        # 2026-07: yangxy/GPEN HF repo gone — repointed to the FaceFusion assets mirror (same weights family as gfpgan/codeformer entries).
        "available": True
    },
    "nafnet-denoise": {
        "name": "NAFNet SIDD (Denoising / Restoration Pre-Pass)",
        "url": "https://huggingface.co/deepghs/image_restoration/resolve/main/NAFNet-SIDD-width64.onnx",
        "scale": 1,
        "description": "NAFNet (SIDD width64) - ECCV 2022 image denoiser. Use as pre-pass before upscaling on noisy source material. Large export (~446MB) - prefer the hqdn3d/nlmeans denoise prefilter on weak hardware.",
        "type": "onnx",
        "category": "film-restore",
        "model_type": "restoration",
        "license": "MIT",
        "attribution": "megvii-research/NAFNet; deepghs ONNX export",
        "sha256": "a31bd8339fd8664c1e7253d8d762dc04a8f6c8d0a3a7f7a71d19922f1c282b67",
        # 2026-07: megvii HF repo gone — repointed to the deepghs image_restoration export (SIDD width64). Size corrected.
        "available": True
    },

    # ============================================================
    # === Catalog expansion (issue-review pass, 2026-06) ===
    # Sourced from notaneimu/onnx-image-models (same base URL the
    # loader already uses for clearreality/nomosuni/lsdir). All URLs
    # HEAD-verified. Focus: compressed/streaming sources + artifact
    # cleanup, the real-world Jellyfin use case (h264/h265 frames).
    # ============================================================

    # Real-ESRGAN general v3 — the modern tiny all-rounder. Dynamic
    # input shape, ~5MB. Key is "realesr-general" (NOT "realesrgan"),
    # so it is unaffected by the benchmark 64px heuristic.
    "realesr-general-x4v3": {
        "name": "Real-ESRGAN General x4 v3 (Versatile Default)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/realesr-general-x4v3.onnx",
        "scale": 4,
        "description": "Real-ESRGAN general-purpose v3 - tiny (~5MB), fast, dynamic-shape all-rounder. Best modern default for mixed live-action/anime streaming content.",
        "type": "onnx",
        "category": "video-fast",
        "model_type": "realesr-general",
        "available": True
    },
    "realesr-general-wdn-x4v3": {
        "name": "Real-ESRGAN General x4 v3 (Denoise)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/realesr-general-wdn-x4v3.onnx",
        "scale": 4,
        "description": "Real-ESRGAN general v3 with denoise weighting (wdn) - ideal for noisy/compressed sources. ~5MB.",
        "type": "onnx",
        "category": "film-restore",
        "model_type": "realesr-general",
        "available": True
    },

    # Compressed/web-source specialists — trained on degraded inputs,
    # the closest match to real streaming frames.
    "realwebphoto-v4-dat2-x4": {
        "name": "RealWebPhoto v4 DAT2 x4 (Compressed Sources)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/4xRealWebPhoto_v4_dat2_fp32_opset17.onnx",
        "scale": 4,
        "description": "DAT2 trained specifically on degraded web/compressed images - best match for h264/h265 streaming frames with blocking + ringing. ~49MB.",
        "type": "onnx",
        "category": "video-quality",
        "model_type": "dat2",
        "available": True
    },
    "nomoswebphoto-realplksr-x4": {
        "name": "NomosWebPhoto RealPLKSR x4 (Web/Stream Quality)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/4xNomosWebPhoto_RealPLKSR.onnx",
        "scale": 4,
        "description": "RealPLKSR trained on web photos - efficient quality restore for compressed sources. ~30MB.",
        "type": "onnx",
        "category": "video-quality",
        "model_type": "realplksr",
        "available": True
    },

    # 1x artifact-cleanup pre-passes (no upscale) — run before a 4x
    # model on heavily compressed streams.
    "dejpg-realplksr-1x": {
        "name": "DeJPEG RealPLKSR 1x (Artifact Cleanup Pre-Pass)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/1xDeJPG_realplksr_otf_fp32_fullyoptimized.onnx",
        "scale": 1,
        "description": "1x DeJPEG restoration - removes JPEG/block compression artifacts before upscaling. Pair with any 4x model on heavily compressed streams. ~30MB.",
        "type": "onnx",
        "category": "film-restore",
        "model_type": "realplksr",
        "available": True
    },
    "denoise-realplksr-1x": {
        "name": "DeNoise RealPLKSR 1x (Denoise Pre-Pass)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/1xDeNoise_realplksr_otf_fp32.onnx",
        "scale": 1,
        "description": "1x denoise restoration pre-pass - complements NAFNet with the faster RealPLKSR arch. ~30MB.",
        "type": "onnx",
        "category": "film-restore",
        "model_type": "realplksr",
        "available": True
    },

    # Community-favorite ESRGAN classics + new RGT-S architecture.
    "foolhardy-remacri-x4": {
        "name": "Remacri x4 (Community Favorite)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/4x_foolhardy_Remacri.onnx",
        "scale": 4,
        "description": "4x_foolhardy_Remacri - legendary general-purpose ESRGAN upscaler, sharp detail without over-smoothing. ~67MB.",
        "type": "onnx",
        "category": "video-quality",
        "model_type": "esrgan",
        "available": True
    },
    "nmkd-siax-x4": {
        "name": "NMKD Siax x4 (Detail Restore)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/4x_NMKD-Siax_200k.onnx",
        "scale": 4,
        "description": "4x_NMKD-Siax_200k - top-rated ESRGAN for detail/general content, 200k iterations. ~67MB.",
        "type": "onnx",
        "category": "video-quality",
        "model_type": "esrgan",
        "available": True
    },
    "nomos8k-hat-l-x4": {
        "name": "Nomos8k HAT-L x4 (Maximum Quality)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/4xNomos8kSCHAT-L.onnx",
        "scale": 4,
        "description": "Full HAT-L (vs HAT-S in catalog) - highest photo quality, very heavy (~162MB, high VRAM). Poster/backdrop refresh only, not real-time.",
        "type": "onnx",
        "category": "video-quality",
        "model_type": "hat",
        "available": True
    },
    "textures-rgt-s-x4": {
        "name": "Textures RGT-S x4 (New Transformer)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/4xTextures_GTAV_rgt-s_fp32_opset17.onnx",
        "scale": 4,
        "description": "RGT-S (Recursive Generalization Transformer, small) - new architecture not previously in catalog, strong on textures/fine detail. ~46MB.",
        "type": "onnx",
        "category": "nextgen",
        "model_type": "rgt",
        "available": True
    },

    # Low-VRAM / real-time fast lane (for the T600 4GB / Arc A380 6GB
    # users seen in issues #62/#69).
    "lsdir-compact-v2-x4": {
        "name": "LSDIR Compact v2 x4 (Fast Video)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/4xLSDIRCompactv2.onnx",
        "scale": 4,
        "description": "v2 upgrade of LSDIR Compact - tiny (~2.5MB), fast real-time 4x for low-power/low-VRAM devices.",
        "type": "onnx",
        "category": "video-fast",
        "model_type": "compact",
        "available": True
    },
    "spanx2-ch48": {
        "name": "SPAN x2 ch48 (Real-Time Fast)",
        "url": "https://huggingface.co/notaneimu/onnx-image-models/resolve/main/2x-spanx2-ch48.onnx",
        "scale": 2,
        "description": "SPAN 2x with 48 channels - very fast (~1.7MB), good for real-time on modest GPUs (T600/Arc A380).",
        "type": "onnx",
        "category": "video-fast",
        "model_type": "span",
        "available": True
    },

}


# Backward-compatibility aliases — map legacy model keys (from saved user configs
# in v1.6.1.11 and earlier) to their v1.6.1.12 replacements. Resolved in
# _resolve_model_key() below so both old key + new key work transparently.
MODEL_ALIASES: dict[str, str] = {
    "rife-v4.6": "rife-v4.9",          # v1.6.1.11 rife-v4.6 pointed at a 404 placeholder; v4.9 is the quality successor
    "rife-v4.6-lite": "rife-v4.7",     # v4.6-lite was the fast variant; v4.7 (smaller) is its replacement
}


def _resolve_model_key(model_name: str) -> str:
    """Translate a legacy model key to its current canonical name.

    Returns the aliased key when `model_name` is a known alias; otherwise
    returns the input unchanged.
    """
    return MODEL_ALIASES.get(model_name, model_name)


def _resolve_fp16_setting() -> bool:
    """Determine whether FP16 mixed precision should be enabled.

    Rules:
      - USE_FP16="true"  -> always on
      - USE_FP16="false" -> always off
      - USE_FP16="auto"  -> on for CUDA/TensorRT GPUs with compute capability >= 7.0
                            (Volta and newer), off for CPU / OpenVINO
    """
    if USE_FP16 == "true":
        logger.info("FP16 mixed precision forced ON via USE_FP16=true")
        return True
    if USE_FP16 == "false":
        logger.info("FP16 mixed precision forced OFF via USE_FP16=false")
        return False

    # Auto-detect: check for CUDA provider and compute capability >= 7.0
    if not ONNX_AVAILABLE:
        logger.info("FP16 auto: ONNX Runtime not available, disabling FP16")
        return False

    available = ort.get_available_providers()
    has_cuda = 'CUDAExecutionProvider' in available or 'TensorrtExecutionProvider' in available
    if not has_cuda:
        logger.info("FP16 auto: no CUDA/TensorRT provider, disabling FP16")
        return False

    # Check compute capability from gpu_list (populated by detect_hardware)
    for gpu in state.gpu_list:
        cc = gpu.get('compute_capability', '')
        if cc:
            try:
                major = int(cc.split('.')[0])
                if major >= 7:
                    logger.info(f"FP16 auto: GPU compute capability {cc} >= 7.0, enabling FP16")
                    return True
            except (ValueError, IndexError):
                pass

    # Fallback: try querying nvidia-smi directly
    try:
        result = subprocess.run(
            ["nvidia-smi", "--query-gpu=compute_cap", "--format=csv,noheader"],
            capture_output=True, text=True, timeout=10
        )
        if result.returncode == 0:
            for cc_line in result.stdout.strip().split('\n'):
                cc = cc_line.strip()
                if cc:
                    try:
                        major = int(cc.split('.')[0])
                        if major >= 7:
                            logger.info(f"FP16 auto: GPU compute capability {cc} >= 7.0, enabling FP16")
                            return True
                    except (ValueError, IndexError):
                        pass
    except Exception as e:
        logger.debug(f"FP16 detection check failed: {e}")

    logger.info("FP16 auto: could not confirm compute capability >= 7.0, disabling FP16")
    return False


def _session_input_is_fp16(session) -> bool:
    """Return True iff the ONNX session's primary input expects tensor(float16).

    Guards the FP16-cast paths in _onnx_infer_tile / _onnx_infer_multiframe_tile:
    state.use_fp16 alone is not sufficient — the loaded model must also have been
    exported with float16 inputs, otherwise session.run() raises INVALID_ARGUMENT
    (see issue #67).
    """
    try:
        return session.get_inputs()[0].type == 'tensor(float16)'
    except (IndexError, AttributeError):
        return False


def _parse_clinfo_intel_name(clinfo_output: str) -> Optional[str]:
    """Extract an Intel GPU name (e.g. 'Intel(R) Arc(TM) A380 Graphics') from
    `clinfo --list` output. Used by the WSL2 /dev/dxg detection branch
    (see issue #66 — Windows 11 + Docker Desktop + Intel Arc).
    """
    for line in clinfo_output.splitlines():
        line = line.strip()
        if "Intel" in line and ("Arc" in line or "Iris" in line or "Graphics" in line):
            # Strip leading "Device Name:" / "Platform Name:" prefixes if present
            if ":" in line:
                return line.split(":", 1)[-1].strip()
            return line
    return None


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

            # WSL2 / Docker Desktop branch: /dev/dxg present without /dev/dri/renderD* (issue #66)
            elif Path("/dev/dxg").exists():
                try:
                    wsl_clinfo = subprocess.run(
                        ["clinfo", "--list"],
                        capture_output=True, text=True, timeout=5
                    )
                    if wsl_clinfo.returncode == 0 and "Intel" in wsl_clinfo.stdout:
                        state.gpu_name = _parse_clinfo_intel_name(wsl_clinfo.stdout) or "Intel GPU (WSL2)"
                        state.gpu_memory = "Shared (DXG)"
                        state.gpu_list.append({
                            "index": 0,
                            "name": state.gpu_name,
                            "memory": "Shared (DXG)",
                            "type": "intel-wsl2",
                            "render_node": "/dev/dxg"
                        })
                        gpu_detected = True
                        logger.info(f"Detected Intel GPU via WSL2 /dev/dxg: {state.gpu_name}")
                    else:
                        logger.warning(
                            "Intel GPU: /dev/dxg present but clinfo shows no Intel platform. "
                            "WSL2 setup likely missing -v /usr/lib/wsl:/usr/lib/wsl:ro mount or "
                            "LD_LIBRARY_PATH=/usr/lib/wsl/lib env var. "
                            "See docker-compose.yml WSL2 section."
                        )
                except FileNotFoundError:
                    logger.warning(
                        "Intel GPU: /dev/dxg detected (WSL2) but clinfo not installed in container. "
                        "Cannot verify Intel platform. Falling back to OpenVINO/CPU."
                    )
                except Exception as wsl_err:
                    logger.debug(f"Intel WSL2 detection failed: {wsl_err}")

            # If no render nodes but OpenVINO is available, still mark as detected
            elif ONNX_AVAILABLE and 'OpenVINOExecutionProvider' in ort.get_available_providers():
                state.gpu_name = "Intel OpenVINO (CPU inference only)"
                state.gpu_memory = "Shared"
                gpu_detected = True
                logger.warning(
                    "Intel OpenVINO available but no /dev/dri render nodes found. "
                    "Models will run on CPU. To enable GPU acceleration:\n"
                    "  1. Pass --device=/dev/dri to Docker (Linux native)\n"
                    "  2. OR mount /dev/dxg + /usr/lib/wsl for WSL2/Docker Desktop (see docker-compose.yml)\n"
                    "  3. Ensure intel-compute-runtime is installed in the container\n"
                    "  4. Add user to 'render' and 'video' groups (Linux native)"
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


def _register_custom_models_from_disk(models_dir, available_models) -> int:
    """Re-register custom-uploaded models after a restart (v1.8.3.7).

    /models/upload persists the .onnx into MODELS_DIR plus a small
    <name>.custom.json sidecar with the validated metadata, but the
    AVAILABLE_MODELS entry itself only lived in memory — so every container
    restart silently dropped uploaded/imported models from the catalog while
    their files kept sitting in the volume. This scans the sidecars and
    restores the registry entries. The sidecar values were validated by the
    upload endpoint (ONNX InferenceSession + 4D shape) before being written,
    so they are trusted here — no model load at startup.

    Returns the number of restored models. Pure function over its arguments
    so tests can drive it with a tmp dir + plain dict.
    """
    restored = 0
    try:
        sidecars = sorted(models_dir.glob("*.custom.json"))
    except OSError:
        return 0
    for sidecar in sidecars:
        try:
            with open(sidecar, encoding="utf-8") as fh:
                meta = json.load(fh)
            model_name = meta.get("model_name", "")
            filename = meta.get("filename", f"{model_name}.onnx")
            if not re.fullmatch(r"[a-zA-Z0-9_-]{1,64}", model_name):
                logger.warning(f"Skipping custom-model sidecar with invalid name: {sidecar.name}")
                continue
            model_path = (models_dir / filename).resolve()
            if not str(model_path).startswith(str(models_dir.resolve())) or not model_path.is_file():
                logger.warning(f"Skipping custom-model sidecar without model file: {sidecar.name}")
                continue
            if model_name in available_models:
                continue  # built-in or already restored — never shadow
            available_models[model_name] = {
                "name": model_name,
                "description": meta.get("description") or f"Custom uploaded model ({meta.get('scale', 2)}x)",
                "url": "",
                "filename": filename,
                "type": "onnx",
                "scale": int(meta.get("scale", 2)),
                "category": "super-resolution",
                "input_channels": meta.get("input_channels"),
                "available": True,
                "custom": True,
            }
            restored += 1
        except Exception:
            logger.warning(f"Failed to restore custom model from {sidecar.name}", exc_info=True)
    return restored


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Application startup and shutdown."""
    logger.info(f"Starting AI Upscaler Service v{VERSION}...")
    
    # Create directories
    MODELS_DIR.mkdir(parents=True, exist_ok=True)
    CACHE_DIR.mkdir(parents=True, exist_ok=True)

    # Restore custom/imported models from their sidecar files (v1.8.3.7 —
    # previously they vanished from the catalog on every restart).
    restored = _register_custom_models_from_disk(MODELS_DIR, AVAILABLE_MODELS)
    if restored:
        logger.info(f"Restored {restored} custom model(s) from {MODELS_DIR}")
    
    # Detect hardware (run in executor to avoid blocking event loop with subprocess calls)
    loop = asyncio.get_running_loop()
    global _LOG_LOOP
    _LOG_LOOP = loop
    _attach_buffer_to_uvicorn()
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
    global _upscale_semaphore, _benchmark_lock
    _upscale_semaphore = asyncio.Semaphore(state.max_concurrent)
    _benchmark_lock = asyncio.Lock()

    # Track service uptime
    state.service_start_time = time.time()

    # Resolve FP16 mixed precision setting (must run after detect_hardware
    # so that gpu_list / compute_capability info is available)
    state.use_fp16 = _resolve_fp16_setting()

    logger.info(f"GPU Enabled: {state.use_gpu}")
    logger.info(f"FP16 Mixed Precision: {state.use_fp16}")
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
    _cpu_executor.shutdown(wait=False)


app = FastAPI(
    title="AI Upscaler Service",
    description="Neural network image upscaling service for Jellyfin",
    version=VERSION,
    lifespan=lifespan,
    docs_url="/docs" if ENABLE_API_DOCS else None,
    redoc_url="/redoc" if ENABLE_API_DOCS else None,
    openapi_url="/openapi.json" if ENABLE_API_DOCS else None,
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
        
        # Set GPU backend only when CUDA is actually present at runtime.
        # setPreferableBackend(CUDA) does NOT throw on non-CUDA machines;
        # the assertion error surfaces only at inference time. Check the
        # device count to guard against this.
        cuda_device_count = 0
        try:
            cuda_device_count = cv2.cuda.getCudaEnabledDeviceCount()
        except Exception:
            pass
        if state.use_gpu and cuda_device_count > 0:
            sr.setPreferableBackend(cv2.dnn.DNN_BACKEND_CUDA)
            sr.setPreferableTarget(cv2.dnn.DNN_TARGET_CUDA)
            logger.info("Using CUDA backend for OpenCV DNN")
        else:
            sr.setPreferableBackend(cv2.dnn.DNN_BACKEND_DEFAULT)
            sr.setPreferableTarget(cv2.dnn.DNN_TARGET_CPU)
            if state.use_gpu and cuda_device_count == 0:
                logger.info("No CUDA device available — using CPU backend for OpenCV DNN")
        
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
                ramp_len = min(overlap * scale, th // 2) if th > 1 else 0
                if ramp_len > 0:
                    ramp = np.linspace(0, 1, ramp_len + 1, dtype=np.float32)[1:]
                    wy[:len(ramp)] = ramp
                    wy[-len(ramp):] = ramp[::-1]
                ramp_len_x = min(overlap * scale, tw // 2) if tw > 1 else 0
                if ramp_len_x > 0:
                    ramp_x = np.linspace(0, 1, ramp_len_x + 1, dtype=np.float32)[1:]
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
    if not model_path_str.startswith(str(MODELS_DIR.resolve())):
        raise ValueError("Model path outside allowed directory")
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
                    'options': [{'device_id': int(device_id)}, {}],
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
        openvino_cpu_fallback = False

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
                        del session
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
                        openvino_cpu_fallback = True
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
                            {'device_id': int(device_id)},
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
            # Release old ONNX session before replacing to free GPU memory
            old_session = state.onnx_session
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
            if openvino_cpu_fallback:
                state.use_gpu = False
        # Explicitly free old session outside lock to avoid holding it during GC
        if old_session is not None and old_session is not session:
            del old_session

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

            # Integrity gate — catalog entries may pin a sha256; verify BEFORE the
            # file becomes visible as a valid model (supply-chain / corruption guard).
            expected_sha = (model_info.get("sha256") or "").strip().lower()
            if expected_sha:
                h = hashlib.sha256()
                with open(temp_path, "rb") as f:
                    for chunk in iter(lambda: f.read(1 << 20), b""):
                        h.update(chunk)
                actual_sha = h.hexdigest()
                if actual_sha != expected_sha:
                    raise ValueError(
                        f"sha256 mismatch for {model_name}: expected {expected_sha}, got {actual_sha} "
                        "(corrupted download or upstream file changed)"
                    )
                logger.info(f"Model {model_name} sha256 verified")

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

    h, w = img.shape[:2]
    if h * w > MAX_IMAGE_PIXELS:
        raise ValueError(f"Image too large: {w}x{h} ({h*w} pixels). Maximum: {MAX_IMAGE_PIXELS} pixels")

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


def tonemap_hdr_to_sdr(frame_16bit: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """PQ (ST.2084) tone-map 16-bit HDR frame to 8-bit SDR for AI processing.
    Returns (sdr_frame_8bit, luminance_map) where luminance_map preserves HDR info."""
    # Normalize 16-bit to [0, 1] float
    frame_float = frame_16bit.astype(np.float64) / 65535.0

    # Compute per-pixel luminance (Rec.2020 weights) for restoration later
    luminance_map = (0.2627 * frame_float[:, :, 2]
                     + 0.6780 * frame_float[:, :, 1]
                     + 0.0593 * frame_float[:, :, 0])  # BGR order

    # PQ EOTF inverse: linearize from PQ domain
    # ST.2084 constants
    m1 = 0.1593017578125
    m2 = 78.84375
    c1 = 0.8359375
    c2 = 18.8515625
    c3 = 18.6875

    # Apply PQ inverse EOTF to get linear light
    Ym1 = np.power(np.clip(frame_float, 1e-10, 1.0), 1.0 / m2)
    numerator = np.maximum(Ym1 - c1, 0.0)
    denominator = c2 - c3 * Ym1
    denominator = np.maximum(denominator, 1e-10)
    linear = np.power(numerator / denominator, 1.0 / m1)

    # Simple Reinhard tone-map to SDR range
    linear_tonemapped = linear / (1.0 + linear)

    # Apply sRGB gamma (~2.2)
    sdr_float = np.power(np.clip(linear_tonemapped, 0.0, 1.0), 1.0 / 2.2)

    # Convert to 8-bit
    sdr_8bit = np.clip(sdr_float * 255.0, 0, 255).astype(np.uint8)

    return sdr_8bit, luminance_map.astype(np.float32)


def inverse_tonemap_sdr_to_hdr(sdr_upscaled: np.ndarray, luminance_map: np.ndarray,
                                original_16bit: np.ndarray, scale: int) -> np.ndarray:
    """Restore HDR range from upscaled SDR frame using preserved luminance map.
    Upscales the luminance map to match the output resolution, then reapplies HDR range."""
    # Upscale luminance map to match output resolution using bicubic interpolation
    out_h, out_w = sdr_upscaled.shape[:2]
    luminance_upscaled = cv2.resize(luminance_map, (out_w, out_h), interpolation=cv2.INTER_CUBIC)
    luminance_upscaled = np.clip(luminance_upscaled, 1e-6, None)

    # Convert SDR upscaled back to float [0, 1]
    sdr_float = sdr_upscaled.astype(np.float64) / 255.0

    # Remove sRGB gamma to get linear SDR
    sdr_linear = np.power(np.clip(sdr_float, 1e-10, 1.0), 2.2)

    # Inverse Reinhard: L_hdr = L_sdr / (1 - L_sdr)
    sdr_linear_clamped = np.clip(sdr_linear, 0.0, 0.999)
    hdr_linear = sdr_linear_clamped / (1.0 - sdr_linear_clamped)

    # Modulate by luminance ratio to restore HDR brightness structure
    sdr_lum = (0.2627 * sdr_linear[:, :, 2]
               + 0.6780 * sdr_linear[:, :, 1]
               + 0.0593 * sdr_linear[:, :, 0])
    sdr_lum = np.maximum(sdr_lum, 1e-6)
    lum_ratio = luminance_upscaled / sdr_lum
    lum_ratio = np.clip(lum_ratio, 0.01, 100.0)  # Clamp to prevent extreme values while preserving HDR peaks

    hdr_linear = hdr_linear * lum_ratio[:, :, np.newaxis]

    # Apply PQ OETF (forward) to encode back to PQ domain
    m1 = 0.1593017578125
    m2 = 78.84375
    c1 = 0.8359375
    c2 = 18.8515625
    c3 = 18.6875

    Lm1 = np.power(np.clip(hdr_linear, 1e-10, 1.0), m1)
    pq = np.power((c1 + c2 * Lm1) / (1.0 + c3 * Lm1), m2)

    # Convert to 16-bit
    hdr_16bit = np.clip(pq * 65535.0, 0, 65535).astype(np.uint16)

    return hdr_16bit


def upscale_image_hdr(image_bytes: bytes) -> bytes:
    """Upscale a 16-bit HDR image: tone-map to SDR, upscale, inverse tone-map back to HDR.
    Accepts 16-bit PNG bytes, returns 16-bit PNG bytes."""
    # Decode 16-bit image
    nparr = np.frombuffer(image_bytes, np.uint8)
    img_16bit = cv2.imdecode(nparr, cv2.IMREAD_UNCHANGED)

    if img_16bit is None:
        raise ValueError("Failed to decode 16-bit HDR image")

    if img_16bit.dtype != np.uint16:
        raise ValueError(f"Expected 16-bit image, got {img_16bit.dtype}")
    if img_16bit.ndim != 3 or img_16bit.shape[2] != 3:
        raise ValueError(f"HDR images must be 3-channel BGR, got shape {img_16bit.shape}")

    h, w = img_16bit.shape[:2]
    if h * w > MAX_IMAGE_PIXELS:
        raise ValueError(f"Image too large: {w}x{h} ({h*w} pixels). Maximum: {MAX_IMAGE_PIXELS} pixels")

    # Determine model scale
    with _model_lock:
        if state.current_model is None:
            raise ModelNotReadyError("No model loaded")
        if state.current_model_type == "onnx":
            model_scale = state.onnx_model_scale
        elif state.current_model_type == "ncnn":
            model_scale = state.ncnn_model_scale
        else:
            model_scale = state.cv_model_scale

    # Step 1: Tone-map HDR to SDR for AI processing
    sdr_8bit, luminance_map = tonemap_hdr_to_sdr(img_16bit)

    # Step 2: Upscale the SDR frame using the standard pipeline
    sdr_upscaled = upscale_image_array(sdr_8bit)

    # Step 3: Inverse tone-map back to HDR
    hdr_result = inverse_tonemap_sdr_to_hdr(sdr_upscaled, luminance_map, img_16bit, model_scale)

    # Encode as 16-bit PNG
    _, buffer = cv2.imencode('.png', hdr_result)
    return buffer.tobytes()


def _onnx_infer_tile(img_rgb_float: np.ndarray, session, input_name: str, output_name: str) -> np.ndarray:
    """Run ONNX inference on a single tile (HWC float32 [0,1] RGB). Returns HWC float32 RGB."""
    img_nchw = np.transpose(img_rgb_float, (2, 0, 1))  # HWC to CHW
    img_batch = np.expand_dims(img_nchw, axis=0)  # Add batch dimension
    # FP16 only when both globally enabled AND the loaded model expects float16 (issue #67)
    use_fp16 = state.use_fp16 and _session_input_is_fp16(session)
    if use_fp16:
        img_batch = img_batch.astype(np.float16)
    result = session.run([output_name], {input_name: img_batch})[0]
    if use_fp16:
        result = result.astype(np.float32)
    result = np.squeeze(result, axis=0)
    result = np.transpose(result, (1, 2, 0))  # CHW to HWC
    return result


def _onnx_infer_multiframe_tile(tiles: list, session, input_name: str, output_name: str, num_frames: int) -> np.ndarray:
    """Infer a multi-frame tile stack. tiles = list of num_frames arrays, each (H,W,3) float32 [0,1]."""
    stacked = np.stack(tiles, axis=0)                    # (T, H, W, 3)
    stacked = np.transpose(stacked, (0, 3, 1, 2))       # (T, 3, H, W)
    batch = np.expand_dims(stacked, axis=0).astype(np.float32)  # (1, T, 3, H, W)
    # FP16 only when both globally enabled AND the loaded model expects float16 (issue #67)
    use_fp16 = state.use_fp16 and _session_input_is_fp16(session)
    if use_fp16:
        batch = batch.astype(np.float16)

    result = session.run([output_name], {input_name: batch})[0]
    if use_fp16:
        result = result.astype(np.float32)

    # Handle models that output all T frames: (1, T, 3, H*s, W*s)
    if result.ndim == 5:
        result = result[:, num_frames // 2, :, :, :]     # center frame only

    result = np.squeeze(result, axis=0)                  # (3, H*s, W*s)
    result = np.transpose(result, (1, 2, 0))             # (H*s, W*s, 3)
    return result


def _is_cuda_oom(exc: Exception) -> bool:
    """Check if an exception is a CUDA out-of-memory error."""
    msg = str(exc).lower()
    return "out of memory" in msg or "cuda" in msg and ("oom" in msg or "alloc" in msg)


def _run_onnx_tiled(img_rgb: np.ndarray, tile_size: int, overlap: int,
                     session, input_name: str, output_name: str, scale: int) -> np.ndarray:
    """Run tile-based ONNX upscaling with the given tile size. Returns float32 RGB output."""
    h, w = img_rgb.shape[:2]

    # Small image: skip tiling, process directly
    if w <= tile_size and h <= tile_size:
        return _onnx_infer_tile(img_rgb, session, input_name, output_name)

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
                ramp = np.linspace(0, 1, ramp_y + 1, dtype=np.float32)[1:]
                wy[:ramp_y] = ramp
                wy[-ramp_y:] = ramp[::-1]
            if ramp_x > 0:
                ramp = np.linspace(0, 1, ramp_x + 1, dtype=np.float32)[1:]
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
    return output


def upscale_with_onnx(img: np.ndarray) -> np.ndarray:
    """Upscale an image using the loaded ONNX model (Real-ESRGAN).

    Uses tile-based processing for large images to prevent GPU OOM.
    Tile size controlled by ONNX_TILE_SIZE env var (default 512).
    Overlap of 32px between tiles prevents seam artifacts.

    On CUDA OOM, adaptively halves the tile size and retries (max 3 retries,
    minimum tile size 64px). Updates the global ONNX_TILE_SIZE on success so
    subsequent requests use the working size.
    """
    global ONNX_TILE_SIZE
    overlap = 32
    tile_size = ONNX_TILE_SIZE
    h, w = img.shape[:2]
    min_tile_size = 64
    max_retries = 3

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

    for attempt in range(max_retries + 1):
        try:
            result = _run_onnx_tiled(img_rgb, tile_size, overlap, session,
                                     input_name, output_name, scale)
            # Success — persist working tile size for future requests
            if tile_size != ONNX_TILE_SIZE:
                with _model_lock:
                    logger.info(f"Updating global ONNX_TILE_SIZE from {ONNX_TILE_SIZE} to {tile_size} after OOM recovery")
                    ONNX_TILE_SIZE = tile_size
            result = np.clip(result * 255.0, 0, 255).astype(np.uint8)
            return cv2.cvtColor(result, cv2.COLOR_RGB2BGR)
        except Exception as exc:
            if not _is_cuda_oom(exc):
                raise
            new_tile_size = tile_size // 2
            if new_tile_size < min_tile_size or attempt >= max_retries:
                logger.error(f"CUDA OOM at tile_size={tile_size} and cannot reduce further (min={min_tile_size}). Giving up.")
                raise
            logger.warning(
                f"CUDA OOM during ONNX inference with tile_size={tile_size} "
                f"(attempt {attempt + 1}/{max_retries + 1}). "
                f"Halving tile size to {new_tile_size} and retrying."
            )
            tile_size = new_tile_size


def detect_scene_change(frame_a: np.ndarray, frame_b: np.ndarray, threshold: float = 0.35) -> bool:
    """Detect scene change between two frames using histogram comparison.

    Converts both frames to grayscale, computes normalised 256-bin
    histograms, and compares them with cv2.HISTCMP_CORREL.  Correlation
    of 1.0 means identical; values below *threshold* indicate a scene
    change.

    Args:
        frame_a: BGR uint8 image.
        frame_b: BGR uint8 image.
        threshold: Correlation below this value triggers a scene-change
                   detection.  Range 0.0-1.0, default 0.35.

    Returns:
        True when a scene change is detected.
    """
    gray_a = cv2.cvtColor(frame_a, cv2.COLOR_BGR2GRAY) if frame_a.ndim == 3 else frame_a
    gray_b = cv2.cvtColor(frame_b, cv2.COLOR_BGR2GRAY) if frame_b.ndim == 3 else frame_b

    hist_a = cv2.calcHist([gray_a], [0], None, [256], [0, 256])
    hist_b = cv2.calcHist([gray_b], [0], None, [256], [0, 256])

    cv2.normalize(hist_a, hist_a)
    cv2.normalize(hist_b, hist_b)

    correlation = cv2.compareHist(hist_a, hist_b, cv2.HISTCMP_CORREL)
    return correlation < threshold


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
                    blend_y[i] = (i + 1) / (ramp + 1)
                    blend_y[actual_th - 1 - i] = (i + 1) / (ramp + 1)
                for i in range(ramp_x):
                    blend_x[i] = (i + 1) / (ramp + 1)
                    blend_x[actual_tw - 1 - i] = (i + 1) / (ramp + 1)
            blend_w = blend_y[:, None] * blend_x[None, :]
            blend_w3 = blend_w[:, :, None]

            output[oy:oy_end, ox:ox_end] += out_tile[:actual_th, :actual_tw].astype(np.float32) * blend_w3
            weight[oy:oy_end, ox:ox_end] += blend_w3

    weight = np.maximum(weight, 1e-8)
    output = output / weight
    output = np.clip(output * 255.0, 0, 255).astype(np.uint8)
    return cv2.cvtColor(output, cv2.COLOR_RGB2BGR)


def load_rife_model(model_name: str = "rife-v4.9") -> bool:
    """Load a RIFE interpolation model into memory using ONNX Runtime.

    Follows the same provider fallback chain as load_onnx_model but stores
    the session in state.rife_session so it does not conflict with the
    upscaling model stored in state.onnx_session.
    """
    if not ONNX_AVAILABLE:
        logger.error("ONNX Runtime not available — cannot load RIFE model")
        return False

    if model_name not in AVAILABLE_MODELS:
        logger.error(f"Unknown RIFE model: {model_name}")
        return False

    model_info = AVAILABLE_MODELS[model_name]
    if model_info.get("category") != "interpolation":
        logger.error(f"Model {model_name} is not an interpolation model")
        return False

    model_path = get_model_path(model_name)
    if not model_path.exists():
        logger.error(f"RIFE model file not found: {model_path}")
        return False

    try:
        # Build provider list — prefer GPU, fall back to CPU
        available_providers = ort.get_available_providers()
        providers = []
        if 'CUDAExecutionProvider' in available_providers and state.use_gpu:
            providers.append('CUDAExecutionProvider')
        if 'OpenVINOExecutionProvider' in available_providers and state.use_gpu:
            providers.append('OpenVINOExecutionProvider')
        providers.append('CPUExecutionProvider')

        session = ort.InferenceSession(str(model_path), providers=providers)
        active_providers = session.get_providers()

        with _model_lock:
            state.rife_session = session
            state.rife_model_name = model_name
            state.rife_loaded = True

        logger.info(f"RIFE model {model_name} loaded successfully (providers: {active_providers})")
        state.model_last_used[model_name] = time.time()
        return True

    except Exception as e:
        logger.error(f"Failed to load RIFE model {model_name}: {e}")
        state.last_load_error = str(e)
        return False


def interpolate_frame_rife(frame1: np.ndarray, frame2: np.ndarray,
                           session, timestep: float = 0.5) -> np.ndarray:
    """Run frame interpolation between two frames (architecture-adaptive).

    Despite the name, this drives any interpolation ONNX whose I/O matches a
    known shape: RIFE (1 concatenated input, or [combined, timestep]), IFRNet
    ([img0, img1, timestep], 3 inputs) and CAIN ([img0, img1], 2x3ch inputs).
    The feed dict is chosen from session.get_inputs() count + channel shape.

    Args:
        frame1: First frame as BGR numpy array (H, W, 3) uint8.
        frame2: Second frame as BGR numpy array (H, W, 3) uint8.
        session: ONNX Runtime InferenceSession for the RIFE model.
        timestep: Interpolation position between frame1 (0.0) and frame2 (1.0).
                  Default 0.5 produces the midpoint frame.

    Returns:
        Interpolated frame as BGR numpy array (H, W, 3) uint8.
    """
    # Convert BGR to RGB and normalize to [0, 1] float32
    f1 = cv2.cvtColor(frame1, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
    f2 = cv2.cvtColor(frame2, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0

    h, w, _ = f1.shape

    # Pad to multiple of 32 (RIFE architecture requirement)
    pad_h = (32 - h % 32) % 32
    pad_w = (32 - w % 32) % 32
    if pad_h > 0 or pad_w > 0:
        f1 = np.pad(f1, ((0, pad_h), (0, pad_w), (0, 0)), mode='reflect')
        f2 = np.pad(f2, ((0, pad_h), (0, pad_w), (0, 0)), mode='reflect')

    # Transpose to NCHW: (1, 3, H, W)
    f1_t = np.transpose(f1, (2, 0, 1))[np.newaxis, ...]
    f2_t = np.transpose(f2, (2, 0, 1))[np.newaxis, ...]

    # Concatenate on channel dimension -> (1, 6, H, W) — two RGB frames
    combined = np.concatenate([f1_t, f2_t], axis=1)

    # Timestep tensor (1, 1, H, W) filled with the timestep value
    ts = np.full((1, 1, f1.shape[0], f1.shape[1]), timestep, dtype=np.float32)

    # Determine input names and build feed dict dynamically
    input_names = [inp.name for inp in session.get_inputs()]
    output_names = [out.name for out in session.get_outputs()]

    if len(input_names) == 1:
        # Single input: concatenated frames (1, 6, H, W)
        feed = {input_names[0]: combined}
    elif len(input_names) == 2:
        # v1.8.2 — two-input models come in two architecture flavours; disambiguate by
        # the first input's channel count instead of assuming RIFE:
        #   * 6ch  -> RIFE-style [concatenated frames, timestep]
        #   * 3ch  -> CAIN-style [img0, img1] (fixed midpoint, no timestep input)
        first_shape = session.get_inputs()[0].shape
        first_ch = first_shape[1] if len(first_shape) >= 2 and isinstance(first_shape[1], int) else None
        if first_ch == 3:
            feed = {input_names[0]: f1_t, input_names[1]: f2_t}
        else:
            feed = {input_names[0]: combined, input_names[1]: ts}
    elif len(input_names) == 3:
        # Three inputs: frame1, frame2, timestep — RIFE 3-input AND IFRNet (img0, img1, embt)
        feed = {input_names[0]: f1_t, input_names[1]: f2_t, input_names[2]: ts}
    else:
        # Fallback: try frames + timestep as first two inputs
        feed = {input_names[0]: combined, input_names[1]: ts}

    # Run inference
    results = session.run(output_names, feed)
    output = results[0]  # (1, 3, H, W)

    # Convert back to HWC BGR uint8
    output = np.squeeze(output, axis=0)  # (3, H, W)
    output = np.transpose(output, (1, 2, 0))  # (H, W, 3)

    # Remove padding
    if pad_h > 0 or pad_w > 0:
        output = output[:h, :w, :]

    # Denormalize and convert RGB -> BGR
    output = np.clip(output * 255.0, 0, 255).astype(np.uint8)
    output = cv2.cvtColor(output, cv2.COLOR_RGB2BGR)

    return output


# ============================================================
# === Face Restoration Pipeline (v1.6.1.7) ===
# ============================================================
# Detect faces with OpenCV's bundled Haar cascade (no extra deps), run each
# 512x512 crop through an ONNX face-restore model (GFPGAN/CodeFormer), then
# paste the restored face back with a feathered alpha mask to avoid visible
# seams.  Designed to be fast enough to use in the real-time frame pipeline.

_face_cascade_path_cache: Optional[str] = None
_face_restore_lock = threading.Lock()


def _get_face_detector():
    """Return a cached OpenCV Haar cascade face detector.

    Uses OpenCV's bundled haarcascade_frontalface_default.xml — no external
    download, works on every OpenCV install. Lazy-loaded on first use.
    """
    global _face_cascade_path_cache
    if state.face_detector is not None:
        return state.face_detector
    if _face_cascade_path_cache is None:
        _face_cascade_path_cache = cv2.data.haarcascades + "haarcascade_frontalface_default.xml"
    detector = cv2.CascadeClassifier(_face_cascade_path_cache)
    if detector.empty():
        raise RuntimeError(f"Failed to load Haar cascade at {_face_cascade_path_cache}")
    state.face_detector = detector
    return detector


def load_face_restore_model(model_name: str) -> dict:
    """Load a face-restore ONNX model into memory (GFPGAN / CodeFormer).

    Unlike the main upscaler, we don't auto-download here — that's the
    download_model_endpoint's job. If the file isn't present, surface a
    clear error so the UI can prompt the user to run a download first.
    """
    if not ONNX_AVAILABLE:
        raise RuntimeError("ONNX Runtime not available — face restore disabled")
    if model_name not in AVAILABLE_MODELS:
        raise ValueError(f"Unknown model {model_name}")
    model_info = AVAILABLE_MODELS[model_name]
    if model_info.get("category") != "face_restore":
        raise ValueError(f"Model {model_name} is not a face-restore model")

    model_path = get_model_path(model_name)
    if not model_path.exists():
        raise FileNotFoundError(
            f"Face-restore model file not found: {model_path}. "
            f"Run POST /download-model?model_name={model_name} first."
        )

    available_providers = ort.get_available_providers()
    providers = []
    if 'CUDAExecutionProvider' in available_providers and state.use_gpu:
        providers.append('CUDAExecutionProvider')
    providers.append('CPUExecutionProvider')

    sess = ort.InferenceSession(str(model_path), providers=providers)
    with _model_lock:
        state.face_restore_session = sess
        state.face_restore_model_name = model_name
        state.face_restore_loaded = True
        state.face_restore_input_size = int(model_info.get("input_size", 512))
    logger.info(f"Face-restore model loaded: {model_name}")
    return {
        "success": True,
        "model": model_name,
        "input_size": state.face_restore_input_size,
        "providers": list(sess.get_providers())
    }


def unload_face_restore_model() -> dict:
    """Unload the face-restore model to free VRAM."""
    with _model_lock:
        state.face_restore_session = None
        state.face_restore_model_name = None
        state.face_restore_loaded = False
    return {"success": True, "message": "Face-restore model unloaded"}


def _restore_face_crop(face_bgr: np.ndarray, target_size: int = 512) -> np.ndarray:
    """Run the loaded face-restore ONNX model on a single face crop.

    Input: BGR np.uint8 array, any size. Output: BGR np.uint8 array at target_size x target_size.
    """
    sess = state.face_restore_session
    if sess is None:
        raise ModelNotReadyError("No face-restore model loaded")

    # Resize crop to model's expected input size
    face_resized = cv2.resize(face_bgr, (target_size, target_size), interpolation=cv2.INTER_CUBIC)
    # BGR -> RGB, normalise to [-1, 1] (GFPGAN/CodeFormer convention)
    face_rgb = cv2.cvtColor(face_resized, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
    face_rgb = (face_rgb - 0.5) / 0.5
    blob = np.transpose(face_rgb, (2, 0, 1))[np.newaxis, ...].astype(np.float32)
    if state.use_fp16:
        blob = blob.astype(np.float16)

    input_name = sess.get_inputs()[0].name
    output_name = sess.get_outputs()[0].name

    # CodeFormer takes an additional 'w' fidelity param in [0,1]; pass 0.7 as sensible default
    inputs = {input_name: blob}
    for inp in sess.get_inputs()[1:]:
        if inp.name.lower() in ("w", "fidelity", "weight"):
            inputs[inp.name] = np.array([0.7], dtype=np.float32)

    result = sess.run([output_name], inputs)[0]
    if state.use_fp16:
        result = result.astype(np.float32)
    if result.ndim == 4:
        result = result[0]
    if result.ndim == 3 and result.shape[0] in (1, 3):
        result = np.transpose(result, (1, 2, 0))
    # Denormalise back to [0, 255]
    result = ((result * 0.5) + 0.5) * 255.0
    result = np.clip(result, 0, 255).astype(np.uint8)
    return cv2.cvtColor(result, cv2.COLOR_RGB2BGR)


def _feathered_mask(h: int, w: int, feather_ratio: float = 0.12) -> np.ndarray:
    """Build a feathered alpha mask (h, w, 1) float32 in [0, 1].

    Center of the mask is 1.0, edges fade to 0 over feather_ratio*min(h,w) pixels.
    Used for seamless paste-back of restored face crops into the source frame.
    """
    mask = np.ones((h, w), dtype=np.float32)
    feather = max(1, int(min(h, w) * feather_ratio))
    # Horizontal fade
    for i in range(feather):
        alpha = (i + 1) / (feather + 1)
        mask[:, i] *= alpha
        mask[:, w - 1 - i] *= alpha
    # Vertical fade
    for i in range(feather):
        alpha = (i + 1) / (feather + 1)
        mask[i, :] *= alpha
        mask[h - 1 - i, :] *= alpha
    return mask[..., np.newaxis]


def restore_faces_in_frame(img: np.ndarray, max_faces: int = 6) -> tuple:
    """Detect and restore faces in a BGR image.

    Returns (output_image, face_count). If no face-restore model is loaded or
    no faces are detected, returns (img, 0) unchanged.
    """
    if not state.face_restore_loaded or state.face_restore_session is None:
        return img, 0

    detector = _get_face_detector()
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    # Conservative detection params — avoid too many false positives
    faces = detector.detectMultiScale(
        gray,
        scaleFactor=1.15,
        minNeighbors=5,
        minSize=(48, 48)
    )
    if len(faces) == 0:
        return img, 0

    # Limit face count — runaway detection on busy scenes would tank frame rate
    faces = list(faces)[:max_faces]
    output = img.copy()
    input_size = state.face_restore_input_size

    with _face_restore_lock:
        for (x, y, w, h) in faces:
            # Expand crop by 20% to include jawline/hair for better context
            pad = int(0.20 * max(w, h))
            x0 = max(0, x - pad); y0 = max(0, y - pad)
            x1 = min(img.shape[1], x + w + pad); y1 = min(img.shape[0], y + h + pad)
            face_crop = img[y0:y1, x0:x1]
            if face_crop.size == 0:
                continue
            try:
                restored = _restore_face_crop(face_crop, target_size=input_size)
            except Exception as e:
                logger.warning(f"Face restore failed on one crop: {e}")
                continue
            # Resize restored face back to original crop dimensions
            restored_resized = cv2.resize(restored, (x1 - x0, y1 - y0), interpolation=cv2.INTER_LANCZOS4)
            # Feathered alpha blend to hide seams
            mask = _feathered_mask(y1 - y0, x1 - x0, feather_ratio=0.12)
            blended = (output[y0:y1, x0:x1].astype(np.float32) * (1.0 - mask) +
                       restored_resized.astype(np.float32) * mask)
            output[y0:y1, x0:x1] = np.clip(blended, 0, 255).astype(np.uint8)

    return output, len(faces)


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


async def upscale_frame_realtime(frame: np.ndarray, session, local_state) -> np.ndarray:
    """Optimized single-pass upscaling for real-time playback.
    Uses full-frame inference when possible (small enough for VRAM),
    falls back to minimal-overlap tiling only if needed.
    Skips blend weighting for speed."""
    model_type = local_state["model_type"]

    # OpenCV DNN: already fast, just call directly
    if model_type == "opencv":
        cv_model = local_state["cv_model"]
        if cv_model is None:
            raise ModelNotReadyError("No OpenCV model loaded")
        loop = asyncio.get_running_loop()
        return await loop.run_in_executor(_cpu_executor, cv_model.upsample, frame)

    # ncnn: delegate to existing function
    if model_type == "ncnn":
        loop = asyncio.get_running_loop()
        return await loop.run_in_executor(_cpu_executor, upscale_with_ncnn, frame)

    # ONNX: optimized single-pass (no blend weighting)
    if session is None:
        raise ModelNotReadyError("No ONNX session loaded")

    input_name = session.get_inputs()[0].name
    output_name = session.get_outputs()[0].name
    scale = local_state.get("scale", 4)
    h, w = frame.shape[:2]

    # Convert BGR -> RGB float32
    img_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0

    max_pixels = 512 * 512  # Threshold for full-frame inference

    def _infer_full():
        """Full-frame inference without tiling."""
        blob = np.transpose(img_rgb, (2, 0, 1))[np.newaxis, ...]
        if state.use_fp16:
            blob = blob.astype(np.float16)
        result = session.run([output_name], {input_name: blob})[0]
        if state.use_fp16:
            result = result.astype(np.float32)
        if result.ndim == 4:
            result = result[0]
        if result.ndim == 3 and result.shape[0] in (1, 3):
            result = np.transpose(result, (1, 2, 0))
        result = np.clip(result * 255.0, 0, 255).astype(np.uint8)
        return cv2.cvtColor(result, cv2.COLOR_RGB2BGR)

    def _infer_tiled():
        """Minimal-overlap tiling without blend weighting for speed."""
        tile_size = min(ONNX_TILE_SIZE, 512)
        overlap = 8  # Minimal overlap for speed
        step = max(tile_size - overlap, 1)
        out_h, out_w = h * scale, w * scale
        output = np.zeros((out_h, out_w, 3), dtype=np.float32)

        y_tiles = list(range(0, max(h - tile_size, 0) + 1, step))
        if not y_tiles or y_tiles[-1] + tile_size < h:
            y_tiles.append(max(h - tile_size, 0))
        x_tiles = list(range(0, max(w - tile_size, 0) + 1, step))
        if not x_tiles or x_tiles[-1] + tile_size < w:
            x_tiles.append(max(w - tile_size, 0))

        for y in y_tiles:
            for x in x_tiles:
                tile = img_rgb[y:y + tile_size, x:x + tile_size]
                blob = np.transpose(tile, (2, 0, 1))[np.newaxis, ...]
                if state.use_fp16:
                    blob = blob.astype(np.float16)
                res = session.run([output_name], {input_name: blob})[0]
                if state.use_fp16:
                    res = res.astype(np.float32)
                res = np.squeeze(res, axis=0)
                if res.shape[0] == 3:
                    res = np.transpose(res, (1, 2, 0))

                oy, ox = y * scale, x * scale
                oh, ow = res.shape[:2]
                output[oy:oy + oh, ox:ox + ow] = res

        output = np.clip(output * 255.0, 0, 255).astype(np.uint8)
        return cv2.cvtColor(output, cv2.COLOR_RGB2BGR)

    loop = asyncio.get_running_loop()
    if h * w <= max_pixels:
        return await loop.run_in_executor(_cpu_executor, _infer_full)
    else:
        return await loop.run_in_executor(_cpu_executor, _infer_tiled)


def run_benchmark(test_size: int = 256) -> dict:
    """Run a quick benchmark on the loaded model."""
    if state.current_model is None:
        return {"error": "No model loaded"}

    # Adjust test_size to the model's ACTUAL expected input dimensions.
    # Dynamic-shape models accept any tile (use a small, fast 64px tile);
    # fixed-shape models such as "realesrgan-x4-256" bake a 256x256 input
    # into the graph and raise a Reshape error during warmup if fed 64x64
    # (FIX-3 / issue #70 — the "Reshape" failure Gemini misattributed to the
    # GPU). Read the real shape from the loaded ONNX session instead of
    # guessing from the model name (the substring "realesrgan" matched both
    # the dynamic realesrgan-x4 and the fixed realesrgan-x4-256).
    if state.current_model_type == "onnx":
        fixed_dim = None
        if state.onnx_session is not None:
            try:
                ishape = state.onnx_session.get_inputs()[0].shape  # e.g. [1,3,256,256] or [1,3,'h','w']
                h = ishape[2] if len(ishape) >= 3 else None
                if isinstance(h, int) and h > 0:
                    fixed_dim = h
            except Exception:
                fixed_dim = None
        if fixed_dim is not None:
            test_size = fixed_dim          # fixed-shape model: input MUST match exactly
        elif "realesrgan" in state.current_model:
            test_size = 64                 # dynamic Real-ESRGAN: small tile (prior behavior)

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
        "using_gpu": gpu_is_active()
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


@app.get("/logs/recent")
async def logs_recent(limit: int = 200):
    """Return the most recent log entries (from ring buffer). Used when the
    Console tab is first opened to populate history before live-streaming."""
    limit = max(1, min(limit, 1000))
    with _LOG_LOCK:
        items = list(LOG_BUFFER)[-limit:]
    return {"count": len(items), "entries": items}


@app.get("/logs/stream")
async def logs_stream(request: Request):
    """Server-Sent Events stream of live log records. The client gets every
    log line in near real-time. Auto-cleans subscriber queue on disconnect."""
    q: "asyncio.Queue[dict]" = asyncio.Queue(maxsize=500)

    # Register subscriber + prime with recent history so UI shows context
    with _LOG_LOCK:
        recent = list(LOG_BUFFER)[-100:]
        _LOG_SUBSCRIBERS.add(q)

    async def event_gen():
        try:
            # Replay recent history first
            for entry in recent:
                yield f"data: {json.dumps(entry)}\n\n"
            # Then stream live. Heartbeat every 15s so proxies don't kill idle conn.
            while True:
                if await request.is_disconnected():
                    break
                try:
                    entry = await asyncio.wait_for(q.get(), timeout=15.0)
                    yield f"data: {json.dumps(entry)}\n\n"
                except asyncio.TimeoutError:
                    yield ": heartbeat\n\n"
        finally:
            with _LOG_LOCK:
                _LOG_SUBSCRIBERS.discard(q)

    return StreamingResponse(
        event_gen(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache, no-transform",
            "X-Accel-Buffering": "no",
            "Connection": "keep-alive",
        },
    )


# --- GPU-active reporting (FIX-4 / issues #69, #70) -----------------------
# state.use_gpu is the *requested* USE_GPU intent and was effectively only
# tracked for CUDA/ROCm, so OpenVINO/CoreML setups got reported as "no GPU":
# the dashboard showed GPU active while the System tab said "CPU only", and
# /gpu-verify returned "using_gpu": false even with OpenVINO in the active
# provider list. Report the truth derived from the live provider list. Only
# the *displayed* value changes here — control flow still keys off
# state.use_gpu.
_NON_CPU_PROVIDERS = frozenset({
    "CUDAExecutionProvider", "TensorrtExecutionProvider",
    "OpenVINOExecutionProvider", "ROCMExecutionProvider",
    "MIGraphXExecutionProvider", "CoreMLExecutionProvider",
    "DmlExecutionProvider",
})


def gpu_is_active() -> bool:
    """True when a non-CPU execution provider is active. Before any model is
    loaded (provider list still empty) fall back to the requested USE_GPU
    intent so a freshly started GPU box doesn't read 'no GPU'."""
    if state.providers:
        return any(p in _NON_CPU_PROVIDERS for p in state.providers)
    return state.use_gpu


@app.get("/health")
async def health():
    """Health check endpoint."""
    status_data = {
        "status": "degraded" if state.circuit_open else "healthy",
        "model_loaded": state.current_model is not None,
        "model_name": state.current_model,
        "model_type": state.current_model_type,
        "providers": state.providers,
        "using_gpu": gpu_is_active(),
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
    
    # Expose auth posture so the UI can show a one-time banner instead of error toasts
    # on every call when the operator forgot to set API_TOKEN. "disable" = explicit opt-out.
    api_token_env = os.getenv("API_TOKEN", "")
    managed_token_count = token_store.count()
    api_token_configured = (bool(api_token_env) and api_token_env != "disable") or managed_token_count > 0
    auth_enabled = bool(api_token_env)

    return {
        "status": "running",
        "version": VERSION,
        "current_model": state.current_model,
        "model_type": state.current_model_type,
        "available_providers": state.providers,
        "using_gpu": gpu_is_active(),
        "loaded_models": [state.current_model] if state.current_model else [],
        "processing_count": state.processing_count,
        "max_concurrent": state.max_concurrent,
        "onnx_available": ONNX_AVAILABLE,
        "model_scale": scale,
        "cuda_available": has_cuda,
        "tensorrt_available": has_tensorrt,
        "input_frames": state.current_model_input_frames,
        "use_fp16": state.use_fp16,
        "api_token_configured": api_token_configured,
        "converter_available": _converter_available(),
        "auth_enabled": auth_enabled,
        "managed_tokens": managed_token_count,
        "scene_change_detection": {
            "enabled": SCENE_CHANGE_THRESHOLD < 1.0,
            "threshold": SCENE_CHANGE_THRESHOLD
        },
        "hdr_supported": True
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
        "using_gpu": gpu_is_active(),
        "gpu_list": state.gpu_list
    }


def recommend_model():
    """Hardware-aware model recommendation.

    Picks a model + scale the detected hardware can actually run in reasonable
    time — addressing the #1 setup friction (pushing realesrgan-x4 onto a weak
    CPU box and hitting the seconds-per-frame wall). Pure function of the
    detected state (providers / gpu_list / vram / cpu_cores), so it is unit-testable.
    """
    providers = state.providers or []
    has_cuda = any(("CUDA" in p) or ("Tensorrt" in p) for p in providers)
    has_rocm = any(("ROCM" in p) or ("MIGraphX" in p) for p in providers)
    has_ov = any("OpenVINO" in p for p in providers)
    gpu_present = bool(state.gpu_list)
    gpu_active = gpu_present and (has_cuda or has_rocm or has_ov)

    vram = 0
    try:
        vram = int(str(state.gpu_memory).split()[0])
    except (ValueError, IndexError, AttributeError, TypeError):
        vram = 0
    cores = state.cpu_cores or 0

    if gpu_active and (has_cuda or has_rocm) and vram >= 6000:
        model_id, scale, tier = "realesrgan-x4", 4, "strong-gpu"
        reason = "Dedicated GPU with %d MB VRAM — best quality (Real-ESRGAN x4)." % vram
        alts = ["realesrgan-x4-256", "fsrcnn-x2"]
    elif gpu_active and (has_cuda or has_rocm):
        model_id, scale, tier = "realesrgan-x4-256", 4, "mid-gpu"
        reason = "GPU with limited VRAM (%d MB) — 256px-tiled Real-ESRGAN keeps memory in check." % vram
        alts = ["realesrgan-x4", "fsrcnn-x2"]
    elif gpu_active and has_ov:
        model_id, scale, tier = "realesrgan-x4-256", 4, "igpu"
        reason = "Intel iGPU via OpenVINO — tiled Real-ESRGAN; switch to fsrcnn-x2 if it's too slow."
        alts = ["fsrcnn-x2", "fsrcnn-x3"]
    elif cores >= 8:
        model_id, scale, tier = "fsrcnn-x2", 2, "strong-cpu"
        reason = "CPU only (%d cores) — a lightweight model at 2x; heavy ONNX models will saturate the host." % cores
        alts = ["fsrcnn-x3", "realesrgan-x4-256"]
    else:
        model_id, scale, tier = "fsrcnn-x2", 2, "weak-cpu"
        reason = ("Weak CPU (%d cores, e.g. Celeron) — lightest model at 2x. For live playback prefer "
                  "Anime4K on the client GPU instead of server-side upscaling.") % cores
        alts = ["fsrcnn-x3"]

    # never recommend something that isn't in the catalog
    if model_id not in AVAILABLE_MODELS:
        model_id, scale, tier = "fsrcnn-x2", 2, tier
    alts = [a for a in alts if a in AVAILABLE_MODELS and a != model_id]

    return {
        "tier": tier,
        "recommended_model": model_id,
        "recommended_scale": scale,
        "reason": reason,
        "alternatives": alts,
        "hardware": {
            "gpu": state.gpu_name,
            "gpu_active": gpu_active,
            "vram_mb": vram,
            "cpu": state.cpu_name,
            "cpu_cores": cores,
            "providers": providers,
        },
    }


@app.get("/recommend")
async def recommend_endpoint():
    """Hardware-aware model + scale recommendation (read-only, no token)."""
    return recommend_model()


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
        "using_gpu": gpu_is_active(),
        "gpu_requested": state.use_gpu,
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

    # WSL2 / Docker Desktop diagnostics (issue #66)
    diagnostics["wsl2"] = {
        "is_wsl2_environment": Path("/dev/dxg").exists(),
        "wsl_lib_mounted": Path("/usr/lib/wsl/lib").exists(),
        "ld_library_path": os.environ.get("LD_LIBRARY_PATH", ""),
        "hint": (
            "WSL2 GPU passthrough requires: devices: [/dev/dxg:/dev/dxg], "
            "volumes: [/usr/lib/wsl:/usr/lib/wsl:ro], "
            "environment: [LD_LIBRARY_PATH=/usr/lib/wsl/lib]. "
            "See docker-compose.yml WSL2 section."
            if Path("/dev/dxg").exists() and not Path("/usr/lib/wsl/lib").exists()
            else None
        )
    }

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


# --- Setup Doctor (WS1, v1.7.9) ----------------------------------------------
# One-shot self-service diagnostic that condenses the recent setup-friction
# saga (#66 WSL2 / #69 wrong image / #70 "switches to CPU") into a single curl.
# Every check is strictly read-only EXCEPT model_smoke (one tiny inference),
# which is timeout-guarded and degrades to warn — never fail — on a no-model box.

def _detect_backend() -> str:
    """Best-effort backend label. There is no single stored backend value, so
    derive it from the live ONNX provider list, falling back to device hints
    when no model is loaded yet. Returns one clean key:
    nvidia | amd | intel | intel-wsl2 | apple | directml | cpu."""
    providers = state.providers or []
    if any("Tensorrt" in p or "CUDA" in p for p in providers):
        return "nvidia"
    if any("ROCM" in p or "MIGraphX" in p for p in providers):
        return "amd"
    if any("OpenVINO" in p for p in providers):
        return "intel-wsl2" if Path("/dev/dxg").exists() else "intel"
    if any("CoreML" in p for p in providers):
        return "apple"
    if any("Dml" in p for p in providers):
        return "directml"
    # No GPU provider active yet → derive from device hints / requested intent.
    if Path("/dev/dxg").exists():
        return "intel-wsl2"
    return "cpu"


def _model_smoke_sync() -> dict:
    """Tiny, bounded smoke test on the already-loaded model. ONNX models get a
    real run() built from their declared input shape (same probe as /gpu-verify);
    non-ONNX (ncnn/opencv) models report 'loaded' (no cheap isolated inference
    path). Runs in a worker thread under a timeout in the route."""
    if ONNX_AVAILABLE and state.onnx_session is not None:
        model_input = state.onnx_session.get_inputs()[0]
        input_name = model_input.name
        input_shape = model_input.shape
        test_shape = [d if isinstance(d, int) and d > 0 else 16 for d in input_shape]
        test_input = np.random.rand(*test_shape).astype(np.float32)
        t = time.time()
        state.onnx_session.run(None, {input_name: test_input})
        return {"ok": True, "ms": round((time.time() - t) * 1000, 2),
                "shape": str(input_shape), "kind": "onnx"}
    return {"ok": True, "ms": None, "shape": None,
            "kind": state.current_model_type or "unknown"}


@app.get("/doctor")
async def doctor():
    """Setup Doctor — one-shot diagnostic checklist for the running instance.
    Each item: {check, status: ok|warn|fail, detail, fix}. Pairs with the
    website support bot: the bot answers questions, the doctor diagnoses THIS box."""
    checks = []
    backend = _detect_backend()
    providers = state.providers or []
    gpu_active = gpu_is_active()

    # Probes computed once, reused across checks 2-4 (avoids a double nvidia-smi).
    dri = Path("/dev/dri")
    has_dri = dri.exists() and any(dri.glob("renderD*"))
    has_dxg = Path("/dev/dxg").exists()
    try:
        _nv = subprocess.run(["nvidia-smi", "-L"], capture_output=True, text=True, timeout=5)
        nvidia_ok = _nv.returncode == 0
    except Exception:
        nvidia_ok = False
    passthrough_ok = has_dri or has_dxg or nvidia_ok
    pt_detail = f"/dev/dri renderD*={has_dri}, /dev/dxg={has_dxg}, nvidia-smi={nvidia_ok}"
    avail = set(ort.get_available_providers()) if ONNX_AVAILABLE else set()
    gpu_eps = avail & set(_NON_CPU_PROVIDERS)

    # Per-backend device-passthrough fix line (reused by checks 2 & 3).
    device_fix = {
        "nvidia": "Grant the GPU: compose `deploy.resources.reservations.devices: "
                  "[{driver: nvidia, count: all, capabilities: [gpu]}]` "
                  "(older Docker: `runtime: nvidia`; CLI: `docker run --gpus all`).",
        "amd": "Pass ROCm devices: `--device=/dev/kfd --device=/dev/dri` + "
               "`group_add: [video, render]`.",
        "intel": "Pass the render node: `--device=/dev/dri` + `group_add: render`.",
        "intel-wsl2": "WSL2/Docker-Desktop: `devices: [/dev/dxg:/dev/dxg]` + "
                      "`volumes: [/usr/lib/wsl:/usr/lib/wsl:ro]` + "
                      "`environment: [LD_LIBRARY_PATH=/usr/lib/wsl/lib]`.",
        "apple": "Apple Silicon runs natively (install-native-macos.sh); "
                 "Docker cannot pass the Apple GPU.",
        "directml": "DirectML needs a Windows host with a DX12-capable GPU.",
    }

    # 1) backend — info only.
    checks.append({
        "check": "backend",
        "status": "ok",
        "detail": f"detected backend: {backend}"
                  + ("" if providers else " (no model loaded yet — derived from device/intent)"),
        "fix": None,
    })

    # 2) gpu_provider_active — a non-CPU provider is actually live.
    if gpu_active:
        checks.append({"check": "gpu_provider_active", "status": "ok",
                       "detail": f"active providers: {providers}", "fix": None})
    elif gpu_eps and passthrough_ok:
        # WS2: the onnxruntime build HAS GPU EPs and a GPU device is passed, but none
        # is active -> the GPU runtime failed to initialise (the image is correct).
        checks.append({"check": "gpu_provider_active", "status": "fail",
                       "detail": f"GPU device present and {sorted(gpu_eps)} compiled in, "
                                 f"but inference runs on CPU (active providers={providers})",
                       "fix": "GPU runtime failed to initialise -- almost always a HOST "
                              "driver/toolkit version mismatch (your image is correct). Update the "
                              "host NVIDIA driver (>= what the image's CUDA needs) or ROCm, recreate "
                              "the container, and check `docker logs` for the onnxruntime error "
                              "(e.g. 'CUDA driver version is insufficient for CUDA runtime version')."})
    elif backend == "cpu":
        checks.append({"check": "gpu_provider_active", "status": "warn",
                       "detail": "running on CPU (CPU image / no GPU image selected)",
                       "fix": "This is CPU-only upscaling. For GPU, pull "
                              "`docker7-<nvidia|amd|intel|...>` and pass the device "
                              "(see device_passthrough)."})
    else:
        checks.append({"check": "gpu_provider_active", "status": "fail",
                       "detail": f"on CPU though backend '{backend}' was detected; "
                                 f"providers={providers}",
                       "fix": f"Pull `docker7-{backend}` and pass the device "
                              f"(see device_passthrough)."})

    # 3) device_passthrough — the GPU device reaches the container.
    if backend == "cpu":
        checks.append({"check": "device_passthrough", "status": "ok",
                       "detail": "CPU image — no GPU device required. " + pt_detail,
                       "fix": None})
    elif passthrough_ok:
        checks.append({"check": "device_passthrough", "status": "ok",
                       "detail": pt_detail, "fix": None})
    else:
        checks.append({"check": "device_passthrough", "status": "fail",
                       "detail": pt_detail,
                       "fix": device_fix.get(backend, "Pass your GPU device into the container.")})

    # 4) onnx_provider_pkg — the right onnxruntime build (no vendor shadowing).
    avail = set(ort.get_available_providers()) if ONNX_AVAILABLE else set()
    gpu_eps = avail & set(_NON_CPU_PROVIDERS)
    if not ONNX_AVAILABLE:
        checks.append({"check": "onnx_provider_pkg", "status": "warn",
                       "detail": "onnxruntime not importable in this image",
                       "fix": "Use an image that ships onnxruntime (docker7 variants do)."})
    elif state.use_gpu and not gpu_eps and (has_dri or has_dxg or nvidia_ok):
        # Only a *shadowed/wrong* build: GPU intent + a GPU device is actually
        # present, yet no GPU EP. On a plain CPU image (no device) USE_GPU
        # defaults to "true", so without this guard a CPU box would false-fail.
        checks.append({"check": "onnx_provider_pkg", "status": "fail",
                       "detail": f"GPU requested but only {sorted(avail)} available",
                       "fix": "Wrong/shadowed onnxruntime: a plain `onnxruntime` installed "
                              "next to the vendor build shadows it (Azure/CPU only) → silent "
                              "CPU fallback. Pull the clean `docker7-<backend>` image."})
    else:
        checks.append({"check": "onnx_provider_pkg", "status": "ok",
                       "detail": f"available EPs: {sorted(avail)}", "fix": None})

    # 5) api_token — auth is configured (a token, or explicit disable).
    api_token_env = os.getenv("API_TOKEN", "")
    if api_token_env == "disable":
        checks.append({"check": "api_token", "status": "ok",
                       "detail": "API_TOKEN=disable (auth off — fine for trusted LAN)", "fix": None})
    elif api_token_env:
        checks.append({"check": "api_token", "status": "ok",
                       "detail": "API_TOKEN set (auth on)", "fix": None})
    else:
        checks.append({"check": "api_token", "status": "warn",
                       "detail": "API_TOKEN not set",
                       "fix": "Set `API_TOKEN=disable` for a trusted LAN, or the SAME token "
                              "on both the Jellyfin plugin and this service."})

    # 6) model_smoke — the one non-read-only check. Warn (never fail) with no model;
    #    timeout-guarded so /doctor never blocks.
    smoke_timeout = 8.0
    if not state.current_model:
        checks.append({"check": "model_smoke", "status": "warn",
                       "detail": "no model loaded yet — load a model to run a smoke test",
                       "fix": "Load a model (POST /models/load or pick one in the UI), then re-run /doctor."})
    else:
        try:
            loop = asyncio.get_running_loop()
            smoke = await asyncio.wait_for(loop.run_in_executor(None, _model_smoke_sync),
                                           timeout=smoke_timeout)
            if smoke.get("kind") == "onnx":
                checks.append({"check": "model_smoke", "status": "ok",
                               "detail": f"ONNX inference ok in {smoke.get('ms')} ms "
                                         f"(input {smoke.get('shape')})", "fix": None})
            else:
                checks.append({"check": "model_smoke", "status": "ok",
                               "detail": f"model loaded ({smoke.get('kind')}); "
                                         f"ONNX deep smoke not applicable", "fix": None})
        except asyncio.TimeoutError:
            checks.append({"check": "model_smoke", "status": "warn",
                           "detail": f"smoke test exceeded {smoke_timeout:.0f}s "
                                     f"(skipped to keep /doctor responsive)",
                           "fix": "Model may be slow to warm up; retry or check logs."})
        except Exception as e:
            checks.append({"check": "model_smoke", "status": "fail",
                           "detail": f"inference failed: {e}",
                           "fix": "Model load/warmup failed — check server logs and the model file."})

    statuses = [c["status"] for c in checks]
    overall = "fail" if "fail" in statuses else ("warn" if "warn" in statuses else "ok")
    return {"version": VERSION, "backend": backend, "overall": overall, "checks": checks}


@app.get("/connections")
async def plugin_connections():
    """Get plugin connection status."""
    return {
        "connections": state.plugin_connections,
        "total": len(state.plugin_connections)
    }


@app.post("/connections/register")
async def register_connection(
    request: Request,
    plugin_id: str = Form(...),
    jellyfin_url: str = Form(...)
):
    """Register a plugin connection."""
    _require_api_token(request)
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
            # hostname is a DNS name — resolve it and check each IP against private ranges
            try:
                addrinfos = socket.getaddrinfo(hostname, None)
                for family, _type, _proto, _canonname, sockaddr in addrinfos:
                    resolved_ip = ipaddress.ip_address(sockaddr[0])
                    if resolved_ip.is_private or resolved_ip.is_loopback or resolved_ip.is_link_local or resolved_ip.is_reserved:
                        raise HTTPException(
                            status_code=400,
                            detail="Private/loopback URLs are not allowed (DNS resolves to private IP)."
                        )
            except socket.gaierror:
                raise HTTPException(status_code=400, detail="Could not resolve hostname.")
            except HTTPException:
                raise

    connection = {
        "plugin_id": plugin_id,
        "jellyfin_url": jellyfin_url,
        "connected_at": time.strftime("%Y-%m-%d %H:%M:%S"),
        "last_ping": time.strftime("%Y-%m-%d %H:%M:%S")
    }
    
    # Update or add connection (guarded by lock to prevent race conditions)
    with _connections_lock:
        for i, conn in enumerate(state.plugin_connections):
            if conn["plugin_id"] == plugin_id:
                state.plugin_connections[i] = connection
                return {"status": "updated", "connection": connection}

        if len(state.plugin_connections) >= 100:
            state.plugin_connections.pop(0)
        state.plugin_connections.append(connection)
    return {"status": "registered", "connection": connection}


@app.get("/models")
async def list_models():
    """List available models.

    Filesystem existence checks are cached for _MODELS_CACHE_TTL seconds to
    avoid 40+ Path.exists() calls on every request.  The cache is invalidated
    whenever a model is loaded, downloaded, or deleted.
    """
    global _models_cache, _models_cache_expiry
    now = time.time()
    if _models_cache is not None and now < _models_cache_expiry:
        return _models_cache

    # Snapshot AVAILABLE_MODELS under lock to prevent concurrent mutation
    # (upload/delete) from causing "dictionary changed size during iteration".
    with _model_lock:
        items_snapshot = list(AVAILABLE_MODELS.items())
    models = []
    for model_id, info in items_snapshot:
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
    result = {"models": models, "total": len(models)}
    _models_cache = result
    _models_cache_expiry = now + _MODELS_CACHE_TTL
    return result


@app.post("/models/download")
async def download_model_endpoint(model_name: str = Form(...), request: Request = None):
    """Download a model."""
    _require_api_token(request)
    model_name = _resolve_model_key(model_name)
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
    
    _invalidate_models_cache()
    return {"status": "success", "model": model_name, "size_mb": round(size_mb, 2)}


async def _run_download_job(job_id: str, model_name: str):
    """Background worker for /models/download-async: runs the download and records
    the outcome in the job registry so the caller can poll without holding a request."""
    with _download_jobs_guard:
        if job_id in _download_jobs:
            _download_jobs[job_id]["status"] = "downloading"
    try:
        ok = await download_model(model_name)
        model_path = get_model_path(model_name)
        size_mb = model_path.stat().st_size / 1024 / 1024 if model_path.exists() else 0
        with _download_jobs_guard:
            job = _download_jobs.get(job_id)
            if job is not None:
                job["status"] = "completed" if ok else "failed"
                job["size_mb"] = round(size_mb, 2)
                if not ok:
                    job["error"] = "download returned failure"
        if ok:
            _invalidate_models_cache()
    except Exception as e:
        logger.error(f"Async download job {job_id} for {model_name} failed: {e}")
        with _download_jobs_guard:
            job = _download_jobs.get(job_id)
            if job is not None:
                job["status"] = "failed"
                job["error"] = str(e)


@app.post("/models/download-async")
async def download_model_async_endpoint(model_name: str = Form(...), request: Request = None):
    """v1.8.2 — start a model download in the background and return a job id immediately.
    Decouples big downloads from the HTTP request lifetime (no more client timeouts).
    Poll /models/download-status/{job_id} for progress."""
    _require_api_token(request)
    model_name = _resolve_model_key(model_name)
    if model_name not in AVAILABLE_MODELS:
        raise HTTPException(status_code=404, detail=f"Model {model_name} not found")
    if not AVAILABLE_MODELS[model_name].get("available", True):
        raise HTTPException(status_code=400, detail=f"Model {model_name} is not yet available")

    # already on disk -> short-circuit, no job needed
    if get_model_path(model_name).exists():
        return {"status": "completed", "model": model_name, "job_id": None, "already_present": True}

    job_id = uuid.uuid4().hex
    with _download_jobs_guard:
        _download_jobs[job_id] = {
            "job_id": job_id,
            "model": model_name,
            "status": "queued",
            "error": None,
            "started_at": time.time(),
        }
    asyncio.create_task(_run_download_job(job_id, model_name))
    return {"status": "queued", "model": model_name, "job_id": job_id}


@app.get("/models/download-status/{job_id}")
async def download_status_endpoint(job_id: str, request: Request = None):
    """Poll the state of an async download job (queued|downloading|completed|failed)."""
    _require_api_token(request)
    with _download_jobs_guard:
        job = _download_jobs.get(job_id)
        if job is None:
            raise HTTPException(status_code=404, detail=f"No download job {job_id}")
        return dict(job)


@app.post("/models/load")
async def load_model_endpoint(
    model_name: str = Form(...),
    use_gpu: bool = Form(True),
    gpu_device_id: Optional[int] = Form(None),
    request: Request = None
):
    """Load a model into memory. Optionally specify GPU device index."""
    _require_api_token(request)
    model_name = _resolve_model_key(model_name)
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

    _invalidate_models_cache()
    return {
        "status": "success",
        "model": model_name,
        "model_type": state.current_model_type,
        "using_gpu": gpu_is_active(),
        "gpu_device_id": state.gpu_device_id,
        "providers": state.providers
    }


@app.post("/upscale")
async def upscale_endpoint(
    request: Request,
    file: UploadFile = File(...),
    scale: int = Form(2)
):
    """Upscale an image. Scale is determined by the loaded model; the scale parameter is validated for consistency."""
    _require_api_token(request)
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
    # Python 3.12 broke asyncio.wait_for(coro, timeout=0): the coroutine is
    # wrapped in a Task, cancellation is scheduled immediately, and the Task
    # never gets to run before being cancelled — causing permanent 429.
    # Fix: check _value directly (safe in asyncio; no await between check and
    # acquire, so no other coroutine can interleave).
    if sem is None or sem._value <= 0:
        raise HTTPException(status_code=429, detail="Too many concurrent requests")
    await sem.acquire()
    acquired = True
    with _processing_count_lock:
        state.processing_count += 1

    start_time = time.time()
    model_name = state.current_model or "unknown"
    try:
        # Read image
        image_bytes = await file.read()
        if len(image_bytes) > MAX_UPLOAD_BYTES:
            raise HTTPException(status_code=413, detail=f"Image too large ({len(image_bytes)} bytes, max {MAX_UPLOAD_BYTES})")

        # Upscale in thread pool to not block async
        loop = asyncio.get_running_loop()
        result = await loop.run_in_executor(_cpu_executor, upscale_image, image_bytes)

        duration_ms = (time.time() - start_time) * 1000
        _record_success(model_name, duration_ms)
        return Response(content=result, media_type="image/png")

    except ModelNotReadyError as e:
        _record_failure(model_name)
        logger.warning(f"Upscale input error: {e}")
        raise HTTPException(status_code=400, detail="Invalid input or model not ready")
    except ValueError as e:
        _record_failure(model_name)
        logger.warning(f"Upscale validation error: {e}")
        raise HTTPException(status_code=413, detail=str(e))
    except Exception as e:
        _record_failure(model_name)
        logger.error(f"Upscale failed: {e}")
        raise HTTPException(status_code=500, detail="Upscaling failed")
    finally:
        if acquired:
            with _processing_count_lock:
                state.processing_count -= 1
            sem.release()


@app.post("/upscale-hdr")
async def upscale_frame_hdr(
    request: Request,
    file: UploadFile = File(...),
    scale: int = Form(2)
):
    """Upscale a 16-bit HDR frame. Accepts 16-bit PNG, returns 16-bit PNG.
    The pipeline: receive 16-bit -> tone-map to 8-bit SDR -> upscale -> inverse tone-map back to 16-bit."""
    _require_api_token(request)
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
        logger.warning(f"HDR upscale: requested scale={scale} differs from model scale={model_scale}. Using model's native scale={model_scale}.")

    # Capture semaphore reference for safe release
    sem = _upscale_semaphore
    acquired = False
    # See /upscale for explanation of why we avoid asyncio.wait_for(timeout=0)
    if sem is None or sem._value <= 0:
        raise HTTPException(status_code=429, detail="Too many concurrent requests")
    await sem.acquire()
    acquired = True
    with _processing_count_lock:
        state.processing_count += 1

    start_time = time.time()
    model_name = state.current_model or "unknown"
    try:
        # Read 16-bit HDR image
        image_bytes = await file.read()
        if len(image_bytes) > MAX_UPLOAD_BYTES:
            raise HTTPException(status_code=413, detail=f"Image too large ({len(image_bytes)} bytes, max {MAX_UPLOAD_BYTES})")

        # Upscale HDR in thread pool to not block async
        loop = asyncio.get_running_loop()
        result = await loop.run_in_executor(_cpu_executor, upscale_image_hdr, image_bytes)

        duration_ms = (time.time() - start_time) * 1000
        _record_success(model_name, duration_ms)
        logger.info(f"HDR frame upscaled in {duration_ms:.0f}ms ({len(image_bytes)} -> {len(result)} bytes)")
        return Response(content=result, media_type="image/png")

    except ModelNotReadyError as e:
        _record_failure(model_name)
        logger.warning(f"HDR upscale input error: {e}")
        raise HTTPException(status_code=400, detail="Invalid input or model not ready")
    except ValueError as e:
        _record_failure(model_name)
        logger.warning(f"HDR upscale validation error: {e}")
        raise HTTPException(status_code=413, detail=str(e))
    except Exception as e:
        _record_failure(model_name)
        logger.error(f"HDR upscale failed: {e}")
        raise HTTPException(status_code=500, detail="HDR upscaling failed")
    finally:
        if acquired:
            with _processing_count_lock:
                state.processing_count -= 1
            sem.release()


@app.get("/benchmark")
async def benchmark_endpoint(request: Request = None):
    """Run a benchmark on the current model."""
    _require_api_token(request)
    if state.cv_model is None and state.onnx_session is None and state.ncnn_upscaler is None:
        raise HTTPException(status_code=400, detail="No model loaded")

    try:
        await asyncio.wait_for(_benchmark_lock.acquire(), timeout=5.0)
    except asyncio.TimeoutError:
        raise HTTPException(status_code=429, detail="Benchmark already in progress")
    try:
        loop = asyncio.get_running_loop()
        result = await loop.run_in_executor(_cpu_executor, run_benchmark, 256)
        return result
    finally:
        _benchmark_lock.release()


@app.post("/upscale-frame")
async def upscale_frame_endpoint(request: Request):
    """Fast frame upscaling for real-time playback. Raw JPEG in, JPEG out. Returns 503 when busy."""
    _require_api_token(request)
    _check_circuit_breaker()

    if state.cv_model is None and state.onnx_session is None and state.ncnn_upscaler is None:
        raise HTTPException(status_code=400, detail="No model loaded")

    # Capture semaphore reference for safe release
    sem = _upscale_semaphore
    acquired = False
    # See /upscale for explanation of why we avoid asyncio.wait_for(timeout=0)
    if sem is None or sem._value <= 0:
        raise HTTPException(status_code=503, detail="Busy")
    await sem.acquire()
    acquired = True
    with _processing_count_lock:
        state.processing_count += 1

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

        h, w = img.shape[:2]
        if h * w > MAX_IMAGE_PIXELS:
            raise HTTPException(status_code=413, detail=f"Image too large: {w}x{h}")

        # Upscale using array helper (no double encode/decode)
        loop = asyncio.get_running_loop()
        result = await loop.run_in_executor(_cpu_executor, upscale_image_array, img)

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
    _require_api_token(request)
    _check_circuit_breaker()

    if state.current_model is None:
        raise HTTPException(status_code=400, detail="No model loaded")

    # Read expected_frames under model lock for consistency with the loaded model
    with _model_lock:
        expected_frames = min(state.current_model_input_frames, MAX_INPUT_FRAMES)

    # Capture semaphore reference for safe release
    sem = _upscale_semaphore
    acquired = False
    # See /upscale for explanation of why we avoid asyncio.wait_for(timeout=0)
    if sem is None or sem._value <= 0:
        raise HTTPException(status_code=503, detail="Busy")
    await sem.acquire()
    acquired = True
    with _processing_count_lock:
        state.processing_count += 1

    start_time = time.time()
    model_name = state.current_model or "unknown"
    form = None
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
            h, w = img.shape[:2]
            if h * w > MAX_IMAGE_PIXELS:
                raise HTTPException(status_code=413, detail=f"Image too large: {w}x{h}")
            frames.append(img)

        # If single-frame model loaded, transparent fallback: upscale center frame only
        if expected_frames == 1:
            center = frames[len(frames) // 2]
            loop = asyncio.get_running_loop()
            result = await loop.run_in_executor(_cpu_executor, upscale_image_array, center)
        else:
            # Scene-change detection: check consecutive frame pairs for abrupt
            # changes.  If any pair crosses the threshold the multi-frame model
            # would blend frames from different scenes, producing ghosting
            # artifacts.  Fall back to single-frame upscaling for the center
            # frame in that case.
            scene_change_detected = False
            if SCENE_CHANGE_THRESHOLD < 1.0:
                for i in range(len(frames) - 1):
                    if detect_scene_change(frames[i], frames[i + 1], SCENE_CHANGE_THRESHOLD):
                        scene_change_detected = True
                        logger.info(
                            "Scene change detected between frame_%d and frame_%d "
                            "(threshold=%.2f). Falling back to single-frame upscaling.",
                            i, i + 1, SCENE_CHANGE_THRESHOLD,
                        )
                        break

            if scene_change_detected:
                center = frames[len(frames) // 2]
                loop = asyncio.get_running_loop()
                result = await loop.run_in_executor(_cpu_executor, upscale_image_array, center)
            else:
                loop = asyncio.get_running_loop()
                result = await loop.run_in_executor(_cpu_executor, upscale_multiframe, frames)

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
        if form is not None:
            await form.close()
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
        "using_gpu": gpu_is_active(),
        "capture_width": width,
        "capture_height": height,
        "output_width": width * (state.onnx_model_scale if state.current_model_type == "onnx" else (state.ncnn_model_scale if state.current_model_type == "ncnn" else state.cv_model_scale)),
        "output_height": height * (state.onnx_model_scale if state.current_model_type == "onnx" else (state.ncnn_model_scale if state.current_model_type == "ncnn" else state.cv_model_scale))
    }


@app.post("/upscale-stream")
async def upscale_stream(request: Request):
    """Stream-based upscaling for real-time playback.
    Accepts raw video frames as a continuous stream, upscales each frame,
    and returns upscaled frames as a streaming response.

    Headers:
    - X-Frame-Width: input frame width
    - X-Frame-Height: input frame height
    - X-Frame-Format: 'rgb24' or 'bgr24' (default bgr24)
    - X-Target-FPS: target FPS for rate limiting (default 30)

    Input: raw frame bytes (width * height * 3 per frame)
    Output: streaming response with upscaled raw frames
    """
    _require_api_token(request)
    _check_circuit_breaker()

    if state.cv_model is None and state.onnx_session is None and state.ncnn_upscaler is None:
        raise HTTPException(status_code=400, detail="No model loaded")

    # Validate headers BEFORE acquiring semaphore to prevent leaks on bad input
    try:
        frame_width = int(request.headers.get("X-Frame-Width", "0"))
        frame_height = int(request.headers.get("X-Frame-Height", "0"))
    except (ValueError, TypeError):
        raise HTTPException(status_code=400, detail="Invalid X-Frame-Width or X-Frame-Height header")

    if frame_width <= 0 or frame_height <= 0:
        raise HTTPException(status_code=400, detail="X-Frame-Width and X-Frame-Height must be positive integers")

    if frame_width > 3840 or frame_height > 2160:
        raise HTTPException(status_code=400, detail="Input resolution too high for real-time streaming (max 3840x2160)")

    frame_format = request.headers.get("X-Frame-Format", "bgr24").lower()
    if frame_format not in ("rgb24", "bgr24"):
        raise HTTPException(status_code=400, detail="X-Frame-Format must be 'rgb24' or 'bgr24'")

    try:
        target_fps = float(request.headers.get("X-Target-FPS", "30"))
    except (ValueError, TypeError):
        target_fps = 30.0
    target_fps = max(1.0, min(target_fps, 120.0))

    # Acquire concurrency semaphore AFTER validation (prevents leak on bad headers)
    sem = _upscale_semaphore
    if sem is None or sem._value <= 0:
        raise HTTPException(status_code=429, detail="Too many concurrent requests")
    await sem.acquire()
    with _processing_count_lock:
        state.processing_count += 1

    frame_size = frame_width * frame_height * 3
    min_frame_interval = 1.0 / target_fps

    # Capture model state once under lock for the duration of the stream
    with _model_lock:
        local_state = {
            "model_type": state.current_model_type,
            "cv_model": state.cv_model,
            "scale": (state.onnx_model_scale if state.current_model_type == "onnx"
                      else state.ncnn_model_scale if state.current_model_type == "ncnn"
                      else state.cv_model_scale),
        }
        onnx_session = state.onnx_session

    _realtime_stats.reset()
    model_name = state.current_model or "unknown"

    max_buffer_bytes = frame_size * 10  # Cap: at most 10 buffered frames to prevent OOM

    async def frame_generator():
        """Read raw frames from request body stream and yield upscaled frames."""
        buffer = bytearray()
        frames_ok = 0

        async for chunk in request.stream():
            buffer.extend(chunk)

            # OOM protection: drop oldest frames if buffer grows too large
            while len(buffer) > max_buffer_bytes + frame_size:
                del buffer[:frame_size]
                _realtime_stats.record_drop()

            # Process all complete frames in the buffer
            while len(buffer) >= frame_size:
                raw_frame = bytes(buffer[:frame_size])
                del buffer[:frame_size]

                frame_start = time.time()

                try:
                    # Decode raw bytes to numpy array
                    frame = np.frombuffer(raw_frame, dtype=np.uint8).reshape((frame_height, frame_width, 3))

                    # Convert RGB to BGR if needed (OpenCV uses BGR internally)
                    if frame_format == "rgb24":
                        frame = cv2.cvtColor(frame, cv2.COLOR_RGB2BGR)

                    # Upscale with real-time optimized path
                    upscaled = await upscale_frame_realtime(frame, onnx_session, local_state)

                    # Convert back to requested format for output
                    if frame_format == "rgb24":
                        upscaled = cv2.cvtColor(upscaled, cv2.COLOR_BGR2RGB)

                    duration = time.time() - frame_start
                    _realtime_stats.record_frame(duration)
                    frames_ok += 1

                    # Rate limiting: if we processed faster than target FPS, yield immediately
                    # (the consumer controls the actual playback rate)
                    yield upscaled.tobytes()

                    # Adaptive frame dropping: if processing is too slow, skip buffered frames
                    if duration > min_frame_interval * 2 and len(buffer) >= frame_size:
                        # Drop one frame to catch up — yield last good frame to keep consumer aligned
                        del buffer[:frame_size]
                        _realtime_stats.record_drop()
                        yield upscaled.tobytes()  # Duplicate last frame to maintain alignment
                        logger.debug("Dropped frame to maintain real-time pace (%.1fms per frame)", duration * 1000)

                except Exception as exc:
                    logger.error("Real-time frame upscale failed: %s", exc)
                    _realtime_stats.record_drop()
                    # Yield empty frame marker (all zeros) so consumer knows a frame was skipped
                    continue

        # Log final stats and release semaphore
        try:
            stats = _realtime_stats.snapshot()
            logger.info(
                "Stream ended: %d frames processed, %d dropped, avg %.1fms/frame, %.1f FPS",
                stats["frames_processed"], stats["dropped_frames"],
                stats["avg_frame_ms"], stats["current_fps"]
            )
            if frames_ok > 0:
                _record_success(model_name, stats["total_time_s"] * 1000)
        finally:
            with _processing_count_lock:
                state.processing_count -= 1
            sem.release()

    return StreamingResponse(frame_generator(), media_type="application/octet-stream")


@app.get("/realtime-stats")
async def realtime_stats_endpoint():
    """Return current real-time upscaling performance stats."""
    return _realtime_stats.snapshot()


@app.get("/benchmark-frame")
async def benchmark_frame_endpoint(width: int = 480, height: int = 270, request: Request = None):
    """Benchmark at actual capture resolution for real-time upscaling feasibility."""
    _require_api_token(request)
    if state.cv_model is None and state.onnx_session is None and state.ncnn_upscaler is None:
        raise HTTPException(status_code=400, detail="No model loaded")

    if state.current_model is None:
        raise HTTPException(status_code=400, detail="No model loaded. Load a model first via POST /models/load")

    # Clamp dimensions to reasonable range
    width = max(64, min(width, 1920))
    height = max(64, min(height, 1080))

    try:
        await asyncio.wait_for(_benchmark_lock.acquire(), timeout=5.0)
    except asyncio.TimeoutError:
        raise HTTPException(status_code=429, detail="Benchmark already in progress")
    try:
        loop = asyncio.get_running_loop()
        result = await loop.run_in_executor(_cpu_executor, _run_frame_benchmark, width, height)
        return result
    finally:
        _benchmark_lock.release()


@app.post("/config")
async def update_config(
    use_gpu: Optional[bool] = Form(None),
    max_concurrent: Optional[int] = Form(None),
    gpu_device_id: Optional[int] = Form(None),
    request: Request = None
):
    """Update service configuration."""
    _require_api_token(request)
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
# === Managed API tokens (CRUD) ===
# Admin-managed, hashed, persistent tokens (see token_store). Every endpoint
# requires an already-valid credential (env bootstrap token OR an existing
# managed token) via _require_api_token — you must be authenticated to manage
# tokens. The env API_TOKEN therefore bootstraps the very first token.
# ============================================================
@app.get("/auth/tokens")
async def list_api_tokens(request: Request = None):
    """List managed tokens. Never returns the secret or its hash; each entry
    carries a derived `expired` flag so the UI can grey out dead tokens."""
    _require_api_token(request)
    env_token = os.getenv("API_TOKEN", "")
    return {
        "tokens": token_store.list_tokens(),
        "bootstrap_env": bool(env_token) and env_token != "disable",
    }


@app.post("/auth/tokens")
async def create_api_token(
    name: str = Form(...),
    expires_days: Optional[int] = Form(None),
    request: Request = None,
):
    """Create a managed token. Omit expires_days (or 0/null) for a token that
    never expires; otherwise 1..3650 days. Returns the plaintext token EXACTLY
    ONCE — it is hashed at rest and can never be retrieved again."""
    _require_api_token(request)
    try:
        token, info = token_store.create_token((name or "").strip(), expires_days)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc))
    return {"token": token, "info": info}


@app.delete("/auth/tokens/{token_id}")
async def revoke_api_token(token_id: str, request: Request = None):
    """Revoke (permanently delete) a managed token by id."""
    _require_api_token(request)
    if not token_store.revoke_token(token_id):
        raise HTTPException(status_code=404, detail="Token not found")
    return {"revoked": True, "id": token_id}


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
    # Require API token for all cleanup operations (info disclosure + destructive)
    _require_api_token(request)
    if not dry_run:
        # Double-check token is actually set for destructive operations
        expected_token = os.getenv("API_TOKEN", "")
        if expected_token:
            provided_token = request.headers.get("x-api-token", "") if request else ""
            if not hmac.compare_digest(provided_token, expected_token):
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


# ============================================================
# === Frame Interpolation (RIFE) ===
# ============================================================

@app.post("/interpolate-frames")
async def interpolate_frames(request: Request):
    """Interpolate a new frame between two input frames using RIFE.

    Accepts multipart form with 'frame1' and 'frame2' as PNG images.
    Optional 'model' string to select RIFE variant (default 'rife-v4.9').
    Optional 'timestep' float (0.0-1.0, default 0.5 for midpoint).
    Returns the interpolated frame as PNG.
    """
    _require_api_token(request)
    _check_circuit_breaker()

    if not ONNX_AVAILABLE:
        raise HTTPException(status_code=500, detail="ONNX Runtime not available — frame interpolation requires ONNX")

    # Parse multipart form
    form = await request.form()

    frame1_file = form.get("frame1")
    frame2_file = form.get("frame2")
    if frame1_file is None or frame2_file is None:
        raise HTTPException(status_code=400, detail="Both 'frame1' and 'frame2' files are required")

    timestep_raw = form.get("timestep", "0.5")
    try:
        timestep = float(timestep_raw)
    except (ValueError, TypeError):
        raise HTTPException(status_code=400, detail="timestep must be a float between 0.0 and 1.0")
    if not 0.0 <= timestep <= 1.0:
        raise HTTPException(status_code=400, detail="timestep must be between 0.0 and 1.0")

    model_name = _resolve_model_key(str(form.get("model", "rife-v4.9")))
    if model_name not in AVAILABLE_MODELS:
        raise HTTPException(status_code=400, detail=f"Unknown model: {model_name}")
    if AVAILABLE_MODELS[model_name].get("category") != "interpolation":
        raise HTTPException(status_code=400, detail=f"Model {model_name} is not an interpolation model")

    # v1.8.2 — experimental/self-host interpolation archs (IFRNet/CAIN) have no verified
    # public ONNX to auto-download. If the user hasn't placed one in the models dir, return a
    # clear 501 instead of a confusing 404 download failure. They run fine once self-hosted —
    # the interpolation engine is architecture-adaptive (RIFE / IFRNet / CAIN).
    if AVAILABLE_MODELS[model_name].get("self_host") and not get_model_path(model_name).exists():
        raise HTTPException(
            status_code=501,
            detail=f"Model {model_name} is experimental/self-host: place its ONNX export in the models directory "
                   f"(no verified public download yet)."
        )

    # Read frame data
    frame1_bytes = await frame1_file.read()
    frame2_bytes = await frame2_file.read()
    if not frame1_bytes or not frame2_bytes:
        raise HTTPException(status_code=400, detail="Empty frame data")
    if len(frame1_bytes) > MAX_UPLOAD_BYTES or len(frame2_bytes) > MAX_UPLOAD_BYTES:
        raise HTTPException(status_code=413, detail=f"Frame too large (max {MAX_UPLOAD_BYTES} bytes)")

    # Decode frames
    nparr1 = np.frombuffer(frame1_bytes, np.uint8)
    nparr2 = np.frombuffer(frame2_bytes, np.uint8)
    img1 = cv2.imdecode(nparr1, cv2.IMREAD_COLOR)
    img2 = cv2.imdecode(nparr2, cv2.IMREAD_COLOR)
    if img1 is None or img2 is None:
        raise HTTPException(status_code=400, detail="Failed to decode one or both frames")

    h1, w1 = img1.shape[:2]
    if h1 * w1 > MAX_IMAGE_PIXELS:
        raise HTTPException(status_code=413, detail=f"Image too large: {w1}x{h1}")
    h2, w2 = img2.shape[:2]
    if h2 * w2 > MAX_IMAGE_PIXELS:
        raise HTTPException(status_code=413, detail=f"Image too large: {w2}x{h2}")

    if img1.shape != img2.shape:
        raise HTTPException(status_code=400, detail=f"Frame dimensions must match: frame1={img1.shape}, frame2={img2.shape}")

    # Load RIFE model if not already loaded or if a different model is requested
    with _model_lock:
        needs_load = state.rife_session is None or state.rife_model_name != model_name

    if needs_load:
        # Auto-download if not present
        model_path = get_model_path(model_name)
        if not model_path.exists():
            logger.info(f"RIFE model {model_name} not downloaded — auto-downloading...")
            dl_success = await download_model(model_name)
            if not dl_success:
                raise HTTPException(status_code=500, detail=f"Failed to auto-download RIFE model {model_name}")

        loop = asyncio.get_running_loop()
        loaded = await loop.run_in_executor(None, load_rife_model, model_name)
        if not loaded:
            raise HTTPException(status_code=500, detail=f"Failed to load RIFE model {model_name}")

    # Capture session under lock
    with _model_lock:
        session = state.rife_session
    if session is None:
        raise HTTPException(status_code=500, detail="RIFE model session not available")

    # Run interpolation in executor to avoid blocking the event loop
    start_time = time.time()
    model_key = state.rife_model_name or model_name
    try:
        loop = asyncio.get_running_loop()
        result = await loop.run_in_executor(
            _cpu_executor, interpolate_frame_rife, img1, img2, session, timestep
        )

        # Encode result as PNG
        success, buffer = cv2.imencode('.png', result)
        if not success:
            raise HTTPException(status_code=500, detail="Failed to encode interpolated frame")

        duration_ms = (time.time() - start_time) * 1000
        _record_success(model_key, duration_ms)
        logger.info(f"Frame interpolation completed in {duration_ms:.1f}ms (model={model_key}, timestep={timestep})")

        return Response(content=buffer.tobytes(), media_type="image/png")

    except HTTPException:
        raise
    except Exception as e:
        _record_failure(model_key)
        logger.error(f"Frame interpolation failed: {e}")
        raise HTTPException(status_code=500, detail="Frame interpolation failed")


@app.get("/interpolation/status")
async def interpolation_status():
    """Show the status of the RIFE frame interpolation model."""
    with _model_lock:
        rife_loaded = state.rife_loaded
        rife_model = state.rife_model_name
        rife_session = state.rife_session

    # Gather available interpolation models
    interpolation_models = []
    for model_id, info in AVAILABLE_MODELS.items():
        if info.get("category") == "interpolation":
            model_path = get_model_path(model_id)
            interpolation_models.append({
                "id": model_id,
                "name": info["name"],
                "description": info["description"],
                "downloaded": model_path.exists(),
                "loaded": rife_model == model_id,
                "available": info.get("available", True)
            })

    providers = []
    if rife_session is not None:
        try:
            providers = rife_session.get_providers()
        except Exception as e:
            logger.warning(f"Failed to get RIFE session providers: {e}")
            providers = ["unknown"]

    return {
        "interpolation_available": ONNX_AVAILABLE,
        "model_loaded": rife_loaded and rife_session is not None,
        "current_model": rife_model,
        "providers": providers,
        "models": interpolation_models
    }


# ══════════════════════════════════════════════════════════════════════════════
# Feature: Face Restoration (v1.6.1.7 — GFPGAN / CodeFormer)
# ══════════════════════════════════════════════════════════════════════════════

@app.post("/face-restore/load")
async def face_restore_load_endpoint(model_name: str = Form("gfpgan-v1.4"), request: Request = None):
    """Load a face-restore model (GFPGAN or CodeFormer) into memory."""
    _require_api_token(request)
    try:
        loop = asyncio.get_running_loop()
        result = await loop.run_in_executor(None, load_face_restore_model, model_name)
        return result
    except FileNotFoundError as e:
        # Auto-download on first load, same pattern as RIFE
        logger.info(f"Face-restore model {model_name} not downloaded — auto-downloading...")
        dl_success = await download_model(model_name)
        if not dl_success:
            raise HTTPException(status_code=404, detail=str(e))
        loop = asyncio.get_running_loop()
        result = await loop.run_in_executor(None, load_face_restore_model, model_name)
        return result
    except (ValueError, RuntimeError) as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        logger.error(f"Face-restore model load failed: {e}")
        raise HTTPException(status_code=500, detail="Face-restore model load failed")


@app.post("/face-restore/unload")
async def face_restore_unload_endpoint(request: Request = None):
    """Unload the face-restore model to free memory."""
    _require_api_token(request)
    return unload_face_restore_model()


@app.get("/face-restore/status")
async def face_restore_status(request: Request = None):
    """Show status of the face-restore subsystem."""
    _require_api_token(request)
    with _model_lock:
        loaded = state.face_restore_loaded
        model = state.face_restore_model_name
        sess = state.face_restore_session
        input_size = state.face_restore_input_size

    providers: list = []
    if sess is not None:
        try:
            providers = list(sess.get_providers())
        except Exception:
            providers = ["unknown"]

    models = []
    for mid, info in AVAILABLE_MODELS.items():
        if info.get("category") == "face_restore":
            models.append({
                "id": mid,
                "name": info["name"],
                "description": info["description"],
                "downloaded": get_model_path(mid).exists(),
                "loaded": model == mid,
                "input_size": info.get("input_size", 512),
                "available": info.get("available", True)
            })

    return {
        "face_restore_available": ONNX_AVAILABLE,
        "model_loaded": loaded and sess is not None,
        "current_model": model,
        "input_size": input_size,
        "providers": providers,
        "models": models
    }


@app.post("/face-restore/frame")
async def face_restore_frame_endpoint(request: Request):
    """Detect and restore faces in a single frame.

    Accepts JPEG or PNG bytes in the request body. Returns the processed
    frame as JPEG. If no model is loaded, auto-loads GFPGAN as a default.
    """
    _require_api_token(request)

    body = await request.body()
    if not body:
        raise HTTPException(status_code=400, detail="Empty body")
    if len(body) > MAX_UPLOAD_BYTES:
        raise HTTPException(status_code=413, detail=f"Image too large ({len(body)} bytes)")

    nparr = np.frombuffer(body, np.uint8)
    img = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
    if img is None:
        raise HTTPException(status_code=400, detail="Failed to decode image")

    h, w = img.shape[:2]
    if h * w > MAX_IMAGE_PIXELS:
        raise HTTPException(status_code=413, detail=f"Image too large: {w}x{h}")

    # Auto-load default face-restore model if none loaded yet
    if not state.face_restore_loaded:
        try:
            model_path = get_model_path("gfpgan-v1.4")
            if not model_path.exists():
                await download_model("gfpgan-v1.4")
            loop = asyncio.get_running_loop()
            await loop.run_in_executor(None, load_face_restore_model, "gfpgan-v1.4")
        except Exception as e:
            raise HTTPException(status_code=503, detail=f"Face-restore model unavailable: {e}")

    start_time = time.time()
    try:
        loop = asyncio.get_running_loop()
        restored, face_count = await loop.run_in_executor(
            _cpu_executor, restore_faces_in_frame, img
        )
    except Exception as e:
        logger.error(f"Face restoration failed: {e}")
        raise HTTPException(status_code=500, detail="Face restoration failed")

    duration_ms = (time.time() - start_time) * 1000
    logger.info(f"Face restore: {face_count} faces in {duration_ms:.1f}ms (model={state.face_restore_model_name})")

    success, buffer = cv2.imencode('.jpg', restored, [cv2.IMWRITE_JPEG_QUALITY, 92])
    if not success:
        raise HTTPException(status_code=500, detail="Failed to encode result")

    headers = {
        "X-Face-Count": str(face_count),
        "X-Duration-Ms": f"{duration_ms:.1f}",
        "X-Face-Model": state.face_restore_model_name or "unknown"
    }
    return Response(content=buffer.tobytes(), media_type="image/jpeg", headers=headers)


# ══════════════════════════════════════════════════════════════════════════════
# Feature: Quality Metrics (PSNR / SSIM)
# ══════════════════════════════════════════════════════════════════════════════

def compute_quality_metrics(original: np.ndarray, upscaled: np.ndarray) -> dict:
    """Compute PSNR and SSIM between original (resized to match) and upscaled image.

    The original is upscaled with bicubic interpolation to the same size as the
    AI-upscaled image so both can be compared pixel-by-pixel.

    Returns dict with psnr_db, ssim, improvement_estimate.
    """
    if original is None or upscaled is None:
        return {"error": "missing input"}

    # Resize original to match upscaled dimensions using bicubic
    h, w = upscaled.shape[:2]
    original_resized = cv2.resize(original, (w, h), interpolation=cv2.INTER_CUBIC)

    # Convert to grayscale for SSIM (standard practice)
    gray_orig = cv2.cvtColor(original_resized, cv2.COLOR_BGR2GRAY).astype(np.float64)
    gray_upsc = cv2.cvtColor(upscaled, cv2.COLOR_BGR2GRAY).astype(np.float64)

    # PSNR
    mse = np.mean((gray_orig - gray_upsc) ** 2)
    if mse == 0:
        psnr = float("inf")
    else:
        psnr = 10.0 * np.log10((255.0 ** 2) / mse)

    # SSIM (simplified implementation — matches scikit-image for 8-bit grayscale)
    C1 = (0.01 * 255) ** 2
    C2 = (0.03 * 255) ** 2

    mu_x = cv2.GaussianBlur(gray_orig, (11, 11), 1.5)
    mu_y = cv2.GaussianBlur(gray_upsc, (11, 11), 1.5)

    sigma_x2 = cv2.GaussianBlur(gray_orig ** 2, (11, 11), 1.5) - mu_x ** 2
    sigma_y2 = cv2.GaussianBlur(gray_upsc ** 2, (11, 11), 1.5) - mu_y ** 2
    sigma_xy = cv2.GaussianBlur(gray_orig * gray_upsc, (11, 11), 1.5) - mu_x * mu_y

    ssim_map = ((2 * mu_x * mu_y + C1) * (2 * sigma_xy + C2)) / \
               ((mu_x ** 2 + mu_y ** 2 + C1) * (sigma_x2 + sigma_y2 + C2))
    ssim = float(np.mean(ssim_map))

    return {
        "psnr_db": round(psnr, 2) if psnr != float("inf") else 999.0,
        "ssim": round(ssim, 4),
        "mse": round(mse, 2),
    }


@app.post("/quality-metrics", tags=["Quality"])
async def quality_metrics_endpoint(
    request: Request,
    file: UploadFile = File(...),
):
    """Upload an image, upscale it, and return quality metrics (PSNR, SSIM)
    comparing bicubic-upscaled original vs AI-upscaled result.

    Configurable via ENABLE_QUALITY_METRICS env var."""
    _require_api_token(request)
    if not ENABLE_QUALITY_METRICS:
        raise HTTPException(status_code=403, detail="Quality metrics disabled (ENABLE_QUALITY_METRICS=false)")

    data = await file.read()
    if len(data) > MAX_UPLOAD_BYTES:
        raise HTTPException(status_code=413, detail="Image too large")

    arr = np.frombuffer(data, np.uint8)
    original = cv2.imdecode(arr, cv2.IMREAD_COLOR)
    if original is None:
        raise HTTPException(status_code=400, detail="Invalid image")
    if original.shape[0] * original.shape[1] > MAX_IMAGE_PIXELS:
        raise HTTPException(status_code=400, detail="Image dimensions too large")

    # Upscale
    try:
        upscaled = upscale_image_array(original)
    except ModelNotReadyError:
        raise HTTPException(status_code=503, detail="No model loaded")

    metrics = compute_quality_metrics(original, upscaled)
    return JSONResponse(metrics)


# ══════════════════════════════════════════════════════════════════════════════
# Feature: Film Grain Management
# ══════════════════════════════════════════════════════════════════════════════

def remove_grain(img: np.ndarray, strength: int = 5) -> np.ndarray:
    """Remove film grain / noise using non-local means denoising.

    Args:
        img: BGR uint8 image.
        strength: Filter strength (1-30). Higher = more denoising.
    """
    # fastNlMeansDenoisingColored is effective for grain removal
    return cv2.fastNlMeansDenoisingColored(img, None, strength, strength, 7, 21)


def add_grain(img: np.ndarray, intensity: float = 5.0) -> np.ndarray:
    """Add synthetic film grain (Gaussian noise) back to upscaled image.

    Args:
        img: BGR uint8 image.
        intensity: Standard deviation of Gaussian noise (0-50).
    """
    if intensity <= 0:
        return img
    noise = np.random.normal(0, intensity, img.shape).astype(np.float32)
    result = np.clip(img.astype(np.float32) + noise, 0, 255).astype(np.uint8)
    return result


@app.post("/process-grain", tags=["Grain"])
async def process_grain_endpoint(
    request: Request,
    file: UploadFile = File(...),
    action: str = Form("remove"),
    strength: int = Form(5),
    intensity: float = Form(0.0),
):
    """Film grain management: remove grain before upscaling or add grain after.

    Args:
        action: "remove" (denoise) or "add" (re-grain) or "both" (remove, upscale, re-add).
        strength: Denoise strength 1-30 (for remove/both).
        intensity: Grain noise intensity 0-50 (for add/both).

    Configurable via ENABLE_GRAIN_MANAGEMENT env var."""
    _require_api_token(request)
    if not ENABLE_GRAIN_MANAGEMENT:
        raise HTTPException(status_code=403, detail="Grain management disabled (ENABLE_GRAIN_MANAGEMENT=false)")

    data = await file.read()
    if len(data) > MAX_UPLOAD_BYTES:
        raise HTTPException(status_code=413, detail="Image too large")

    arr = np.frombuffer(data, np.uint8)
    img = cv2.imdecode(arr, cv2.IMREAD_COLOR)
    if img is None:
        raise HTTPException(status_code=400, detail="Invalid image")
    if img.shape[0] * img.shape[1] > MAX_IMAGE_PIXELS:
        raise HTTPException(status_code=400, detail="Image dimensions too large")

    strength = max(1, min(30, strength))
    intensity = max(0.0, min(50.0, intensity))

    if action == "remove":
        result = remove_grain(img, strength)
    elif action == "add":
        result = add_grain(img, intensity)
    elif action == "both":
        # Remove grain → upscale → re-add grain
        denoised = remove_grain(img, strength)
        try:
            upscaled = upscale_image_array(denoised)
        except ModelNotReadyError:
            raise HTTPException(status_code=503, detail="No model loaded")
        result = add_grain(upscaled, intensity) if intensity > 0 else upscaled
    else:
        raise HTTPException(status_code=400, detail="action must be 'remove', 'add', or 'both'")

    _, encoded = cv2.imencode(".png", result)
    return Response(content=encoded.tobytes(), media_type="image/png")


# ══════════════════════════════════════════════════════════════════════════════
# Feature: Face Enhancement (GFPGAN-style via ONNX)
# ══════════════════════════════════════════════════════════════════════════════

# Face detection + enhancement state
_face_cascade = None
_face_cascade_lock = threading.Lock()
_face_enhance_session = None
_face_enhance_lock = threading.Lock()


def _get_face_cascade():
    """Lazy-load OpenCV's Haar cascade for face detection (thread-safe)."""
    global _face_cascade
    if _face_cascade is None:
        with _face_cascade_lock:
            if _face_cascade is None:  # Double-check after lock
                cascade_path = cv2.data.haarcascades + "haarcascade_frontalface_default.xml"
                _face_cascade = cv2.CascadeClassifier(cascade_path)
    return _face_cascade


def detect_faces(img: np.ndarray, min_size: int = 48) -> list:
    """Detect faces in an image using Haar cascades.

    Returns list of (x, y, w, h) rectangles.
    """
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    cascade = _get_face_cascade()
    faces = cascade.detectMultiScale(gray, scaleFactor=1.1, minNeighbors=5, minSize=(min_size, min_size))
    return faces if len(faces) > 0 else []


def enhance_face_region(face_img: np.ndarray, strength: float = 0.7) -> np.ndarray:
    """Enhance a cropped face region.

    Uses bilateral filtering + detail enhancement for sharpening.
    If a GFPGAN ONNX model is loaded, uses neural enhancement instead.

    Args:
        face_img: BGR uint8 face crop.
        strength: Blend ratio between original (0.0) and enhanced (1.0).
    """
    # Copy session reference under lock; run inference outside to allow concurrency
    with _face_enhance_lock:
        session = _face_enhance_session

    if session is not None and ONNX_AVAILABLE:
        try:
            # GFPGAN ONNX inference: resize to 512x512, normalize, run, denormalize
            h, w = face_img.shape[:2]
            input_face = cv2.resize(face_img, (512, 512))
            input_face = input_face.astype(np.float32) / 255.0
            input_face = np.transpose(input_face, (2, 0, 1))[np.newaxis, ...]

            input_name = session.get_inputs()[0].name
            output_name = session.get_outputs()[0].name
            output = session.run([output_name], {input_name: input_face})[0]

            output = np.squeeze(output, axis=0)
            output = np.transpose(output, (1, 2, 0))
            output = np.clip(output * 255.0, 0, 255).astype(np.uint8)
            enhanced = cv2.resize(output, (w, h))
        except Exception as e:
            logger.warning(f"Face enhance ONNX inference failed, using fallback: {e}")
            enhanced = _enhance_face_fallback(face_img)
    else:
        enhanced = _enhance_face_fallback(face_img)

    # Blend original and enhanced based on strength
    blended = cv2.addWeighted(face_img, 1.0 - strength, enhanced, strength, 0)
    return blended


def _enhance_face_fallback(face_img: np.ndarray) -> np.ndarray:
    """Classical face enhancement: bilateral filter + unsharp mask."""
    # Bilateral filter preserves edges while smoothing skin
    smooth = cv2.bilateralFilter(face_img, 9, 75, 75)
    # Detail enhancement via unsharp mask
    gaussian = cv2.GaussianBlur(smooth, (0, 0), 3)
    enhanced = cv2.addWeighted(smooth, 1.5, gaussian, -0.5, 0)
    return enhanced


def enhance_faces_in_image(img: np.ndarray, strength: float = 0.7) -> tuple:
    """Detect and enhance all faces in an image.

    Returns (enhanced_image, face_count).
    """
    faces = detect_faces(img)
    if len(faces) == 0:
        return img, 0

    result = img.copy()
    for (x, y, w, h) in faces:
        # Expand bounding box by 20% for better context
        pad_x = int(w * 0.2)
        pad_y = int(h * 0.2)
        x1 = max(0, x - pad_x)
        y1 = max(0, y - pad_y)
        x2 = min(img.shape[1], x + w + pad_x)
        y2 = min(img.shape[0], y + h + pad_y)

        face_crop = result[y1:y2, x1:x2].copy()
        enhanced = enhance_face_region(face_crop, strength)

        # Create a soft mask for blending (avoid hard edges)
        mask = np.zeros((y2 - y1, x2 - x1), dtype=np.float32)
        cv2.ellipse(mask, ((x2 - x1) // 2, (y2 - y1) // 2),
                    ((x2 - x1) // 2, (y2 - y1) // 2), 0, 0, 360, 1.0, -1)
        mask = cv2.GaussianBlur(mask, (15, 15), 5)
        mask_3c = np.stack([mask] * 3, axis=-1)

        blended = (enhanced * mask_3c + result[y1:y2, x1:x2] * (1 - mask_3c)).astype(np.uint8)
        result[y1:y2, x1:x2] = blended

    return result, len(faces)


@app.post("/enhance-faces", tags=["Face Enhancement"])
async def enhance_faces_endpoint(
    request: Request,
    file: UploadFile = File(...),
    strength: float = Form(0.7),
):
    """Detect and enhance faces in an uploaded image.

    Uses GFPGAN ONNX model if available, otherwise falls back to classical
    bilateral filter + unsharp mask enhancement.

    Args:
        strength: Enhancement blend ratio (0.0 = no change, 1.0 = full enhancement).

    Configurable via ENABLE_FACE_ENHANCE and FACE_ENHANCE_STRENGTH env vars."""
    _require_api_token(request)
    if not ENABLE_FACE_ENHANCE:
        raise HTTPException(status_code=403, detail="Face enhancement disabled (ENABLE_FACE_ENHANCE=false)")

    data = await file.read()
    if len(data) > MAX_UPLOAD_BYTES:
        raise HTTPException(status_code=413, detail="Image too large")

    arr = np.frombuffer(data, np.uint8)
    img = cv2.imdecode(arr, cv2.IMREAD_COLOR)
    if img is None:
        raise HTTPException(status_code=400, detail="Invalid image")
    if img.shape[0] * img.shape[1] > MAX_IMAGE_PIXELS:
        raise HTTPException(status_code=400, detail="Image dimensions too large")

    strength = max(0.0, min(1.0, strength))
    result, face_count = enhance_faces_in_image(img, strength)

    _, encoded = cv2.imencode(".png", result)
    return Response(
        content=encoded.tobytes(),
        media_type="image/png",
        headers={"X-Faces-Detected": str(face_count)}
    )


@app.post("/models/upload-face-enhance", tags=["Face Enhancement"])
async def upload_face_enhance_model(request: Request, file: UploadFile = File(...)):
    """Upload a GFPGAN/CodeFormer ONNX model for face enhancement.

    The model must accept (1, 3, 512, 512) float32 input and produce
    (1, 3, 512, 512) float32 output.

    Configurable via ENABLE_FACE_ENHANCE env var."""
    _require_api_token(request)
    if not ENABLE_FACE_ENHANCE:
        raise HTTPException(status_code=403, detail="Face enhancement disabled")
    if not ONNX_AVAILABLE:
        raise HTTPException(status_code=503, detail="ONNX Runtime not available")

    global _face_enhance_session
    data = await file.read()
    if len(data) > MAX_MODEL_UPLOAD_BYTES:
        raise HTTPException(status_code=413, detail="Model file too large")

    model_dir = MODELS_DIR
    model_dir.mkdir(parents=True, exist_ok=True)
    model_path = model_dir / "face_enhance.onnx"

    # Validate ONNX model before saving
    with tempfile.NamedTemporaryFile(suffix=".onnx", delete=False) as tmp:
        tmp.write(data)
        tmp_path = tmp.name

    try:
        test_session = ort.InferenceSession(tmp_path, providers=["CPUExecutionProvider"])
        inp = test_session.get_inputs()[0]
        out = test_session.get_outputs()[0]
        # Validate shape: must be image-to-image
        if len(inp.shape) != 4 or inp.shape[1] != 3:
            raise ValueError(f"Expected input shape (N, 3, H, W), got {inp.shape}")
        if len(out.shape) != 4 or out.shape[1] != 3:
            raise ValueError(f"Expected output shape (N, 3, H, W), got {out.shape}")
        del test_session
    except ValueError as e:
        os.unlink(tmp_path)
        raise HTTPException(status_code=400, detail=f"Invalid ONNX model shape: {e}")
    except Exception:
        os.unlink(tmp_path)
        logger.warning("Face enhance ONNX validation failed", exc_info=True)
        raise HTTPException(status_code=400, detail="Invalid or corrupt ONNX model file")

    # Save validated model
    try:
        shutil.move(tmp_path, str(model_path))
    except Exception as e:
        if os.path.exists(tmp_path):
            os.unlink(tmp_path)
        raise HTTPException(status_code=500, detail="Failed to save model file")

    # Load into session
    with _face_enhance_lock:
        try:
            providers = ["CPUExecutionProvider"]
            if ONNX_AVAILABLE:
                avail = ort.get_available_providers()
                if "CUDAExecutionProvider" in avail and state.use_gpu:
                    providers.insert(0, "CUDAExecutionProvider")
            _face_enhance_session = ort.InferenceSession(str(model_path), providers=providers)
            logger.info(f"Face enhance model loaded: {model_path}")
        except Exception as e:
            _face_enhance_session = None
            if model_path.exists():
                os.unlink(str(model_path))
            logger.error(f"Failed to load face enhance model: {e}")
            raise HTTPException(status_code=500, detail="Failed to load model")

    return {"status": "ok", "message": "Face enhancement model loaded"}


# ══════════════════════════════════════════════════════════════════════════════
# Feature: Custom ONNX Model Upload + OpenModelDB import/convert (v1.8.3.8)
# ══════════════════════════════════════════════════════════════════════════════

def _ingest_onnx_bytes(data: bytes, model_name: str, scale: int, description: str) -> dict:
    """Validate, persist and register ONNX model bytes.

    Shared core of /models/upload, /models/import-from-catalog and the pth
    converter — one gate set, one sidecar format, one registry shape.
    Raises HTTPException on any validation failure.
    """
    if not re.match(r"^[a-zA-Z0-9_-]{1,64}$", model_name):
        raise HTTPException(status_code=400, detail="model_name must be alphanumeric with hyphens/underscores, max 64 chars")

    scale = max(1, min(8, scale))

    if len(data) > MAX_MODEL_UPLOAD_BYTES:
        raise HTTPException(status_code=413, detail=f"Model too large (max {MAX_MODEL_UPLOAD_BYTES // (1024*1024)} MB)")

    # Validate ONNX model
    with tempfile.NamedTemporaryFile(suffix=".onnx", delete=False) as tmp:
        tmp.write(data)
        tmp_path = tmp.name

    try:
        test_session = ort.InferenceSession(tmp_path, providers=["CPUExecutionProvider"])
        inp = test_session.get_inputs()[0]
        out = test_session.get_outputs()[0]

        # Must be image model: (N, C, H, W)
        if len(inp.shape) != 4:
            raise ValueError(f"Expected 4D input (N, C, H, W), got shape {inp.shape}")
        if len(out.shape) != 4:
            raise ValueError(f"Expected 4D output (N, C, H, W), got shape {out.shape}")

        input_channels = inp.shape[1]
        output_channels = out.shape[1]

        del test_session
    except ValueError as e:
        os.unlink(tmp_path)
        raise HTTPException(status_code=400, detail=f"Invalid ONNX model shape: {e}")
    except Exception:
        os.unlink(tmp_path)
        logger.warning("Custom model ONNX validation failed", exc_info=True)
        raise HTTPException(status_code=400, detail="Invalid or corrupt ONNX model file")

    # Save model (defense-in-depth path check, matches delete endpoint)
    model_dir = MODELS_DIR
    model_dir.mkdir(parents=True, exist_ok=True)
    model_filename = f"{model_name}.onnx"
    model_path = (model_dir / model_filename).resolve()
    if not str(model_path).startswith(str(model_dir.resolve())):
        os.unlink(tmp_path)
        raise HTTPException(status_code=400, detail="Invalid model name")

    try:
        shutil.move(tmp_path, str(model_path))
    except Exception:
        if os.path.exists(tmp_path):
            os.unlink(tmp_path)
        raise HTTPException(status_code=500, detail="Failed to save model file")

    # Persist a metadata sidecar so the registration survives restarts
    # (v1.8.3.7 — the file used to survive in the volume while the registry
    # entry silently vanished on every container restart).
    sidecar_path = model_dir / f"{model_name}.custom.json"
    try:
        with open(sidecar_path, "w", encoding="utf-8") as fh:
            json.dump({
                "model_name": model_name,
                "filename": model_filename,
                "scale": scale,
                "description": description or f"Custom uploaded model ({scale}x)",
                "input_channels": input_channels,
                "output_channels": output_channels,
            }, fh)
    except OSError:
        logger.warning(f"Could not write custom-model sidecar for {model_name} — model will not survive a restart", exc_info=True)

    # Register in AVAILABLE_MODELS (restored from the sidecar on restart)
    with _model_lock:
        AVAILABLE_MODELS[model_name] = {
            "name": model_name,
            "description": description or f"Custom uploaded model ({scale}x)",
            "url": "",  # No download URL for custom models
            "filename": model_filename,
            "type": "onnx",
            "scale": scale,
            "category": "super-resolution",
            "input_channels": input_channels,
            "available": True,
            "custom": True,
        }
    _invalidate_models_cache()

    logger.info(f"Custom model registered: {model_name} ({scale}x, {len(data) / (1024*1024):.1f} MB)")

    return {
        "status": "ok",
        "model_name": model_name,
        "scale": scale,
        "input_channels": input_channels,
        "output_channels": output_channels,
        "file_size_mb": round(len(data) / (1024 * 1024), 2),
        "path": str(model_path),
    }


# ── OpenModelDB import catalog (v1.8.3.8) ────────────────────────────────────
# The service can now import catalog models itself (dashboard + plugin both
# call this). Same security model as the plugin importer: catalog ids only,
# host allowlist, sha256 pin mandatory, size cap, validated ingest.

_IMPORT_CATALOG_URLS = (
    "https://kuschel-code.github.io/JellyfinUpscalerPlugin/models-import.json",
    "https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/site/models-import.json",
)
_IMPORT_ALLOWED_HOSTS = (
    "github.com",
    "raw.githubusercontent.com",
    "huggingface.co",
    "objectstorage.us-phoenix-1.oraclecloud.com",
)
_IMPORT_CATALOG_TTL = 6 * 3600
_import_catalog_cache: dict = {"data": None, "ts": 0.0}


def _import_host_allowed(url: str) -> bool:
    try:
        host = urllib.parse.urlparse(url).hostname or ""
    except ValueError:
        return False
    return any(host == h or host.endswith("." + h) for h in _IMPORT_ALLOWED_HOSTS)


def _fetch_import_catalog() -> dict | None:
    now = time.time()
    if _import_catalog_cache["data"] and now - _import_catalog_cache["ts"] < _IMPORT_CATALOG_TTL:
        return _import_catalog_cache["data"]
    for url in _IMPORT_CATALOG_URLS:
        try:
            r = httpx.get(url, timeout=30, follow_redirects=True)
            if r.status_code == 200:
                doc = r.json()
                if doc.get("direct_onnx"):
                    _import_catalog_cache["data"] = doc
                    _import_catalog_cache["ts"] = now
                    return doc
        except Exception as e:
            logger.warning(f"Import catalog fetch failed from {url}: {e}")
    return _import_catalog_cache["data"]  # possibly stale/None — caller surfaces the error


async def _fetch_import_catalog_async() -> dict | None:
    """Async wrapper: the sync httpx call would otherwise block the single
    uvicorn event loop for up to ~60s on a cache miss (review finding) —
    freezing every in-flight request incl. /health and /upscale-stream."""
    loop = asyncio.get_running_loop()
    return await loop.run_in_executor(None, _fetch_import_catalog)


def _import_gate(entry: dict, exts: tuple = (".onnx", ".zip")) -> str | None:
    """None if the entry passes all import gates, else a human-readable reason."""
    url = entry.get("download_url") or ""
    if not url.startswith("https://"):
        return "no https download url"
    if not _import_host_allowed(url):
        host = urllib.parse.urlparse(url).hostname or "?"
        return f"host not allowlisted ({host}) - download manually and use the upload form"
    path = urllib.parse.urlparse(url).path.lower()
    if not any(path.endswith(e) for e in exts):
        return f"not a direct {'/'.join(exts)} file"
    if not entry.get("sha256"):
        return "no sha256 pin in the catalog"
    if (entry.get("size_bytes") or 0) > MAX_MODEL_UPLOAD_BYTES:
        return f"exceeds the {MAX_MODEL_UPLOAD_BYTES // (1024*1024)} MB import limit"
    return None


def _to_import_model_name(catalog_id: str) -> str:
    """Catalog id -> omdb- namespaced model name (mirrors the plugin's ToModelName)."""
    cleaned = re.sub(r"[^a-z0-9-]+", "-", catalog_id.lower()).strip("-")
    return ("omdb-" + cleaned)[:64].rstrip("-")


def _catalog_scale(entry: dict) -> int:
    try:
        return max(1, min(8, int(entry.get("scale"))))
    except (TypeError, ValueError):
        return 2


def _extract_pinned_onnx_from_zip(data: bytes, sha256_pin: str) -> bytes:
    """v1.8.3.9 fix: OMDB pins the INNER .onnx file, not the zip container
    (live-verified against the AnimeJaNai release: ONE zip ships FIVE model
    variants, and the catalog's sha256/size describe exactly one member).
    Select the member whose sha256 matches the pin — the zip is just transport
    and stays unpinned. Decompression is hard-capped per member (zip-bomb
    guard — infolist sizes can lie)."""
    import zipfile as _zipfile
    import io as _io
    try:
        zf = _zipfile.ZipFile(_io.BytesIO(data))
    except _zipfile.BadZipFile:
        raise HTTPException(status_code=502, detail="Downloaded file is not a valid zip archive")
    pin = (sha256_pin or "").lower()
    candidates = [m for m in zf.infolist() if m.filename.lower().endswith(".onnx") and not m.is_dir()]
    if not candidates:
        raise HTTPException(status_code=502, detail="Zip contains no .onnx file")
    for m in candidates:
        with zf.open(m) as fh:
            content = fh.read(MAX_MODEL_UPLOAD_BYTES + 1)
        if len(content) > MAX_MODEL_UPLOAD_BYTES:
            continue
        if hashlib.sha256(content).hexdigest() == pin:
            return content
    raise HTTPException(status_code=502, detail=f"No .onnx inside the zip matches the catalog's sha256 pin ({len(candidates)} candidates) - the upstream release changed; the weekly catalog refresh will re-pin it if legitimate.")


async def _download_capped(url: str) -> bytes:
    """Download from an allowlisted URL with the size cap. Redirects are followed
    (GitHub releases redirect to objects.githubusercontent.com); safe because the
    payload is verified against the catalog pin afterwards and no secret is sent."""
    async with httpx.AsyncClient(follow_redirects=True, timeout=httpx.Timeout(570.0, connect=30.0)) as client:
        resp = await client.get(url)
        if resp.status_code != 200:
            raise HTTPException(status_code=502, detail=f"Download failed (HTTP {resp.status_code} from source)")
        data = resp.content
    if len(data) > MAX_MODEL_UPLOAD_BYTES:
        raise HTTPException(status_code=502, detail=f"Downloaded file exceeds the {MAX_MODEL_UPLOAD_BYTES // (1024*1024)} MB import limit")
    return data


async def _download_pinned(url: str, sha256_pin: str) -> bytes:
    """_download_capped + sha256 pin verification (for non-zip payloads, where
    the catalog pin describes the downloaded file itself)."""
    data = await _download_capped(url)
    digest = hashlib.sha256(data).hexdigest()
    if digest.lower() != (sha256_pin or "").lower():
        logger.warning(f"Catalog import rejected - sha256 mismatch (expected {sha256_pin}, got {digest})")
        raise HTTPException(status_code=502, detail="sha256 mismatch - the upstream file changed since the catalog was generated. Import refused; the weekly catalog refresh will re-pin it if the change is legitimate.")
    return data


def _converter_available() -> bool:
    """True when torch+spandrel are installed (the docker7-converter image)."""
    import importlib.util
    return importlib.util.find_spec("spandrel") is not None and importlib.util.find_spec("torch") is not None


def _convert_pth_bytes_to_onnx(pth_data: bytes) -> tuple:
    """Load a .pth/.safetensors via spandrel, export ONNX (opset 17, dynamic H/W)
    and verify the export against the torch output. Returns (onnx_bytes, scale,
    input_channels).

    SECURITY NOTE: loading .pth files can execute pickled code. This is why the
    converter (a) is an OPT-IN image, (b) auto-downloads only sha256-pinned files
    from allowlisted hosts, and (c) otherwise requires an admin to hand it a file
    they chose to trust. spandrel additionally restricts unpickling internally.
    """
    if not _converter_available():
        raise HTTPException(status_code=501, detail="Converter not available - this image ships without torch/spandrel. Use the kuscheltier/jellyfin-ai-upscaler:docker7-converter image to convert .pth models.")
    import torch
    import spandrel
    import numpy as _np

    with tempfile.NamedTemporaryFile(suffix=".pth", delete=False) as tmp:
        tmp.write(pth_data)
        tmp_path = tmp.name
    onnx_path = None
    try:
        desc = spandrel.ModelLoader().load_from_file(tmp_path)
        if not isinstance(desc, spandrel.ImageModelDescriptor):
            raise HTTPException(status_code=400, detail=f"Unsupported model type for conversion: {type(desc).__name__} (only single-image SR models)")
        model = desc.model.eval()
        in_ch = int(desc.input_channels)
        scale = int(desc.scale)
        dummy = torch.rand(1, in_ch, 64, 64)
        with tempfile.NamedTemporaryFile(suffix=".onnx", delete=False) as otmp:
            onnx_path = otmp.name
        # v1.8.3.9: torch >=2.9 switched torch.onnx.export to the dynamo
        # exporter by default, which requires onnxscript (live failure:
        # "No module named 'onnxscript'"). Pin the legacy TorchScript
        # exporter our dynamic_axes design targets; onnxscript is in the
        # converter image anyway as a fallback.
        torch.onnx.export(
            model, dummy, onnx_path, opset_version=17, dynamo=False,
            input_names=["input"], output_names=["output"],
            dynamic_axes={"input": {0: "batch", 2: "height", 3: "width"},
                          "output": {0: "batch", 2: "height", 3: "width"}})
        # Verify: the exported graph must reproduce the torch output
        sess = ort.InferenceSession(onnx_path, providers=["CPUExecutionProvider"])
        ort_out = sess.run(None, {sess.get_inputs()[0].name: dummy.numpy()})[0]
        with torch.no_grad():
            torch_out = model(dummy).numpy()
        max_diff = float(_np.abs(ort_out - torch_out).max())
        if max_diff > 1e-2:
            raise HTTPException(status_code=502, detail=f"Conversion verification failed (max output diff {max_diff:.4f}) - this architecture does not export cleanly")
        with open(onnx_path, "rb") as fh:
            onnx_bytes = fh.read()
        return onnx_bytes, scale, in_ch
    except HTTPException:
        raise
    except Exception as e:
        logger.warning("pth->onnx conversion failed", exc_info=True)
        raise HTTPException(status_code=400, detail=f"Conversion failed: {e}")
    finally:
        if os.path.exists(tmp_path):
            os.unlink(tmp_path)
        if onnx_path and os.path.exists(onnx_path):
            os.unlink(onnx_path)


@app.get("/models/import-catalog", tags=["Models"])
async def get_import_catalog():
    """OpenModelDB import catalog with per-entry import/convert eligibility.
    Data source: models-import.json (regenerated weekly by CI, cached 6h here).
    No token: the catalog is public data (same content as the website page);
    the import/convert ACTIONS below do require the token."""
    doc = await _fetch_import_catalog_async()
    if not doc:
        raise HTTPException(status_code=502, detail="Import catalog unavailable (could not fetch models-import.json)")

    def row(e: dict, kind: str) -> dict:
        exts = (".onnx", ".zip") if kind == "direct" else (".pth", ".pt", ".safetensors")
        reason = _import_gate(e, exts)
        return {
            "id": e.get("id"), "name": e.get("name"), "scale": e.get("scale"),
            "architecture": e.get("architecture"), "license": e.get("license") or "",
            "non_commercial": "NC" in (e.get("license") or "").upper(),
            "size_bytes": e.get("size_bytes") or 0, "omdb_url": e.get("omdb_url"),
            "kind": kind, "eligible": reason is None, "reason": reason,
            "model_name": _to_import_model_name(e.get("id") or ""),
        }

    return {
        "generated": doc.get("generated"),
        "converter_available": _converter_available(),
        "direct": [row(e, "direct") for e in doc.get("direct_onnx", [])],
        "convertible": [row(e, "convert") for e in doc.get("requires_conversion", [])],
    }


@app.post("/models/import-from-catalog", tags=["Models"])
async def import_model_from_catalog(request: Request, body: dict = Body(...)):
    """One-click import of a direct-ONNX catalog model: download from the pinned,
    allowlisted URL, verify sha256, unzip if needed, then validated ingest."""
    _require_api_token(request)
    if not ENABLE_MODEL_UPLOAD:
        raise HTTPException(status_code=403, detail="Model upload disabled (ENABLE_MODEL_UPLOAD=false)")
    model_id = (body.get("id") or "").strip()
    if not model_id:
        raise HTTPException(status_code=400, detail="id is required")
    doc = await _fetch_import_catalog_async()
    if not doc:
        raise HTTPException(status_code=502, detail="Import catalog unavailable")
    entry = next((e for e in doc.get("direct_onnx", []) if (e.get("id") or "").lower() == model_id.lower()), None)
    if entry is None:
        raise HTTPException(status_code=404, detail=f"'{model_id}' is not in the direct-ONNX import catalog")
    reason = _import_gate(entry)
    if reason:
        raise HTTPException(status_code=400, detail=f"Not importable: {reason}")

    # v1.8.3.9: for zips the catalog pin describes the INNER .onnx, not the
    # container - download capped, then select the pinned member.
    if urllib.parse.urlparse(entry["download_url"]).path.lower().endswith(".zip"):
        data = await _download_capped(entry["download_url"])
        data = _extract_pinned_onnx_from_zip(data, entry.get("sha256") or "")
    else:
        data = await _download_pinned(entry["download_url"], entry.get("sha256") or "")

    model_name = _to_import_model_name(entry.get("id") or "")
    desc = f"{entry.get('name')} (OpenModelDB import, license: {entry.get('license') or 'unclear'})"
    result = _ingest_onnx_bytes(data, model_name, _catalog_scale(entry), desc)
    return {**result, "imported_as": model_name,
            "license": entry.get("license"),
            "non_commercial": "NC" in (entry.get("license") or "").upper()}


@app.post("/models/convert-from-catalog", tags=["Models"])
async def convert_model_from_catalog(request: Request, body: dict = Body(...)):
    """Download a pth catalog model (allowlisted host + sha256 pin required),
    convert it to ONNX via spandrel and register it. 501 without the converter image."""
    _require_api_token(request)
    if not ENABLE_MODEL_UPLOAD:
        raise HTTPException(status_code=403, detail="Model upload disabled (ENABLE_MODEL_UPLOAD=false)")
    if not _converter_available():
        raise HTTPException(status_code=501, detail="Converter not available - use the docker7-converter image")
    model_id = (body.get("id") or "").strip()
    if not model_id:
        raise HTTPException(status_code=400, detail="id is required")
    doc = await _fetch_import_catalog_async()
    if not doc:
        raise HTTPException(status_code=502, detail="Import catalog unavailable")
    entry = next((e for e in doc.get("requires_conversion", []) if (e.get("id") or "").lower() == model_id.lower()), None)
    if entry is None:
        raise HTTPException(status_code=404, detail=f"'{model_id}' is not in the convertible catalog")
    reason = _import_gate(entry, exts=(".pth", ".pt", ".safetensors"))
    if reason:
        raise HTTPException(status_code=400, detail=f"Not auto-convertible: {reason}")

    pth_data = await _download_pinned(entry["download_url"], entry.get("sha256") or "")
    loop = asyncio.get_running_loop()
    onnx_bytes, scale, _in_ch = await loop.run_in_executor(None, _convert_pth_bytes_to_onnx, pth_data)

    model_name = _to_import_model_name(entry.get("id") or "")
    desc = f"{entry.get('name')} (OpenModelDB, converted pth->onnx, license: {entry.get('license') or 'unclear'})"
    result = _ingest_onnx_bytes(onnx_bytes, model_name, scale or _catalog_scale(entry), desc)
    return {**result, "imported_as": model_name, "converted": True,
            "license": entry.get("license"),
            "non_commercial": "NC" in (entry.get("license") or "").upper()}


# ── Async import/convert jobs (v1.8.3.11) ───────────────────────────────────
# Big community models (60+ MB pth on a CPU box) can exceed the plugin's proxy
# timeout chain on the synchronous endpoints. Same job pattern as
# /models/download-async: start, get a job id, poll. Honest PHASE reporting
# (downloading/extracting/converting/validating) instead of a fake percent.

_import_jobs: dict = {}
_import_jobs_guard = threading.Lock()


def _import_job_set(job_id: str, **kw):
    with _import_jobs_guard:
        job = _import_jobs.get(job_id)
        if job is not None:
            job.update(kw)


async def _run_import_job(job_id: str, kind: str, entry: dict):
    try:
        _import_job_set(job_id, status="downloading")
        is_zip = urllib.parse.urlparse(entry["download_url"]).path.lower().endswith(".zip")
        data = await _download_capped(entry["download_url"])
        model_name = _to_import_model_name(entry.get("id") or "")
        if kind == "convert":
            digest = hashlib.sha256(data).hexdigest()
            if digest.lower() != (entry.get("sha256") or "").lower():
                raise HTTPException(status_code=502, detail="sha256 mismatch - the upstream file changed since the catalog was generated. Import refused.")
            _import_job_set(job_id, status="converting")
            loop = asyncio.get_running_loop()
            onnx_bytes, scale, _in_ch = await loop.run_in_executor(None, _convert_pth_bytes_to_onnx, data)
            desc = f"{entry.get('name')} (OpenModelDB, converted pth->onnx, license: {entry.get('license') or 'unclear'})"
            _import_job_set(job_id, status="validating")
            result = _ingest_onnx_bytes(onnx_bytes, model_name, scale or _catalog_scale(entry), desc)
        else:
            if is_zip:
                _import_job_set(job_id, status="extracting")
                data = _extract_pinned_onnx_from_zip(data, entry.get("sha256") or "")
            else:
                digest = hashlib.sha256(data).hexdigest()
                if digest.lower() != (entry.get("sha256") or "").lower():
                    raise HTTPException(status_code=502, detail="sha256 mismatch - the upstream file changed since the catalog was generated. Import refused.")
            desc = f"{entry.get('name')} (OpenModelDB import, license: {entry.get('license') or 'unclear'})"
            _import_job_set(job_id, status="validating")
            result = _ingest_onnx_bytes(data, model_name, _catalog_scale(entry), desc)
        _import_job_set(job_id, status="completed", imported_as=model_name,
                        converted=(kind == "convert"),
                        license=entry.get("license"),
                        non_commercial="NC" in (entry.get("license") or "").upper(),
                        file_size_mb=result.get("file_size_mb"))
    except HTTPException as e:
        logger.warning(f"Async import job {job_id} failed: {e.detail}")
        _import_job_set(job_id, status="failed", error=str(e.detail))
    except Exception as e:
        logger.error(f"Async import job {job_id} failed: {e}", exc_info=True)
        _import_job_set(job_id, status="failed", error=str(e))


@app.post("/models/import-async", tags=["Models"])
async def import_model_async(request: Request, body: dict = Body(...)):
    """Start a catalog import/convert in the background and return a job id.
    Poll /models/import-status/{job_id}. Same gates as the synchronous endpoints."""
    _require_api_token(request)
    if not ENABLE_MODEL_UPLOAD:
        raise HTTPException(status_code=403, detail="Model upload disabled (ENABLE_MODEL_UPLOAD=false)")
    model_id = (body.get("id") or "").strip()
    if not model_id:
        raise HTTPException(status_code=400, detail="id is required")
    doc = await _fetch_import_catalog_async()
    if not doc:
        raise HTTPException(status_code=502, detail="Import catalog unavailable")
    entry = next((e for e in doc.get("direct_onnx", []) if (e.get("id") or "").lower() == model_id.lower()), None)
    kind = "direct"
    if entry is None:
        entry = next((e for e in doc.get("requires_conversion", []) if (e.get("id") or "").lower() == model_id.lower()), None)
        kind = "convert"
    if entry is None:
        raise HTTPException(status_code=404, detail=f"'{model_id}' is not in the import catalog")
    exts = (".onnx", ".zip") if kind == "direct" else (".pth", ".pt", ".safetensors")
    reason = _import_gate(entry, exts)
    if reason:
        raise HTTPException(status_code=400, detail=f"Not importable: {reason}")
    if kind == "convert" and not _converter_available():
        raise HTTPException(status_code=501, detail="Converter not available - use the docker7-converter image")

    job_id = uuid.uuid4().hex
    with _import_jobs_guard:
        _import_jobs[job_id] = {
            "job_id": job_id, "id": entry.get("id"), "kind": kind,
            "status": "queued", "error": None, "started_at": time.time(),
        }
    asyncio.create_task(_run_import_job(job_id, kind, dict(entry)))
    return {"status": "queued", "job_id": job_id, "kind": kind}


@app.get("/models/import-status/{job_id}", tags=["Models"])
async def import_status(job_id: str, request: Request = None):
    """Poll an async import job (queued|downloading|extracting|converting|validating|completed|failed)."""
    _require_api_token(request)
    with _import_jobs_guard:
        job = _import_jobs.get(job_id)
        if job is None:
            raise HTTPException(status_code=404, detail=f"No import job {job_id}")
        return dict(job)


@app.post("/models/convert-upload", tags=["Models"])
async def convert_uploaded_model(
    request: Request,
    file: UploadFile = File(...),
    model_name: str = Form(...),
    description: str = Form(""),
):
    """Convert an uploaded .pth/.safetensors to ONNX and register it. For models
    whose download host the importer cannot use (Google Drive, Mega, ...): download
    in your browser, hand the file to this endpoint. 501 without the converter image."""
    _require_api_token(request)
    if not ENABLE_MODEL_UPLOAD:
        raise HTTPException(status_code=403, detail="Model upload disabled (ENABLE_MODEL_UPLOAD=false)")
    if not _converter_available():
        raise HTTPException(status_code=501, detail="Converter not available - use the docker7-converter image")
    if not re.match(r"^[a-zA-Z0-9_-]{1,64}$", model_name):
        raise HTTPException(status_code=400, detail="model_name must be alphanumeric with hyphens/underscores, max 64 chars")
    pth_data = await file.read()
    if len(pth_data) > MAX_MODEL_UPLOAD_BYTES:
        raise HTTPException(status_code=413, detail=f"Model too large (max {MAX_MODEL_UPLOAD_BYTES // (1024*1024)} MB)")
    loop = asyncio.get_running_loop()
    onnx_bytes, scale, _in_ch = await loop.run_in_executor(None, _convert_pth_bytes_to_onnx, pth_data)
    result = _ingest_onnx_bytes(onnx_bytes, model_name, scale, description or f"Converted upload ({scale}x)")
    return {**result, "converted": True}


@app.post("/models/upload", tags=["Models"])
async def upload_custom_model(
    request: Request,
    file: UploadFile = File(...),
    model_name: str = Form(...),
    scale: int = Form(2),
    description: str = Form(""),
):
    """Upload a custom ONNX super-resolution model.

    The model is validated (input/output shape check), saved to MODELS_DIR
    (default /app/models, overridable via MODELS_DIR env var),
    and registered in AVAILABLE_MODELS so it appears in the model list.

    Args:
        model_name: Unique identifier (e.g. "my-custom-4x").
        scale: Upscaling factor this model produces (1-8).
        description: Optional human-readable description.

    Configurable via ENABLE_MODEL_UPLOAD and MAX_MODEL_UPLOAD_BYTES env vars."""
    _require_api_token(request)
    if not ENABLE_MODEL_UPLOAD:
        raise HTTPException(status_code=403, detail="Model upload disabled (ENABLE_MODEL_UPLOAD=false)")
    if not ONNX_AVAILABLE:
        raise HTTPException(status_code=503, detail="ONNX Runtime not available")

    # v1.8.3.8: gates + persistence live in _ingest_onnx_bytes (shared with the
    # catalog importer and the pth converter)
    data = await file.read()
    return _ingest_onnx_bytes(data, model_name, scale, description)


@app.delete("/models/upload/{model_name}", tags=["Models"])
async def delete_custom_model(request: Request, model_name: str):
    """Delete a custom-uploaded model.

    Only models marked as 'custom' can be deleted via this endpoint."""
    _require_api_token(request)
    if not ENABLE_MODEL_UPLOAD:
        raise HTTPException(status_code=403, detail="Model upload disabled")

    # All checks and mutations under lock to prevent TOCTOU races
    with _model_lock:
        if model_name not in AVAILABLE_MODELS:
            raise HTTPException(status_code=404, detail="Model not found")

        model_info = AVAILABLE_MODELS[model_name]
        if not model_info.get("custom"):
            raise HTTPException(status_code=403, detail="Cannot delete built-in models")

        # Path traversal protection: resolve and verify within models dir
        models_dir = MODELS_DIR.resolve()
        model_path = (models_dir / model_info.get("filename", f"{model_name}.onnx")).resolve()
        if not str(model_path).startswith(str(models_dir)):
            raise HTTPException(status_code=403, detail="Invalid model path")

        # Remove file + its persistence sidecar (v1.8.3.7)
        if model_path.exists() and model_path.is_file():
            os.unlink(str(model_path))
        sidecar = models_dir / f"{model_name}.custom.json"
        if sidecar.exists() and sidecar.is_file():
            os.unlink(str(sidecar))

        # Unregister and unload if active.
        # _model_lock is already held here — acquiring _models_registry_lock
        # inside it would create a nested-lock (ABBA deadlock) risk if any other
        # code path takes them in the opposite order.  Since _model_lock already
        # serialises all model state mutations, the dict pop is safe without the
        # registry lock.
        AVAILABLE_MODELS.pop(model_name, None)
        if state.current_model == model_name:
            state.current_model = None
            state.onnx_session = None
            logger.info(f"Unloaded active model: {model_name}")

    _invalidate_models_cache()
    logger.info(f"Custom model deleted: {model_name}")
    return {"status": "ok", "deleted": model_name}


# ══════════════════════════════════════════════════════════════════════════════
# Feature: Config endpoint for feature toggles
# ══════════════════════════════════════════════════════════════════════════════

@app.get("/features", tags=["Configuration"])
async def get_feature_status():
    """Return the current status of all configurable features."""
    # Read shared mutable state under locks to avoid races
    with _face_enhance_lock:
        face_model_loaded = _face_enhance_session is not None
    with _model_lock:
        custom_models = [k for k, v in AVAILABLE_MODELS.items() if v.get("custom")]
    return {
        "quality_metrics": {
            "enabled": ENABLE_QUALITY_METRICS,
        },
        "face_enhancement": {
            "enabled": ENABLE_FACE_ENHANCE,
            "strength": FACE_ENHANCE_STRENGTH,
            "onnx_model_loaded": face_model_loaded,
        },
        "grain_management": {
            "enabled": ENABLE_GRAIN_MANAGEMENT,
            "denoise_strength": GRAIN_DENOISE_STRENGTH,
            "readd_intensity": GRAIN_READD_INTENSITY,
        },
        "model_upload": {
            "enabled": ENABLE_MODEL_UPLOAD,
            "max_size_mb": MAX_MODEL_UPLOAD_BYTES // (1024 * 1024),
            "custom_models": custom_models,
        },
        "api_docs": {
            "enabled": ENABLE_API_DOCS,
            "docs_url": "/docs" if ENABLE_API_DOCS else None,
            "redoc_url": "/redoc" if ENABLE_API_DOCS else None,
        },
        "fp16_mixed_precision": {
            "enabled": state.use_fp16 if hasattr(state, 'use_fp16') else False,
            "setting": USE_FP16,
        },
        "scene_change_detection": {
            "threshold": SCENE_CHANGE_THRESHOLD,
        },
    }


# service_start_time is set in lifespan() — no deprecated on_event("startup") needed
