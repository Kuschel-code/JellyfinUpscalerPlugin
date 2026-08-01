"""OpenModelDB import + pth->ONNX conversion helpers.

Extracted verbatim from main.py in v1.8.3.20 (Phase 2). main.py had grown past
6800 lines and this block is the one part of it that is genuinely self-contained:
catalog fetch/cache, the import gate, the capped and pinned downloads, the pinned
zip member extraction, and the spandrel-based converter. Same split as
token_store.py.

PURE MOVE - no behaviour changed. Every function body below is byte-identical to
what stood in main.py; only the imports at the top and the wiring comment on
MAX_MODEL_UPLOAD_BYTES are new. The endpoints that call these stay in main.py
with their decorators untouched, so no route moved either.

Security model (unchanged, restated because it now lives in one place):
  * catalog ids only - callers never pass a URL
  * host allowlist, checked by hostname and not by prefix
  * sha256 pin mandatory; for zips the pin describes the INNER .onnx, and the
    member is selected BY that pin
  * hard size cap on both the download and each decompressed zip member
  * loading .pth executes pickled code, which is why the converter is an opt-in
    image and only ever sees pinned files or admin-supplied ones
"""
import asyncio
import hashlib
import os
import re
import tempfile
import time
import urllib.parse

import logging

import httpx
import onnxruntime as ort
from fastapi import HTTPException

# The one intentional difference from the moved code: log records now carry
# "app.model_import" instead of "app.main". Nothing reads the logger name
# programmatically, and import failures being distinguishable from the rest of
# main's output is the point of splitting the file.
logger = logging.getLogger(__name__)

# Wired by main.py at import time so the cap keeps exactly one definition - the
# env parsing (_safe_int_env) stays where the other limits live. The default here
# only applies if this module is imported standalone, e.g. by a test.
MAX_MODEL_UPLOAD_BYTES = 500 * 1024 * 1024


# ── OpenModelDB import catalog (v1.8.3.8) ────────────────────────────────────
# The service can now import catalog models itself (dashboard + plugin both
# call this). Same security model as the plugin importer: catalog ids only,
# host allowlist, sha256 pin mandatory, size cap, validated ingest.

_IMPORT_CATALOG_URLS = (
    "https://kuschel-code.github.io/JellyfinUpscalerPlugin/models-import.json",
    "https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/site/models-import.json",
)
_IMPORT_ALLOWED_HOSTS = (
    "github.com",
    "raw.githubusercontent.com",
    "huggingface.co",
    "objectstorage.us-phoenix-1.oraclecloud.com",
)
_IMPORT_CATALOG_TTL = 6 * 3600
_import_catalog_cache: dict = {"data": None, "ts": 0.0}


def _import_host_allowed(url: str) -> bool:
    try:
        host = urllib.parse.urlparse(url).hostname or ""
    except ValueError:
        return False
    return any(host == h or host.endswith("." + h) for h in _IMPORT_ALLOWED_HOSTS)


def _fetch_import_catalog() -> dict | None:
    now = time.time()
    if _import_catalog_cache["data"] and now - _import_catalog_cache["ts"] < _IMPORT_CATALOG_TTL:
        return _import_catalog_cache["data"]
    for url in _IMPORT_CATALOG_URLS:
        try:
            r = httpx.get(url, timeout=30, follow_redirects=True)
            if r.status_code == 200:
                doc = r.json()
                if doc.get("direct_onnx"):
                    _import_catalog_cache["data"] = doc
                    _import_catalog_cache["ts"] = now
                    return doc
        except Exception as e:
            logger.warning(f"Import catalog fetch failed from {url}: {e}")
    return _import_catalog_cache["data"]  # possibly stale/None — caller surfaces the error


async def _fetch_import_catalog_async() -> dict | None:
    """Async wrapper: the sync httpx call would otherwise block the single
    uvicorn event loop for up to ~60s on a cache miss (review finding) —
    freezing every in-flight request incl. /health and /upscale-stream."""
    loop = asyncio.get_running_loop()
    return await loop.run_in_executor(None, _fetch_import_catalog)


