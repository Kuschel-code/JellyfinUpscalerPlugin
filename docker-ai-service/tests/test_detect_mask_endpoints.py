"""End-to-end cover for the object-masking endpoints, driven by real ONNX models.

Everything else around this feature is static: unit tests on the decoder, a symtable
guard on global names. Neither actually CALLS the endpoints, which is how the first
version shipped with a loader that referenced a helper this service has never had -
imported fine, appeared in the OpenAPI schema, and would have raised NameError on
first use.

So these tests build genuine ONNX graphs - one per detector family - and drive
POST /models/load-detector and POST /detect-mask through the real app: real
onnxruntime, real OpenCV, real JPEG in and out. The graphs emit constant detections,
because what is under test is this service's plumbing and decoding, not a detector's
accuracy.
"""
import os
import sys
import tempfile

import numpy as np
import pytest

onnx = pytest.importorskip("onnx", reason="onnx is needed to synthesise detector models")
ort = pytest.importorskip("onnxruntime")
cv2 = pytest.importorskip("cv2")
from fastapi.testclient import TestClient  # noqa: E402
from onnx import TensorProto, helper  # noqa: E402

DOG = 16          # COCO index of "dog"
CAT = 15


def _const_graph(name, inputs, outputs):
    """A graph whose outputs are constants, with the inputs declared but unused.

    Unused inputs are legal ONNX and exactly what is wanted: the plumbing has to feed
    them correctly (the NMS family fails outright without its second input), while the
    detections stay fixed so the assertions can be precise.
    """
    nodes, value_infos = [], []
    for out_name, arr in outputs:
        nodes.append(helper.make_node(
            "Constant", [], [out_name],
            value=helper.make_tensor(
                out_name + "_v",
                TensorProto.FLOAT if arr.dtype == np.float32 else TensorProto.INT32,
                arr.shape, arr.flatten().tolist()),
        ))
        value_infos.append(helper.make_tensor_value_info(
            out_name,
            TensorProto.FLOAT if arr.dtype == np.float32 else TensorProto.INT32,
            arr.shape))
    graph = helper.make_graph(
        nodes, name,
        [helper.make_tensor_value_info(n, TensorProto.FLOAT, s) for n, s in inputs],
        value_infos,
    )
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 13)])
    model.ir_version = 9      # onnxruntime 1.x rejects newer IR versions
    onnx.checker.check_model(model)
    return model


def _single_head_model():
    """v8 shape: (1, 4+C, N) with N > 4+C, no objectness. One dog, centred."""
    n_anchors, n_classes = 200, 80
    out = np.zeros((1, 4 + n_classes, n_anchors), dtype=np.float32)
    out[0, 0, 0], out[0, 1, 0] = 320.0, 320.0        # cx, cy on a 640 net
    out[0, 2, 0], out[0, 3, 0] = 200.0, 200.0        # w, h
    out[0, 4 + DOG, 0] = 0.95
    return _const_graph("single", [("images", [1, 3, 640, 640])], [("output0", out)])


def _nms_head_model():
    """The ONNX YOLOv3 shape: two inputs, three outputs, boxes as (y1,x1,y2,x2)."""
    n = 10
    boxes = np.zeros((1, n, 4), dtype=np.float32)
    boxes[0, 3] = (100.0, 200.0, 300.0, 500.0)       # y1, x1, y2, x2 in SOURCE coords
    scores = np.zeros((1, 80, n), dtype=np.float32)
    scores[0, DOG, 3] = 0.9
    indices = np.array([[[0, DOG, 3]]], dtype=np.int32)
    return _const_graph(
        "nms3",
        [("input_1", [1, 3, 416, 416]), ("image_shape", [1, 2])],
        [("boxes", boxes), ("scores", scores), ("indices", indices)],
    )


@pytest.fixture
def service():
    """The real app: no mocked cv2, no mocked onnxruntime, auth off."""
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
            onnx.save(_single_head_model(), os.path.join(dirs["models"], "singledet.pb"))
            onnx.save(_nms_head_model(), os.path.join(dirs["models"], "nmsdet.pb"))
            with TestClient(app_main.app) as client:
                yield client
        finally:
            for k, v in old.items():
                if v is None:
                    os.environ.pop(k, None)
                else:
                    os.environ[k] = v
            for mod in ("app.main", "app"):
                sys.modules.pop(mod, None)


def _frame(w=640, h=480):
    img = np.full((h, w, 3), 255, dtype=np.uint8)
    return cv2.imencode(".jpg", img)[1].tobytes()


def _decode(body):
    return cv2.imdecode(np.frombuffer(body, np.uint8), cv2.IMREAD_COLOR)


# ── The failure that motivated this file ─────────────────────────────────

