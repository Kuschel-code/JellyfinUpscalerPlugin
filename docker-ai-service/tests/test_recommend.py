"""Tests for the hardware-aware model recommendation (recommend_model + /recommend).

Uses the `client` fixture to load the app (heavy deps mocked); the branch tests
then drive recommend_model() directly with synthetic detected-hardware state.
"""


def _set_hw(main, providers, gpu_list, vram, cores):
    main.state.providers = providers
    main.state.gpu_list = gpu_list
    main.state.gpu_memory = vram
    main.state.cpu_cores = cores
    main.state.gpu_name = (gpu_list[0]["name"] if gpu_list else "None")


def test_recommend_endpoint_returns_catalog_model(client):
    from app import main
    r = client.get("/recommend")
    assert r.status_code == 200
    d = r.json()
    assert d["recommended_model"] in main.AVAILABLE_MODELS
    assert d["recommended_scale"] in (2, 3, 4)
    assert d["tier"] and d["reason"] and "hardware" in d


def test_weak_cpu_gets_light_model(client):
    from app import main
    _set_hw(main, ["CPUExecutionProvider"], [], "Unknown", 2)
    rec = main.recommend_model()
    assert rec["recommended_model"] == "fsrcnn-x2"
    assert rec["recommended_scale"] == 2
    assert rec["tier"] == "weak-cpu"
    assert rec["hardware"]["gpu_active"] is False


def test_strong_cpu_tier(client):
    from app import main
    _set_hw(main, ["CPUExecutionProvider"], [], "Unknown", 16)
    rec = main.recommend_model()
    assert rec["tier"] == "strong-cpu"
    assert rec["recommended_model"] == "fsrcnn-x2"


def test_strong_gpu_gets_realesrgan(client):
    from app import main
    _set_hw(main, ["CUDAExecutionProvider", "CPUExecutionProvider"], [{"name": "RTX 4070"}], "12000 MB", 16)
    rec = main.recommend_model()
    assert rec["recommended_model"] == "realesrgan-x4"
    assert rec["tier"] == "strong-gpu"
    assert rec["hardware"]["gpu_active"] is True


def test_low_vram_gpu_gets_tiled(client):
    from app import main
    _set_hw(main, ["CUDAExecutionProvider"], [{"name": "GTX 1650"}], "4096 MB", 8)
    rec = main.recommend_model()
    assert rec["recommended_model"] == "realesrgan-x4-256"
    assert rec["tier"] == "mid-gpu"


def test_recommended_and_alts_always_in_catalog(client):
    from app import main
    _set_hw(main, ["OpenVINOExecutionProvider"], [{"name": "Arc A380"}], "garbage", 4)
    rec = main.recommend_model()
    assert rec["recommended_model"] in main.AVAILABLE_MODELS
    assert all(a in main.AVAILABLE_MODELS for a in rec["alternatives"])
    assert rec["recommended_model"] not in rec["alternatives"]
