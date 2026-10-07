"""Issue #98 - AMD GPU (RX 6800) detected but never used.

Three independent defects, each enough on its own to keep an AMD box on the CPU or to hide
its GPU:

1. load_onnx_model had no ROCm chain at all. The AMD image listed ROCMExecutionProvider and
   every model still loaded on CPUExecutionProvider, because nothing ever asked for ROCm.
2. The onnxruntime-rocm wheel the image installs is built against ROCm 7.2.4
   (libamdhip64.so.7), the base shipped ROCm 6.2 (libamdhip64.so.6). get_available_providers()
   still lists ROCm - it reports the build config, not a successful load - so the build guard
   passed, while onnxruntime silently fell back to the CPU at session creation.
3. /gpus only knew nvidia-smi and Intel render nodes, so the dashboard said "no GPUs".

And one trap for the fix itself: with a correct image but no reachable GPU, onnxruntime-rocm
does not raise - it ABORTS the process ("HIP failure 100: no ROCm-capable device is detected",
verified in the real rocm/dev-ubuntu-22.04:7.2.4-complete base). ROCm is therefore probed in a
child process before the service creates a ROCm session itself.
"""
import asyncio
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import pytest


class _FakeSession:
    def __init__(self, providers, run_error=None):
        self._providers = providers
        self._run_error = run_error

    def get_providers(self):
        return self._providers

    def get_inputs(self):
        return [SimpleNamespace(name="x", shape=[1, 3, "h", "w"])]

    def run(self, *_a, **_k):
        if self._run_error:
            raise RuntimeError(self._run_error)
        return [np.zeros(1, dtype=np.float32)]


def _fake_ort(created, *, rocm_works, run_error=None):
    """onnxruntime-rocm stand-in. Like the real one, a ROCm EP that cannot start does NOT
    raise: the session comes back on the plain CPU provider."""
    def inference_session(_path, _opts=None, providers=None, provider_options=None, **_kw):
        created.append(providers[0])
        if providers[0] == "ROCMExecutionProvider":
            if not rocm_works:
                return _FakeSession(["CPUExecutionProvider"])
            return _FakeSession(["ROCMExecutionProvider", "CPUExecutionProvider"], run_error)
        return _FakeSession(["CPUExecutionProvider"])

    return SimpleNamespace(
        get_available_providers=lambda: ["ROCMExecutionProvider", "CPUExecutionProvider"],
        SessionOptions=lambda: SimpleNamespace(),
        GraphOptimizationLevel=SimpleNamespace(ORT_ENABLE_ALL=99),
        InferenceSession=inference_session,
    )


@pytest.fixture
def amd_service(client, monkeypatch):
    from app import main
    monkeypatch.delenv("ONNX_PROVIDERS", raising=False)
    monkeypatch.setattr(main, "ONNX_AVAILABLE", True)
    monkeypatch.setattr(main.state, "use_gpu", True)
    monkeypatch.setattr(main.state, "gpu_device_id", 0)
    monkeypatch.setattr(main, "_rocm_unavailable_reason", lambda: "rocm provider cannot be loaded (test)")
    monkeypatch.setattr(main, "_probe_rocm_subprocess", lambda *_a: None)   # probe says: ROCm works
    return main


def _load(main):
    return asyncio.run(main.load_onnx_model("fake-x2", {"scale": 2}, Path("fake.onnx")))


# ---------------------------------------------------------------------------
# 1) the chain exists and is used
# ---------------------------------------------------------------------------

def test_amd_model_loads_on_rocm(amd_service, monkeypatch):
    main = amd_service
    created = []
    monkeypatch.setattr(main, "ort", _fake_ort(created, rocm_works=True))
    assert _load(main) is True
    assert created == ["ROCMExecutionProvider"], created
    assert main.state.providers[0] == "ROCMExecutionProvider"
    assert main.gpu_is_active() is True
    assert main.state.gpu_unavailable_reason is None


def test_rocm_is_not_requested_when_the_user_turned_the_gpu_off(amd_service, monkeypatch):
    main = amd_service
    created = []
    monkeypatch.setattr(main.state, "use_gpu", False)
    monkeypatch.setattr(main, "ort", _fake_ort(created, rocm_works=True))
    assert _load(main) is True
    assert "ROCMExecutionProvider" not in created


# ---------------------------------------------------------------------------
# 2) a ROCm EP that does not start is caught and explained
# ---------------------------------------------------------------------------

def test_silent_rocm_fallback_is_caught_and_explained(amd_service, monkeypatch):
    main = amd_service
    created = []
    monkeypatch.setattr(main, "ort", _fake_ort(created, rocm_works=False))
    assert _load(main) is True
    assert created == ["ROCMExecutionProvider", "CPUExecutionProvider"], created
    assert main.state.providers == ["CPUExecutionProvider"]
    assert main.gpu_is_active() is False
    assert main.state.gpu_unavailable_reason == "rocm provider cannot be loaded (test)"


