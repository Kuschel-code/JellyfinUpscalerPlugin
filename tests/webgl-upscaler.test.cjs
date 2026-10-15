'use strict';
// The Lanczos/CAS WebGL engine (Configuration/webgl-upscaler.js), run against a recording
// WebGL stand-in. The native <video> is hidden (opacity 0) once the canvas draws, so what
// the canvas last drew is what the viewer sees.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function load({ rvfc = true, box = [1920, 1080], dpr = 1, size = [1920, 1080] } = {}) {
  const draws = [];
  const uniforms = {};
  let frameCallback = null;
  let rafCallback = null;
  const video = {
    readyState: 4, videoWidth: size[0], videoHeight: size[1], paused: false, ended: false, currentTime: 10,
    clientWidth: box[0], clientHeight: box[1], style: { opacity: '1' }, parentElement: { style: {}, appendChild() {} },
  };
  if (rvfc) {
    video.requestVideoFrameCallback = (cb) => { frameCallback = cb; return 7; };
    video.cancelVideoFrameCallback = () => { frameCallback = null; };
  }
  const gl = new Proxy({}, { get(t, k) {
    if (k === 'isContextLost') return () => false;
    if (k === 'getShaderParameter' || k === 'getProgramParameter') return () => true;
    if (k === 'getUniformLocation') return (program, name) => name;
    if (k === 'uniform1f') return (loc, value) => { uniforms[loc] = value; };
    if (k === 'drawArrays') return () => draws.push({ t: video.currentTime, w: canvas.width, h: canvas.height });
    return typeof k === 'string' && /^[A-Z_0-9]+$/.test(k) ? 1 : () => ({});
  } });
  const canvas = { style: {}, width: 0, height: 0, clientWidth: 0, clientHeight: 0, addEventListener() {}, getContext: () => gl };
  const sandbox = {
    console: { log() {}, warn() {}, error() {} }, performance: { now: () => 0 }, devicePixelRatio: dpr,
    document: { createElement: () => canvas }, localStorage: { getItem: () => null },
    requestAnimationFrame: (cb) => { rafCallback = cb; return 3; }, cancelAnimationFrame() { rafCallback = null; },
    getComputedStyle: () => ({ objectFit: 'contain' }),
  };
  sandbox.window = sandbox;
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../Configuration/webgl-upscaler.js'), 'utf8'), sandbox);
  const engine = sandbox.AIUpscalerWebGL;
  assert.ok(engine.init(video));
  engine.enable();
  // the browser presents the next frame (or runs the next animation frame)
  const tick = () => { const cb = rvfc ? frameCallback : rafCallback; frameCallback = rafCallback = null; if (cb) cb(); };
  return { engine, video, canvas, draws, uniforms, tick, pending: () => (rvfc ? frameCallback : rafCallback) };
}

test('a seek while paused draws the frame the video now shows', () => {
  const { video, draws, tick } = load();
  tick();
  video.paused = true;
  video.currentTime = 600; // the viewer drags the timeline to 10:00
  tick();
  assert.equal(draws.at(-1).t, 600);
  assert.equal(video.style.opacity, '0', 'the video stays hidden, so the canvas must be current');
});

test('a paused video with nothing new is not redrawn on every animation frame', () => {
  const { video, draws, tick } = load({ rvfc: false });
  tick();
  video.paused = true;
  const before = draws.length;
  for (let i = 0; i < 30; i++) tick();
  assert.equal(draws.length, before);
  video.currentTime = 42;
  tick();
  assert.equal(draws.length, before + 1);
});

for (const [label, opts, expected] of [
  ['1080p shown at 1080p is drawn at 1080p, not 4K', { size: [1920, 1080], box: [1920, 1080] }, [1920, 1080]],
  ['720p shown at 1080p is drawn at 1080p', { size: [1280, 720], box: [1920, 1080] }, [1920, 1080]],
  ['a small video on a 4K screen stops at twice its size', { size: [640, 360], box: [3840, 2160] }, [1280, 720]],
  ['HiDPI counts device pixels', { size: [1280, 720], box: [960, 540], dpr: 2 }, [1920, 1080]],
  ['a 4K video in a small window stays at its own size', { size: [3840, 2160], box: [1280, 720] }, [3840, 2160]],
  ['4:3 in a 16:9 box keeps 4:3', { size: [640, 480], box: [1920, 1080] }, [1280, 960]],
  ['no layout yet: source size', { size: [1280, 720], box: [0, 0] }, [1280, 720]],
]) {
  test('canvas size: ' + label, () => {
    const { draws } = load(opts);
    assert.deepEqual([draws.at(-1).w, draws.at(-1).h], expected);
  });
}

test("the default look is main's: sharpness 0.5, deblocking off", () => {
  const { uniforms } = load();
  assert.equal(uniforms.u_sharpness, 0.5);
  assert.equal(uniforms.u_deblock, 0);
});

test('disable stops the frame callback and shows the video again', () => {
  const { engine, video, pending } = load();
  assert.ok(pending(), 'a frame callback is scheduled while enabled');
  engine.disable();
  assert.equal(pending(), null);
  assert.equal(video.style.opacity, '1');
});
