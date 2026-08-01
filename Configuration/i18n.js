/*
 * i18n.js - v1.8.3.20 (Phase 2, groundwork only)
 *
 * English is the SOURCE language here, not a translation target: strings.en.json is
 * where these strings live, and the UI reads them from it. Adding a locale means
 * copying that file, translating the values, and shipping it - no UI change.
 *
 * Deliberately tiny. It does three things:
 *   t(key, params)      - look up a string, interpolate {name} placeholders
 *   applyTo(root)       - fill every [data-i18n] element's text from its key
 *   ready()             - a promise that resolves once the catalogue is loaded
 *
 * There is no plural handling, no gender, no date formatting. Those are real
 * problems and they need a real library; inventing half of one here would be
 * worse than the inline strings it replaces. When a second locale actually
 * lands and needs plurals, this gets swapped - the call sites will not change.
 *
 * MISSING KEYS DO NOT BLANK THE UI. t() returns the key itself, which is ugly
 * and obvious in a screenshot, rather than an empty string that silently
 * deletes a label. A test asserts every key used in the code exists in the JSON,
 * so this fallback should never be reachable in a release.
 */
(function () {
    'use strict';

    var CATALOGUE = {};
    var loaded = false;
    var loadPromise = null;

    function interpolate(text, params) {
        if (!params) return text;
        return text.replace(/\{(\w+)\}/g, function (whole, name) {
            return Object.prototype.hasOwnProperty.call(params, name) ? String(params[name]) : whole;
        });
    }

    function t(key, params) {
        var raw = Object.prototype.hasOwnProperty.call(CATALOGUE, key) ? CATALOGUE[key] : null;
        if (raw === null) {
            // Loud, not silent: an untranslated key is visible as its own name.
            if (loaded) console.warn('AI Upscaler i18n: missing key "' + key + '"');
            return key;
        }
        return interpolate(raw, params);
    }

    function load() {
        if (loadPromise) return loadPromise;
        var url = (window.ApiClient && ApiClient.getUrl)
            ? ApiClient.getUrl('web/ConfigurationPage', { name: 'UPSCALERStringsEn' })
            : 'strings.en.json';   // standalone/test fallback
        loadPromise = fetch(url)
            .then(function (r) { return r.ok ? r.json() : {}; })
            .then(function (doc) {
                Object.keys(doc || {}).forEach(function (k) {
                    if (k !== '_meta') CATALOGUE[k] = doc[k];
                });
                loaded = true;
                return CATALOGUE;
            })
            .catch(function (err) {
                // A failed catalogue must not take the page with it: t() falls back
                // to key names, which is degraded but navigable.
                console.warn('AI Upscaler i18n: catalogue unavailable, falling back to keys', err);
                loaded = true;
                return CATALOGUE;
            });
        return loadPromise;
    }

    function applyTo(root) {
        if (!root) return;
        root.querySelectorAll('[data-i18n]').forEach(function (el) {
            el.textContent = t(el.getAttribute('data-i18n'));
        });
        root.querySelectorAll('[data-i18n-title]').forEach(function (el) {
            el.setAttribute('title', t(el.getAttribute('data-i18n-title')));
        });
    }

    window.AiUpscalerI18n = {
        t: t,
        applyTo: applyTo,
        ready: load,
        // Exposed for the key-coverage test and for debugging in the console.
        _catalogue: function () { return CATALOGUE; }
    };
})();
