const {test} = require('node:test');
const assert = require('node:assert/strict');
const {loadPlayer, flush} = require('./player-harness.cjs');
function start() { const h = loadPlayer(); h.rt.start(h.video, {RealtimeMode: 'server'}, {fps: 30}); return h; }
test('only one request including capture and decode; 24 frames continue', async () => {
    const h = start();
    for (let frame = 0; frame < 24; frame++) {
        h.tick(); h.rt._captureAndSend();
        assert.equal(h.requests.length, frame + 1);
        h.respond(frame); await flush(); h.tick();
        assert.equal(h.requests.length, frame + 1, 'decode also keeps the request slot');
        h.images[frame].onload();
        if (frame < 23) h.tick();
    }
    assert.equal(h.rt._active, true);
});
for (const status of [429, 503]) test('HTTP ' + status + ' retries a fresh frame after Retry-After and recovers', async () => {
    const h = start(); h.respond(0, status, '3', 'model warming up'); await flush();
    assert.match(h.rt.getStatus().reason, /model warming up/);
    h.video.currentTime = 5; h.advance(2999); h.tick(); assert.equal(h.requests.length, 1);
    h.advance(1); h.tick(); assert.equal(h.requests.length, 2);
    assert.equal(h.requests[1].options.body.frame, 5);
    h.respond(1); await flush(); h.images[0].onload();
    assert.equal(h.rt._retryCount, 0); assert.equal(h.rt.getStatus().reason, null);
});
test('exponential 250 ms to 2 s backoff, bounded jitter, and HTTP date', () => {
    const h = loadPlayer();
    for (const delay of [250, 500, 1000, 2000, 2000]) { h.rt._waitForFrame(null, 'Busy'); assert.equal(h.rt._nextFrameAt, delay); }
    h.sandbox.Math.random = () => 0.8; h.rt._retryCount = 0; h.rt._waitForFrame(null, 'Busy');
    assert.ok(h.rt._nextFrameAt > 250 && h.rt._nextFrameAt <= 2000);
    assert.ok(h.rt._retryAfterMs(new Date(Date.now() + 20000).toUTCString()) >= 19000);
    assert.equal(h.rt._retryAfterMs('garbage'), 0);
});
test('stop and mode switch abort the request and ignore delayed responses', async () => {
    const h = start(); const signal = h.requests[0].options.signal;
    h.rt.stop(); assert.equal(signal.aborted, true);
    h.rt.start(h.video, {RealtimeMode: 'server'}, {});
    h.respond(0); await flush();
    assert.equal(h.images.length, 0); assert.equal(h.rt._pendingFrame, true);
    h.respond(1); await flush(); const image = h.images[0];
    h.rt.start(h.video, {RealtimeMode: 'lanczos', ClientDriverUpscalingActive: true}, {});
    const before = h.draws.length; image.onload(); assert.equal(h.draws.length, before);
    assert.equal(h.rt._mode, 'off');
});
test('pause does not trigger a server timeout', () => {
    const h = start(); h.video.paused = true;
    h.advance(60000); h.tick(); assert.equal(h.rt._mode, 'server'); assert.equal(h.requests.length, 1);
    h.video.paused = false; h.advance(1000); assert.equal(h.rt._mode, 'server');
});
test('resuming after suspended pause timers gives the server a fresh timeout window', () => {
    const h = start();
    h.video.paused = true; h.video.dispatch('pause');
    h.advance(60000, false);
    h.video.paused = false; h.video.dispatch('playing');
    h.advance(1000);
    assert.equal(h.rt._mode, 'server');
    assert.equal(h.requests[0].options.signal.aborted, false);
    // Stop removes listeners from this video; late resume events must not mutate
    // the clock belonging to later playback.
    h.rt.stop(); const stoppedAt = h.rt._lastSuccessfulFrame;
    h.advance(1000, false); h.video.dispatch('playing');
    assert.equal(h.rt._lastSuccessfulFrame, stoppedAt);
});
test('a delayed toBlob from the old playback never sends a request', () => {
    const h = loadPlayer(); let complete;
    const create = h.sandbox.document.createElement;
    h.sandbox.document.createElement = () => { const c = create(); c.toBlob = cb => { complete = cb; }; return c; };
    h.rt.start(h.video, {RealtimeMode: 'server'}, {}); h.rt.stop(); complete({old: true});
    assert.equal(h.requests.length, 0);
});
