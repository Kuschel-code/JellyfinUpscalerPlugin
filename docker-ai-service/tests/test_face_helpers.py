"""Face-restore helper invariants (v1.8.3.5 hardening).

_feathered_mask is pure numpy, so its math runs for real; restore_faces_in_frame
is exercised on its no-model early-return path (the guard that keeps face
restore a strict no-op when no ONNX session is loaded). The `client` fixture is
required for both: importing app.main needs the mocked cv2/onnxruntime modules.
"""
import numpy as np


def test_feathered_mask_shape(client):
    from app import main
    mask = main._feathered_mask(64, 48)
    assert mask.shape == (64, 48, 1)
    assert mask.dtype == np.float32


def test_feathered_mask_center_is_one(client):
    from app import main
    mask = main._feathered_mask(64, 64)
    assert mask[32, 32, 0] == 1.0


def test_feathered_mask_corners_below_center(client):
    from app import main
    mask = main._feathered_mask(64, 64)
    center = mask[32, 32, 0]
    for corner in (mask[0, 0, 0], mask[0, -1, 0], mask[-1, 0, 0], mask[-1, -1, 0]):
        assert corner < center


def test_feathered_mask_values_in_unit_range(client):
    from app import main
    # Also cover the tiny-crop edge case where feather >= half the side length.
    for h, w in ((64, 64), (8, 8), (5, 96)):
        mask = main._feathered_mask(h, w)
        assert float(mask.min()) >= 0.0
        assert float(mask.max()) <= 1.0


def test_restore_faces_without_model_is_a_noop(client):
    from app import main
    main.state.face_restore_loaded = False
    main.state.face_restore_session = None
    img = np.random.randint(0, 255, size=(120, 160, 3), dtype=np.uint8)
    before = img.copy()
    out, count = main.restore_faces_in_frame(img)
    assert count == 0
    assert out is img          # early return hands back the same object
    assert np.array_equal(img, before)  # and never mutated it
