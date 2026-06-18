"""Tests for the architecture-adaptive frame-interpolation engine (v1.8.2).

The interpolation inference (interpolate_frame_rife) drives multiple architectures
by inspecting the ONNX session's input signature. Adding IFRNet (3-input incl.
timestep) and CAIN (2x3ch inputs, no timestep) as a second architecture family
means the feed-dict dispatch must pick the right tensors — that's what these guard.

cv2 is mocked by the `client` fixture, so we patch cvtColor to a shape-preserving
passthrough (BGR<->RGB doesn't change shape) to let the real numpy math run.
"""
import numpy as np


class _FakeInput:
    def __init__(self, name, shape):
        self.name = name
        self.shape = shape


class _FakeOutput:
    def __init__(self, name):
        self.name = name


class _FakeSession:
    """Records the feed dict so we can assert which tensors were fed."""
    def __init__(self, inputs):
        self._inputs = inputs
        self.captured = None

    def get_inputs(self):
        return self._inputs

    def get_outputs(self):
        return [_FakeOutput("out")]

    def run(self, output_names, feed):
        self.captured = feed
        arr = next(iter(feed.values()))
        h, w = arr.shape[-2], arr.shape[-1]
        return [np.zeros((1, 3, h, w), dtype=np.float32)]


def _patch_cv2(main):
    # passthrough colour conversion (shape-preserving) so numpy ops run for real
    main.cv2.cvtColor = lambda a, code=None: a


def _frames():
    return (np.zeros((64, 64, 3), np.uint8), np.full((64, 64, 3), 255, np.uint8))


# ------------------------------------------------------------------- catalog

def test_catalog_has_second_interpolation_architectures(client):
    from app import main
    for key in ("ifrnet", "cain"):
        assert key in main.AVAILABLE_MODELS, f"{key} missing from catalog"
        assert main.AVAILABLE_MODELS[key]["category"] == "interpolation"
        assert main.AVAILABLE_MODELS[key]["arch"] == key
        # experimental / self-host so we never auto-download a 404
        assert main.AVAILABLE_MODELS[key].get("self_host") is True
        assert main.AVAILABLE_MODELS[key].get("available") is False


def test_rife_still_present_and_distinct_arch(client):
    from app import main
    assert main.AVAILABLE_MODELS["rife-v4.9"]["arch"] == "rife"


# --------------------------------------------------------------- dispatch

def test_cain_two_3ch_inputs_fed_separately(client):
    """CAIN: two 3-channel inputs -> feed img0, img1 separately (NOT a 6ch combined)."""
    from app import main
    _patch_cv2(main)
    f1, f2 = _frames()
    sess = _FakeSession([_FakeInput("img0", [1, 3, 64, 64]), _FakeInput("img1", [1, 3, 64, 64])])
    out = main.interpolate_frame_rife(f1, f2, sess, 0.5)
    assert out.shape == (64, 64, 3)
    assert len(sess.captured) == 2
    for v in sess.captured.values():
        assert v.shape[1] == 3, "CAIN inputs must be separate 3-channel frames"


def test_rife_two_input_uses_combined_plus_timestep(client):
    """RIFE 2-input: first input is 6ch concatenated frames + a timestep tensor."""
    from app import main
    _patch_cv2(main)
    f1, f2 = _frames()
    sess = _FakeSession([_FakeInput("x", [1, 6, 64, 64]), _FakeInput("ts", [1, 1, 64, 64])])
    main.interpolate_frame_rife(f1, f2, sess, 0.5)
    vals = list(sess.captured.values())
    assert vals[0].shape[1] == 6, "RIFE first input must be the 6ch combined frames"
    assert vals[1].shape[1] == 1, "second input must be the timestep tensor"


def test_ifrnet_three_inputs_img0_img1_timestep(client):
    """IFRNet: 3 inputs -> img0 (3ch), img1 (3ch), timestep (1ch)."""
    from app import main
    _patch_cv2(main)
    f1, f2 = _frames()
    sess = _FakeSession([
        _FakeInput("img0", [1, 3, 64, 64]),
        _FakeInput("img1", [1, 3, 64, 64]),
        _FakeInput("embt", [1, 1, 64, 64]),
    ])
    main.interpolate_frame_rife(f1, f2, sess, 0.5)
    vals = list(sess.captured.values())
    assert len(vals) == 3
    assert vals[0].shape[1] == 3 and vals[1].shape[1] == 3
    assert vals[2].shape[1] == 1, "third input must be the timestep tensor"
