"""Regression guards for the multi-frame VSR contract (v1.8.2).

The multi-frame VSR pipeline already exists (sliding-window consumer +
/upscale-video-chunk endpoint + EDVR/RealBasicVSR/AnimeSR catalog entries). These
lock that contract: the models are gated as experimental self-host (no public ONNX
mirror) and the consumer endpoint the C# plugin posts windows to is registered.
"""


def test_multiframe_vsr_models_present_and_gated(client):
    from app import main
    for key in ("edvr-m-x4", "realbasicvsr-x4", "animesr-v2-x4"):
        m = main.AVAILABLE_MODELS[key]
        assert m["category"] == "video-sr"
        assert m["input_frames"] == 5, "VSR models use a 5-frame temporal window"
        assert m.get("available") is False, "no public ONNX mirror -> not auto-downloadable"
        assert m.get("self_host") is True, "marked experimental self-host like ifrnet/cain"


def test_upscale_video_chunk_endpoint_registered(client):
    from app import main
    paths = {getattr(r, "path", None) for r in main.app.routes}
    assert "/upscale-video-chunk" in paths, "the multi-frame sliding-window consumer endpoint"


def test_single_and_multi_frame_models_distinguishable(client):
    """input_frames separates single-frame SR (1) from multi-frame VSR (>1)."""
    from app import main
    assert main.AVAILABLE_MODELS["edvr-m-x4"]["input_frames"] > 1
    # a plain SR model defaults to single-frame
    assert main.AVAILABLE_MODELS["realesrgan-x4"].get("input_frames", 1) == 1
