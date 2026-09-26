const {test} = require('node:test');
const assert = require('node:assert/strict');
const {loadPlayer, flush} = require('./player-harness.cjs');

// v1.8.3.34 - #86/#87: Jellyfin 10.9+ plays at "#/video" with no item id in the URL.
// 1.8.3.31-33 read the id from the hash only, found nothing, and refused realtime for
// every video with "Video color metadata unavailable". The URLs below are the ones the
// Jellyfin 12.1 web client builds (Videos/{id}/stream.{ext}, the server's TranscodingUrl,
// Items/{id}/PlaybackInfo).
const SERVER = 'http://jf.local:8096';
const ITEM = 'a1b2c3d4e5f60718293a4b5c6d7e8f90';
const V2 = 'ffeeddccbbaa99887766554433221100';
const OTHER = '0123456789abcdef0123456789abcdef';
const SDR = [{Type: 'Audio'}, {Type: 'Video', VideoRangeType: 'SDR', ColorTransfer: 'bt709', AverageFrameRate: 23.976}];
const HDR = [{Type: 'Video', VideoRangeType: 'HDR10', ColorTransfer: 'smpte2084', ColorPrimaries: 'bt2020'}];
const directPlay = id => SERVER + '/Videos/' + id + '/stream.mkv?Static=true&mediaSourceId=' + id + '&deviceId=dev1&ApiKey=secret&Tag=etag';
const playbackInfo = (id, source) => SERVER + '/Items/' + id + '/PlaybackInfo?UserId=u1&StartTimeTicks=0&IsPlayback=true' +
    '&AutoOpenLiveStream=true' + (source ? '&MediaSourceId=' + source : '') + '&MaxStreamingBitrate=140000000';
const master = (id, source) => SERVER + '/videos/' + id + '/master.m3u8?DeviceId=dev1&MediaSourceId=' + source + '&VideoCodec=h264&api_key=secret';
const segment = (id, source) => SERVER + '/videos/' + id + '/hls1/main/0.mp4?DeviceId=dev1&MediaSourceId=' + source + '&api_key=secret';
const NOISE = [
    SERVER + '/Items/' + OTHER + '/Images/Primary?fillHeight=300&quality=96',
    SERVER + '/Videos/' + ITEM + '/' + V2 + '/Subtitles/0/0/Stream.vtt?api_key=secret',
    SERVER + '/Videos/' + OTHER + '/Trickplay/320/0.jpg?MediaSourceId=' + OTHER
];
const entry = (name, startTime) => ({name, startTime, entryType: 'resource'});

function observerMock(options = {}) {
    const state = {callback: null, options: null, pending: []};
    class PerformanceObserver {
        constructor(callback) { state.callback = callback; }
        observe(opts) {
            if (options.entryTypesOnly && !opts.entryTypes) throw new TypeError('entryTypes must be specified');
            state.options = opts;
        }
        takeRecords() { const out = state.pending; state.pending = []; return out; }
    }
    return {PerformanceObserver, state,
        deliver(...entries) { state.callback({getEntries: () => entries}); },
        hold(...entries) { state.pending.push(...entries); }};
}

function setup({hash = '#/video', src = 'blob:' + SERVER + '/5f0c', streams = {[ITEM]: {sdr: SDR}}, globals = {}} = {}) {
    const lookups = [];
    const items = {};
    for (const [id, versions] of Object.entries(streams)) {
        const main = versions.sdr || versions.hdr;
        items[id] = {Id: id, Genres: ['Anime'], MediaStreams: main,
            MediaSources: [{Id: id, MediaStreams: main}].concat(versions.v2 ? [{Id: V2, MediaStreams: versions.v2}] : [])};
    }
    const ApiClient = {getUrl: x => x, accessToken: () => 'test', getCurrentUserId: () => 'u1',
        getItem: async (userId, id) => { lookups.push([userId, id]); return items[id] || null; },
        ajax: async req => { lookups.push(['ajax', req.url]); return {success: true, recommended_model: 'realesrgan-x4'}; }};
    const h = loadPlayer({realStreamLookup: true, globals: Object.assign({ApiClient}, globals)});
    h.sandbox.location = {hash};
    h.video.currentSrc = src;
    let started = null;
    for (const m of ['_startWebGL', '_startAnime4K', '_startWebGPUAI']) h.rt[m] = () => { started = m; };
    return {h, player: h.sandbox.PlayerIntegration, lookups, started: () => started};
}

