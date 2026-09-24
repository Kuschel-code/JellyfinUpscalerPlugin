const {test} = require('node:test');
const assert = require('node:assert/strict');
const {loadPlayer} = require('./player-harness.cjs');
for (const mode of ['auto', 'server', 'lanczos', 'anime4k', 'ai-webgpu', 'webgl']) {
    test('driver guard blocks ' + mode, () => {
        const h = loadPlayer();
        for (const method of ['_startServer', '_startWebGL', '_startAnime4K', '_startWebGPUAI']) h.rt[method] = () => assert.fail(method + ' started');
        h.rt.start(h.video, {RealtimeMode: mode, ClientDriverUpscalingActive: true}, {fps: 60});
        assert.equal(h.rt._mode, 'off');
        assert.equal(h.rt._active, false);
        assert.match(h.rt.getStatus().reason, /driver/);
        assert.equal(h.rt._decideTier({fps: 60}, h.video), 'off');
    });
    test('masking replaces ' + mode + ' but preserves capture with driver active', () => {
        const h = loadPlayer();
        for (const method of ['_startWebGL', '_startAnime4K', '_startWebGPUAI']) h.rt[method] = () => assert.fail(method + ' started');
        h.rt.start(h.video, {RealtimeMode: mode, EnableObjectMasking: true, ClientDriverUpscalingActive: true}, {fps: 60});
        assert.equal(h.rt._mode, 'server');
        assert.equal(h.requests[0].url, 'Upscaler/detect-mask');
        h.rt._config.ClientDriverUpscalingActive = false;
        assert.equal(h.rt._decideTier({fps: 60}, h.video), 'off');
        h.advance(30000);
        assert.equal(h.rt._mode, 'server');
    });
}
test('auto follows the actual video FPS benchmark threshold', () => {
    const h = loadPlayer(); h.rt._config = {};
    assert.equal(h.rt._decideTier({fps: 24 * 0.8, videoFps: 24}, h.video), 'server');
    assert.equal(h.rt._decideTier({fps: 19.19, videoFps: 24}, h.video), 'webgl');
    h.video.getVideoPlaybackQuality = () => ({totalVideoFrames: 2});
    assert.equal(h.rt._decideTier({fps: 24, videoFps: 30}, h.video), 'server');
    assert.equal(h.rt._decideTier({fps: 23.99, videoFps: 30}, h.video), 'webgl');
    h.video.playbackRate = 2;
    assert.equal(h.rt._decideTier({fps: 47.9, videoFps: 30}, h.video), 'webgl');
    assert.equal(h.rt._decideTier({fps: 48, videoFps: 30}, h.video), 'server');
    assert.equal(h.rt._decideTier({fps: 60, error: 'failed'}, h.video), 'webgl');
    assert.equal(h.rt._decideTier(null, h.video), 'webgl');
    assert.equal(h.rt._decideTier({fps: 500}, h.video), 'webgl');
    assert.equal(h.rt._decideTier({fps: 47.9, videoFps: 60}, {playbackRate: 1}), 'webgl');
});

test('player startup skips model work for driver mode and preserves masking when upscaling is disabled', async () => {
    const {flush} = require('./player-harness.cjs');
    for (const config of [
        {ClientDriverUpscalingActive: true, EnableAutoModelSelection: true},
        {EnableObjectMasking: true, EnableRealtimeUpscaling: false, ClientDriverUpscalingActive: true}
    ]) {
        const h = loadPlayer(), player = h.sandbox.PlayerIntegration;
        player.getPluginConfig = async () => config;
        player.findVideoElement = () => h.video;
        player._autoSelectForVideo = () => assert.fail('Unnecessary model selection');
        await player.startRealtimeUpscaling(); await flush();
        if (config.EnableObjectMasking) assert.equal(h.requests[0].url, 'Upscaler/detect-mask');
        else { assert.equal(h.requests.length, 0); assert.equal(h.rt._mode, 'off'); }
    }
});

