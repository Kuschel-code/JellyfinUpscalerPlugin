const {test} = require('node:test');
const assert = require('node:assert/strict');
const {loadPlayer} = require('./player-harness.cjs');

// v1.8.3.35 - the Lanczos, Anime4K and WebGPU engines load relative to the web client, like the
// player script's own tag in index.html. They used an absolute "/web/..." path, which drops a
// configured base URL; Jellyfin answers such a request with a redirect to its start page, so
// behind a base URL none of the three engines ever loaded.
function loadWithScripts() {
    const appended = [];
    const document = {
        readyState: 'loading', addEventListener() {}, getElementById() { return null; }, querySelector() { return null; },
        createElement: () => ({style: {}, attrs: {}, setAttribute(name, value) { this.attrs[name] = value; }}),
        head: {appendChild(element) { appended.push(element); }}, body: {appendChild() {}}
    };
    return {h: loadPlayer({globals: {document}}), appended};
}

const PAGES = ['http://jf.local:8096/web/index.html', 'http://jf.local:8096/web/', 'https://media.example/jellyfin/web/index.html'];

for (const [loader, name] of [['_loadWebGLScript', 'UPSCALERWebGLShader'], ['_loadAnime4KLibrary', 'UPSCALERAnime4K'],
                              ['_loadWebGPUAIScript', 'UPSCALERWebGPUAI']]) {
    test(name + ' is requested under the web client, with or without a base URL', () => {
        const {h, appended} = loadWithScripts();
        h.rt[loader](() => {});
        assert.equal(appended.length, 1);
        for (const page of PAGES) {
            const url = new URL(appended[0].src, page);
            assert.equal(url.pathname, new URL('configurationpage', page).pathname, page);
            assert.equal(url.searchParams.get('name'), name);
            assert.match(url.searchParams.get('release'), /^\d+\.\d+\.\d+\.\d+$/);
        }
    });
}