test('direct play at #/video (Jellyfin 12.1) finds the item and starts realtime', async () => {
    const {h, player, lookups, started} = setup({src: directPlay(ITEM)});
    await player._startRtWithConfig(h.video, {RealtimeMode: 'lanczos'});
    await flush();
    assert.deepEqual(lookups, [['u1', ITEM]]);
    assert.equal(h.rt._mode, 'lanczos');
    assert.equal(started(), '_startWebGL');
    assert.equal(h.notices.length, 0, JSON.stringify(h.notices));
});

test('direct play at #/video reaches server mode startup', async () => {
    const {h, player} = setup({src: directPlay(ITEM)});
    await player._startRtWithConfig(h.video, {RealtimeMode: 'server'});
    assert.notEqual(h.rt._mode, 'off');
    assert.match(h.requests[0].url, /models\/load/);
});

test('hls.js (blob: source) uses the newest playback request', async () => {
    const obs = observerMock();
    const {h, player, lookups} = setup({streams: {[ITEM]: {sdr: SDR}, [OTHER]: {hdr: HDR}},
        globals: {PerformanceObserver: obs.PerformanceObserver}});
    obs.deliver(entry(playbackInfo(OTHER), 100), entry(playbackInfo(ITEM), 200), entry(master(ITEM, ITEM), 300),
        ...NOISE.map((url, i) => entry(url, 400 + i)));
    await player._startRtWithConfig(h.video, {RealtimeMode: 'lanczos'});
    await flush();
    assert.deepEqual(lookups, [['u1', ITEM]]);
    assert.equal(h.rt._mode, 'lanczos');
});

test('the version that plays decides, not the first one listed', async () => {
    for (const [v2, mode] of [[HDR, 'off'], [SDR, 'lanczos']]) {
        const obs = observerMock();
        const {h, player} = setup({streams: {[ITEM]: {sdr: SDR, v2}}, globals: {PerformanceObserver: obs.PerformanceObserver}});
        obs.deliver(entry(playbackInfo(ITEM, V2), 100), entry(segment(ITEM, V2.toUpperCase()), 200));
        await player._startRtWithConfig(h.video, {RealtimeMode: 'lanczos'});
        await flush();
        assert.equal(h.rt._mode, mode);
        if (mode === 'off') assert.match(h.rt._reason, /HDR/);
    }
});

test('a request that started earlier does not win by finishing later', () => {
    const obs = observerMock();
    const {h, player} = setup({globals: {PerformanceObserver: obs.PerformanceObserver}});
    obs.deliver(entry(master(ITEM, ITEM), 300));
    obs.deliver(entry(playbackInfo(OTHER), 250));
    assert.equal(player._getPlayingMedia(h.video).itemId, ITEM);
});

test('entries the observer has not delivered yet are read at lookup time', () => {
    const obs = observerMock();
    const {h, player} = setup({globals: {PerformanceObserver: obs.PerformanceObserver}});
    obs.hold(entry(playbackInfo(ITEM), 100));
    assert.equal(player._getPlayingMedia(h.video).itemId, ITEM);
});

test('the watcher starts at load and asks for earlier requests too', () => {
    const obs = observerMock();
    setup({globals: {PerformanceObserver: obs.PerformanceObserver}});
    assert.deepEqual({...obs.state.options}, {type: 'resource', buffered: true});
});

