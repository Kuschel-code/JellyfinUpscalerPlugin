"""Real endpoint recovery after circuit cooldown, rejected probes and contention."""
from concurrent.futures import ThreadPoolExecutor
from threading import Event
import numpy as np
import pytest
from fastapi import Request
from starlette.responses import StreamingResponse


@pytest.fixture
def recovering_service(client, monkeypatch):
    from app import main
    monkeypatch.setenv("API_TOKEN", "disable")
    monkeypatch.setattr(main.time, "time", lambda: 100.0)
    main.state.current_model = "test-sr"
    main.state.cv_model = object()
    main.state.circuit_open = True
    main.state.circuit_open_at = 0.0
    main.state.circuit_breaker_reset_seconds = 10
    monkeypatch.setattr(main, "upscale_image_array", lambda image: image)
    return main


def test_open_circuit_returns_remaining_retry_after(client, recovering_service):
    main = recovering_service
    main.state.circuit_open_at = 99.25
    response = client.post("/upscale-frame", content=b"frame")
    assert response.status_code == 503
    assert response.headers["Retry-After"] == "10"
    assert "Retry in 10s" in response.json()["detail"]
    assert not main.state.circuit_half_open


@pytest.mark.parametrize("rejection", ["invalid_hdr", "busy", "missing_model"])
def test_rejected_probe_does_not_block_next_valid_frame(client, recovering_service, rejection):
    main = recovering_service
    slots = main._upscale_semaphore._value
    params = {}
    if rejection == "invalid_hdr":
        params["headers"] = {"X-Color-Transfer": "arib-std-b67"}
        expected_status = 422
    elif rejection == "busy":
        main._upscale_semaphore._value = 0
        expected_status = 503
    else:
        main.state.cv_model = None
        expected_status = 400

    response = client.post("/upscale-frame", content=b"frame", **params)
    assert response.status_code == expected_status
    if rejection == "busy":
        assert response.headers["Retry-After"] == "1"
    assert not main.state.circuit_half_open
    assert main.state.circuit_probe_id is None

    main._upscale_semaphore._value = slots
    main.state.cv_model = object()
    response = client.post("/upscale-frame", content=b"frame")
    assert response.status_code == 200, response.text
    assert not main.state.circuit_open
    assert not main.state.circuit_half_open
    assert main.state.processing_count == 0
    assert main._upscale_semaphore._value == slots


def test_competing_request_cannot_release_an_active_probe(client, recovering_service, monkeypatch):
    main = recovering_service
    entered, release = Event(), Event()

    def infer(image):
        entered.set()
        assert release.wait(5), "test did not release the inference probe"
        return np.zeros_like(image)

    monkeypatch.setattr(main, "upscale_image_array", infer)
    with ThreadPoolExecutor(max_workers=1) as pool:
        first = pool.submit(client.post, "/upscale-frame", content=b"frame")
        try:
            assert entered.wait(5), "probe never reached inference"
            second = client.post("/upscale-frame", content=b"frame")
            assert second.status_code == 503
            assert second.headers["Retry-After"] == "1"
            assert main.state.circuit_half_open
            assert main.state.circuit_probe_id is not None
        finally:
            release.set()
        assert first.result(timeout=5).status_code == 200
    assert not main.state.circuit_open
    assert not main.state.circuit_half_open


@pytest.mark.asyncio
async def test_stream_probe_is_owned_until_response_body_finishes(recovering_service):
    main = recovering_service
    request = Request({"type": "http", "path": "/upscale-stream", "headers": []})

    async def body():
        assert main.state.circuit_half_open
        yield b"frame"

    async def next_handler(req):
        main._check_circuit_breaker(req)
        return StreamingResponse(body())

    response = await main.limit_body_size(request, next_handler)
    assert main.state.circuit_half_open
    assert [chunk async for chunk in response.body_iterator] == [b"frame"]
    assert not main.state.circuit_half_open
    assert main.state.circuit_probe_id is None