def test_failed_probe_keeps_rocm_out_of_this_process(amd_service, monkeypatch):
    """A probe that died (the abort case) must not be followed by an in-process ROCm session."""
    main = amd_service
    created = []
    monkeypatch.setattr(main, "_probe_rocm_subprocess",
                        lambda *_a: "HIP failure 100: no ROCm-capable device is detected (child killed by signal 6)")
    monkeypatch.setattr(main, "ort", _fake_ort(created, rocm_works=True))
    assert _load(main) is True
    assert "ROCMExecutionProvider" not in created, created
    assert main.state.providers == ["CPUExecutionProvider"]
    assert "HIP failure 100" in main.state.gpu_unavailable_reason
    assert "rocm provider cannot be loaded (test)" in main.state.gpu_unavailable_reason


def test_failed_test_inference_on_rocm_gives_a_reason(amd_service, monkeypatch):
    main = amd_service
    monkeypatch.setattr(main, "ort", _fake_ort([], rocm_works=True, run_error="miopenStatusUnknownError"))
    assert _load(main) is True
    assert main.state.providers == ["CPUExecutionProvider"]
    assert "miopenStatusUnknownError" in main.state.gpu_unavailable_reason


def test_load_error_names_the_missing_library(client, monkeypatch):
    """The real defect: the reason must say it is the IMAGE, so nobody debugs their host."""
    from app import main
    monkeypatch.setattr(main, "_rocm_provider_load_error",
                        lambda: "libamdhip64.so.7: cannot open shared object file: No such file or directory")
    reason = main._rocm_unavailable_reason()
    assert "libamdhip64.so.7" in reason
    assert "image" in reason and "not your setup" in reason


def test_missing_kfd_is_reported(client, monkeypatch):
    from app import main
    monkeypatch.setattr(main, "_rocm_provider_load_error", lambda: None)
    real_exists = Path.exists
    monkeypatch.setattr(Path, "exists", lambda self: False if str(self) == "/dev/kfd" else real_exists(self))
    assert "/dev/kfd" in main._rocm_unavailable_reason()


def _fake_rocm_lib(main, monkeypatch, tmp_path):
    capi = tmp_path / "onnxruntime" / "capi"
    capi.mkdir(parents=True)
    (capi / "libonnxruntime_providers_rocm.so").write_bytes(b"\x7fELF")
    monkeypatch.setattr(main, "ort", SimpleNamespace(__file__=str(tmp_path / "onnxruntime" / "__init__.py")))


def test_provider_load_error_lists_what_ldd_cannot_find(client, monkeypatch, tmp_path):
    """The ROCm 6.2 base + ROCm 7.2.4 wheel case, as ldd prints it."""
    from app import main
    _fake_rocm_lib(main, monkeypatch, tmp_path)
    ldd_out = ("\tlibhipblas.so.3 => not found\n\tlibMIOpen.so.1 => /opt/rocm/lib/libMIOpen.so.1 (0x1)\n"
               "\tlibamdhip64.so.7 => not found\n\tlibamdhip64.so.7 => not found\n")
    monkeypatch.setattr(main.subprocess, "run", lambda *a, **k: SimpleNamespace(returncode=0, stdout=ldd_out, stderr=""))
    assert main._rocm_provider_load_error() == "libhipblas.so.3, libamdhip64.so.7 not found"


def test_provider_that_resolves_has_no_load_error(client, monkeypatch, tmp_path):
    from app import main
    _fake_rocm_lib(main, monkeypatch, tmp_path)
    monkeypatch.setattr(main.subprocess, "run", lambda *a, **k: SimpleNamespace(
        returncode=0, stdout="\tlibamdhip64.so.7 => /opt/rocm/lib/libamdhip64.so.7 (0x1)\n", stderr=""))
    assert main._rocm_provider_load_error() is None


def test_library_check_never_dlopens_the_rocm_provider(client, monkeypatch, tmp_path):
    """dlopen runs HIP initialisation, which aborts the whole process without a GPU."""
    import ctypes
    from app import main
    _fake_rocm_lib(main, monkeypatch, tmp_path)
    monkeypatch.setattr(main.subprocess, "run", lambda *a, **k: SimpleNamespace(returncode=0, stdout="", stderr=""))
    def no_dlopen(*_a, **_k):
        raise AssertionError("must not dlopen the ROCm provider")
    monkeypatch.setattr(ctypes, "CDLL", no_dlopen)
    assert main._rocm_provider_load_error() is None


