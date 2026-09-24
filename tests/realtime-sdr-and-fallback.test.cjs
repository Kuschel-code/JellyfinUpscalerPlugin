const {test} = require('node:test');
const assert = require('node:assert/strict');
const {loadPlayer, flush} = require('./player-harness.cjs');

// v1.8.3.33 - the realtime HDR guard needs positive HDR evidence. 1.8.3.31/32 refused
// every SDR file whose transfer tag was not bt709/sRGB (NTSC/PAL DVD rips) and every
// untagged 10-bit file, in all modes including the browser-only shaders.
const SDR = [
    {Type: 'Video', ColorTransfer: 'smpte170m', VideoRangeType: 'SDR', BitDepth: 8},
    {Type: 'Video', ColorTransfer: 'bt470bg', VideoRangeType: 'SDR', BitDepth: 8},
    {Type: 'Video', VideoRangeType: 'SDR', BitDepth: 10},
    {Type: 'Video', ColorTransfer: 'unknown', BitDepth: 10},
    {Type: 'Video', ColorTransfer: 'bt2020-10', ColorPrimaries: 'bt2020', BitDepth: 10}
];
for (const stream of SDR) {
    for (const mode of ['lanczos', 'anime4k']) {
        test('SDR ' + JSON.stringify(stream) + ' starts in ' + mode, async () => {
            const h = loadPlayer(), player = h.sandbox.PlayerIntegration;
            let started = null;
            for (const m of ['_startWebGL', '_startAnime4K', '_startServer', '_startWebGPUAI']) h.rt[m] = () => { started = m; };
            player._readPlayingVideoStream = async () => stream;
            await player._startRtWithConfig(h.video, {RealtimeMode: mode});
            await flush();
            assert.equal(h.rt._mode, mode);
            assert.ok(started, 'an engine started');
            assert.equal(h.notices.length, 0, 'no HDR warning for SDR video');
        });
    }
    test('SDR ' + JSON.stringify(stream) + ' reaches server mode startup', async () => {
        const h = loadPlayer(), player = h.sandbox.PlayerIntegration;
        player._readPlayingVideoStream = async () => stream;
        await player._startRtWithConfig(h.video, {RealtimeMode: 'server'});
        assert.notEqual(h.rt._mode, 'off');
        assert.match(h.requests[0].url, /models\/load/);
    });
}
test('HDR evidence still refuses realtime before any capture', async () => {
    for (const stream of [{VideoRangeType: 'HDR10'}, {ColorTransfer: 'smpte428'}, {ColorPrimaries: 'bt2020', BitDepth: 10},
                          {ColorTransfer: 'smpte2084', VideoRangeType: 'HDR10'}, {ColorTransfer: 'arib-std-b67'}]) {
        const h = loadPlayer(), player = h.sandbox.PlayerIntegration;
        player._readPlayingVideoStream = async () => stream;
        await player._startRtWithConfig(h.video, {RealtimeMode: 'lanczos'});
        assert.equal(h.rt._mode, 'off', JSON.stringify(stream));
        assert.equal(h.draws.length, 0);
        assert.match(h.rt._reason, /HDR/);
    }
});

// v1.8.3.33 - server mode keeps its #79 retry behaviour but gets its safety nets back.
function serverAt(videoFps, config) {
    const h = loadPlayer();
    h.rt.start(h.video, Object.assign({RealtimeMode: 'server'}, config || {}), {fps: 30, videoFps});
    return h;
}
async function deliverFrame(h) {
    const i = h.requests.length - 1;
    h.respond(i); await flush();
    h.images[h.images.length - 1].onload();
}
test('a server that cannot follow the video falls back to Lanczos after 5 s', async () => {
    const h = serverAt(24);
    let fellBack = false;
    h.rt._startWebGL = () => { fellBack = true; };
    for (let s = 0; s < 8 && h.rt._mode === 'server'; s++) {
        await deliverFrame(h);                 // one finished frame per second
        h.advance(1000); h.tick();
    }
    assert.equal(h.rt._mode, 'lanczos');
    assert.ok(fellBack);
    assert.match(h.notices.map(n => n[0]).join(' | '), /Lanczos/);
});
test('a fast enough server keeps server mode', async () => {
    const h = serverAt(24);
    for (let s = 0; s < 100; s++) { await deliverFrame(h); h.advance(50); h.tick(); }   // 20 fps for 5 s
    assert.equal(h.rt._mode, 'server');
    assert.ok(h.rt._currentFps >= 12, 'measured ' + h.rt._currentFps + ' fps');
});
test('during an outage the stale frame is hidden, and it comes back with the server', async () => {
    const h = serverAt(24);
    await deliverFrame(h);
    const overlay = h.rt._overlayCanvas;
    assert.notEqual(overlay.style.visibility, 'hidden');
    for (let s = 0; s < 60; s++) {           // 503 with Retry-After 30 for a minute
        h.advance(1000); h.tick();
        const last = h.requests.length - 1;
        if (!h.requests[last].done) { h.requests[last].done = true; h.respond(last, 503, '30', 'Circuit breaker open'); await flush(); }
    }
    assert.equal(h.rt._mode, 'server', 'service-requested waits are not a reason to give up');
    assert.equal(overlay.style.visibility, 'hidden', 'the last frame must not freeze over the playing video');
    h.advance(1000); h.tick();              // the 30 s wait ends; a fresh frame goes out at once
    await deliverFrame(h);
    assert.equal(h.rt._mode, 'server');
    assert.equal(overlay.style.visibility, '');
});
test('object masking keeps the last covered frame rather than revealing the video', async () => {
    const h = serverAt(24, {EnableObjectMasking: true});
    await deliverFrame(h);
    for (let s = 0; s < 20; s++) {
        h.advance(1000); h.tick();
        const last = h.requests.length - 1;
        if (!h.requests[last].done) { h.requests[last].done = true; h.respond(last, 503, '30', 'Busy'); await flush(); }
    }
    assert.equal(h.rt._mode, 'server');
    assert.notEqual(h.rt._overlayCanvas.style.visibility, 'hidden');
});
test('a paused video keeps its frame and is never judged slow', async () => {
    const h = serverAt(24);
    await deliverFrame(h);
    h.video.paused = true;
    for (let s = 0; s < 30; s++) h.advance(1000);
    assert.equal(h.rt._mode, 'server');
    assert.notEqual(h.rt._overlayCanvas.style.visibility, 'hidden');
});
