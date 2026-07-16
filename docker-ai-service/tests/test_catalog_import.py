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


def test_zip_extracts_the_pinned_member(client):
    # v1.8.3.9: OMDB pins the INNER file - multi-member zips (AnimeJaNai ships
    # five variants in one release zip) must yield exactly the pinned member.
    import hashlib
    from app import main
    target = b"\x08\x07target-model"
    pin = hashlib.sha256(target).hexdigest()
    out = main._extract_pinned_onnx_from_zip(
        _zip_bytes({"a.onnx": b"other", "sub/target.onnx": target, "install.ps1": b"x"}), pin)
    assert out == target


def test_zip_rejects_when_no_member_matches_pin(client):
    from app import main
    with pytest.raises(HTTPException) as ei:
        main._extract_pinned_onnx_from_zip(_zip_bytes({"a.onnx": b"x", "b.onnx": b"y"}), "0" * 64)
    assert ei.value.status_code == 502
    assert "pin" in ei.value.detail.lower()


def test_zip_rejects_no_onnx_at_all(client):
    from app import main
    with pytest.raises(HTTPException) as ei:
        main._extract_pinned_onnx_from_zip(_zip_bytes({"README.txt": b"no model"}), "0" * 64)
    assert ei.value.status_code == 502


def test_zip_rejects_garbage(client):
    from app import main
    with pytest.raises(HTTPException) as ei:
        main._extract_pinned_onnx_from_zip(b"this is not a zip", "0" * 64)
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


@pytest.fixture
def no_auth(monkeypatch):
    from app import main
    monkeypatch.setattr(main, "_require_api_token", lambda r=None: None)


def test_import_async_requires_id(client, no_auth):
    r = client.post("/models/import-async", json={})
    assert r.status_code == 400


def test_import_async_unknown_id_404(client, no_auth, monkeypatch):
    from app import main
    monkeypatch.setattr(main, "_fetch_import_catalog", lambda: {"direct_onnx": [], "requires_conversion": []})
    r = client.post("/models/import-async", json={"id": "does-not-exist"})
    assert r.status_code == 404


def test_import_async_gate_rejected_400(client, no_auth, monkeypatch):
    from app import main
    entry = {"id": "bad-host", "name": "x", "scale": 2, "license": "MIT",
             "download_url": "https://mega.nz/file/x.onnx", "sha256": "a" * 64, "size_bytes": 10}
    monkeypatch.setattr(main, "_fetch_import_catalog", lambda: {"direct_onnx": [entry], "requires_conversion": []})
    r = client.post("/models/import-async", json={"id": "bad-host"})
    assert r.status_code == 400
    assert "allowlisted" in r.json()["detail"]


def test_import_status_unknown_job_404(client, no_auth):
    r = client.get("/models/import-status/deadbeef")
    assert r.status_code == 404