test('older engines fall back to entryTypes plus the recorded timeline', () => {
    const obs = observerMock({entryTypesOnly: true});
    const performance = {now: () => 0, getEntriesByType: () => [entry(playbackInfo(ITEM), 100)]};
    const {h, player} = setup({globals: {PerformanceObserver: obs.PerformanceObserver, performance}});
    assert.deepEqual([...obs.state.options.entryTypes], ['resource']);
    assert.equal(player._getPlayingMedia(h.video).itemId, ITEM);
});

test('without PerformanceObserver the recorded timeline is read at lookup time', () => {
    const entries = [];
    const performance = {now: () => 0, getEntriesByType: () => entries};
    const {h, player} = setup({globals: {performance}});
    entries.push(entry(playbackInfo(ITEM), 100));
    assert.equal(player._getPlayingMedia(h.video).itemId, ITEM);
});

test('subtitle, trickplay and image requests name no playing item', () => {
    const obs = observerMock();
    const {h, player} = setup({globals: {PerformanceObserver: obs.PerformanceObserver}});
    obs.deliver(...NOISE.map((url, i) => entry(url, i)));
    assert.equal(player._getPlayingMedia(h.video), null);
});

test('a lookup keeps only the ids, never the API key in the URL', () => {
    const {h, player} = setup({src: directPlay(ITEM)});
    assert.deepEqual({...player._getPlayingMedia(h.video)}, {itemId: ITEM, mediaSourceId: ITEM});
});

test('dashed ids and a base URL still match', () => {
    const dashed = 'a1b2c3d4-e5f6-0718-293a-4b5c6d7e8f90';
    const {h, player} = setup({src: SERVER + '/jellyfin/Videos/' + dashed + '/stream?Static=true'});
    assert.deepEqual({...player._getPlayingMedia(h.video)}, {itemId: dashed, mediaSourceId: null});
});

test('a malformed MediaSourceId neither throws nor stops the watcher', () => {
    const obs = observerMock();
    const {h, player} = setup({globals: {PerformanceObserver: obs.PerformanceObserver}});
    obs.deliver(entry(SERVER + '/videos/' + ITEM + '/master.m3u8?MediaSourceId=%E0%A4%A', 100), entry(master(OTHER, OTHER), 200));
    assert.equal(player._getPlayingMedia(h.video).itemId, OTHER);
    h.video.currentSrc = SERVER + '/Videos/' + ITEM + '/stream.mkv?mediaSourceId=%zz';
    assert.deepEqual({...player._getPlayingMedia(h.video)}, {itemId: ITEM, mediaSourceId: '%zz'});
});

test('the hash id is still read when nothing else names the item', () => {
    const {h, player} = setup({hash: '#/video?id=' + ITEM});
    assert.equal(player._getPlayingMedia(h.video).itemId, ITEM);
});

test('nothing names the video: realtime stays off with the metadata message', async () => {
    const {h, player, lookups, started} = setup();
    await player._startRtWithConfig(h.video, {RealtimeMode: 'lanczos'});
    await flush();
    assert.equal(lookups.length, 0);
    assert.equal(h.rt._mode, 'off');
    assert.equal(started(), null);
    assert.equal(h.draws.length, 0);
    assert.match(h.rt._reason, /color metadata unavailable/);
});

test('an item the server cannot return keeps realtime off', async () => {
    const {h, player} = setup({src: directPlay(OTHER)});
    await player._startRtWithConfig(h.video, {RealtimeMode: 'lanczos'});
    await flush();
    assert.equal(h.rt._mode, 'off');
    assert.match(h.rt._reason, /color metadata unavailable/);
});

test('auto mode reads genres for the item in the player, not the hash', async () => {
    const {h, player, lookups} = setup({src: directPlay(ITEM)});
    h.sandbox.document.querySelector = sel => (sel === 'video' ? h.video : null);
    assert.equal(player._getPlayingItemId(), ITEM);
    const pick = await player._autoSelectForVideo(h.video, {EnableAutoModelSelection: true});
    assert.equal(pick.model, 'realesrgan-x4');
    assert.deepEqual(lookups[0], ['u1', ITEM]);
    assert.match(lookups[1][1], /recommend-model\?width=640&height=360&isBatch=false&genres=Anime$/);
});