def test_no_rocm_library_means_no_load_error(client, monkeypatch, tmp_path):
    from app import main
    (tmp_path / "onnxruntime" / "capi").mkdir(parents=True)
    monkeypatch.setattr(main, "ort", SimpleNamespace(__file__=str(tmp_path / "onnxruntime" / "__init__.py")))
    assert main._rocm_provider_load_error() is None


def test_probe_reports_the_hip_error_of_an_aborted_child(client, monkeypatch):
    from app import main
    stderr = ("terminate called after throwing an instance of 'onnxruntime::OnnxRuntimeException'\n"
              "  what():  rocm_call.cc:141 ... HIP failure 100: no ROCm-capable device is detected ; GPU=-1\n")
    monkeypatch.setattr(main.subprocess, "run", lambda *a, **k: SimpleNamespace(returncode=-6, stdout="", stderr=stderr))
    err = main._probe_rocm_subprocess("/app/models/x.onnx", 0)
    assert err.startswith("HIP failure 100: no ROCm-capable device is detected")
    assert "signal 6" in err


def test_probe_reports_a_silent_cpu_fallback(client, monkeypatch):
    from app import main
    monkeypatch.setattr(main.subprocess, "run", lambda *a, **k: SimpleNamespace(
        returncode=1, stdout="FAIL:onnxruntime fell back to the CPU provider\n", stderr="EP Error ..."))
    assert main._probe_rocm_subprocess("/app/models/x.onnx", 0) == "FAIL:onnxruntime fell back to the CPU provider"


def test_probe_ok(client, monkeypatch):
    from app import main
    monkeypatch.setattr(main.subprocess, "run", lambda *a, **k: SimpleNamespace(returncode=0, stdout="OK\n", stderr=""))
    assert main._probe_rocm_subprocess("/app/models/x.onnx", 0) is None


def test_doctor_flags_an_unloadable_rocm_provider(client, monkeypatch):
    from app import main
    monkeypatch.setattr(main, "ONNX_AVAILABLE", True)
    monkeypatch.setattr(main.ort, "get_available_providers",
                        lambda: ["ROCMExecutionProvider", "CPUExecutionProvider"], raising=False)
    monkeypatch.setattr(main, "_rocm_provider_load_error", lambda: "libhipblas.so.3: cannot open shared object file")
    checks = {c["check"]: c for c in client.get("/doctor").json()["checks"]}
    assert checks["rocm_provider_loadable"]["status"] == "fail"
    assert "libhipblas.so.3" in checks["rocm_provider_loadable"]["detail"]


# ---------------------------------------------------------------------------
# 3) /gpus lists AMD cards
# ---------------------------------------------------------------------------

def _drm(tmp_path, node, vendor, vram_total=None, vram_used=None, device="0x73bf"):
    dev = tmp_path / node / "device"
    dev.mkdir(parents=True)
    (dev / "vendor").write_text(vendor + "\n")
    (dev / "device").write_text(device + "\n")
    if vram_total is not None:
        (dev / "mem_info_vram_total").write_text(f"{vram_total}\n")
        (dev / "mem_info_vram_used").write_text(f"{vram_used}\n")


def test_amd_render_node_is_listed_with_vram(client, tmp_path):
    from app import main
    _drm(tmp_path, "renderD128", "0x1002", vram_total=16 * 1024 ** 3, vram_used=1024 ** 3)
    _drm(tmp_path, "renderD129", "0x8086")   # an Intel iGPU next to it is not an AMD GPU
    gpus = main._amd_gpus_from_sysfs(tmp_path, start_index=1)
    assert len(gpus) == 1
    g = gpus[0]
    assert g["type"] == "amd" and g["index"] == 1
    assert g["memory_total_mb"] == 16384 and g["memory_free_mb"] == 15360


def test_amd_without_vram_counters_still_listed(client, tmp_path):
    from app import main
    _drm(tmp_path, "renderD128", "0x1002")
    gpus = main._amd_gpus_from_sysfs(tmp_path)
    assert len(gpus) == 1 and gpus[0]["memory_total_mb"] == 0


def test_gpus_endpoint_includes_amd(client, monkeypatch):
    from app import main
    fake = [{"index": 0, "name": "Navi 21", "memory_total_mb": 16384, "memory_free_mb": 15000,
             "driver": "amdgpu", "type": "amd"}]
    monkeypatch.setattr(main, "_amd_gpus_from_sysfs", lambda *a, **k: fake)
    body = client.get("/gpus").json()
    assert any(g["type"] == "amd" for g in body["gpus"])


