"""TensorRT engine cache configuration helpers.

ONNX Runtime's TensorrtExecutionProvider can serialize the compiled
TensorRT engine to disk and reuse it on subsequent loads. This is
controlled via provider_options:

  trt_engine_cache_enable     bool  enable caching at all
  trt_engine_cache_path       str   directory for .engine files
  trt_engine_cache_prefix     str   optional filename prefix
  trt_fp16_enable             bool  FP16 inference (2x speedup on Ampere+)
  trt_builder_optimization_level int  0..5, higher = longer build, faster runtime
  trt_max_workspace_size      int   bytes available to TRT during build

Without caching, every container restart recompiles each model's engine —
1 to 5 minutes per model on RTX 3060 / 4070 class hardware. With caching
warm, the second load is <1 second.

Engines are GPU-architecture-specific. ORT computes a cache key from
(onnx_path, GPU compute capability, ORT version, TRT version) so a cache
written on RTX 3090 is automatically rebuilt when the container is
moved to RTX 5090.

The cache directory must be on a persistent volume — the typical Docker
setup mounts /app/models and we use /app/models/.trt-engine-cache for
that reason.
"""

from __future__ import annotations

import os
from pathlib import Path
from typing import Optional


DEFAULT_CACHE_DIR_NAME = ".trt-engine-cache"
"""Lives next to the ONNX models so it shares the same persistent volume."""


def get_cache_dir(models_dir: Path) -> Path:
    """Return the engine-cache dir, creating it if missing.

    Idempotent. Caller is responsible for the parent ``models_dir`` existing
    (it always does at runtime — that's where ONNX files are stored).
    """
    cache = models_dir / DEFAULT_CACHE_DIR_NAME
    cache.mkdir(parents=True, exist_ok=True)
    return cache


def is_tensorrt_caching_enabled() -> bool:
    """SKIP_TENSORRT=true takes precedence; otherwise default-on for caching.

    Note: this only controls whether *caching* is requested. ORT itself still
    decides whether to use TensorRT at all based on provider availability.
    """
    return os.getenv("SKIP_TENSORRT", "false").lower() != "true"


def trt_provider_options(device_id: int,
                          models_dir: Optional[Path] = None,
                          fp16: bool = True,
                          builder_optimization_level: int = 5,
                          max_workspace_bytes: int = 2 * 1024 * 1024 * 1024) -> dict:
    """Build the TensorrtExecutionProvider options dict.

    When caching is disabled (SKIP_TENSORRT=true or models_dir is None) the
    returned dict only carries the base options (device_id, max_workspace).
    """
    opts: dict = {
        "device_id": int(device_id),
        "trt_max_workspace_size": int(max_workspace_bytes),
    }
    if not is_tensorrt_caching_enabled():
        return opts
    if models_dir is None:
        return opts

    cache_dir = get_cache_dir(Path(models_dir))
    opts.update({
        "trt_engine_cache_enable": True,
        "trt_engine_cache_path": str(cache_dir),
        "trt_fp16_enable": bool(fp16),
        "trt_builder_optimization_level": int(builder_optimization_level),
    })
    return opts
