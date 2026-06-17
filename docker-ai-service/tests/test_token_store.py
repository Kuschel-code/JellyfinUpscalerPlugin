"""Unit tests for the persistent multi-token store (app/token_store.py).

Pure stdlib — does NOT import the FastAPI app, so it runs without the ML deps.
CONFIG_DIR is pointed at a per-test temp dir so nothing touches /app/config.
"""
import importlib
from datetime import timedelta

import pytest

from app import token_store


@pytest.fixture(autouse=True)
def _isolated_config(tmp_path, monkeypatch):
    monkeypatch.setenv("CONFIG_DIR", str(tmp_path))
    token_store._last_used_flushed.clear()  # reset in-memory throttle between tests
    token_store._cache = None               # reset (path, mtime) file cache between tests
    token_store._cache_key = None
    yield


def test_create_returns_plaintext_and_info():
    token, info = token_store.create_token("Living room")
    assert token.startswith("upsk_")
    assert info["name"] == "Living room"
    assert info["prefix"] == token[:9]
    assert info["expires_at"] is None
    assert info["expired"] is False
    assert "hash" not in info  # never expose the hash to the API/UI


def test_verify_accepts_correct_rejects_wrong():
    token, _ = token_store.create_token("k")
    assert token_store.verify(token) is True
    assert token_store.verify("upsk_definitely_wrong") is False
    assert token_store.verify("") is False


def test_revoke_invalidates():
    token, info = token_store.create_token("temp")
    assert token_store.verify(token) is True
    assert token_store.revoke_token(info["id"]) is True
    assert token_store.verify(token) is False
    assert token_store.revoke_token("tok_nope") is False


def test_hash_stored_not_plaintext(tmp_path):
    token, _ = token_store.create_token("secret-named")
    raw = (tmp_path / "tokens.json").read_text(encoding="utf-8")
    assert token not in raw       # plaintext is never written to disk
    assert '"hash":' in raw       # only its hash is


def test_never_expires_by_default():
    token, info = token_store.create_token("forever", expires_days=None)
    assert info["expires_at"] is None
    assert token_store.verify(token) is True


def test_lazy_expiry(monkeypatch):
    token, info = token_store.create_token("month", expires_days=30)
    assert info["expires_at"] is not None
    assert token_store.verify(token) is True
    # jump 31 days into the future -> token must stop authenticating (no sweep needed)
    real_now = token_store._now()
    monkeypatch.setattr(token_store, "_now", lambda: real_now + timedelta(days=31))
    assert token_store.verify(token) is False


def test_invalid_inputs():
    with pytest.raises(ValueError):
        token_store.create_token("")               # empty name
    with pytest.raises(ValueError):
        token_store.create_token("ok", expires_days=0)
    with pytest.raises(ValueError):
        token_store.create_token("ok", expires_days=99999)


def test_count_list_and_has_any():
    assert token_store.has_any() is False
    token_store.create_token("a")
    token_store.create_token("b", expires_days=90)
    assert token_store.count() == 2
    assert token_store.has_any() is True
    listed = token_store.list_tokens()
    assert {t["name"] for t in listed} == {"a", "b"}
    assert all("hash" not in t for t in listed)


def test_external_edit_invalidates_cache(tmp_path):
    """An external rewrite of tokens.json is picked up via the (path, mtime) cache key."""
    import json
    import os
    import time
    token, _ = token_store.create_token("ext")
    assert token_store.verify(token) is True
    path = tmp_path / "tokens.json"
    path.write_text(json.dumps({"version": 1, "tokens": []}), encoding="utf-8")
    future = time.time() + 5  # ensure a distinct mtime so the cache key changes
    os.utime(path, (future, future))
    assert token_store.verify(token) is False


def test_survives_reload(monkeypatch, tmp_path):
    """A managed token must survive a process restart (on-disk persistence)."""
    token, _ = token_store.create_token("persistent")
    import app  # ensure parent package is present (other tests' client fixture purges it)
    reloaded = importlib.reload(token_store)         # simulate a fresh process
    monkeypatch.setenv("CONFIG_DIR", str(tmp_path))   # reload cleared module state; re-point
    assert reloaded.verify(token) is True
