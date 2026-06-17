"""Integration tests for the managed-token CRUD endpoints (/auth/tokens).

Uses the TestClient `client` fixture (heavy deps mocked, CONFIG_DIR isolated).
"""
import os
from unittest.mock import patch

BOOT = {"API_TOKEN": "bootstrap"}


def test_create_list_use_revoke_flow(client):
    with patch.dict(os.environ, BOOT):
        # create
        r = client.post("/auth/tokens", data={"name": "Living room"},
                        headers={"x-api-token": "bootstrap"})
        assert r.status_code == 200, r.text
        body = r.json()
        token = body["token"]
        assert token.startswith("upsk_")
        assert body["info"]["name"] == "Living room"
        assert body["info"]["expires_at"] is None
        tid = body["info"]["id"]

        # list shows it; never the secret or hash
        r = client.get("/auth/tokens", headers={"x-api-token": "bootstrap"})
        assert r.status_code == 200
        listing = r.json()
        assert listing["bootstrap_env"] is True
        assert any(t["id"] == tid for t in listing["tokens"])
        assert all("hash" not in t and "token" not in t for t in listing["tokens"])

        # the freshly-created managed token now authenticates a protected route
        r = client.post("/models/download", data={"model_name": "realesrgan-x4"},
                        headers={"x-api-token": token})
        assert r.status_code != 403

        # revoke it
        r = client.delete(f"/auth/tokens/{tid}", headers={"x-api-token": "bootstrap"})
        assert r.status_code == 200 and r.json()["revoked"] is True

        # revoked token no longer authenticates
        r = client.post("/models/download", data={"model_name": "realesrgan-x4"},
                        headers={"x-api-token": token})
        assert r.status_code == 403


def test_endpoints_require_auth(client):
    with patch.dict(os.environ, BOOT):
        assert client.get("/auth/tokens").status_code == 403
        assert client.post("/auth/tokens", data={"name": "x"}).status_code == 403
        assert client.delete("/auth/tokens/tok_x").status_code == 403


def test_create_with_expiry(client):
    with patch.dict(os.environ, BOOT):
        r = client.post("/auth/tokens", data={"name": "temp", "expires_days": "30"},
                        headers={"x-api-token": "bootstrap"})
        assert r.status_code == 200, r.text
        assert r.json()["info"]["expires_at"] is not None


def test_revoke_unknown_returns_404(client):
    with patch.dict(os.environ, BOOT):
        r = client.delete("/auth/tokens/tok_does_not_exist",
                          headers={"x-api-token": "bootstrap"})
        assert r.status_code == 404


def test_empty_name_rejected(client):
    with patch.dict(os.environ, BOOT):
        r = client.post("/auth/tokens", data={"name": ""},
                        headers={"x-api-token": "bootstrap"})
        assert r.status_code in (400, 422)
