'use strict';
// Behaviour tests for site/assets/support-diagnose.js - the part of the support chatbots that
// reads pasted /doctor, /status and /gpu-verify output and logs. Runs the real file in a vm
// sandbox with the real knowledge base, so a renamed KB entry or a changed signature fails here.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.join(__dirname, '..', 'site', 'assets');
const KB = JSON.parse(fs.readFileSync(path.join(root, 'support-kb.json'), 'utf8'));
function load() {
  const sandbox = { window: {}, console };
  sandbox.window.window = sandbox.window;
  vm.createContext(sandbox);
  vm.runInContext(fs.readFileSync(path.join(root, 'support-diagnose.js'), 'utf8'), sandbox);
  return sandbox.window.SupportDiagnose;
}
const D = load();
const LATEST = { version: '1.8.3.36' };
const ids = r => r.entries.map(e => e.id);

test('module exposes analyze and is loadable without a DOM', () => {
  assert.equal(typeof D.analyze, 'function');
});

test('plain questions are not treated as diagnostics', () => {
  for (const q of ['how do i install the plugin', 'what is the latest version', 'hi', '', 'is amd supported?']) {
    assert.equal(D.analyze(q, KB, LATEST), null, q);
  }
});

// ---- the log from issue #90 ---------------------------------------------------------------
const LOG_90 = [
  "2026-09-28 18:17:45,381 | INFO | Trying chain 1/2: OpenVINO GPU (['OpenVINOExecutionProvider', 'CPUExecutionProvider'])",
  '[E:onnxruntime:Default, provider_bridge_ort.cc:2197 Create] [ERROR] [OpenVINO] Device GPU is not available',
  '*************** EP Error ***************',
  "Falling back to ['CPUExecutionProvider'] and retrying.",
].join('\n');

test('issue #90 log -> the GPU-device entry, with the render-group explanation', () => {
  const r = D.analyze(LOG_90, KB, LATEST);
  assert.ok(r, 'a diagnosis');
  assert.equal(ids(r)[0], 'gpu-device-not-available');
  assert.match(r.markdown, /group_add/);
});

test('a log that already names the device GID gets the personal fix, not a generic one', () => {
  const log = LOG_90 + '\n[entrypoint] WARNING: /dev/dri/renderD128 exists but this container user cannot open it (uid=1000, groups=1001 1000).\n[entrypoint]   The device belongs to GID 105. Give the container the HOST\'s numeric GID:';
  const r = D.analyze(log, KB, LATEST);
  assert.match(r.markdown, /group_add: \["105"\]/);
  assert.equal(r.facts.gid, '105');
});

test('the service-side warning text names the GID too (startup log wording)', () => {
  const log = '/dev/dri/renderD128 exists, but this container process (uid=1000, groups=[1000, 1001]) may not open it; the device is owned by GID [105]. Fix: give the container the HOST\'s numeric GID';
  const r = D.analyze(log, KB, LATEST);
  assert.equal(r.facts.gid, '105');
  assert.equal(ids(r)[0], 'gpu-device-not-available');
});

test('other known signatures map to the right entry', () => {
  const cases = [
    ['Video color metadata unavailable; realtime processing cannot be validated', 'realtime-standby'],
    ['AI service is busy (HTTP 429); waiting 4s before retry 2/6', 'service-busy'],
    ['Docker AI service did not produce an upscaled frame: HTTP 503 Busy', 'service-busy'],
    ['CUDA driver version is insufficient for CUDA runtime version', 'nvidia-error'],
    ['BUILD FAIL: ROCMExecutionProvider missing', 'amd-vulkan'],
    ['AMD ROCm GPU is not usable: no /dev/kfd in the container', 'amd-vulkan'],
    ['HIP failure 100: no ROCm-capable device is detected ; GPU=-1', 'amd-vulkan'],
    ['libamdhip64.so.7 => not found', 'amd-vulkan'],
    ["onnx_providers: ['AzureExecutionProvider', 'CPUExecutionProvider']", 'gpu-on-cpu'],
    ['sha256 mismatch for model realesrgan-x4', 'model-sha256-mismatch'],
  ];
  for (const [text, want] of cases) {
    const r = D.analyze(text, KB, LATEST);
    assert.ok(r, text);
    assert.ok(ids(r).includes(want), `${text} -> ${r && ids(r)} (wanted ${want})`);
  }
});

