"""Shared pytest fixtures for AI service tests."""
import io
import sys
import pytest
import numpy as np
from PIL import Image
from unittest.mock import MagicMock, patch


def make_test_png(width: int = 64, height: int = 64) -> bytes:
    """Create a small valid PNG for testing."""
    img = Image.fromarray(np.random.randint(0, 255, (height, width, 3), dtype=np.uint8))
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


@pytest.fixture
def test_png():
    return make_test_png()


def _make_mock_cv2():
    mock = MagicMock()
    mock.imdecode.return_value = np.zeros((64, 64, 3), dtype=np.uint8)
    mock.imencode.return_value = (True, np.zeros(100, dtype=np.uint8))
    mock.resize.return_value = np.zeros((128, 128, 3), dtype=np.uint8)
    mock.IMREAD_COLOR = 1
    mock.INTER_CUBIC = 2
    return mock


@pytest.fixture
def client():
    """FastAPI TestClient with mocked heavy dependencies."""
    mock_cv2 = _make_mock_cv2()
    mock_onnx = MagicMock()
    mock_ncnn = MagicMock()
    mock_torch = MagicMock()

    # Remove cached imports to avoid stale state
    for mod in list(sys.modules.keys()):
        if mod in ("app.main", "app"):
            del sys.modules[mod]

    with patch.dict("sys.modules", {
        "cv2": mock_cv2,
        "onnxruntime": mock_onnx,
        "ncnn": mock_ncnn,
        "torch": mock_torch,
        "torchvision": MagicMock(),
    }):
        from starlette.testclient import TestClient
        from app import main as app_module
        # Ensure service starts cleanly with no model loaded
        app_module.state.onnx_session = None
        app_module.state.cv_model = None
        app_module.state.ncnn_upscaler = None
        app_module.state.current_model = None
        # Use context-manager form so the FastAPI lifespan runs, which
        # initialises _upscale_semaphore and _benchmark_lock.  Without this
        # those module-level values remain None and semaphore tests fail.
        with TestClient(app_module.app) as client:
            yield client