def test_loading_a_detector_actually_runs(service):
    # The version that shipped would have raised NameError right here.
    r = service.post("/models/load-detector", data={"model_name": "singledet", "input_size": 640})
    assert r.status_code == 200, r.text
    assert r.json()["style"] == "single"
    assert r.json()["input_size"] == 640, "the size must come from the model's own input shape"


def test_detect_mask_without_a_detector_is_a_clean_400(service):
    r = service.post("/detect-mask", content=_frame())
    assert r.status_code == 400
    assert "load-detector" in r.json()["detail"], "the error must say how to fix it"


# ── Single-head family, end to end ───────────────────────────────────────

def test_single_head_masks_the_detected_region(service):
    assert service.post("/models/load-detector",
                        data={"model_name": "singledet", "input_size": 640}).status_code == 200

    r = service.post("/detect-mask?classes=animals&mode=box&pad=0", content=_frame())

    assert r.status_code == 200, r.text
    assert r.headers["X-Detections"] == "1"
    out = _decode(r.content)
    # Model box is centred on a 640 net; the frame is 640x480, so it scales to the
    # frame centre. JPEG is lossy, hence a threshold rather than an equality.
    assert out[240, 320].mean() < 40, "the centre must be covered"
    assert out[5, 5].mean() > 200, "the corner must be untouched"


def test_class_filter_reaches_the_model_output(service):
    service.post("/models/load-detector", data={"model_name": "singledet", "input_size": 640})

    r = service.post("/detect-mask?classes=cat", content=_frame())

    assert r.status_code == 200
    assert r.headers["X-Detections"] == "0", "the only detection is a dog"
    assert _decode(r.content)[240, 320].mean() > 200, "nothing may be covered"


def test_unknown_class_is_rejected_rather_than_silently_masking_nothing(service):
    service.post("/models/load-detector", data={"model_name": "singledet", "input_size": 640})
    r = service.post("/detect-mask?classes=platypus", content=_frame())
    assert r.status_code == 400


# ── NMS-head family: the model discussion #11 actually named ─────────────

def test_nms_head_is_recognised_and_driven_with_its_second_input(service):
    # onnxruntime raises if a declared input is missing, so a 200 here proves the
    # image_shape input is really being fed - the exact thing the first version
    # would have got wrong.
    r = service.post("/models/load-detector", data={"model_name": "nmsdet", "input_size": 640})
    assert r.status_code == 200, r.text
    assert r.json()["style"] == "nms3"
    assert r.json()["input_size"] == 416, "the size comes from the model, not the request"

    r = service.post("/detect-mask?classes=dog&mode=box&pad=0", content=_frame())
    assert r.status_code == 200, r.text
    assert r.headers["X-Detections"] == "1"


def test_nms_path_honours_the_class_filter_too(service):
    # Mutation testing caught this gap: the class-filter test above only exercises the
    # single-head decoder, so gutting the filter in the NMS decoder left the suite
    # green. The two paths filter in separate code and both need covering.
    service.post("/models/load-detector", data={"model_name": "nmsdet", "input_size": 640})

    r = service.post("/detect-mask?classes=cat&mode=box&pad=0", content=_frame())

    assert r.status_code == 200
    assert r.headers["X-Detections"] == "0", "the only detection is a dog"
    assert _decode(r.content)[150, 450].mean() > 200, "nothing may be covered"


def test_nms_boxes_land_where_the_model_said_not_transposed(service):
    # The model reports (y1,x1,y2,x2) = (100,200,300,500), i.e. x 200..500, y 100..300.
    # Reading it as (x1,y1,x2,y2) would cover x 100..300, y 200..500 instead - which is
    # still a plausible-looking box, and wrong. These two probes tell them apart.
    service.post("/models/load-detector", data={"model_name": "nmsdet", "input_size": 640})

    out = _decode(service.post("/detect-mask?classes=dog&mode=box&pad=0",
                               content=_frame()).content)

    assert out[150, 450].mean() < 40, "inside the real box (x=450, y=150)"
    assert out[400, 250].mean() > 200, "inside the TRANSPOSED box only - must be untouched"


# ── Refusals happen at load time, not mid-playback ───────────────────────

def test_an_upscaler_is_refused_when_it_is_loaded_not_when_it_is_used(service, tmp_path):
    up = _const_graph("upscaler", [("input", [1, 3, 64, 64])],
                      [("output", np.zeros((1, 3, 256, 256), dtype=np.float32))])
    models_dir = service.app.state if False else None   # keep the fixture's dir authoritative
    from app import main as app_main
    onnx.save(up, str(app_main.MODELS_DIR / "faux-upscaler.pb"))

    r = service.post("/models/load-detector", data={"model_name": "faux-upscaler"})

    assert r.status_code == 400
    assert "not detections" in r.json()["detail"]


def test_a_missing_model_says_so_and_does_not_500(service):
    r = service.post("/models/load-detector", data={"model_name": "nope"})
    assert r.status_code == 404
    assert "upload" in r.json()["detail"].lower(), "it must point at the import path"