test('a ROCm log gets the AMD answer first, not the Intel render-group one', () => {
  const log = "Trying chain 2/3: ROCm\n*************** EP Error ***************\nEP Error rocm_call.cc HIP failure 100: no ROCm-capable device is detected\nFalling back to ['CPUExecutionProvider'] and retrying.";
  const r = D.analyze(log, KB, LATEST);
  assert.equal(ids(r)[0], 'amd-vulkan');
});

test('each GPU-device phrase is recognised on its own (not only inside the full #90 log)', () => {
  const alone = [
    '[ERROR] [OpenVINO] Device GPU is not available',
    "Falling back to ['CPUExecutionProvider'] and retrying.",
    'OpenVINO is running on the CPU device, NOT the GPU (see reason above)',
    'OpenVINO GPU is not usable: no /dev/dri render node in the container',
    'permission denied: /dev/dri/renderD128',
    'cannot open /dev/dri/renderD128',
    'The device belongs to GID 105. Give the container the HOST numeric GID',
  ];
  for (const text of alone) {
    const r = D.analyze(text, KB, LATEST);
    assert.ok(r, text);
    assert.equal(ids(r)[0], 'gpu-device-not-available', text);
  }
});

test('every KB entry id the signatures point at really exists', () => {
  const known = new Set(KB.entries.map(e => e.id));
  for (const id of D.signatureEntryIds()) assert.ok(known.has(id), 'unknown KB id in signatures: ' + id);
});

// ---- /doctor ---------------------------------------------------------------------------------
const DOCTOR = {
  version: '1.8.3.36', backend: 'intel', overall: 'fail',
  checks: [
    { check: 'backend', status: 'ok', detail: 'detected backend: intel', fix: null },
    { check: 'gpu_provider_active', status: 'fail', detail: 'GPU device present ...', fix: 'GPU runtime failed to initialise -- almost always a HOST driver/toolkit version mismatch' },
    { check: 'device_passthrough', status: 'ok', detail: '/dev/dri renderD*=True', fix: null },
    { check: 'gpu_device_access', status: 'fail', detail: '/dev/dri/renderD128 gid=105 mode=0o660 accessible=False',
      fix: '/dev/dri/renderD128 exists, but ... Fix: give the container the HOST\'s numeric GID - compose `group_add: ["105"]`' },
    { check: 'api_token', status: 'warn', detail: 'API_TOKEN not set', fix: 'Set `API_TOKEN=disable` for a trusted LAN' },
  ],
};

test('pasted /doctor JSON -> every failing check with its own fix, the most specific one first', () => {
  const r = D.analyze(JSON.stringify(DOCTOR, null, 2), KB, LATEST);
  assert.ok(r); assert.equal(r.kind, 'doctor');
  assert.match(r.markdown, /gpu_device_access/);
  assert.match(r.markdown, /group_add: \["105"\]/);
  assert.match(r.markdown, /api_token/i);
  // the generic "driver mismatch" advice would mislead here; it must come AFTER the device-access fix
  assert.ok(r.markdown.indexOf('gpu_device_access') < r.markdown.indexOf('gpu_provider_active'));
  assert.equal(r.facts.gid, '105');
});

test('/doctor JSON with surrounding text (curl command, trailing prompt) still parses', () => {
  const text = '$ curl http://nas:5000/doctor\n' + JSON.stringify(DOCTOR) + '\nroot@nas:~#';
  assert.equal(D.analyze(text, KB, LATEST).kind, 'doctor');
});

