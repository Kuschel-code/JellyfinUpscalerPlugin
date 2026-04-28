"""Tests for the TensorRT engine cache helpers.

The helpers are pure-Python and don't touch ONNX Runtime or CUDA, so they
can be unit-tested without a GPU.
"""
from pathlib import Path
from unittest import mock

import pytest

from app.trt_engine_cache import (
    DEFAULT_CACHE_DIR_NAME,
    get_cache_dir,
    is_tensorrt_caching_enabled,
    trt_provider_options,
)


def test_get_cache_dir_creates_subdir(tmp_path: Path) -> None:
    cache = get_cache_dir(tmp_path)
    assert cache == tmp_path / DEFAULT_CACHE_DIR_NAME
    assert cache.is_dir()


def test_get_cache_dir_is_idempotent(tmp_path: Path) -> None:
    a = get_cache_dir(tmp_path)
    b = get_cache_dir(tmp_path)
    assert a == b
    assert a.is_dir()


def test_caching_disabled_when_skip_tensorrt_true(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("SKIP_TENSORRT", "true")
    assert is_tensorrt_caching_enabled() is False


def test_caching_enabled_by_default(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("SKIP_TENSORRT", raising=False)
    assert is_tensorrt_caching_enabled() is True


def test_provider_options_minimal_when_skipped(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("SKIP_TENSORRT", "true")
    opts = trt_provider_options(device_id=0, models_dir=tmp_path)
    assert opts == {"device_id": 0, "trt_max_workspace_size": 2 * 1024 * 1024 * 1024}
    assert "trt_engine_cache_enable" not in opts


def test_provider_options_minimal_when_no_models_dir(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("SKIP_TENSORRT", raising=False)
    opts = trt_provider_options(device_id=0, models_dir=None)
    assert opts.get("trt_engine_cache_enable") is None
    assert opts["device_id"] == 0


def test_provider_options_full_when_enabled(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("SKIP_TENSORRT", raising=False)
    opts = trt_provider_options(device_id=1, models_dir=tmp_path)
    assert opts["device_id"] == 1
    assert opts["trt_engine_cache_enable"] is True
    assert opts["trt_engine_cache_path"] == str(tmp_path / DEFAULT_CACHE_DIR_NAME)
    assert opts["trt_fp16_enable"] is True
    assert opts["trt_builder_optimization_level"] == 5
    assert opts["trt_max_workspace_size"] == 2 * 1024 * 1024 * 1024


def test_provider_options_overrides(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("SKIP_TENSORRT", raising=False)
    opts = trt_provider_options(
        device_id=0, models_dir=tmp_path,
        fp16=False, builder_optimization_level=3, max_workspace_bytes=1 << 30
    )
    assert opts["trt_fp16_enable"] is False
    assert opts["trt_builder_optimization_level"] == 3
    assert opts["trt_max_workspace_size"] == 1 << 30
