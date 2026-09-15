const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
exports.loadPlayer = function () {
    let now = 0, serial = 0;
    const requests = [], draws = [], images = [], intervals = new Map(), notices = [];
    const context = {drawImage: (...args) => draws.push(args)};
    const parent = {style: {}, appendChild(c) { c.parentElement = this; }, removeChild(c) { c.parentElement = null; }};
    const videoListeners = new Map();
    const video = {paused: false, playbackRate: 1, videoWidth: 640, videoHeight: 360, currentTime: 0, parentElement: parent,
        addEventListener(type, fn) { if (!videoListeners.has(type)) videoListeners.set(type, new Set()); videoListeners.get(type).add(fn); },
        removeEventListener(type, fn) { videoListeners.get(type)?.delete(fn); },
        dispatch(type) { for (const fn of videoListeners.get(type) || []) fn(); }
    };
    const sandbox = {console: {log() {}, warn() {}, error() {}}, AbortController, Date, Math: Object.create(Math),
        performance: {now: () => now},
        document: {readyState: 'loading', addEventListener() {}, getElementById() { return null; },
            querySelector() { return null; }, body: parent,
            createElement() { return {style: {}, getContext: () => context, toBlob(cb) { cb({frame: video.currentTime}); }}; }},
        requestAnimationFrame() { return ++serial; }, cancelAnimationFrame() {},
        setInterval(fn) { intervals.set(++serial, fn); return serial; }, clearInterval(id) { intervals.delete(id); },
        setTimeout, clearTimeout,
        ApiClient: {getUrl: x => x, accessToken: () => 'test'},
        URL: {createObjectURL: () => 'blob:' + (++serial), revokeObjectURL() {}},
        Image: class {constructor() { this.width = 960; this.height = 540; images.push(this); } set src(value) { this.url = value; }},
        fetch(url, options) { return new Promise((resolve, reject) => { requests.push({url, options, resolve, reject}); }); }
    };
    sandbox.Math.random = () => 0;
    sandbox.window = sandbox;
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../Configuration/player-integration.js'), 'utf8'), sandbox);
    sandbox.PlayerIntegration.showPlayerNotification = (...args) => notices.push(args);
    sandbox.PlayerIntegration._readPlayingVideoStream = async () => ({Type: 'Video', ColorTransfer: 'bt709', BitDepth: 8, AverageFrameRate: 30});
    const rt = sandbox.RealtimeUpscaler;
    rt._createFpsOverlay = () => {};
    return {rt, video, requests, draws, images, notices, sandbox,
        advance(ms, runIntervals = true) { now += ms; if (runIntervals) for (const fn of intervals.values()) fn(); },
        tick() { rt._serverRenderLoop(); },
        respond(index, status = 200, retry = null, detail = 'Busy') {
            requests[index].resolve({ok: status === 200, status, headers: {get: key => key === 'Retry-After' ? retry : null},
                text: async () => JSON.stringify({detail}), json: async () => ({detail}), blob: async () => ({result: index})});
        }
    };
};
exports.flush = () => new Promise(resolve => setImmediate(resolve));
