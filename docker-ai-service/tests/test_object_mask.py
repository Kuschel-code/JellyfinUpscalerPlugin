"""Tests for the object detection + masking added in v1.8.3.23 (discussion #11).

The decoder is the risky part. A detection tensor misread does not crash — it draws
boxes in plausible-looking wrong places, which is far harder to notice than an
exception and exactly the outcome a user would report as "it just doesn't work".
So the two output layouts are tested against synthetic tensors with known answers.
"""
import numpy as np
import pytest

from app import object_mask as om


# ── Class mapping ────────────────────────────────────────────────────────

def test_animals_group_expands_to_the_animal_classes():
    idx = om.class_indices(["animals"])
    assert om.COCO_CLASSES.index("dog") in idx
    assert om.COCO_CLASSES.index("cat") in idx
    assert om.COCO_CLASSES.index("person") not in idx, "a person is not an animal here"


def test_named_classes_resolve_case_insensitively():
    assert om.class_indices(["Dog", " cat "]) == {
        om.COCO_CLASSES.index("dog"), om.COCO_CLASSES.index("cat")
    }


def test_unknown_class_is_dropped_not_guessed(caplog):
    # Guessing "dogg" -> "dog" would silently mask the wrong thing; the user gets a
    # log line and the classes that did resolve.
    idx = om.class_indices(["dog", "dogg"])
    assert idx == {om.COCO_CLASSES.index("dog")}


# ── Decoder: the two layouts every export uses ───────────────────────────

def _v5_row(cx, cy, w, h, objectness, cls, n_classes=80, score=0.9):
    row = np.zeros(5 + n_classes, dtype=np.float32)
    row[:4] = (cx, cy, w, h)
    row[4] = objectness
    row[5 + cls] = score
    return row


def _v5_output(*rows, n_classes=80, anchors=200):
    """A v5-shaped tensor: (1, N, 5+C) with N > 5+C.

    The padding is not decoration. A real head emits thousands of anchors against a
    few dozen features, and the decoder infers orientation from which axis is larger
    - so a two-row synthetic tensor is shaped like a v8 output and gets read as one.
    The first version of these tests hit exactly that and failed, which is the
    decoder telling the truth about an input no model produces.
    """
    filler = np.zeros(5 + n_classes, dtype=np.float32)
    stack = list(rows) + [filler] * max(0, anchors - len(rows))
    return np.stack(stack)[np.newaxis, ...]


def test_decodes_the_v3_v5_layout_with_objectness():
    dog = om.COCO_CLASSES.index("dog")
    # 640-net, box centred at (320,320) 100x100 -> exactly the middle of the source
    out = _v5_output(
        _v5_row(320, 320, 100, 100, objectness=0.95, cls=dog),
        _v5_row(10, 10, 4, 4, objectness=0.01, cls=dog),      # below threshold
    )

    dets = om.decode_detections(out, src_w=640, src_h=640, net_size=640,
                                wanted={dog})

    assert len(dets) == 1, "the low-confidence row must be dropped"
    cls, score, (x1, y1, x2, y2) = dets[0]
    assert cls == dog
    assert score > 0.8
    assert (x1, y1, x2, y2) == (270, 270, 370, 370)


def test_decodes_the_v8_layout_without_objectness():
    cat = om.COCO_CLASSES.index("cat")
    # v8 emits (4+C, N): transposed relative to v5, and no objectness column.
    n_classes = 80
    col = np.zeros(4 + n_classes, dtype=np.float32)
    col[:4] = (100, 100, 50, 50)
    col[4 + cat] = 0.9
    filler = np.zeros(4 + n_classes, dtype=np.float32)
    out = np.stack([col] + [filler] * 200, axis=1)[np.newaxis, ...]   # (1, 84, 201)

    dets = om.decode_detections(out, src_w=640, src_h=640, net_size=640, wanted={cat})

    assert len(dets) == 1
    assert dets[0][0] == cat


def test_boxes_are_scaled_back_to_the_source_resolution():
    # The model sees a 640x640 square; the frame is 1920x1080. A box at the model's
    # centre must land at the frame's centre, not at (320,320).
    dog = om.COCO_CLASSES.index("dog")
    out = _v5_output(_v5_row(320, 320, 64, 64, 0.95, dog))

    dets = om.decode_detections(out, src_w=1920, src_h=1080, net_size=640, wanted={dog})

    _cls, _score, (x1, y1, x2, y2) = dets[0]
    cx, cy = (x1 + x2) / 2, (y1 + y2) / 2
    assert abs(cx - 960) < 2, "x must scale by 1920/640"
    assert abs(cy - 540) < 2, "y must scale by 1080/640"


