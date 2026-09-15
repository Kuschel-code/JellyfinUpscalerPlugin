"""Real OpenCV/NumPy pixel boundaries. The inference stub is not a GPU acceptance test."""
import cv2
import numpy as np
import pytest
from app.hdr_contract import require_pq, require_rgb16_png, reject_realtime_hdr


@pytest.fixture(autouse=True)
def hdr_test_auth(monkeypatch):
    monkeypatch.setenv("API_TOKEN", "hdr-contract-test")


def ramp():
    image = (np.arange(16 * 16 * 3, dtype=np.uint16).reshape(16, 16, 3) * 71 + 1000).astype(np.uint16)
    ok, data = cv2.imencode('.png', image)
    assert ok
    return image, data.tobytes()


@pytest.mark.parametrize('transfer', ['arib-std-b67', 'unknown', '', 'bt709', 'hdr10+'])
def test_hlg_unknown_and_dynamic_transfers_are_rejected(transfer):
    with pytest.raises(ValueError, match='Only PQ'):
        require_pq(transfer)


def test_real_rgb16_boundary_and_realtime_rejection():
    image, data = ramp()
    require_pq('smpte2084')
    require_rgb16_png(data)
    np.testing.assert_array_equal(cv2.imdecode(np.frombuffer(data, np.uint8), cv2.IMREAD_UNCHANGED), image)
    assert np.any(image % 257 != 0)
    with pytest.raises(ValueError, match='16-bit RGB'):
        require_rgb16_png(cv2.imencode('.png', (image // 257).astype(np.uint8))[1].tobytes())
    with pytest.raises(ValueError, match='not supported'):
        reject_realtime_hdr(data)


def test_actual_hdr_pipeline_keeps_rgb16_and_native_scale(client, monkeypatch):
    from app import main
    image, data = ramp()
    monkeypatch.setattr(main, 'cv2', cv2)  # actual pixel libraries, only the model is substituted
    main.state.current_model = 'synthetic-x4'
    main.state.current_model_type = 'onnx'
    main.state.onnx_model_scale = 4
    monkeypatch.setattr(main, 'upscale_image_array', lambda x: cv2.resize(x, (64, 64), interpolation=cv2.INTER_LINEAR))
    result = main.upscale_image_hdr(data)
    require_rgb16_png(result)
    decoded = cv2.imdecode(np.frombuffer(result, np.uint8), cv2.IMREAD_UNCHANGED)
    assert decoded.shape == (64, 64, 3)
    assert decoded.dtype == np.uint16
    assert len(np.unique(decoded % 257)) > 10


def test_hdr_identity_inference_preserves_neutral_pq_luminance(client, monkeypatch):
    """PQ luminance must not be divided by linear luminance during reconstruction."""
    from app import main
    monkeypatch.setattr(main, 'cv2', cv2)
    levels = np.array([0, 1000, 10000, 32768, 50000, 65535], dtype=np.uint16)
    original = np.repeat(levels[np.newaxis, :, np.newaxis], 3, axis=2)
    sdr, luminance = main.tonemap_hdr_to_sdr(original)
    reconstructed = main.inverse_tonemap_sdr_to_hdr(sdr, luminance, original, 1)
    np.testing.assert_allclose(reconstructed.astype(np.int32), original.astype(np.int32), atol=2, rtol=0)


def test_hdr_black_model_output_never_becomes_bright_source_pixels(client, monkeypatch):
    from app import main
    monkeypatch.setattr(main, 'cv2', cv2)
    original = np.full((16, 16, 3), 50000, dtype=np.uint16)
    sdr, luminance = main.tonemap_hdr_to_sdr(original)
    assert np.all(sdr > 0)
    reconstructed = main.inverse_tonemap_sdr_to_hdr(np.zeros_like(sdr), luminance, original, 1)
    assert np.all(reconstructed == 0)


def test_hdr_endpoint_requires_explicit_transfer(client):
    response = client.post('/upscale-hdr', headers={'X-Api-Token': 'hdr-contract-test'},
                           files={'file': ('frame.png', ramp()[1], 'image/png')})
    assert response.status_code == 422
    assert 'Only PQ' in response.json()['detail']


@pytest.mark.parametrize('primaries', ['', 'bt709', 'unknown'])
def test_hdr_endpoint_requires_bt2020_primaries(client, primaries):
    response = client.post('/upscale-hdr', headers={'X-Api-Token': 'hdr-contract-test'},
                           files={'file': ('frame.png', ramp()[1], 'image/png')},
                           data={'transfer': 'smpte2084', 'primaries': primaries})
    assert response.status_code == 422
    assert 'BT.2020' in response.json()['detail']


def test_hdr_endpoint_accepts_explicit_pq_bt2020_rgb16(client, monkeypatch):
    from app import main
    monkeypatch.setattr(main, 'cv2', cv2)
    main.state.current_model = 'synthetic-x4'
    main.state.current_model_type = 'onnx'
    main.state.onnx_model_scale = 4
    main.state.onnx_session = object()
    monkeypatch.setattr(main, 'upscale_image_array', lambda x: cv2.resize(x, (64, 64)))
    response = client.post('/upscale-hdr', headers={'X-Api-Token': 'hdr-contract-test'},
                           files={'file': ('frame.png', ramp()[1], 'image/png')},
                           data={'transfer': 'smpte2084', 'primaries': 'bt2020'})
    assert response.status_code == 200, response.text
    require_rgb16_png(response.content)


@pytest.mark.parametrize('transfer', ['arib-std-b67', 'unknown', 'hdr10+'])
def test_hdr_endpoint_rejects_transfer_before_model_work(client, transfer):
    response = client.post('/upscale-hdr', headers={'X-Api-Token': 'hdr-contract-test'}, files={'file': ('frame.png', ramp()[1], 'image/png')}, data={'transfer': transfer})
    assert response.status_code == 422
    assert 'Only PQ' in response.json()['detail']


def test_multiframe_endpoint_never_downconverts_16bit(client):
    from app import main
    main.state.current_model = 'synthetic'
    main.state.current_model_input_frames = 1
    response = client.post('/upscale-video-chunk', headers={'X-Api-Token': 'hdr-contract-test'}, files={'frame_0': ('frame.png', ramp()[1], 'image/png')})
    assert response.status_code == 422
    assert 'HDR realtime and multi-frame' in response.json()['detail']
