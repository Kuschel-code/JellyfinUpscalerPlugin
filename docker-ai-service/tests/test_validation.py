"""Tests for input validation — file size, model names, circuit breaker."""
import io
import os
import numpy as np
import pytest
from PIL import Image
from fastapi import HTTPException, Request
from starlette.datastructures import UploadFile
from unittest.mock import patch, AsyncMock, MagicMock


def _make_png(w=64, h=64) -> bytes:
    img = Image.fromarray(np.zeros((h, w, 3), dtype=np.uint8))
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


@pytest.mark.asyncio
async def test_upscale_rejects_oversized_file(client, monkeypatch):
    """/upscale must return 413 when file exceeds MAX_UPLOAD_BYTES."""
    from app import main as app_module
    monkeypatch.setattr(app_module, "MAX_UPLOAD_BYTES", 1024)
    monkeypatch.setattr(app_module.state, "cv_model", MagicMock())
    monkeypatch.setenv("API_TOKEN", "disable")
    failures_before = app_module.state.consecutive_failures
    slots_before = app_module._upscale_semaphore._value
    with patch.object(app_module, "upscale_image") as inference:
        # Call the actual endpoint with a streamed file: no Content-Length
        # middleware check can hide the endpoint's HTTPException handling.
        upload = UploadFile(filename="test.png", file=io.BytesIO(b"x" * 1025))
        try:
            with pytest.raises(HTTPException) as error:
                await app_module.upscale_endpoint(Request({"type": "http", "headers": []}), upload, 2)
        finally:
            await upload.close()
        assert error.value.status_code == 413
        assert "max 1024" in error.value.detail
        inference.assert_not_called()
    assert app_module.state.consecutive_failures == failures_before
    assert app_module._upscale_semaphore._value == slots_before
    assert app_module.state.processing_count == 0


@pytest.mark.parametrize("model_name", ["rife-v4.7", "gfpgan-v1.4", "tiny-yolov3"])
def test_non_upscaler_categories_cannot_load_as_upscalers(client, monkeypatch, model_name):
    from app import main
    monkeypatch.setenv("API_TOKEN", "disable")
    download = AsyncMock(return_value=True)
    load = AsyncMock(return_value=True)
    monkeypatch.setattr(main, "download_model", download)
    monkeypatch.setattr(main, "load_model", load)
    response = client.post("/models/load", data={"model_name": model_name})
    assert response.status_code == 422, response.text
    assert "not an upscaler" in response.json()["detail"]
    download.assert_not_awaited()
    load.assert_not_awaited()


def test_unknown_imported_upscaler_category_remains_loadable(client, monkeypatch):
    from app import main
    monkeypatch.setenv("API_TOKEN", "disable")
    monkeypatch.setitem(main.AVAILABLE_MODELS, "custom-sr", {
        "type": "onnx", "category": "custom-restoration", "available": True, "scale": 2,
    })
    monkeypatch.setattr(main, "download_model", AsyncMock(return_value=True))
    load = AsyncMock(return_value=True)
    monkeypatch.setattr(main, "load_model", load)
    response = client.post("/models/load", data={"model_name": "custom-sr"})
    assert response.status_code == 200, response.text
    load.assert_awaited_once_with("custom-sr")


@pytest.mark.parametrize("setting, skipped", [(None, True), ("true", True), ("", True), ("false", False)])
def test_tensorrt_requires_explicit_opt_in(client, monkeypatch, setting, skipped):
    from app import main
    if setting is None:
        monkeypatch.delenv("SKIP_TENSORRT", raising=False)
    else:
        monkeypatch.setenv("SKIP_TENSORRT", setting)
    assert main._skip_tensorrt() is skipped


def test_model_name_path_traversal_rejected(client):
    """Model name with ../ must be rejected before processing."""
    env = {k: v for k, v in os.environ.items() if k != "API_TOKEN"}
    with patch.dict(os.environ, env, clear=True):
        resp = client.post("/models/download", data={"model": "../../../etc/passwd"})
        assert resp.status_code in (400, 422), f"path traversal not rejected: {resp.status_code}"


def test_model_name_with_special_chars_rejected(client):
    """Model names with shell metacharacters must be rejected."""
    env = {k: v for k, v in os.environ.items() if k != "API_TOKEN"}
    with patch.dict(os.environ, env, clear=True):
        resp = client.post("/models/download", data={"model": "model; rm -rf /"})
        assert resp.status_code in (400, 422)


def test_health_never_returns_500(client):
    """Health endpoint must never return 500 regardless of internal state."""
    for _ in range(3):
        resp = client.get("/health")
        assert resp.status_code != 500, f"health returned 500: {resp.text}"


def test_upscale_no_file_returns_422(client):
    """/upscale without file attachment must return 422 (validation error)."""
    resp = client.post("/upscale", data={"scale": "2"})
    assert resp.status_code == 422


def test_config_max_concurrent_below_1_rejected(client):
    """max_concurrent=0 must be rejected by /config."""
    from app import main as app_module
    env = {k: v for k, v in os.environ.items() if k != "API_TOKEN"}
    with patch.dict(os.environ, env, clear=True):
        resp = client.post("/config", data={"max_concurrent": "0"})
        assert resp.status_code in (400, 403, 422)
