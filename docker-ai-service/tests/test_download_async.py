"""Tests for the decoupled async model-download (v1.8.2).

The synchronous /models/download blocks the HTTP request for the whole (possibly
multi-GB) download, tripping client/proxy timeouts. /models/download-async returns
a job id immediately; the caller polls /models/download-status/{id}. download_model
is mocked so no real network is touched.
"""
import os
import time
from unittest.mock import patch

import pytest


@pytest.fixture(autouse=True)
def _disable_auth():
    """These endpoints are token-gated (secure-by-default). API_TOKEN=disable opts
    out of auth so the tests can exercise the download-job machinery directly."""
    with patch.dict(os.environ, {"API_TOKEN": "disable"}):
        yield


def test_download_async_starts_job_and_completes(client, monkeypatch):
    from app import main

    async def fake_dl(name):
        return True
    monkeypatch.setattr(main, "download_model", fake_dl)

    r = client.post("/models/download-async", data={"model_name": "fsrcnn-x2"})
    assert r.status_code == 200
    body = r.json()
    assert body["status"] == "queued"
    job_id = body["job_id"]
    assert job_id, "a real job id is returned when the model is not on disk"

    final = None
    for _ in range(60):
        s = client.get(f"/models/download-status/{job_id}").json()
        if s["status"] in ("completed", "failed"):
            final = s
            break
        time.sleep(0.05)
    assert final is not None, "job never reached a terminal state"
    assert final["status"] == "completed"
    assert final["model"] == "fsrcnn-x2"


def test_download_async_reports_failure(client, monkeypatch):
    from app import main

    async def fake_dl(name):
        return False
    monkeypatch.setattr(main, "download_model", fake_dl)

    job_id = client.post("/models/download-async", data={"model_name": "fsrcnn-x2"}).json()["job_id"]
    final = None
    for _ in range(60):
        s = client.get(f"/models/download-status/{job_id}").json()
        if s["status"] in ("completed", "failed"):
            final = s
            break
        time.sleep(0.05)
    assert final is not None and final["status"] == "failed"
    assert final["error"]


def test_download_async_short_circuits_when_present(client):
    from app import main
    p = main.get_model_path("fsrcnn-x2")
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_bytes(b"already here")
    try:
        r = client.post("/models/download-async", data={"model_name": "fsrcnn-x2"})
        body = r.json()
        assert body["status"] == "completed"
        assert body["already_present"] is True
        assert body["job_id"] is None
    finally:
        try:
            p.unlink()
        except OSError:
            pass


def test_download_async_unknown_model_404(client):
    r = client.post("/models/download-async", data={"model_name": "does-not-exist-xyz"})
    assert r.status_code == 404


def test_download_status_unknown_job_404(client):
    r = client.get("/models/download-status/deadbeefdeadbeef")
    assert r.status_code == 404
