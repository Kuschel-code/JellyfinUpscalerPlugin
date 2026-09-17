const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const flush = () => new Promise(resolve => setImmediate(resolve));

function element() {
    const children = [];
    let value = '';
    let text = '', html = '';
    return {style: {}, children,
        get textContent() { return text; },
        set textContent(v) { text = String(v); html = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'); },
        get innerHTML() { return html; }, set innerHTML(v) { html = String(v); },
        get value() { return value; }, set value(v) { value = String(v); },
        appendChild(child) { const old = children.indexOf(child); if (old >= 0) children.splice(old, 1); children.push(child); },
        querySelector() { return null; }, querySelectorAll() { return children; },
        addEventListener() {}, closest() { return null; }};
}

function select(values, current) {
    const options = values.map(value => ({value: String(value)}));
    let selected = String(current);
    return {options,
        get value() { return selected; },
        set value(value) { selected = options.some(o => o.value === String(value)) ? String(value) : ''; },
        set innerHTML(_) { options.length = 0; selected = ''; },
        appendChild(option) { options.push(option); if (!selected) selected = option.value; }};
}

function harness(models, config = {}) {
    const requests = [], notices = [], nodes = {};
    nodes['#Model'] = select(models.map(m => m.id), models[0]?.id || '');
    nodes['#ScaleFactor'] = select([2, 3, 4], 2);
    for (const id of ['btn-live-bench', 'btn-stop-bench', 'live-bench-progress', 'live-bench-bar', 'live-bench-status', 'live-bench-body', 'model-catalog-body', 'model-catalog-table']) {
        nodes['#' + id] = element();
    }
    const rows = {};
    const page = {_allModels: models, querySelector: id => nodes[id] || null, querySelectorAll: () => []};
    const sandbox = {console, notices, setTimeout() {}, setInterval() {}, clearInterval() {},
        document: {addEventListener() {}, getElementById: id => rows[id] || null,
            querySelector: id => id === '#UpscalerConfigurationPage' ? page : null,
            createElement(type) {
                const el = element();
                if (type === 'tr') {
                    let id;
                    Object.defineProperty(el, 'id', {get: () => id, set: value => { id = value; rows[id] = el; }});
                    const cells = {};
                    el.querySelector = selector => cells[selector] ||= element();
                }
                return el;
            }},
        ApiClient: {getPluginConfiguration: async () => config, getUrl: url => url,
            ajax: async options => {
                requests.push(options.url);
                if (options.url === 'Upscaler/models') return {models};
                if (options.url === 'Upscaler/model-benchmark') return {fps: 2, avg_time_ms: 500};
                return {available: true};
            }}
    };
    sandbox.window = sandbox;
    sandbox._aiUpscalerLoaded = true;
    const html = fs.readFileSync(path.join(__dirname, '../Configuration/configurationpage.html'), 'utf8');
    let script = html.match(/<script type="text\/javascript">([\s\S]*)<\/script>/)[1];
    const end = script.lastIndexOf('})();');
    script = script.slice(0, end) + `
        modelsData = globalThis.testModels;
        showToast = message => notices.push(message);
        globalThis.api = {updateScaleOptions, loadConfig, runLiveVideoBenchmark, loadModelCatalog};
    ` + script.slice(end);
    sandbox.testModels = models;
    vm.runInNewContext(script, sandbox);
    return {sandbox, api: sandbox.api, page, nodes, requests, notices};
}

const mixedModels = [
    {id: 'rife-v4.9', category: 'interpolation', available: true},
    {id: 'tiny-yolov3', category: 'object-detection', available: true},
    {id: 'gfpgan-v1.4', category: 'FACE_RESTORE', available: true},
    {id: 'codeformer', category: 'face-restore', available: true},
    {id: 'unavailable-x4', category: 'video-fast', available: false},
    {id: 'my-import-x2', category: 'super-resolution', available: true}
];

test('live benchmark loads only eligible upscalers and still accepts custom imports', async () => {
    const h = harness(mixedModels);
    await h.api.runLiveVideoBenchmark(h.page);
    assert.deepEqual(h.requests.filter(url => url.includes('models/load')),
        ['Upscaler/models/load?model_name=my-import-x2']);
});

test('an all-ineligible benchmark does not restore the unfiltered catalog', async () => {
    const h = harness(mixedModels.slice(0, -1));
    await h.api.runLiveVideoBenchmark(h.page);
    assert.equal(h.requests.filter(url => url.includes('models/load')).length, 0);
    assert.match(h.notices[0], /No available image upscalers/);
});

test('catalog and single-model benchmark do not offer detector, RIFE, face or unavailable entries', async () => {
    const h = harness(mixedModels);
    h.api.loadModelCatalog(h.page); await flush();
    const html = h.nodes['#model-catalog-body'].innerHTML;
    assert.equal((html.match(/data-bench-model=/g) || []).length, 1);
    assert.match(html, /data-bench-model="my-import-x2"/);
    for (const model of mixedModels.slice(0, -1)) h.sandbox.AiUpscaler._benchModel(model.id);
    assert.equal(h.requests.filter(url => url.includes('models/load')).length, 0);
});

test('switching from 2x to a 4x-only model selects its native scale instead of a blank value', () => {
    const h = harness([{id: 'my-import-x4', scale: [4]}]);
    h.api.updateScaleOptions(h.page);
    assert.equal(h.nodes['#ScaleFactor'].value, '4');
});

test('loading a stale configured scale keeps the selected models supported scale', async () => {
    const h = harness([{id: 'my-import-x4', scale: [4]}], {Model: 'my-import-x4', ScaleFactor: 2});
    h.api.loadConfig(h.page); await flush();
    assert.equal(h.nodes['#ScaleFactor'].value, '4');
});

test('a supported choice is retained for models with more than one native scale', () => {
    const h = harness([{id: 'my-import', scale: [2, 4]}]);
    h.api.updateScaleOptions(h.page, 4);
    assert.equal(h.nodes['#ScaleFactor'].value, '4');
});
