"""Tests for API token enforcement — backward-compatible when API_TOKEN not set."""
import io
import os
import numpy as np
from PIL import Image
from unittest.mock import patch


def _make_png() -> bytes:
    img = Image.fromarray(np.zeros((64, 64, 3), dtype=np.uint8))
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def test_health_always_public_no_token_needed(client):
    """Health endpoint must never require a token."""
    with patch.dict(os.environ, {"API_TOKEN": "secret"}):
        resp = client.get("/health")
        assert resp.status_code == 200


def test_wrong_token_returns_403_on_models_download(client):
    """Protected endpoint with wrong token must return 403."""
    with patch.dict(os.environ, {"API_TOKEN": "correct_token"}):
        resp = client.post(
            "/models/download",
            data={"model": "realesrgan-x4"},
            headers={"x-api-token": "wrong_token"},
        )
        assert resp.status_code == 403, f"expected 403, got {resp.status_code}: {resp.text}"


def test_missing_token_returns_403_when_env_set(client):
    """No token header when API_TOKEN is configured must return 403."""
    with patch.dict(os.environ, {"API_TOKEN": "secret123"}):
        resp = client.post("/models/download", data={"model": "realesrgan-x4"})
        assert resp.status_code == 403


def test_correct_token_passes_auth(client):
    """Correct token must not produce 403."""
    with patch.dict(os.environ, {"API_TOKEN": "secret123"}):
        resp = client.post(
            "/models/download",
            data={"model": "realesrgan-x4"},
            headers={"x-api-token": "secret123"},
        )
        assert resp.status_code != 403, f"correct token should not 403, got {resp.status_code}"


def test_no_token_env_skips_auth_check(client):
    """When API_TOKEN env var absent, all endpoints skip auth (backward compat)."""
    env = {k: v for k, v in os.environ.items() if k != "API_TOKEN"}
    with patch.dict(os.environ, env, clear=True):
        resp = client.post("/models/download", data={"model": "realesrgan-x4"})
        # Should not be 403; may be 400/422 for other reasons but not auth
        assert resp.status_code != 403


def test_upscale_without_model_returns_400_not_403(client):
    """/upscale without loaded model returns 400, not 403 (when no API_TOKEN set)."""
    env = {k: v for k, v in os.environ.items() if k != "API_TOKEN"}
    with patch.dict(os.environ, env, clear=True):
        png = _make_png()
        resp = client.post(
            "/upscale",
            files={"file": ("test.png", io.BytesIO(png), "image/png")},
            data={"scale": "2"},
        )
        assert resp.status_code == 400
        assert "model" in resp.json()["detail"].lower()
