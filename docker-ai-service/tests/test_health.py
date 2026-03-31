"""Tests for health and status endpoints — always public, never require auth."""


def test_health_returns_200(client):
    resp = client.get("/health")
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("status") in ("ok", "degraded", "starting"), f"unexpected: {data}"


def test_status_returns_200(client):
    resp = client.get("/status")
    assert resp.status_code == 200
    data = resp.json()
    assert "model_loaded" in data, f"missing model_loaded in {data}"
    assert "processing_count" in data, f"missing processing_count in {data}"


def test_hardware_returns_200(client):
    resp = client.get("/hardware")
    assert resp.status_code == 200


def test_models_list_returns_list(client):
    resp = client.get("/models")
    assert resp.status_code == 200
    data = resp.json()
    assert isinstance(data, list), f"expected list, got {type(data)}"
    assert len(data) > 0, "model list should not be empty"


def test_detailed_health_returns_200_or_503(client):
    resp = client.get("/health/detailed")
    assert resp.status_code in (200, 503), f"unexpected: {resp.status_code}"


def test_root_dashboard_returns_html(client):
    resp = client.get("/")
    assert resp.status_code == 200
    assert "text/html" in resp.headers.get("content-type", ""), f"expected HTML, got: {resp.headers}"


def test_connections_returns_list(client):
    resp = client.get("/connections")
    assert resp.status_code == 200
    data = resp.json()
    assert "connections" in data


def test_gpus_returns_list(client):
    resp = client.get("/gpus")
    assert resp.status_code in (200,)
    data = resp.json()
    assert isinstance(data, list)
