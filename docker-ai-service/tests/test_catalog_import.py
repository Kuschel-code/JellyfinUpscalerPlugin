"""Catalog-import + converter gate invariants (v1.8.3.8).

The service-side importer accepts only catalog entries that pass _import_gate
(https + host allowlist + direct file + sha256 pin + size cap); zips must
contain exactly one .onnx; the converter endpoints 501 cleanly when the image
ships without torch/spandrel (every standard image).
"""
import io
import json
import zipfile

import pytest
from fastapi import HTTPException


def _entry(**over):
    e = {
        "id": "2x-Test-Model",
        "name": "Test Model",
        "scale": 2,
        "license": "CC-BY-4.0",
        "download_url": "https://github.com/x/releases/download/v1/model.onnx",
        "sha256": "a" * 64,
        "size_bytes": 10_000_000,
    }
    e.update(over)
    return e


def test_gate_accepts_pinned_allowlisted_onnx(client):
    from app import main
    assert main._import_gate(_entry()) is None


def test_gate_accepts_zip_for_direct(client):
    from app import main
    assert main._import_gate(_entry(download_url="https://github.com/x/releases/download/v1/pack.zip")) is None


@pytest.mark.parametrize("bad,expect_word", [
    (dict(download_url="http://github.com/x/m.onnx"), "https"),
    (dict(download_url="https://drive.google.com/file/d/abc/m.onnx"), "allowlisted"),
    (dict(download_url="https://mega.nz/file/m.onnx"), "allowlisted"),
    (dict(download_url="https://github.com/x/m.pth"), "direct"),
    (dict(sha256=""), "sha256"),
    (dict(size_bytes=600 * 1024 * 1024), "limit"),
])
def test_gate_rejects(client, bad, expect_word):
    from app import main
    reason = main._import_gate(_entry(**bad))
    assert reason is not None and expect_word in reason


def test_gate_convert_exts(client):
    from app import main
    exts = (".pth", ".pt", ".safetensors")
    assert main._import_gate(_entry(download_url="https://github.com/x/m.pth"), exts) is None
    assert main._import_gate(_entry(download_url="https://github.com/x/m.onnx"), exts) is not None


def test_model_name_mapping_matches_plugin(client):
    from app import main
    name = main._to_import_model_name("4x-UltraSharpV2")
    assert name == "omdb-4x-ultrasharpv2"
    import re
    assert re.fullmatch(r"[a-zA-Z0-9_-]{1,64}", name)


def _zip_bytes(members: dict) -> bytes:
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as zf:
        for name, data in members.items():
            zf.writestr(name, data)
    return buf.getvalue()


def test_zip_extracts_single_onnx(client):
    from app import main
    payload = b"\x08\x07fake-onnx"
    out = main._extract_single_onnx_from_zip(_zip_bytes({"model_fp32.onnx": payload, "README.txt": b"hi"}))
    assert out == payload


@pytest.mark.parametrize("members", [
    {"a.onnx": b"x", "b.onnx": b"y"},   # ambiguous
    {"README.txt": b"no model here"},   # none
])
def test_zip_rejects_wrong_member_count(client, members):
    from app import main
    with pytest.raises(HTTPException) as ei:
        main._extract_single_onnx_from_zip(_zip_bytes(members))
    assert ei.value.status_code == 502


def test_zip_rejects_garbage(client):
    from app import main
    with pytest.raises(HTTPException) as ei:
        main._extract_single_onnx_from_zip(b"this is not a zip")
    assert ei.value.status_code == 502


def test_converter_unavailable_gives_501(client, monkeypatch):
    from app import main
    monkeypatch.setattr(main, "_converter_available", lambda: False)
    with pytest.raises(HTTPException) as ei:
        main._convert_pth_bytes_to_onnx(b"fake")
    assert ei.value.status_code == 501
    assert "converter" in ei.value.detail.lower()


def test_catalog_scale_clamps_and_defaults(client):
    from app import main
    assert main._catalog_scale({"scale": 4}) == 4
    assert main._catalog_scale({"scale": "?"}) == 2
    assert main._catalog_scale({"scale": 99}) == 8
    assert main._catalog_scale({}) == 2
