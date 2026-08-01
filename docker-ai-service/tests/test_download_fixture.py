"""v1.8.3.20 — the download path, exercised against a local fixture server.

The existing async-download test mocks `download_model` wholesale, so the code that
actually matters never ran: the SSRF gate, the streaming loop, the sha256 verification,
the atomic rename, and (since v1.8.3.14) the progress callback. A mock that returns True
proves the job machinery works and nothing about the download.

These tests stand up an HTTP server on loopback, serve a known payload with a known
digest, and let the real `download_model` run against it. No external network is touched,
so this works in a sandbox with no egress.

The SSRF gate is NOT weakened to make that possible: the production predicate stays
https-plus-allowlist and gets its own direct tests below. Only the mechanics test swaps
the predicate out, because pointing a loopback fixture at an https allowlist is not
something the gate should ever permit.
"""
import hashlib
import http.server
import os
import threading
from unittest.mock import patch

import pytest


PAYLOAD = b"onnx-fixture-payload" * 512   # ~10 KB: enough for several stream chunks
PAYLOAD_SHA = hashlib.sha256(PAYLOAD).hexdigest()


@pytest.fixture(autouse=True)
def _disable_auth():
    with patch.dict(os.environ, {"API_TOKEN": "disable"}):
        yield


@pytest.fixture
def fixture_server():
    """A loopback HTTP server serving PAYLOAD at /model.onnx.

    Also serves /truncated.onnx with a body that does NOT match PAYLOAD_SHA, so the
    integrity gate can be tested against a real (wrong) download rather than a stub.
    """
    class Handler(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            body = PAYLOAD if self.path == "/model.onnx" else b"wrong-content"
            self.send_response(200)
            self.send_header("Content-Type", "application/octet-stream")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, *args):
            pass   # keep pytest output readable

    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield f"http://127.0.0.1:{server.server_address[1]}"
    finally:
        server.shutdown()
        server.server_close()


# ── The gate itself ────────────────────────────────────────────────────────
# Direct tests, because until v1.8.3.20 this lived as a local tuple inside
# download_model and could not be reached from a test at all.

@pytest.mark.parametrize("url", [
    "https://huggingface.co/x/model.onnx",
    "https://github.com/x/releases/download/v1/model.onnx",
    "https://raw.githubusercontent.com/x/main/model.onnx",
])
def test_allowlisted_https_urls_pass(url):
    from app.main import _is_allowed_download_url
    assert _is_allowed_download_url(url)


@pytest.mark.parametrize("url", [
    "http://huggingface.co/x/model.onnx",              # plaintext is MITM-able
    "https://evil.test/model.onnx",                    # not on the list
    "https://huggingface.co.evil.test/model.onnx",     # suffix trick: hostname, not prefix
    "https://127.0.0.1/model.onnx",                    # SSRF at the loopback interface
    "file:///etc/passwd",                              # not even http
])
def test_everything_else_is_refused(url):
    from app.main import _is_allowed_download_url
    assert not _is_allowed_download_url(url)


# ── The mechanics ──────────────────────────────────────────────────────────

@pytest.fixture
def registered_model(fixture_server, tmp_path, monkeypatch):
    """Register a model pointing at the fixture server and route downloads to tmp_path."""
    from app import main

    monkeypatch.setitem(main.AVAILABLE_MODELS, "fixture-model-x2", {
        "name": "Fixture Model",
        "type": "onnx",
        "scale": 2,
        "available": True,
        "url": f"{fixture_server}/model.onnx",
        "sha256": PAYLOAD_SHA,
    })
    monkeypatch.setattr(main, "get_model_path", lambda n: tmp_path / f"{n}.onnx")
    # The loopback fixture is http and 127.0.0.1 - exactly what the real gate must
    # refuse. Swap the predicate rather than loosening it.
    monkeypatch.setattr(main, "_is_allowed_download_url", lambda url: True)
    return "fixture-model-x2", tmp_path


@pytest.mark.asyncio
async def test_download_writes_the_verified_file_to_disk(registered_model):
    from app import main
    name, tmp_path = registered_model

    assert await main.download_model(name) is True

    dest = tmp_path / f"{name}.onnx"
    assert dest.exists(), "the model must land on disk under its final name"
    assert dest.read_bytes() == PAYLOAD
    assert not list(tmp_path.glob("*.tmp.*")), "the temp file must not survive a success"


@pytest.mark.asyncio
async def test_a_sha_mismatch_refuses_to_publish_the_file(fixture_server, tmp_path, monkeypatch):
    """The integrity gate, against a real wrong download rather than a stub."""
    from app import main

    monkeypatch.setitem(main.AVAILABLE_MODELS, "fixture-bad-x2", {
        "name": "Fixture Bad", "type": "onnx", "scale": 2, "available": True,
        "url": f"{fixture_server}/truncated.onnx",
        "sha256": PAYLOAD_SHA,               # deliberately does not match the body
    })
    monkeypatch.setattr(main, "get_model_path", lambda n: tmp_path / f"{n}.onnx")
    monkeypatch.setattr(main, "_is_allowed_download_url", lambda url: True)

    assert await main.download_model("fixture-bad-x2") is False
    assert not (tmp_path / "fixture-bad-x2.onnx").exists(), \
        "a mismatched download must never become visible as a valid model"
    assert not list(tmp_path.glob("*.tmp.*")), "the temp file must be cleaned up on failure"


@pytest.mark.asyncio
async def test_progress_is_reported_while_streaming(registered_model):
    """v1.8.3.14 added progress_cb so the UI could show real bytes. Nothing covered it -
    and a signature change to this callback silently broke test_download_async for six
    releases, because that test's mock still had the old arity."""
    from app import main
    name, _ = registered_model

    seen = []
    assert await main.download_model(name, progress_cb=lambda done, total: seen.append((done, total))) is True

    assert seen, "progress_cb must be called at least once"
    done, total = seen[-1]
    assert done == len(PAYLOAD), "the final callback must report the full payload"
    assert total == len(PAYLOAD), "Content-Length was served, so the total must be known"
    assert all(d <= len(PAYLOAD) for d, _ in seen), "progress must never exceed the payload"