test('stopped startup never continues from late model load or config response', async () => {
    const {flush} = require('./player-harness.cjs');
    const h = loadPlayer(), player = h.sandbox.PlayerIntegration;
    await player._startRtWithConfig(h.video, {RealtimeMode: 'server'});
    assert.match(h.requests[0].url, /models\/load/);
    h.rt.stop(); assert.equal(h.requests[0].options.signal.aborted, true);
    h.respond(0); await flush(); assert.equal(h.requests.length, 1); assert.equal(h.rt._active, false);
    let configReady;
    player.getPluginConfig = () => new Promise(resolve => { configReady = resolve; });
    player.findVideoElement = () => h.video;
    const starting = player.startRealtimeUpscaling(); h.rt.stop();
    configReady({RealtimeMode: 'server'}); await starting;
    assert.equal(h.requests.length, 1);
});

test('RIFE, detectors, face restoration and unavailable models are not selectable upscalers', () => {
    const h = loadPlayer(), player = h.sandbox.PlayerIntegration;
    for (const category of ['interpolation', 'object-detection', 'face_restore', 'FACE-RESTORE']) {
        assert.equal(player._renderModelCard({id: category, name: category}, false, {category, available: true}), '');
    }
    assert.equal(player._renderModelCard({id: 'unavailable'}, false, {available: false}), '');
    assert.match(player._renderModelCard({id: 'my-import', name: 'my-import', scale: 2}, false, null), /data-model="my-import"/);
});

// v1.8.3.33 - untagged 10-bit video is SDR (see realtime-sdr-and-fallback.test.cjs);
// a tagged transfer nobody here knows, or BT.2020 without an SDR transfer, is not.
test('PQ, HLG, dynamic HDR and unknown transfers fail before canvas capture', async () => {
    for (const stream of [null,
        {ColorTransfer: 'smpte2084', VideoRangeType: 'HDR10'},
        {ColorTransfer: 'arib-std-b67', VideoRangeType: 'HLG'},
        {ColorTransfer: 'smpte2084', VideoRangeType: 'DOVI'},
        {ColorTransfer: 'smpte428', BitDepth: 10}, {ColorPrimaries: 'bt2020', BitDepth: 10}]) {
        const h = loadPlayer(), player = h.sandbox.PlayerIntegration;
        player._readPlayingVideoStream = async () => stream;
        await player._startRtWithConfig(h.video, {RealtimeMode: 'server'});
        assert.equal(h.rt._mode, 'off'); assert.equal(h.requests.length, 0); assert.equal(h.draws.length, 0);
        assert.match(h.rt._reason, /HDR|metadata unavailable/);
    }
});

test('a delayed Apply Auto reply cannot restart stopped playback', async () => {
    const {flush} = require('./player-harness.cjs');
    const h = loadPlayer(), player = h.sandbox.PlayerIntegration;
    player.getPluginConfig = async () => ({RealtimeMode: 'server'});
    player.findVideoElement = () => h.video;
    let pickReady;
    player._autoSelectForVideo = () => new Promise(resolve => { pickReady = resolve; });
    const applying = player._applyAutoNow(null); await flush();
    h.rt.stop(); pickReady({model: 'fsrcnn-x2'}); await applying;
    assert.equal(h.requests.length, 0); assert.equal(h.rt._active, false);
});

test('leaving playback aborts startup while the model is still loading', async () => {
    const {flush} = require('./player-harness.cjs');
    const h = loadPlayer(), player = h.sandbox.PlayerIntegration;
    h.sandbox.location = {hash: '#/home'};
    await player._startRtWithConfig(h.video, {RealtimeMode: 'server'});
    assert.equal(h.rt._active, false);
    player.onViewShow({detail: {type: 'home'}});
    assert.equal(h.requests[0].options.signal.aborted, true);
    h.respond(0); await flush();
    assert.equal(h.requests.length, 1); assert.equal(h.rt._active, false);
});

