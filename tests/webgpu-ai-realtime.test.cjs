'use strict';
// The WebGPU AI engine (Configuration/webgpu-ai-realtime.js) against stand-ins for WebGPU,
// ONNX Runtime Web, fetch and WebCrypto. A "model" here is the bytes of its URL, so a test
// can tell which source a session was built from.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const FILE = path.join(__dirname, '../Configuration/webgpu-ai-realtime.js');
const SOURCE = fs.readFileSync(FILE, 'utf8');
// url -> sha256 pairs as the catalog states them
const PINNED = [...SOURCE.matchAll(/url: '([^']+)',\s*sha256: '([0-9a-f]+)'/g)].map((m) => ({ url: m[1], sha256: m[2] }));
const EXPECTED = new Map(PINNED.map((p) => [p.url, p.sha256]));

function load({ crypto = true, scale = 4, badHash = [], failUrls = [], runCost = 0, videoSize = [1920, 1080] } = {}) {
  let now = 0;
  const fetches = [], runs = [], created = [], raf = [], fatal = [];
  const ctx2d = {
    drawImage() {}, putImageData() {},
    getImageData: (x, y, w, h) => ({ data: new Uint8ClampedArray(w * h * 4) }),
    createImageData: (w, h) => ({ data: new Uint8ClampedArray(w * h * 4) }),
  };
  class Tensor { constructor(type, data, dims) { this.type = type; this.data = data; this.dims = dims; } }
  const session = {
    inputNames: ['input'], outputNames: ['output'], release() {},
    run: async (feeds) => {
      const d = feeds.input.dims;
      runs.push(Array.from(d)); // plain array: the engine's own lives in another realm
      now += runCost;
      const oh = d[2] * scale, ow = d[3] * scale;
      return { output: { dims: [1, 3, oh, ow], data: new Float32Array(3 * oh * ow) } };
    },
  };
  const sandbox = {
    console: { log() {}, warn() {}, info() {}, error() {} },
    performance: { now: () => now },
    navigator: { gpu: { requestAdapter: async () => ({}) } },
    document: { createElement: () => ({ style: {}, width: 0, height: 0, getContext: () => ctx2d }), head: { appendChild() {} } },
    requestAnimationFrame: (cb) => { raf.push(cb); return raf.length; },
    ApiClient: { accessToken: () => 'jellyfin-session-token', getUrl: (x) => 'http://jellyfin.local:8096/' + x },
    ort: { Tensor, env: { wasm: {} }, InferenceSession: { create: async (buf) => { created.push(new TextDecoder().decode(buf)); return session; } } },
    fetch: async (url, opts) => {
      fetches.push({ url, opts: opts || {} });
      if (failUrls.includes(url)) return { ok: false, status: 404 };
      return { ok: true, status: 200, arrayBuffer: async () => new TextEncoder().encode(url).buffer };
    },
    TextEncoder, TextDecoder,
  };
  if (crypto) {
    sandbox.crypto = { subtle: { digest: async (alg, buf) => {
      assert.equal(alg, 'SHA-256');
      const url = new TextDecoder().decode(buf);
      const hex = badHash.includes(url) ? '00'.repeat(32) : EXPECTED.get(url);
      return Uint8Array.from(hex.match(/../g).map((h) => parseInt(h, 16))).buffer;
    } } };
  }
  sandbox.window = sandbox;
  vm.runInNewContext(SOURCE, sandbox);
  const video = { videoWidth: videoSize[0], videoHeight: videoSize[1], paused: false, ended: false, readyState: 4,
    style: {}, parentElement: { style: {}, appendChild() {} } };
  const engine = sandbox.WebGPUAIUpscaler;
  const start = () => engine.start(video, { onFatal: (reason) => fatal.push(reason) });
  // The rAF wrapper does not return the loop's promise: let each frame's awaits settle.
  const settle = () => new Promise((resolve) => setImmediate(resolve));
  const frames = async (n) => { await settle(); for (let i = 0; i < n && raf.length; i++) { raf.shift()(); await settle(); } };
  return { engine, start, frames, fetches, runs, created, fatal };
}

test('every model source is pinned to a commit and a SHA-256', () => {
  assert.ok(PINNED.length >= 2);
  for (const { url, sha256 } of PINNED) {
    assert.match(url, /^https:\/\/huggingface\.co\/[^/]+\/[^/]+\/resolve\/[0-9a-f]{40}\/[^/]+\.onnx$/, url);
    assert.match(sha256, /^[0-9a-f]{64}$/);
  }
  assert.doesNotMatch(SOURCE, /resolve\/main\//);
});

test('model downloads carry no Jellyfin token and never hit the Jellyfin server', async () => {
  const { start, fetches } = load({ failUrls: PINNED.map((p) => p.url) }); // every source tried
  assert.equal(await start(), false);
  assert.ok(fetches.length >= 2);
  for (const { url, opts } of fetches) {
    assert.ok(url.startsWith('https://huggingface.co/'), url);
    assert.equal(opts.credentials, 'omit');
    assert.equal(opts.headers, undefined, 'no Authorization header to a third party');
  }
});

test('a download with the wrong checksum is refused and the next pinned source is used', async () => {
  const first = load();
  assert.equal(await first.start(), true);
  const defaultSources = PINNED.filter((p) => first.fetches.some((f) => f.url === p.url)).map((p) => p.url);

  const { start, created } = load({ badHash: [defaultSources[0]] });
  assert.equal(await start(), true);
  assert.equal(created.length, 1);
  assert.notEqual(created[0], defaultSources[0]);
  assert.ok(EXPECTED.has(created[0]));
});

test('without WebCrypto (Jellyfin over plain HTTP) the commit-pinned download is used', async () => {
  const { start, created } = load({ crypto: false });
  assert.equal(await start(), true);
  assert.equal(created.length, 1);
});

for (const [scale, input] of [[4, [360, 640]], [2, [720, 1280]]]) {
  test(`a ${scale}x model gets at most ${1440 / scale} input lines, measured, not taken from the catalog`, async () => {
    const { start, runs, frames } = load({ scale });
    assert.equal(await start(), true);
    assert.deepEqual(runs[0], [1, 3, 16, 16], 'the scale probe');
    await frames(1);
    const frame = runs.find((d) => d[2] !== 16);
    assert.deepEqual(frame.slice(2), input);
  });
}

test('a video below the input cap is not enlarged before inference', async () => {
  const { start, runs, frames } = load({ videoSize: [320, 180] });
  assert.equal(await start(), true);
  await frames(1);
  assert.deepEqual(runs.find((d) => d[2] !== 16).slice(2), [180, 320]);
});

test('30 consecutive slow frames hand over to Lanczos', async () => {
  const { engine, start, frames, fatal } = load({ runCost: 60 });
  assert.equal(await start(), true);
  await frames(40);
  assert.deepEqual(fatal, ['inference_too_slow']);
  assert.equal(engine._running, false);
});

test('fast frames keep running', async () => {
  const { engine, start, frames, fatal } = load({ runCost: 10 });
  assert.equal(await start(), true);
  await frames(40);
  assert.deepEqual(fatal, []);
  assert.equal(engine._running, true);
});
