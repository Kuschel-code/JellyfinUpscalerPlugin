"""Object detection + masking — cover things you do not want on screen.

Added in v1.8.3.23 for discussion #11: a user wants animals covered so their dog
stops reacting to dogs and cats on TV. The request was for ffmpeg's dnn_detect /
dnn_classify / drawbox filters via a custom -vf chain.

WHY NOT THAT WAY. jellyfin-ffmpeg is not built with a DNN backend: its debian/rules
carries 46 --enable-* flags and not one of libopenvino / libtensorflow / libtorch,
and the string "dnn" does not appear in the build at all. So dnn_detect cannot run
there no matter what the plugin passes to -vf; the user would have to replace their
Jellyfin ffmpeg, which breaks the supported setup and every hardware-accel path
that build exists for.

Exposing arbitrary -vf would also be a security hole rather than a feature: ffmpeg
filter syntax includes `movie=/etc/passwd` and `subtitles=`, both of which read
files. The controller carries only [Authorize], so that would hand every
authenticated user a file-read primitive — the same class of bug v1.8.3.22 just
closed on POST /process.

WHAT THIS DOES INSTEAD. The pieces were already here: ONNX Runtime, OpenCV, a
per-frame pipeline used by both batch and the realtime stream, and a face-restore
path (restore_faces_in_frame) with exactly this shape — detect regions, alter each,
reassemble. This module does the same with a detection model, and it works on the
stock ffmpeg because ffmpeg is never asked to do the detection.

NO MODEL IS BUNDLED. The catalog's rule is that every entry carries a verified
sha256 pin, and inventing one for a model nobody has hashed would break exactly the
guarantee the importer exists to provide. Bring your own detector through the
existing upload/import path, which already pins and verifies it. The decoder below
handles the common YOLO output layouts.
"""
from __future__ import annotations

import logging
from typing import Iterable, Sequence

import cv2
import numpy as np

logger = logging.getLogger(__name__)

# COCO class order, which YOLOv3/v4/v5/v8 and most zoo exports follow. Used to turn
# a human-readable wish ("dog", "cat") into the class indices a model emits.
COCO_CLASSES = (
    "person", "bicycle", "car", "motorbike", "aeroplane", "bus", "train", "truck",
    "boat", "traffic light", "fire hydrant", "stop sign", "parking meter", "bench",
    "bird", "cat", "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra",
    "giraffe", "backpack", "umbrella", "handbag", "tie", "suitcase", "frisbee",
    "skis", "snowboard", "sports ball", "kite", "baseball bat", "baseball glove",
    "skateboard", "surfboard", "tennis racket", "bottle", "wine glass", "cup",
    "fork", "knife", "spoon", "bowl", "banana", "apple", "sandwich", "orange",
    "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "sofa",
    "pottedplant", "bed", "diningtable", "toilet", "tvmonitor", "laptop", "mouse",
    "remote", "keyboard", "cell phone", "microwave", "oven", "toaster", "sink",
    "refrigerator", "book", "clock", "vase", "scissors", "teddy bear", "hair drier",
    "toothbrush",
)

# The animals someone would plausibly want covered, as a convenience group.
ANIMAL_CLASSES = ("bird", "cat", "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe")


def class_indices(names: Iterable[str]) -> set[int]:
    """Map class names to COCO indices. Unknown names are reported, not guessed —
    a silently-ignored class would look like a model that simply misses things."""
    wanted, unknown = set(), []
    for n in names:
        key = n.strip().lower()
        if key == "animals":
            wanted.update(COCO_CLASSES.index(a) for a in ANIMAL_CLASSES)
        elif key in COCO_CLASSES:
            wanted.add(COCO_CLASSES.index(key))
        else:
            unknown.append(n)
    if unknown:
        logger.warning("Unknown detection class(es) ignored: %s", ", ".join(unknown))
    return wanted


def _iou(a, b) -> float:
    ax1, ay1, ax2, ay2 = a
    bx1, by1, bx2, by2 = b
    ix1, iy1 = max(ax1, bx1), max(ay1, by1)
    ix2, iy2 = min(ax2, bx2), min(ay2, by2)
    iw, ih = max(0.0, ix2 - ix1), max(0.0, iy2 - iy1)
    inter = iw * ih
    if inter <= 0:
        return 0.0
    area_a = max(0.0, ax2 - ax1) * max(0.0, ay2 - ay1)
    area_b = max(0.0, bx2 - bx1) * max(0.0, by2 - by1)
    union = area_a + area_b - inter
    return inter / union if union > 0 else 0.0


def _nms(boxes: list, scores: list, iou_threshold: float) -> list[int]:
    """Plain non-max suppression. cv2.dnn.NMSBoxes exists but its return shape has
    changed between OpenCV releases; this is 15 lines and cannot surprise us."""
    order = sorted(range(len(scores)), key=lambda i: scores[i], reverse=True)
    keep = []
    while order:
        best = order.pop(0)
        keep.append(best)
        order = [i for i in order if _iou(boxes[best], boxes[i]) < iou_threshold]
    return keep


