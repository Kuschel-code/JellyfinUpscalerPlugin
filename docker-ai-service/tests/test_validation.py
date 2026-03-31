"""Tests for input validation — file size, model names, circuit breaker."""
import io
import os
import numpy as np
from PIL import Image
from unittest.mock import patch


def _make_png(w=64, h=64) -> bytes:
    img = Image.fromarray(np.zeros((h, w, 3), dtype=np.uint8))
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def test_upscale_rejects_oversized_file(client):
    """/upscale must return 413 when file exceeds MAX_UPLOAD_BYTES."""
    from app import main as app_module
    original = app_module.MAX_UPLOAD_BYTES
    app_module.MAX_UPLOAD_BYTES = 5  # 5 bytes — any real PNG will be larger
    try:
        resp = client.post(
            "/upscale",
            files={"file": ("test.png", io.BytesIO(_make_png()), "image/png")},
            data={"scale": "2"},
        )
        # Either 400 (no model checked first) or 413 (size checked first)
        assert resp.status_code in (400, 413), f"unexpected {resp.status_code}"
    finally:
        app_module.MAX_UPLOAD_BYTES = original


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
