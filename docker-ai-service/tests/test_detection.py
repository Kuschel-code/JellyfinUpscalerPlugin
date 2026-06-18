"""Tests for hardware detection (detect_hardware + gpu_is_active).

The reviewer flagged this as the most valuable missing test: the detection
paths drive the whole pipeline (which provider, which model, GPU vs CPU) yet
had zero coverage. We mock the external probes (nvidia-smi/rocm-smi) so the
parsing + GPU-selection logic is exercised deterministically on any host.

Uses the `client` fixture only to import `app.main` with the heavy deps mocked;
the tests then drive detect_hardware()/gpu_is_active() directly.
"""
from types import SimpleNamespace
from unittest.mock import patch


def _reset_hw(main):
    """Detection appends to gpu_list, so wipe the detected state per test."""
    main.state.gpu_list = []
    main.state.gpu_name = "Unknown"
    main.state.gpu_memory = "Unknown"
    main.state.gpu_device_id = 0
    main.state.providers = []


def _nvidia_run(gpu_csv, cc_csv="8.9\n"):
    """subprocess.run side_effect: answer nvidia-smi queries, fail everything
    else with FileNotFoundError (simulating the binary being absent)."""
    def run(cmd, **kwargs):
        if cmd[:1] == ["nvidia-smi"]:
            if "--query-gpu=index,name,memory.total" in cmd:
                return SimpleNamespace(returncode=0, stdout=gpu_csv)
            if "--query-gpu=compute_cap" in cmd:
                return SimpleNamespace(returncode=0, stdout=cc_csv)
        raise FileNotFoundError(cmd[0])
    return run


def _no_gpu_run():
    def run(cmd, **kwargs):
        raise FileNotFoundError(cmd[0])
    return run


# ---------------------------------------------------------------- gpu_is_active

def test_gpu_active_empty_providers_follows_intent(client):
    """Before any model loads (empty provider list) we must trust USE_GPU intent
    so a freshly-booted GPU box doesn't mis-report 'no GPU'."""
    from app import main
    main.state.providers = []
    main.state.use_gpu = True
    assert main.gpu_is_active() is True
    main.state.use_gpu = False
    assert main.gpu_is_active() is False


def test_gpu_active_cpu_only_provider_is_false(client):
    from app import main
    main.state.providers = ["CPUExecutionProvider"]
    assert main.gpu_is_active() is False


def test_gpu_active_true_for_each_accelerated_provider(client):
    from app import main
    for prov in (
        "CUDAExecutionProvider", "TensorrtExecutionProvider",
        "OpenVINOExecutionProvider", "ROCMExecutionProvider",
        "MIGraphXExecutionProvider", "CoreMLExecutionProvider",
        "DmlExecutionProvider",
    ):
        main.state.providers = [prov, "CPUExecutionProvider"]
        assert main.gpu_is_active() is True, f"{prov} should count as GPU"


def test_gpu_active_ignores_use_gpu_once_providers_known(client):
    """Once the real provider list is populated it wins over the intent flag."""
    from app import main
    main.state.providers = ["CPUExecutionProvider"]
    main.state.use_gpu = True  # intent says GPU, reality says CPU
    assert main.gpu_is_active() is False


# --------------------------------------------------------------- detect_hardware

def test_detect_single_nvidia_gpu(client):
    from app import main
    _reset_hw(main)
    with patch.object(main.subprocess, "run",
                      side_effect=_nvidia_run("0, NVIDIA GeForce RTX 4070, 12282\n")):
        main.detect_hardware()
    assert len(main.state.gpu_list) == 1
    assert main.state.gpu_name == "NVIDIA GeForce RTX 4070"
    assert main.state.gpu_memory == "12282 MB"
    assert main.state.gpu_list[0]["type"] == "nvidia"
    assert main.state.gpu_list[0]["compute_capability"] == "8.9"


def test_detect_multi_gpu_honors_device_id(client):
    """gpu_device_id selects which detected GPU is the 'active' one; an
    out-of-range id must clamp to the last GPU (min(id, len-1))."""
    from app import main
    _reset_hw(main)
    csv = "0, RTX 4070, 12282\n1, RTX 4060, 8188\n"
    main.state.gpu_device_id = 1
    with patch.object(main.subprocess, "run",
                      side_effect=_nvidia_run(csv, cc_csv="8.9\n8.9\n")):
        main.detect_hardware()
    assert len(main.state.gpu_list) == 2
    assert main.state.gpu_name == "RTX 4060"        # device_id=1 -> second GPU
    assert main.state.gpu_memory == "8188 MB"


def test_detect_device_id_out_of_range_clamps(client):
    from app import main
    _reset_hw(main)
    main.state.gpu_device_id = 9  # only one GPU present
    with patch.object(main.subprocess, "run",
                      side_effect=_nvidia_run("0, RTX 4070, 12282\n")):
        main.detect_hardware()
    assert main.state.gpu_name == "RTX 4070"  # clamped to the single GPU


def test_detect_no_gpu_leaves_list_empty(client):
    """No GPU binaries present + ONNX disabled -> no phantom GPU, CPU only."""
    from app import main
    _reset_hw(main)
    with patch.object(main, "ONNX_AVAILABLE", False):
        with patch.object(main.subprocess, "run", side_effect=_no_gpu_run()):
            main.detect_hardware()
    assert main.state.gpu_list == []


def test_detect_always_populates_cpu(client):
    from app import main
    _reset_hw(main)
    main.state.cpu_cores = 0
    with patch.object(main, "ONNX_AVAILABLE", False):
        with patch.object(main.subprocess, "run", side_effect=_no_gpu_run()):
            main.detect_hardware()
    assert main.state.cpu_cores >= 1          # os.cpu_count() on any host
    assert main.state.cpu_name                # non-empty


# ------------------------------------------------------------- hardware_info API

def test_hardware_info_endpoint_shape(client):
    r = client.get("/hardware")
    assert r.status_code == 200
    d = r.json()
    for key in ("gpu", "cpu", "providers", "using_gpu", "gpu_list"):
        assert key in d, f"missing {key}"
    for key in ("name", "memory", "cuda_available"):
        assert key in d["gpu"]
    assert "cores" in d["cpu"]