test('a healthy /doctor says so instead of inventing problems', () => {
  const ok = { version: '1.8.3.36', backend: 'nvidia', overall: 'ok', checks: [{ check: 'backend', status: 'ok', detail: 'x', fix: null }] };
  const r = D.analyze(JSON.stringify(ok), KB, LATEST);
  assert.equal(r.kind, 'doctor');
  assert.match(r.markdown, /no problems|all checks passed|healthy/i);
  assert.doesNotMatch(r.markdown, /\bfix\b/i);
});

test('truncated or malformed JSON does not throw and is not mistaken for a diagnosis', () => {
  assert.doesNotThrow(() => D.analyze('{"overall": "fail", "checks": [ {"check": "x"', KB, LATEST));
  assert.equal(D.analyze('{"unrelated": true}', KB, LATEST), null);
});

// ---- /status and /gpu-verify -------------------------------------------------------------------
test('/status with using_gpu false and a reason -> shows the reason', () => {
  const st = { status: 'running', version: '1.8.3.36', using_gpu: false, available_providers: ['OpenVINOExecutionProvider', 'CPUExecutionProvider'],
    gpu_unavailable_reason: '/dev/dri/renderD128 exists, but ... the device is owned by GID [105]. Fix: group_add: ["105"]' };
  const r = D.analyze(JSON.stringify(st), KB, LATEST);
  assert.equal(r.kind, 'status');
  assert.match(r.markdown, /group_add: \["105"\]/);
});

test('/gpu-verify with an unopenable render node -> numeric GID fix', () => {
  const gv = { onnx_providers: ['OpenVINOExecutionProvider', 'CPUExecutionProvider'], active_providers: ['OpenVINOExecutionProvider', 'CPUExecutionProvider'],
    using_gpu: false, gpu_requested: false, render_nodes: [{ path: '/dev/dri/renderD128', gid: 105, mode: '0o660', accessible: false }] };
  const r = D.analyze(JSON.stringify(gv), KB, LATEST);
  assert.equal(r.kind, 'gpu-verify');
  assert.match(r.markdown, /group_add: \["105"\]/);
});

test('/gpu-verify with only Azure+CPU providers -> wrong image, not a permission problem', () => {
  const gv = { onnx_providers: ['AzureExecutionProvider', 'CPUExecutionProvider'], active_providers: ['CPUExecutionProvider'], using_gpu: false };
  const r = D.analyze(JSON.stringify(gv), KB, LATEST);
  assert.ok(ids(r).includes('gpu-on-cpu'));
  assert.doesNotMatch(r.markdown, /group_add/);
});

test('/health with the circuit breaker open -> the service-busy entry', () => {
  const r = D.analyze(JSON.stringify({ status: 'degraded', model_loaded: true, circuit_open: true }), KB, LATEST);
  assert.equal(r.kind, 'health');
  assert.ok(ids(r).includes('service-busy'));
});

// ---- version awareness ---------------------------------------------------------------------------
test('an older version in the pasted text triggers an update hint with the installed version named', () => {
  const r = D.analyze('AI Upscaler v1.8.3.30 starting\n' + LOG_90, KB, LATEST);
  assert.match(r.markdown, /1\.8\.3\.30/);
  assert.match(r.markdown, /1\.8\.3\.36/);
  assert.match(r.markdown, /update/i);
});

test('the current version produces no update hint', () => {
  const r = D.analyze('AI Upscaler v1.8.3.36 starting\n' + LOG_90, KB, LATEST);
  assert.doesNotMatch(r.markdown, /newer version|update first/i);
});

test('no update hint when the latest version is unknown (feed not loaded)', () => {
  const r = D.analyze('AI Upscaler v1.8.3.30 starting\n' + LOG_90, KB, null);
  assert.doesNotMatch(r.markdown, /newer version|update first/i);
});

test('output is bounded: a huge pasted log does not produce a huge answer', () => {
  const big = LOG_90 + '\n' + 'noise line with error text\n'.repeat(5000);
  const r = D.analyze(big, KB, LATEST);
  assert.ok(r.markdown.length < 4000, 'markdown length ' + r.markdown.length);
});
