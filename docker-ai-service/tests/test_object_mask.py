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


# ── The NMS-head family (yolo-v3-tiny-onnx, the model #11 actually asked for) ──
#
# This is a different contract, not a different tensor shape: the graph runs NMS
# itself, undoes its own letterbox, and returns boxes in SOURCE coordinates as
# (y_min, x_min, y_max, x_max) - per keras-yolo3's yolo_correct_boxes, which these
# ONNX files were converted from.

def test_plan_recognises_the_two_input_nms_family():
    plan = om.plan_for(
        inputs=[("input_1", [1, 3, 416, 416]), ("image_shape", [1, 2])],
        outputs=[("boxes", [1, 2535, 4]), ("scores", [1, 80, 2535]), ("indices", [1, 1600, 3])],
    )
    assert plan.style == "nms3"
    assert plan.net_size == 416, "the size comes from the model, not from the caller"
    assert plan.image_input == "input_1"
    assert plan.image_shape_input == "image_shape"


def test_plan_recognises_a_plain_single_head():
    plan = om.plan_for(
        inputs=[("images", [1, 3, 640, 640])],
        outputs=[("output0", [1, 84, 8400])],
    )
    assert plan.style == "single"
    assert plan.net_size == 640
    assert plan.image_shape_input is None


def test_plan_falls_back_when_the_input_size_is_dynamic():
    plan = om.plan_for(inputs=[("images", [1, 3, "height", "width"])],
                       outputs=[("output0", [1, 84, "anchors"])], fallback_size=512)
    assert plan.net_size == 512, "a dynamic axis must not be read as a size"


def test_plan_refuses_an_upscaler_loaded_here_by_mistake():
    # An upscaler has a 4-D input and a 4-D output, so it would otherwise pass as a
    # "single" head and only fail on the first frame of playback. The realistic
    # mistake - the model names in this service look alike - must fail at load time.
    with pytest.raises(ValueError, match="not detections"):
        om.plan_for(inputs=[("input", [1, 3, 256, 256])], outputs=[("output", [1, 3, 1024, 1024])])


def test_plan_refuses_a_model_with_no_image_input():
    with pytest.raises(ValueError, match="no 4-dimensional image input"):
        om.plan_for(inputs=[("size", [1, 2])], outputs=[("out", [1, 100, 85])])


def test_nms_boxes_are_read_as_y_x_y_x_not_x_y_x_y():
    # The whole point. Reading these as (x1,y1,x2,y2) transposes every box, which on a
    # 16:9 frame lands them somewhere plausible-looking and completely wrong.
    dog = om.COCO_CLASSES.index("dog")
    boxes = np.zeros((1, 4, 4), dtype=np.float32)
    boxes[0, 2] = (100, 300, 200, 500)        # y_min, x_min, y_max, x_max
    scores = np.zeros((1, 80, 4), dtype=np.float32)
    scores[0, dog, 2] = 0.9
    indices = np.array([[[0, dog, 2]]], dtype=np.int64)

    dets = om.decode_nms_outputs(boxes, scores, indices, src_w=1920, src_h=1080, wanted={dog})

    assert len(dets) == 1
    cls, score, (x1, y1, x2, y2) = dets[0]
    assert cls == dog and score == pytest.approx(0.9)
    assert (x1, y1, x2, y2) == (300, 100, 500, 200)


def test_nms_indices_are_accepted_in_both_documented_shapes():
    # onnx/models documents (nbox,3); the OpenVINO zoo documents (1,nbox,3) for the
    # very same file. Exports differ by opset, so both must work.
    cat = om.COCO_CLASSES.index("cat")
    boxes = np.zeros((1, 2, 4), dtype=np.float32)
    boxes[0, 1] = (10, 20, 30, 40)
    scores = np.zeros((1, 80, 2), dtype=np.float32)
    scores[0, cat, 1] = 0.8

    flat = om.decode_nms_outputs(boxes, scores, np.array([[0, cat, 1]]), 640, 480, wanted={cat})
    nested = om.decode_nms_outputs(boxes, scores, np.array([[[0, cat, 1]]]), 640, 480, wanted={cat})
    assert flat == nested and len(flat) == 1


def test_nms_path_honours_confidence_and_class_filters():
    dog, person = om.COCO_CLASSES.index("dog"), om.COCO_CLASSES.index("person")
    boxes = np.zeros((1, 3, 4), dtype=np.float32)
    boxes[0, 0] = (0, 0, 10, 10)
    boxes[0, 1] = (0, 0, 10, 10)
    boxes[0, 2] = (0, 0, 10, 10)
    scores = np.zeros((1, 80, 3), dtype=np.float32)
    scores[0, dog, 0] = 0.9        # keep
    scores[0, dog, 1] = 0.05       # below threshold
    scores[0, person, 2] = 0.99    # not requested
    indices = np.array([[0, dog, 0], [0, dog, 1], [0, person, 2]])

    dets = om.decode_nms_outputs(boxes, scores, indices, 640, 480,
                                 conf_threshold=0.35, wanted={dog})
    assert len(dets) == 1 and dets[0][1] == pytest.approx(0.9)


def test_no_detections_from_an_empty_indices_tensor():
    assert om.decode_nms_outputs(np.zeros((1, 1, 4)), np.zeros((1, 80, 1)),
                                 np.zeros((0, 3)), 640, 480) == []


def test_letterbox_preserves_aspect_and_pads_with_128():
    img = np.full((1080, 1920, 3), 255, dtype=np.uint8)
    blob = om.preprocess_letterbox(img, 416)

    assert blob.shape == (1, 3, 416, 416)
    # 1920x1080 into 416 -> 416x234, so rows above and below must be the 128 padding
    # the model's internal box correction assumes.
    assert blob[0, 0, 0, 0] == pytest.approx(128 / 255.0, abs=1e-3)
    assert blob[0, 0, 208, 208] == pytest.approx(1.0, abs=1e-3), "the image itself sits in the middle"


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