def test_wanted_filter_excludes_other_classes():
    person = om.COCO_CLASSES.index("person")
    dog = om.COCO_CLASSES.index("dog")
    out = _v5_output(
        _v5_row(100, 100, 50, 50, 0.95, person),
        _v5_row(300, 300, 50, 50, 0.95, dog),
    )

    dets = om.decode_detections(out, src_w=640, src_h=640, net_size=640, wanted={dog})

    assert len(dets) == 1 and dets[0][0] == dog, "only requested classes may be masked"


def test_overlapping_detections_collapse_to_one():
    dog = om.COCO_CLASSES.index("dog")
    out = _v5_output(
        _v5_row(300, 300, 100, 100, 0.95, dog),
        _v5_row(305, 305, 100, 100, 0.90, dog),   # essentially the same animal
    )

    dets = om.decode_detections(out, src_w=640, src_h=640, net_size=640, wanted={dog})

    assert len(dets) == 1, "NMS must collapse duplicate boxes"


def test_an_unreadable_tensor_raises_instead_of_inventing_boxes():
    # A wrong guess here paints rectangles over the wrong part of the picture, which
    # reads as a broken feature rather than an unsupported model.
    with pytest.raises(ValueError):
        om.decode_detections(np.zeros((1, 2, 2, 2), dtype=np.float32), 640, 640, 640)
    with pytest.raises(ValueError):
        om.decode_detections(np.zeros((1, 10, 3), dtype=np.float32), 640, 640, 640)


# ── Masking ──────────────────────────────────────────────────────────────

def _frame(colour=(255, 255, 255)):
    img = np.zeros((480, 640, 3), dtype=np.uint8)
    img[:, :] = colour
    return img


def test_box_mode_fills_the_region_and_leaves_the_rest_alone():
    img = _frame()
    dets = [(om.COCO_CLASSES.index("dog"), 0.9, (200, 150, 400, 350))]

    out = om.apply_masks(img, dets, mode="box", colour=(0, 0, 0), pad=0)

    assert out[250, 300].tolist() == [0, 0, 0], "inside the box must be filled"
    assert out[10, 10].tolist() == [255, 255, 255], "outside must be untouched"
    assert img[250, 300].tolist() == [255, 255, 255], "the input must not be mutated"


def test_padding_grows_the_covered_area():
    img = _frame()
    dets = [(0, 0.9, (200, 150, 400, 350))]

    tight = om.apply_masks(img, dets, mode="box", pad=0)
    padded = om.apply_masks(img, dets, mode="box", pad=20)

    # A detector's box hugs the animal; ears and tail outside it still set a dog off.
    assert tight[145, 300].tolist() == [255, 255, 255]
    assert padded[145, 300].tolist() == [0, 0, 0]


def test_blur_mode_changes_the_region_without_a_hard_edge():
    img = np.random.randint(0, 255, (480, 640, 3), dtype=np.uint8)
    dets = [(0, 0.9, (200, 150, 400, 350))]

    out = om.apply_masks(img, dets, mode="blur", pad=0)

    region_before = img[160:340, 210:390].astype(float)
    region_after = out[160:340, 210:390].astype(float)
    assert region_after.std() < region_before.std(), "blur must reduce local variance"
    assert not np.array_equal(out[10, 10], [0, 0, 0]) or True   # outside untouched
    assert np.array_equal(out[10, 10], img[10, 10])


def test_a_box_outside_the_frame_is_skipped_not_clamped_into_a_stripe():
    img = _frame()
    out = om.apply_masks(img, [(0, 0.9, (700, 500, 800, 600))], mode="box", pad=0)
    assert np.array_equal(out, img), "an off-frame detection must change nothing"


def test_no_detections_returns_the_frame_unchanged():
    img = _frame()
    assert np.array_equal(om.apply_masks(img, [], mode="box"), img)


# ── Preprocessing ────────────────────────────────────────────────────────

def test_preprocess_produces_the_nchw_float_tensor_onnx_expects():
    img = _frame()
    blob = om.preprocess(img, 640)

    assert blob.shape == (1, 3, 640, 640)
    assert blob.dtype == np.float32
    assert 0.0 <= blob.min() and blob.max() <= 1.0, "values must be normalised to [0,1]"
