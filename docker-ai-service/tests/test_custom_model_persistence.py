"""Custom-model persistence invariants (v1.8.3.7).

/models/upload writes a <name>.custom.json sidecar next to the .onnx;
_register_custom_models_from_disk() restores registry entries from those
sidecars at startup. Before v1.8.3.7 the registry entry was RAM-only, so
every container restart silently dropped uploaded/imported models while
their files kept sitting in the volume.
"""
import json


def _write_sidecar(models_dir, name, **overrides):
    meta = {
        "model_name": name,
        "filename": f"{name}.onnx",
        "scale": 4,
        "description": "test model",
        "input_channels": 3,
        "output_channels": 3,
    }
    meta.update(overrides)
    (models_dir / f"{name}.custom.json").write_text(json.dumps(meta), encoding="utf-8")
    return meta


def test_restores_model_from_sidecar(client, tmp_path):
    from app import main
    _write_sidecar(tmp_path, "omdb-starsample-v1-0")
    (tmp_path / "omdb-starsample-v1-0.onnx").write_bytes(b"\x08\x07fake")
    registry = {}
    assert main._register_custom_models_from_disk(tmp_path, registry) == 1
    entry = registry["omdb-starsample-v1-0"]
    assert entry["custom"] is True
    assert entry["available"] is True
    assert entry["scale"] == 4
    assert entry["category"] == "super-resolution"
    assert entry["filename"] == "omdb-starsample-v1-0.onnx"


def test_skips_sidecar_without_model_file(client, tmp_path):
    from app import main
    _write_sidecar(tmp_path, "ghost-model")  # no .onnx written
    registry = {}
    assert main._register_custom_models_from_disk(tmp_path, registry) == 0
    assert "ghost-model" not in registry


def test_skips_invalid_model_name(client, tmp_path):
    from app import main
    # name breaking the upload contract (path traversal / illegal chars)
    _write_sidecar(tmp_path, "evil", model_name="../evil")
    (tmp_path / "evil.onnx").write_bytes(b"x")
    registry = {}
    assert main._register_custom_models_from_disk(tmp_path, registry) == 0


def test_never_shadows_existing_entry(client, tmp_path):
    from app import main
    _write_sidecar(tmp_path, "realesrgan-x4", scale=2)
    (tmp_path / "realesrgan-x4.onnx").write_bytes(b"x")
    builtin = {"name": "realesrgan-x4", "scale": 4, "custom": False}
    registry = {"realesrgan-x4": builtin}
    assert main._register_custom_models_from_disk(tmp_path, registry) == 0
    assert registry["realesrgan-x4"] is builtin  # untouched


def test_corrupt_sidecar_does_not_crash_others(client, tmp_path):
    from app import main
    (tmp_path / "broken.custom.json").write_text("{not json", encoding="utf-8")
    _write_sidecar(tmp_path, "good-model")
    (tmp_path / "good-model.onnx").write_bytes(b"x")
    registry = {}
    assert main._register_custom_models_from_disk(tmp_path, registry) == 1
    assert "good-model" in registry
