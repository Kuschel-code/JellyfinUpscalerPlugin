"""Real-pixel cover for the ncnn backend (the docker7-vulkan image).

Nothing ever ran this path, and it shipped with three defects no test could see:

- It fed the network 0..255 BGR. The realsr-/realesrgan-ncnn-vulkan models expect RGB
  scaled to 0..1 and return 0..1 RGB: their reference code divides by 255 before the
  network and multiplies by 255 after it. On a real photo the old feed came out
  smeared, with colour fringes, worse than bicubic.
- It handed ncnn plain slices of the frame as tiles. Mat.from_pixels reads its buffer
  as packed rows and ignores numpy strides, so every frame larger than one tile
  (ONNX_TILE_SIZE, 512) was upscaled from the wrong pixels.
- "Downloading" an ncnn model answered "bundled, no download needed" for files that
  no image has ever contained, and the load then failed with a generic error.

These tests run real ncnn on the CPU with a tiny hand-written network instead of a
200 MB GAN. Each of its layers exposes one of those mistakes:

  Convolution 1x1  keeps R and G and zeroes RGB channel 2, which is blue.
                   Fed BGR, it zeroes red instead.
  Clip 0..1        what a 0..1 model does to values outside its range. Fed 0..255,
                   every pixel saturates.
  Interp x4        a nearest-neighbour upscale, so the expected output is exact.
"""
import os
import struct
import sys
import tempfile
import time

import numpy as np
import pytest

# Declared in requirements-test.txt: these tests assert on real pixels, so stubbing
# either module would let them pass while proving nothing.
ncnn = pytest.importorskip("ncnn")
cv2 = pytest.importorskip("cv2")
from fastapi.testclient import TestClient  # noqa: E402

MODEL_ID = "ncnn-realesrgan-x4"

_PARAM = """7767517
4 4
Input            data      0 1 data
Convolution      dropblue  1 1 data rg 0=3 1=1 5=0 6=9
Clip             clip01    1 1 rg rgc 0=0.0 1=1.0
Interp           up4       1 1 rgc output 0=1 1=4.0 2=4.0
"""

# Convolution weights, [out][in] for a 1x1 kernel: out0 = in0 (R), out1 = in1 (G),
# out2 = 0. ncnn reads a 4-byte flag before the weights; 0 means raw float32 follows.
_WEIGHTS = [1, 0, 0,
            0, 1, 0,
            0, 0, 0]


def _install_model(models_dir, model_id=MODEL_ID):
    with open(os.path.join(models_dir, f"{model_id}.param"), "w", encoding="ascii") as f:
        f.write(_PARAM)
    with open(os.path.join(models_dir, f"{model_id}.bin"), "wb") as f:
        f.write(struct.pack("<I", 0) + struct.pack("<9f", *_WEIGHTS))


def _expected(img):
    """What the model must return for a BGR frame: x4 nearest, blue dropped."""
    out = cv2.resize(img, None, fx=4, fy=4, interpolation=cv2.INTER_NEAREST)
    out[..., 0] = 0
    return out


@pytest.fixture
def service():
    """The real app with real OpenCV and real ncnn, auth off, empty model folder."""
    with tempfile.TemporaryDirectory() as tmp:
        dirs = {k: os.path.join(tmp, k) for k in ("models", "cache", "static", "config")}
        for d in dirs.values():
            os.makedirs(d)
        env = {
            "MODELS_DIR": dirs["models"], "CACHE_DIR": dirs["cache"],
            "STATIC_DIR": dirs["static"], "CONFIG_DIR": dirs["config"],
            "API_TOKEN": "disable",
        }
        old = {k: os.environ.get(k) for k in env}
        os.environ.update(env)
        for mod in ("app.main", "app"):
            sys.modules.pop(mod, None)
        try:
            from app import main as app_main
            assert app_main.NCNN_AVAILABLE, "ncnn is installed, so the backend must be on"
            with TestClient(app_main.app) as client:
                yield app_main, client, dirs["models"]
        finally:
            for k, v in old.items():
                if v is None:
                    os.environ.pop(k, None)
                else:
                    os.environ[k] = v
            for mod in ("app.main", "app"):
                sys.modules.pop(mod, None)


def _load(client, model_id=MODEL_ID):
    r = client.post("/models/load", data={"model_name": model_id, "use_gpu": "false"})
    assert r.status_code == 200, r.text
    assert r.json()["model_type"] == "ncnn"


def test_the_model_sees_rgb_in_0_1_and_the_frame_comes_back_as_bgr_0_255(service):
    app_main, client, models_dir = service
    _install_model(models_dir)
    _load(client)

    img = np.empty((24, 40, 3), np.uint8)
    img[:] = (40, 120, 200)                      # B, G, R
    img[5:9, 7:30] = (230, 15, 90)               # a block, so position matters too

    out = app_main.upscale_with_ncnn(img)

    assert out.shape == (96, 160, 3) and out.dtype == np.uint8
    # Blue is gone (the model dropped RGB channel 2); red and green came back intact.
    # Fed BGR, red would be gone. Fed 0..255, Clip would have flattened everything.
    assert np.abs(out.astype(int) - _expected(img)).max() <= 1


def test_frames_larger_than_one_tile_are_upscaled_from_their_own_pixels(service):
    app_main, client, models_dir = service
    _install_model(models_dir)
    _load(client)

    tile = app_main.ONNX_TILE_SIZE
    rng = np.random.default_rng(7)
    img = rng.integers(0, 256, (tile + 64, tile + 48, 3), dtype=np.uint8)   # 2x2 tiles

    out = app_main.upscale_with_ncnn(img)

    assert out.shape == (img.shape[0] * 4, img.shape[1] * 4, 3)
    assert np.abs(out.astype(int) - _expected(img)).max() <= 1


def test_an_ncnn_model_without_its_files_says_which_files_belong_where(service):
    app_main, client, models_dir = service
    names = (f"{MODEL_ID}.param", f"{MODEL_ID}.bin")

    for path in ("/models/download", "/models/load"):
        r = client.post(path, data={"model_name": MODEL_ID})
        assert r.status_code == 409, (path, r.text)
        detail = r.json()["detail"]
        assert all(n in detail for n in names) and models_dir in detail, detail

    # The plugin's download button uses the background job; it must fail the same way
    # instead of reporting the "bundled, no download needed" success it used to.
    job = _download_job(client)
    assert job["status"] == "failed", job
    assert all(n in job["error"] for n in names), job

    listed = {m["id"]: m for m in client.get("/models").json()["models"]}
    assert listed[MODEL_ID]["downloaded"] is False

    _install_model(models_dir)
    app_main._invalidate_models_cache()

    assert client.post("/models/download", data={"model_name": MODEL_ID}).status_code == 200
    r = client.post("/models/download-async", data={"model_name": MODEL_ID})
    assert r.json().get("already_present") is True, r.text
    listed = {m["id"]: m for m in client.get("/models").json()["models"]}
    assert listed[MODEL_ID]["downloaded"] is True


def _download_job(client, timeout=10.0):
    """Start /models/download-async and poll it to its end state."""
    r = client.post("/models/download-async", data={"model_name": MODEL_ID})
    assert r.status_code == 200, r.text
    job_id = r.json()["job_id"]
    assert job_id, r.json()
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        job = client.get(f"/models/download-status/{job_id}").json()
        if job["status"] in ("completed", "failed"):
            return job
        time.sleep(0.05)
    raise AssertionError(f"download job {job_id} did not finish: {job}")