test('scheduled auto-start cannot restart playback after Stop or navigation', () => {
    for (const navigate of [false, true]) {
        const h = loadPlayer(), player = h.sandbox.PlayerIntegration;
        h.video.readyState = 2;
        h.sandbox.location = {hash: '#/video'};
        player.findVideoElement = () => h.video;
        let delayed;
        h.sandbox.setTimeout = fn => { delayed = fn; };
        player.startRealtimeUpscaling = () => assert.fail('stale auto-start');
        player._waitForVideoAndAutoStart();
        if (navigate) h.sandbox.location.hash = '#/home';
        else h.rt.stop();
        delayed();
    }
    const h = loadPlayer(), player = h.sandbox.PlayerIntegration;
    h.video.readyState = 1; h.video.paused = true;
    h.sandbox.location = {hash: '#/video'};
    player.findVideoElement = () => h.video;
    h.sandbox.setTimeout = () => assert.fail('stopped playing listener scheduled auto-start');
    player._waitForVideoAndAutoStart();
    h.rt.stop(); h.video.paused = false; h.video.dispatch('playing');

    const pending = loadPlayer(), pendingPlayer = pending.sandbox.PlayerIntegration;
    pending.sandbox.location = {hash: '#/video'};
    pendingPlayer.findVideoElement = () => null;
    let retry;
    pending.sandbox.setTimeout = fn => { retry = fn; };
    pendingPlayer._waitForVideoAndAutoStart();
    pending.rt.stop(); pending.video.readyState = 2;
    pendingPlayer.findVideoElement = () => assert.fail('stopped video lookup continued');
    retry();
    assert.equal(pendingPlayer._autoStartPending, false);
});

test('quick model changes cancel frames and do not restart after a delayed load or config response', async () => {
    const {flush} = require('./player-harness.cjs');
    for (const stopAt of ['config-save', 'model-load', 'config-refresh']) {
        const h = loadPlayer(), player = h.sandbox.PlayerIntegration;
        h.rt.start(h.video, {RealtimeMode: 'server'}, {});
        let saveReady, configReady;
        player.updatePluginConfig = () => new Promise(resolve => { saveReady = resolve; });
        player.findVideoElement = () => h.video;
        player.getPluginConfig = () => new Promise(resolve => { configReady = resolve; });
        const changing = player.quickSetModel('span-x2');
        assert.equal(h.requests[0].options.signal.aborted, true);
        if (stopAt === 'config-save') {
            h.rt.stop(); saveReady(); await changing;
            assert.equal(h.requests.length, 1);
            continue;
        }
        saveReady(); await flush();
        assert.match(h.requests[1].url, /models\/load/);
        if (stopAt === 'model-load') {
            h.rt.stop();
            assert.equal(h.requests[1].options.signal.aborted, true);
            h.respond(1); await changing;
        } else {
            h.respond(1); await flush(); h.rt.stop();
            configReady({RealtimeMode: 'server', Model: 'span-x2'}); await changing;
        }
        assert.equal(h.requests.length, 2); assert.equal(h.rt._active, false);
    }
});

test('quick model selection revalidates HDR before restarting realtime', async () => {
    const {flush} = require('./player-harness.cjs');
    const h = loadPlayer(), player = h.sandbox.PlayerIntegration;
    h.rt.start(h.video, {RealtimeMode: 'server'}, {fps: 30, videoFps: 30});
    player.updatePluginConfig = async () => {};
    player.findVideoElement = () => h.video;
    player.getPluginConfig = async () => ({RealtimeMode: 'server', Model: 'span-x2'});
    player._readPlayingVideoStream = async () => ({ColorTransfer: 'smpte2084', VideoRangeType: 'HDR10'});
    const changing = player.quickSetModel('span-x2'); await flush();
    h.respond(1); await changing;
    assert.equal(h.requests.length, 2);
    assert.equal(h.rt._mode, 'off'); assert.match(h.rt._reason, /HDR/);
});
