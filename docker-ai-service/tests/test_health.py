"""Tests for health and status endpoints — always public, never require auth."""


def test_health_returns_200(client):
    resp = client.get("/health")
    assert resp.status_code == 200
    data = resp.json()
    # Service may return 'ok', 'healthy', 'degraded', or 'starting' depending on version
    assert "status" in data, f"missing status field: {data}"
    assert isinstance(data["status"], str), f"status should be string: {data}"


def test_status_returns_200(client):
    resp = client.get("/status")
    assert resp.status_code == 200
    data = resp.json()
    # 'current_model' is the canonical field; older tests used 'model_loaded'
    assert "current_model" in data or "model_loaded" in data, f"missing model field in {data}"
    assert "processing_count" in data, f"missing processing_count in {data}"


def test_hardware_returns_200(client):
    resp = client.get("/hardware")
    assert resp.status_code == 200


def test_models_list_returns_list(client):
    resp = client.get("/models")
    assert resp.status_code == 200
    data = resp.json()
    # API returns either a plain list or {"models": [...], "total": N}
    model_list = data if isinstance(data, list) else data.get("models", [])
    assert isinstance(model_list, list), f"expected list, got {type(data)}"
    assert len(model_list) > 0, "model list should not be empty"


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
    # API returns either a plain list or {"gpus": [...], "total": N, ...}
    gpu_list = data if isinstance(data, list) else data.get("gpus", [])
    assert isinstance(gpu_list, list), f"expected list or dict with gpus key, got {type(data)}"


def test_doctor_returns_200_and_shape(client):
    """Setup Doctor (WS1) returns a structured checklist."""
    resp = client.get("/doctor")
    assert resp.status_code == 200
    d = resp.json()
    assert d.get("overall") in ("ok", "warn", "fail"), f"bad overall: {d}"
    checks = d.get("checks")
    assert isinstance(checks, list) and len(checks) >= 6, f"expected >=6 checks: {d}"
    names = {c["check"] for c in checks}
    expected = {"backend", "gpu_provider_active", "device_passthrough",
                "onnx_provider_pkg", "api_token", "model_smoke"}
    assert expected <= names, f"missing checks: {expected - names}"
    for c in checks:
        assert {"check", "status", "detail", "fix"} <= set(c.keys()), f"bad check shape: {c}"
        assert c["status"] in ("ok", "warn", "fail"), f"bad status: {c}"


def test_doctor_no_model_smoke_never_fails(client):
    """A freshly started, no-model instance must never show model_smoke=fail
    (it must degrade to warn) — otherwise a clean box reads as broken."""
    d = client.get("/doctor").json()
    smoke = next(c for c in d["checks"] if c["check"] == "model_smoke")
    assert smoke["status"] in ("ok", "warn"), f"model_smoke must not fail on no-model box: {smoke}"