def _import_gate(entry: dict, exts: tuple = (".onnx", ".zip")) -> str | None:
    """None if the entry passes all import gates, else a human-readable reason."""
    url = entry.get("download_url") or ""
    if not url.startswith("https://"):
        return "no https download url"
    if not _import_host_allowed(url):
        host = urllib.parse.urlparse(url).hostname or "?"
        return f"host not allowlisted ({host}) - download manually and use the upload form"
    path = urllib.parse.urlparse(url).path.lower()
    if not any(path.endswith(e) for e in exts):
        return f"not a direct {'/'.join(exts)} file"
    if not entry.get("sha256"):
        return "no sha256 pin in the catalog"
    if (entry.get("size_bytes") or 0) > MAX_MODEL_UPLOAD_BYTES:
        return f"exceeds the {MAX_MODEL_UPLOAD_BYTES // (1024*1024)} MB import limit"
    return None


def _to_import_model_name(catalog_id: str) -> str:
    """Catalog id -> omdb- namespaced model name (mirrors the plugin's ToModelName)."""
    cleaned = re.sub(r"[^a-z0-9-]+", "-", catalog_id.lower()).strip("-")
    return ("omdb-" + cleaned)[:64].rstrip("-")


def _catalog_scale(entry: dict) -> int:
    try:
        return max(1, min(8, int(entry.get("scale"))))
    except (TypeError, ValueError):
        return 2


def _extract_pinned_onnx_from_zip(data: bytes, sha256_pin: str) -> bytes:
    """v1.8.3.9 fix: OMDB pins the INNER .onnx file, not the zip container
    (live-verified against the AnimeJaNai release: ONE zip ships FIVE model
    variants, and the catalog's sha256/size describe exactly one member).
    Select the member whose sha256 matches the pin — the zip is just transport
    and stays unpinned. Decompression is hard-capped per member (zip-bomb
    guard — infolist sizes can lie)."""
    import zipfile as _zipfile
    import io as _io
    try:
        zf = _zipfile.ZipFile(_io.BytesIO(data))
    except _zipfile.BadZipFile:
        raise HTTPException(status_code=502, detail="Downloaded file is not a valid zip archive")
    pin = (sha256_pin or "").lower()
    candidates = [m for m in zf.infolist() if m.filename.lower().endswith(".onnx") and not m.is_dir()]
    if not candidates:
        raise HTTPException(status_code=502, detail="Zip contains no .onnx file")
    for m in candidates:
        with zf.open(m) as fh:
            content = fh.read(MAX_MODEL_UPLOAD_BYTES + 1)
        if len(content) > MAX_MODEL_UPLOAD_BYTES:
            continue
        if hashlib.sha256(content).hexdigest() == pin:
            return content
    raise HTTPException(status_code=502, detail=f"No .onnx inside the zip matches the catalog's sha256 pin ({len(candidates)} candidates) - the upstream release changed; the weekly catalog refresh will re-pin it if legitimate.")


async def _download_capped(url: str) -> bytes:
    """Download from an allowlisted URL with the size cap. Redirects are followed
    (GitHub releases redirect to objects.githubusercontent.com); safe because the
    payload is verified against the catalog pin afterwards and no secret is sent."""
    async with httpx.AsyncClient(follow_redirects=True, timeout=httpx.Timeout(570.0, connect=30.0)) as client:
        resp = await client.get(url)
        if resp.status_code != 200:
            raise HTTPException(status_code=502, detail=f"Download failed (HTTP {resp.status_code} from source)")
        data = resp.content
    if len(data) > MAX_MODEL_UPLOAD_BYTES:
        raise HTTPException(status_code=502, detail=f"Downloaded file exceeds the {MAX_MODEL_UPLOAD_BYTES // (1024*1024)} MB import limit")
    return data


