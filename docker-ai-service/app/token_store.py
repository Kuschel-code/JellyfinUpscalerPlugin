"""Persistent, hashed multi-token store for AI-service API authentication.

Design (see tasks/api-token-management.md):
  * Tokens live HASHED (SHA-256) in CONFIG_DIR/tokens.json — a PERSISTENT path.
    NOT the cache dir: a cache is wipe-able, auth-state must survive recreates.
  * The plaintext token is returned exactly ONCE, at creation. Only the hash,
    a short display prefix and metadata are stored.
  * Auth is timing-safe: hash the provided token, then hmac.compare_digest
    against every stored hash.
  * Expiry is LAZY — a token is valid iff enabled AND (no expiry OR now < expiry).
    There is no background sweep / daemon; expired tokens simply stop working.
  * The env API_TOKEN (handled in main._require_api_token) stays valid as a
    non-revocable bootstrap credential — this module only manages the extra ones.

All writes go through a process-wide lock and an atomic temp-file + os.replace,
so concurrent requests can't corrupt the file or clobber a freshly-created token.
last_used is persisted best-effort and throttled (≤ once/60s per token) to avoid
a disk write on every authenticated request.
"""
from __future__ import annotations

import hashlib
import hmac
import json
import os
import re
import secrets
import tempfile
import threading
import time
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Optional

TOKEN_PREFIX = "upsk_"
NAME_MAX = 64
_NAME_RE = re.compile(r"[\w .\-]{1,%d}" % NAME_MAX)
_LAST_USED_THROTTLE_S = 60  # don't persist last_used more often than this per token

_lock = threading.Lock()
# monotonic timestamp of the last persisted last_used per token id (in-memory only)
_last_used_flushed: dict[str, float] = {}


# --------------------------------------------------------------------------- #
# Paths / helpers
# --------------------------------------------------------------------------- #
def _config_path() -> Path:
    """Resolved at call-time (not import) so tests can override CONFIG_DIR."""
    return Path(os.getenv("CONFIG_DIR", "/app/config")) / "tokens.json"


def _now() -> datetime:
    return datetime.now(timezone.utc)


def _iso(dt: datetime) -> str:
    return dt.replace(microsecond=0).isoformat()


def _hash(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def _empty() -> dict:
    return {"version": 1, "tokens": []}


def _valid_name(name: str) -> bool:
    return bool(name) and _NAME_RE.fullmatch(name) is not None


def _is_expired(rec: dict, now: Optional[datetime] = None) -> bool:
    exp = rec.get("expires_at")
    if not exp:
        return False  # null/empty == never expires
    now = now or _now()
    try:
        return now >= datetime.fromisoformat(exp)
    except ValueError:
        return True  # unparseable expiry -> fail closed (deny); bootstrap env token still works


# --------------------------------------------------------------------------- #
# Load / save (atomic)
# --------------------------------------------------------------------------- #
def _load() -> dict:
    path = _config_path()
    try:
        with path.open("r", encoding="utf-8") as f:
            data = json.load(f)
    except (FileNotFoundError, OSError, json.JSONDecodeError):
        return _empty()
    if not isinstance(data, dict) or not isinstance(data.get("tokens"), list):
        return _empty()
    return data


def _save(data: dict) -> None:
    path = _config_path()
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=str(path.parent), prefix=".tokens-", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)
            f.flush()
            os.fsync(f.fileno())
        os.replace(tmp, path)  # atomic on POSIX & Windows
        try:
            os.chmod(path, 0o600)
        except OSError:
            pass  # best-effort (e.g. Windows / unusual FS)
    finally:
        try:
            if os.path.exists(tmp):
                os.remove(tmp)
        except OSError:
            pass


def _public(rec: dict) -> dict:
    """Safe projection for the API/UI — never exposes the hash."""
    return {
        "id": rec.get("id"),
        "name": rec.get("name"),
        "prefix": rec.get("prefix"),
        "created": rec.get("created"),
        "expires_at": rec.get("expires_at"),
        "last_used": rec.get("last_used"),
        "enabled": rec.get("enabled", True),
        "expired": _is_expired(rec),
    }


# --------------------------------------------------------------------------- #
# Public API
# --------------------------------------------------------------------------- #
def create_token(name: str, expires_days: Optional[int] = None) -> tuple[str, dict]:
    """Create a token. expires_days=None -> never expires.
    Returns (plaintext, public_info). The plaintext is the ONLY time it is exposed."""
    if not _valid_name(name):
        raise ValueError("invalid token name (allowed: letters, digits, space . _ -, 1-%d chars)" % NAME_MAX)
    if expires_days is not None and (expires_days <= 0 or expires_days > 3650):
        raise ValueError("expires_days must be 1..3650 or None")

    plaintext = TOKEN_PREFIX + secrets.token_urlsafe(24)
    now = _now()
    rec = {
        "id": "tok_" + secrets.token_hex(4),
        "name": name,
        "hash": _hash(plaintext),
        "prefix": plaintext[:9],
        "created": _iso(now),
        "expires_at": _iso(now + timedelta(days=expires_days)) if expires_days else None,
        "last_used": None,
        "enabled": True,
    }
    with _lock:
        data = _load()
        data["tokens"].append(rec)
        _save(data)
    return plaintext, _public(rec)


def list_tokens() -> list[dict]:
    with _lock:
        return [_public(t) for t in _load()["tokens"]]


def revoke_token(token_id: str) -> bool:
    with _lock:
        data = _load()
        kept = [t for t in data["tokens"] if t.get("id") != token_id]
        if len(kept) == len(data["tokens"]):
            return False
        data["tokens"] = kept
        _save(data)
    _last_used_flushed.pop(token_id, None)
    return True


def has_any() -> bool:
    with _lock:
        return len(_load()["tokens"]) > 0


def count() -> int:
    with _lock:
        return len(_load()["tokens"])


def verify(provided: str) -> bool:
    """Timing-safe check against every enabled, non-expired token hash.
    Records last_used best-effort (throttled, atomic)."""
    if not provided:
        return False
    provided_hash = _hash(provided)
    now = _now()
    with _lock:
        data = _load()
        matched = None
        for rec in data["tokens"]:
            if not rec.get("enabled", True) or _is_expired(rec, now):
                continue
            if hmac.compare_digest(rec.get("hash", ""), provided_hash):
                matched = rec
                break
        if matched is None:
            return False
        tid = matched.get("id", "")
        if time.monotonic() - _last_used_flushed.get(tid, 0.0) >= _LAST_USED_THROTTLE_S:
            matched["last_used"] = _iso(now)
            _last_used_flushed[tid] = time.monotonic()
            try:
                _save(data)
            except OSError:
                pass  # last_used is best-effort; never fail auth on a write hiccup
        return True
