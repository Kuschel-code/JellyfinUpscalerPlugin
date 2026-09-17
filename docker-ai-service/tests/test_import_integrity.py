"""Exercise download limits and export verification without model downloads."""
import asyncio

import httpx
import numpy as np
import pytest
from fastapi import HTTPException


@pytest.mark.parametrize('oversize', [False, True])
def test_import_download_stops_at_cap(client, monkeypatch, oversize):
    from app import model_import
    cap = 64 * 1024
    monkeypatch.setattr(model_import, 'MAX_MODEL_UPLOAD_BYTES', cap)

    class Body(httpx.AsyncByteStream):
        closed = False
        async def __aiter__(self):
            yield b'a' * cap
            if oversize:
                yield b'b' * cap
                pytest.fail('Download continued after crossing its memory limit')
        async def aclose(self):
            self.closed = True

    body = Body()
    original_client = httpx.AsyncClient
    transport = httpx.MockTransport(lambda request: httpx.Response(200, stream=body))
    monkeypatch.setattr(model_import.httpx, 'AsyncClient',
                        lambda **kwargs: original_client(transport=transport, **kwargs))
    if oversize:
        with pytest.raises(HTTPException) as error:
            asyncio.run(model_import._download_capped('https://github.com/example/model.onnx'))
        assert error.value.status_code == 502
        assert 'limit' in error.value.detail
    else:
        assert asyncio.run(model_import._download_capped('https://github.com/example/model.onnx')) == b'a' * cap
    assert body.closed


@pytest.mark.parametrize('bad', [np.nan, np.inf, -np.inf])
def test_converter_rejects_nonfinite_output(client, bad):
    from app.model_import import _validate_conversion_output
    original = np.zeros((1, 3, 4, 4), dtype=np.float32)
    exported = original.copy(); exported[0, 0, 0, 0] = bad
    with pytest.raises(HTTPException, match='non-finite'):
        _validate_conversion_output(original, exported)
    with pytest.raises(HTTPException, match='non-finite'):
        _validate_conversion_output(exported, original)


def test_converter_rejects_broadcastable_shape_and_bad_pixels(client):
    from app.model_import import _validate_conversion_output
    original = np.zeros((1, 3, 4, 4), dtype=np.float32)
    with pytest.raises(HTTPException, match='shape mismatch'):
        _validate_conversion_output(original, original[:, :1])
    with pytest.raises(HTTPException, match='max output diff'):
        _validate_conversion_output(original, original + 0.1)
    _validate_conversion_output(original, original + 0.001)


@pytest.mark.parametrize('name,extension', [('model.pth', '.pth'), ('model.pt', '.pt'),
    ('MODEL.SAFETENSORS', '.safetensors'), ('https://github.com/a/model.safetensors?download=1', '.safetensors')])
def test_converter_preserves_source_format_for_spandrel(client, monkeypatch, name, extension):
    import sys
    from pathlib import Path
    from types import SimpleNamespace
    from app import model_import
    seen = []

    class Loader:
        def load_from_file(self, path):
            seen.append(Path(path))
            assert Path(path).suffix == extension
            assert Path(path).read_bytes() == b'model-content'
            raise HTTPException(status_code=418, detail='format reached correct loader')

    monkeypatch.setattr(model_import, '_converter_available', lambda: True)
    monkeypatch.setitem(sys.modules, 'spandrel', SimpleNamespace(ModelLoader=Loader))
    with pytest.raises(HTTPException) as error:
        model_import._convert_pth_bytes_to_onnx(b'model-content', name)
    assert error.value.status_code == 418
    assert len(seen) == 1 and not seen[0].exists()


def test_converter_upload_passes_its_original_filename(client, monkeypatch):
    from app import main
    monkeypatch.setenv('API_TOKEN', 'disable')
    monkeypatch.setattr(main, 'ENABLE_MODEL_UPLOAD', True)
    monkeypatch.setattr(main, '_converter_available', lambda: True)
    seen = []
    def convert(data, name):
        seen.append((data, name))
        return b'onnx', 2, 3
    monkeypatch.setattr(main, '_convert_pth_bytes_to_onnx', convert)
    monkeypatch.setattr(main, '_ingest_onnx_bytes', lambda *args: {'status': 'success'})
    response = client.post('/models/convert-upload', data={'model_name': 'custom-model'},
                           files={'file': ('actual.safetensors', b'weights')})
    assert response.status_code == 200, response.text
    assert seen == [(b'weights', 'actual.safetensors')]