async def _download_pinned(url: str, sha256_pin: str) -> bytes:
    """_download_capped + sha256 pin verification (for non-zip payloads, where
    the catalog pin describes the downloaded file itself)."""
    data = await _download_capped(url)
    digest = hashlib.sha256(data).hexdigest()
    if digest.lower() != (sha256_pin or "").lower():
        logger.warning(f"Catalog import rejected - sha256 mismatch (expected {sha256_pin}, got {digest})")
        raise HTTPException(status_code=502, detail="sha256 mismatch - the upstream file changed since the catalog was generated. Import refused; the weekly catalog refresh will re-pin it if the change is legitimate.")
    return data


def _converter_available() -> bool:
    """True when torch+spandrel are installed (the docker7-converter image)."""
    import importlib.util
    return importlib.util.find_spec("spandrel") is not None and importlib.util.find_spec("torch") is not None


def _convert_pth_bytes_to_onnx(pth_data: bytes) -> tuple:
    """Load a .pth/.safetensors via spandrel, export ONNX (opset 17, dynamic H/W)
    and verify the export against the torch output. Returns (onnx_bytes, scale,
    input_channels).

    SECURITY NOTE: loading .pth files can execute pickled code. This is why the
    converter (a) is an OPT-IN image, (b) auto-downloads only sha256-pinned files
    from allowlisted hosts, and (c) otherwise requires an admin to hand it a file
    they chose to trust. spandrel additionally restricts unpickling internally.
    """
    if not _converter_available():
        raise HTTPException(status_code=501, detail="Converter not available - this image ships without torch/spandrel. Use the kuscheltier/jellyfin-ai-upscaler:docker7-converter image to convert .pth models.")
    import torch
    import spandrel
    import numpy as _np

    with tempfile.NamedTemporaryFile(suffix=".pth", delete=False) as tmp:
        tmp.write(pth_data)
        tmp_path = tmp.name
    onnx_path = None
    try:
        desc = spandrel.ModelLoader().load_from_file(tmp_path)
        if not isinstance(desc, spandrel.ImageModelDescriptor):
            raise HTTPException(status_code=400, detail=f"Unsupported model type for conversion: {type(desc).__name__} (only single-image SR models)")
        model = desc.model.eval()
        in_ch = int(desc.input_channels)
        scale = int(desc.scale)
        dummy = torch.rand(1, in_ch, 64, 64)
        with tempfile.NamedTemporaryFile(suffix=".onnx", delete=False) as otmp:
            onnx_path = otmp.name
        # v1.8.3.9: torch >=2.9 switched torch.onnx.export to the dynamo
        # exporter by default, which requires onnxscript (live failure:
        # "No module named 'onnxscript'"). Pin the legacy TorchScript
        # exporter our dynamic_axes design targets; onnxscript is in the
        # converter image anyway as a fallback.
        torch.onnx.export(
            model, dummy, onnx_path, opset_version=17, dynamo=False,
            input_names=["input"], output_names=["output"],
            dynamic_axes={"input": {0: "batch", 2: "height", 3: "width"},
                          "output": {0: "batch", 2: "height", 3: "width"}})
        # Verify: the exported graph must reproduce the torch output
        sess = ort.InferenceSession(onnx_path, providers=["CPUExecutionProvider"])
        ort_out = sess.run(None, {sess.get_inputs()[0].name: dummy.numpy()})[0]
        with torch.no_grad():
            torch_out = model(dummy).numpy()
        max_diff = float(_np.abs(ort_out - torch_out).max())
        if max_diff > 1e-2:
            raise HTTPException(status_code=502, detail=f"Conversion verification failed (max output diff {max_diff:.4f}) - this architecture does not export cleanly")
        with open(onnx_path, "rb") as fh:
            onnx_bytes = fh.read()
        return onnx_bytes, scale, in_ch
    except HTTPException:
        raise
    except Exception as e:
        logger.warning("pth->onnx conversion failed", exc_info=True)
        raise HTTPException(status_code=400, detail=f"Conversion failed: {e}")
    finally:
        if os.path.exists(tmp_path):
            os.unlink(tmp_path)
        if onnx_path and os.path.exists(onnx_path):
            os.unlink(onnx_path)