def decode_detections(output: np.ndarray, src_w: int, src_h: int, net_size: int,
                      conf_threshold: float = 0.35, iou_threshold: float = 0.45,
                      wanted: set[int] | None = None) -> list[tuple[int, float, tuple[int, int, int, int]]]:
    """Turn a raw YOLO output tensor into [(class_id, score, (x1,y1,x2,y2)), ...].

    Handles the two layouts nearly every export uses:
      * (1, N, 4+1+C) — v3/v5/v7, box + objectness + class scores
      * (1, 4+C, N)   — v8/v9, transposed, no separate objectness
    Anything else raises, because guessing at a tensor's meaning produces boxes in
    plausible-looking wrong places, which is worse than a clear failure.
    """
    arr = np.squeeze(output)
    if arr.ndim != 2:
        raise ValueError(f"Unsupported detection output shape {output.shape}")

    # Orientation is inferred from which axis is larger, and that is safe for one
    # specific reason: a detection head emits thousands of ANCHORS and a few dozen
    # FEATURES (4 box + optional objectness + one score per class). 25200x85 for v5,
    # 84x8400 for v8. The anchor axis is therefore always the larger one.
    #
    # If the two are equal the tensor is genuinely ambiguous and we refuse, because
    # picking wrong here does not throw - it paints filled rectangles over the wrong
    # part of the picture, which a user reads as "the feature is broken" rather than
    # "this model is unsupported".
    rows, cols = arr.shape
    if rows == cols:
        raise ValueError(f"Ambiguous detection output shape {output.shape}: cannot tell anchors from features")
    if rows < cols:
        arr = arr.T                 # (4+C, N) -> (N, 4+C); v8/v9 have no objectness
        has_objectness = False
    else:
        has_objectness = True       # (N, 4+1+C); v3/v5/v7

    if arr.shape[1] < 6:
        raise ValueError(
            f"Detection output has too few features per anchor: {arr.shape[1]} "
            f"(need 4 box + objectness/class scores). Raw shape was {output.shape}")

    scale_x, scale_y = src_w / net_size, src_h / net_size
    boxes, scores, classes = [], [], []

    for row in arr:
        if has_objectness:
            objectness = float(row[4])
            if objectness < conf_threshold:
                continue
            class_scores = row[5:]
            cls = int(np.argmax(class_scores))
            score = objectness * float(class_scores[cls])
        else:
            class_scores = row[4:]
            cls = int(np.argmax(class_scores))
            score = float(class_scores[cls])

        if score < conf_threshold:
            continue
        if wanted is not None and cls not in wanted:
            continue

        cx, cy, w, h = (float(v) for v in row[:4])
        x1 = (cx - w / 2) * scale_x
        y1 = (cy - h / 2) * scale_y
        x2 = (cx + w / 2) * scale_x
        y2 = (cy + h / 2) * scale_y
        boxes.append((x1, y1, x2, y2))
        scores.append(score)
        classes.append(cls)

    keep = _nms(boxes, scores, iou_threshold)
    out = []
    for i in keep:
        x1, y1, x2, y2 = boxes[i]
        out.append((
            classes[i], scores[i],
            (max(0, int(x1)), max(0, int(y1)), min(src_w, int(x2)), min(src_h, int(y2))),
        ))
    return out


def preprocess(img: np.ndarray, net_size: int) -> np.ndarray:
    """BGR HxWx3 uint8 -> NCHW float32 in [0,1], letterboxed to a square."""
    resized = cv2.resize(img, (net_size, net_size), interpolation=cv2.INTER_LINEAR)
    rgb = cv2.cvtColor(resized, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
    return np.transpose(rgb, (2, 0, 1))[np.newaxis, ...]


def apply_masks(img: np.ndarray, detections: Sequence, mode: str = "box",
                colour: tuple[int, int, int] = (0, 0, 0), pad: int = 8,
                blur_strength: int = 45) -> np.ndarray:
    """Cover each detection. `box` fills it, `blur` obscures it while keeping motion.

    Padding matters: a detector's box hugs the animal, and a dog watching TV reacts
    to the ears and tail sticking out of a tight box just as well as to the whole
    animal.
    """
    out = img.copy()
    h, w = out.shape[:2]
    for _cls, _score, (x1, y1, x2, y2) in detections:
        x1, y1 = max(0, x1 - pad), max(0, y1 - pad)
        x2, y2 = min(w, x2 + pad), min(h, y2 + pad)
        if x2 <= x1 or y2 <= y1:
            continue
        if mode == "blur":
            region = out[y1:y2, x1:x2]
            k = max(3, blur_strength | 1)   # GaussianBlur needs an odd kernel
            out[y1:y2, x1:x2] = cv2.GaussianBlur(region, (k, k), 0)
        else:
            cv2.rectangle(out, (x1, y1), (x2, y2), colour, thickness=-1)
    return out