def test_doctor_calls_the_amd_image_amd_even_without_a_gpu(client, monkeypatch):
    """docker7-amd without devices was reported as 'CPU image, no GPU image selected'."""
    from app import main
    monkeypatch.setattr(main, "ONNX_AVAILABLE", True)
    monkeypatch.setattr(main.ort, "get_available_providers",
                        lambda: ["ROCMExecutionProvider", "CPUExecutionProvider"], raising=False)
    monkeypatch.setattr(main, "_rocm_provider_load_error", lambda: None)
    monkeypatch.setattr(main.state, "providers", ["CPUExecutionProvider"])
    monkeypatch.setattr(main, "_render_node_access", lambda *a, **k: [])
    # A render node IS passed (as in the report), only /dev/kfd is missing.
    real_exists, real_glob = Path.exists, Path.glob
    monkeypatch.setattr(Path, "exists", lambda self: {"/dev/kfd": False, "/dev/dxg": False, "/dev/dri": True}.get(
        str(self), real_exists(self)))
    monkeypatch.setattr(Path, "glob", lambda self, pat: iter([Path("/dev/dri/renderD128")])
                        if str(self) == "/dev/dri" else real_glob(self, pat))
    checks = {c["check"]: c for c in client.get("/doctor").json()["checks"]}
    assert "amd" in checks["backend"]["detail"]
    assert "renderD*=True" in checks["device_passthrough"]["detail"]
    assert checks["device_passthrough"]["status"] == "fail"
    assert "/dev/kfd=False" in checks["device_passthrough"]["detail"]


# ---------------------------------------------------------------------------
# detector / face restore / RIFE: the single-session loaders
# ---------------------------------------------------------------------------

def test_rocm_usable_for_follows_the_probe(client, monkeypatch):
    from app import main
    monkeypatch.setattr(main, "ONNX_AVAILABLE", True)
    monkeypatch.setattr(main.state, "use_gpu", True)
    monkeypatch.setattr(main.ort, "get_available_providers",
                        lambda: ["ROCMExecutionProvider", "CPUExecutionProvider"], raising=False)
    monkeypatch.setattr(main, "_probe_rocm_subprocess", lambda *_a: None)
    assert main._rocm_usable_for("/app/models/yolo.onnx") is True
    monkeypatch.setattr(main, "_probe_rocm_subprocess", lambda *_a: "HIP failure 100")
    assert main._rocm_usable_for("/app/models/yolo.onnx") is False


def test_rocm_usable_for_does_not_probe_without_rocm_or_gpu_intent(client, monkeypatch):
    from app import main
    calls = []
    monkeypatch.setattr(main, "_probe_rocm_subprocess", lambda *a: calls.append(a))
    monkeypatch.setattr(main, "ONNX_AVAILABLE", True)
    monkeypatch.setattr(main.ort, "get_available_providers", lambda: ["CPUExecutionProvider"], raising=False)
    monkeypatch.setattr(main.state, "use_gpu", True)
    assert main._rocm_usable_for("x.onnx") is False
    monkeypatch.setattr(main.ort, "get_available_providers",
                        lambda: ["ROCMExecutionProvider", "CPUExecutionProvider"], raising=False)
    monkeypatch.setattr(main.state, "use_gpu", False)
    assert main._rocm_usable_for("x.onnx") is False
    assert calls == []


def test_probe_feeds_every_input_with_its_dtype(client, monkeypatch, tmp_path):
    """YOLOv3 exports take the image AND its size. Feeding only the first input made the probe
    fail for a working GPU. Runs the probe's own code against a real two-input model, with the
    provider swapped for CPU (no AMD GPU here)."""
    onnx = pytest.importorskip("onnx")
    pytest.importorskip("onnxruntime")
    import subprocess
    real_run = subprocess.run   # main.subprocess IS this module; keep the original before patching
    from onnx import TensorProto, helper
    from app import main
    img = helper.make_tensor_value_info("image", TensorProto.FLOAT, [1, 3, None, None])
    size = helper.make_tensor_value_info("image_shape", TensorProto.INT64, [1, 2])
    out = helper.make_tensor_value_info("out", TensorProto.FLOAT, None)
    out2 = helper.make_tensor_value_info("shape_out", TensorProto.INT64, None)
    graph = helper.make_graph([helper.make_node("Relu", ["image"], ["out"]),
                               helper.make_node("Identity", ["image_shape"], ["shape_out"])],
                              "two_inputs", [img, size], [out, out2])
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 17)])
    model.ir_version = 8
    path = tmp_path / "two_inputs.onnx"
    onnx.save(model, str(path))

    def cpu_run(args, **kw):
        code = args[2].replace("'ROCMExecutionProvider', 'CPUExecutionProvider'", "'CPUExecutionProvider'") \
                      .replace("[{'device_id': int(sys.argv[2])}, {}]", "[{}]") \
                      .replace("'ROCMExecutionProvider' not in", "'CPUExecutionProvider' not in")
        return real_run([args[0], "-c", code] + list(args[3:]), **kw)
    monkeypatch.setattr(main.subprocess, "run", cpu_run)
    assert main._probe_rocm_subprocess(str(path), 0) is None
