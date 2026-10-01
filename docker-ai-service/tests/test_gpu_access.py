"""Issue #90 - Intel Arc "Won't Use GPU".

The log in the report has two halves that were both invisible to the service:

  [OpenVINO] Device GPU is not available
  EP Error ... Falling back to ['CPUExecutionProvider'] and retrying.

The second line is onnxruntime itself rebuilding the session on the plain CPU provider
and *returning it*. No exception ever reaches load_onnx_model, so the
"OpenVINO GPU failed, trying OpenVINO CPU device" branch written for exactly this was
dead code for this failure, and the service carried on without ever saying why.

The usual reason is not the image: `group_add: render` resolves the group NAME inside the
container, where `render` is a different GID from the host group that owns
/dev/dri/renderD128, so the process cannot open the device at all.
"""
import asyncio
import os
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import pytest


# ---------------------------------------------------------------------------
# Can this process open the render node?
# ---------------------------------------------------------------------------

@pytest.mark.parametrize("mode,uid,groups,expected", [
    (0o660, 1000, [1000, 105], True),    # in the owning group
    (0o660, 1000, [1000, 999], False),   # same node, the container's own "render" GID
    (0o666, 1000, [1000], True),         # world-accessible node needs no group
    (0o640, 1000, [105], False),         # group may read but not write -> cannot run inference
    (0o660, 0, [], True),                # root
])
def test_node_accessibility_follows_mode_and_groups(mode, uid, groups, expected):
    from app import main
    # node owned by uid 0 / gid 105, like /dev/dri/renderD128 on a typical host
    assert main._render_node_accessible(mode, 0, 105, uid, groups) is expected


def test_render_node_access_reports_the_owning_gid(client, tmp_path):
    """The hint is only useful if it names the GID the *device* has, not a guess."""
    from app import main
    node = tmp_path / "renderD128"
    node.write_bytes(b"")
    node.chmod(0o660)
    # Pretend to be a different user so the real file owner/group do not match.
    other = os.getuid() + 12345
    nodes = main._render_node_access(tmp_path, uid=other, groups=[other])
    assert len(nodes) == 1
    assert nodes[0]["path"] == str(node)
    assert nodes[0]["gid"] == node.stat().st_gid
    assert nodes[0]["accessible"] is False


def test_no_render_node_means_no_access_problem(client, tmp_path):
    from app import main
    assert main._render_node_access(tmp_path) == []
    assert main.gpu_access_hint([]) is None


def test_hint_gives_the_numeric_gid_and_explains_why_the_name_fails(client):
    from app import main
    nodes = [{"path": "/dev/dri/renderD128", "gid": 105, "mode": "0o660", "accessible": False}]
    hint = main.gpu_access_hint(nodes, uid=1000, groups=[1000, 999])
    assert hint is not None
    assert 'group_add: ["105"]' in hint
    assert "--group-add 105" in hint
    assert "group_add: render" in hint and "different GID" in hint  # why the name is not enough


def test_hint_is_silent_when_any_node_is_usable(client):
    from app import main
    nodes = [
        {"path": "/dev/dri/renderD128", "gid": 105, "mode": "0o660", "accessible": False},
        {"path": "/dev/dri/renderD129", "gid": 105, "mode": "0o666", "accessible": True},
    ]
    assert main.gpu_access_hint(nodes, uid=1000, groups=[1000]) is None


# ---------------------------------------------------------------------------
# load_onnx_model against an onnxruntime that does what the log in #90 shows
# ---------------------------------------------------------------------------

class _FakeSession:
    def __init__(self, providers):
        self._providers = providers

    def get_providers(self):
        return self._providers

    def get_inputs(self):
        return [SimpleNamespace(name="x", shape=[1, 3, "h", "w"])]

    def run(self, *_a, **_k):
        return [np.zeros(1, dtype=np.float32)]


def _fake_ort(created, *, gpu_works, openvino_works=True):
    """onnxruntime stand-in. When the OpenVINO GPU device is missing it behaves like the
    real one: it does NOT raise, it returns a session rebuilt on the plain CPU provider."""
    def inference_session(_path, _opts=None, providers=None, provider_options=None, **_kw):
        device = ((provider_options or [{}])[0] or {}).get("device_type")
        created.append(device or providers[0])
        if providers and providers[0] == "OpenVINOExecutionProvider":
            if not openvino_works or (device == "GPU" and not gpu_works):
                return _FakeSession(["CPUExecutionProvider"])  # ORT's silent fallback
            return _FakeSession(["OpenVINOExecutionProvider", "CPUExecutionProvider"])
        return _FakeSession(["CPUExecutionProvider"])

    return SimpleNamespace(
        get_available_providers=lambda: ["OpenVINOExecutionProvider", "CPUExecutionProvider"],
        SessionOptions=lambda: SimpleNamespace(),
        GraphOptimizationLevel=SimpleNamespace(ORT_ENABLE_ALL=99),
        InferenceSession=inference_session,
    )


