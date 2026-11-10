'use strict';
// esc() in the plugin's config page and sidebar is used inside double-quoted HTML attributes
// (the queue table puts every job's file path into title="..."). The old helper escaped through
// textContent -> innerHTML, which encodes & < > but leaves quotes alone, so a library file named
//   Movie" onmouseover="..." style="position:fixed;inset:0" x=".mkv
// broke out of the attribute and ran script in the admin's Jellyfin session once the library
// scan had queued it. These tests run the real helpers from both files.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadEsc(file) {
  const src = fs.readFileSync(path.join(__dirname, '..', 'Configuration', file), 'utf8');
  const m = src.match(/function esc\(s\) \{[^\n]*\}/);
  assert.ok(m, `no single-line esc() helper found in ${file}`);
  // A minimal DOM stand-in, so the old textContent/innerHTML helper is measured by what it
  // escapes, not by failing to run: text nodes serialize & < > only.
  const document = { createElement: () => {
    let text = '';
    return {
      set textContent(v) { text = v; },
      get innerHTML() { return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'); },
    };
  } };
  const sandbox = { document };
  vm.createContext(sandbox);
  vm.runInContext(m[0] + '; this.esc = esc;', sandbox);
  return sandbox.esc;
}

const HOSTILE = 'Movie" onmouseover="alert(1)" style="position:fixed;inset:0" x=\'.mkv';

for (const file of ['configurationpage.html', 'sidebar-upscaler.js']) {
  test(`${file}: esc() output cannot leave a double- or single-quoted attribute`, () => {
    const esc = loadEsc(file);
    const out = esc(HOSTILE);
    assert.doesNotMatch(out, /["']/, out);
    assert.equal(esc('a"b\'c<d>&e'), 'a&quot;b&#39;c&lt;d&gt;&amp;e');
  });

  test(`${file}: the queue-row pattern keeps a hostile path inside its title attribute`, () => {
    const esc = loadEsc(file);
    const row = '<td class="file-path" title="' + esc(HOSTILE) + '">' + esc(HOSTILE) + '</td>';
    // exactly the two quotes the template itself writes, nothing from the file name
    assert.equal((row.match(/"/g) || []).length, 4, row);
    assert.doesNotMatch(row, /\sonmouseover="/i, row);
  });

  test(`${file}: esc() keeps its old behaviour for empty values and plain text`, () => {
    const esc = loadEsc(file);
    for (const v of [null, undefined, '', 0]) assert.equal(esc(v), '');
    assert.equal(esc('C:\\Media\\Film (2001).mkv'), 'C:\\Media\\Film (2001).mkv');
    assert.equal(esc(42), '42');
  });
}