@pytest.fixture
def ov_service(client, monkeypatch):
    from app import main
    monkeypatch.delenv("ONNX_PROVIDERS", raising=False)
    monkeypatch.setattr(main, "ONNX_AVAILABLE", True)
    monkeypatch.setattr(main.state, "use_gpu", True)
    monkeypatch.setattr(main.state, "gpu_device_id", 0)
    # An unreadable render node, as in the report.
    monkeypatch.setattr(main, "_gpu_unavailable_reason", lambda: "render node not accessible (test)")
    return main


def _load(main):
    return asyncio.run(main.load_onnx_model("fake-x2", {"scale": 2}, Path("fake.onnx")))


def test_silent_ort_cpu_fallback_is_caught_and_openvino_cpu_device_is_used(ov_service, monkeypatch):
    main = ov_service
    created = []
    monkeypatch.setattr(main, "ort", _fake_ort(created, gpu_works=False))

    assert _load(main) is True

    # GPU tried first, then the OpenVINO *CPU device* - not the generic CPU provider chain.
    assert created == ["GPU", "CPU"], created
    assert "OpenVINOExecutionProvider" in main.state.providers
    assert main.state.openvino_on_cpu is True


def test_openvino_on_cpu_is_not_reported_as_gpu(ov_service, monkeypatch):
    """Dashboard / /health / /gpu-verify derive `using_gpu` from the provider list, which
    contains OpenVINOExecutionProvider even when it runs on the CPU device."""
    main = ov_service
    monkeypatch.setattr(main, "ort", _fake_ort([], gpu_works=False))
    _load(main)
    assert main.gpu_is_active() is False
    assert main.state.gpu_unavailable_reason == "render node not accessible (test)"


def test_working_gpu_is_untouched(ov_service, monkeypatch):
    """The fallback must not trigger when the GPU genuinely works."""
    main = ov_service
    created = []
    monkeypatch.setattr(main, "ort", _fake_ort(created, gpu_works=True))
    assert _load(main) is True
    assert created == ["GPU"], created
    assert main.state.openvino_on_cpu is False
    assert main.gpu_is_active() is True
    assert main.state.gpu_unavailable_reason is None


def test_broken_openvino_still_ends_on_the_plain_cpu_chain(ov_service, monkeypatch):
    """If the OpenVINO runtime itself is unusable the CPU-device retry also falls back to
    the plain CPU provider; that session must NOT be accepted as 'OpenVINO on CPU'."""
    main = ov_service
    created = []
    monkeypatch.setattr(main, "ort", _fake_ort(created, gpu_works=False, openvino_works=False))
    assert _load(main) is True
    assert created == ["GPU", "CPU", "CPUExecutionProvider"], created
    assert main.state.providers == ["CPUExecutionProvider"]
    assert main.state.openvino_on_cpu is False


# ---------------------------------------------------------------------------
# /doctor and /status say it
# ---------------------------------------------------------------------------

def test_doctor_flags_unreadable_render_node_with_the_numeric_fix(client, monkeypatch):
    from app import main
    monkeypatch.setattr(main, "_render_node_access", lambda *a, **k: [
        {"path": "/dev/dri/renderD128", "gid": 105, "mode": "0o660", "accessible": False}])
    checks = {c["check"]: c for c in client.get("/doctor").json()["checks"]}
    access = checks["gpu_device_access"]
    assert access["status"] == "fail", access
    assert 'group_add: ["105"]' in access["fix"], access


def test_doctor_gpu_device_access_ok_when_node_is_usable(client, monkeypatch):
    from app import main
    monkeypatch.setattr(main, "_render_node_access", lambda *a, **k: [
        {"path": "/dev/dri/renderD128", "gid": 105, "mode": "0o660", "accessible": True}])
    checks = {c["check"]: c for c in client.get("/doctor").json()["checks"]}
    assert checks["gpu_device_access"]["status"] == "ok"


def test_doctor_has_no_device_access_check_without_a_render_node(client, monkeypatch):
    from app import main
    monkeypatch.setattr(main, "_render_node_access", lambda *a, **k: [])
    names = {c["check"] for c in client.get("/doctor").json()["checks"]}
    assert "gpu_device_access" not in names


def test_status_exposes_why_the_gpu_is_not_used(client, monkeypatch):
    from app import main
    monkeypatch.setattr(main.state, "gpu_unavailable_reason", "render node not accessible (test)")
    body = client.get("/status").json()
    assert body["gpu_unavailable_reason"] == "render node not accessible (test)"
