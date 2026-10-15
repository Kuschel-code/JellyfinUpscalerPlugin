'use strict';
// Jellyfin's plugin-configuration API (/Plugins/{id}/Configuration) is admin-only. The player
// read its settings there, so for every other user the read failed with 403 and real-time
// upscaling never started. It now reads GET /Upscaler/config, which any signed-in user may
// call and which carries only the settings the player needs.
// Changes still go through Jellyfin's API, and Jellyfin REPLACES the whole configuration with
// what is posted. A change built on that partial copy would reset the AI service URL, its
// token, the webhooks and every other setting the copy leaves out.
const test = require('node:test');
const assert = require('node:assert/strict');
const harness = require('./player-harness.cjs');

const FULL = {
  AiServiceUrl: 'http://ai:5000', AiServiceApiToken: 'secret', WebhookUrl: 'https://hooks.example/x',
  EnabledLibraryIds: ['movies'], MaxQueueSize: 77,
  Model: 'realesrgan-x2', ScaleFactor: 2, EnablePlugin: true, EnableRealtimeUpscaling: true, RealtimeMode: 'auto',
};
const PLAYER_COPY = { Model: 'realesrgan-x2', ScaleFactor: 2, EnablePlugin: true, EnableRealtimeUpscaling: true, RealtimeMode: 'auto' };
const forbidden = () => Promise.reject(Object.assign(new Error('Forbidden'), { status: 403 }));
const plain = (o) => JSON.parse(JSON.stringify(o));

function player({ admin }) {
  const h = harness.loadPlayer({ globals: {} });
  const api = h.sandbox.ApiClient;
  const posted = [];
  api.ajax = (opts) => opts.url === 'Upscaler/config'
    ? Promise.resolve(plain(PLAYER_COPY))
    : Promise.reject(new Error('unexpected request ' + opts.url));
  api.getPluginConfiguration = admin ? () => Promise.resolve(plain(FULL)) : forbidden;
  api.updatePluginConfiguration = admin ? (id, cfg) => { posted.push(plain(cfg)); return Promise.resolve(); } : forbidden;
  return { h, P: h.sandbox.PlayerIntegration, api, posted };
}

test('real-time upscaling starts for a user who is not an administrator', async () => {
  const { h, P } = player({ admin: false });
  P.findVideoElement = () => h.video;
  P._autoSelectForVideo = async () => null;
  let started = null;
  P._startRtWithConfig = (video, config) => { started = plain(config); };
  await P.startRealtimeUpscaling();
  await harness.flush();
  assert.ok(started, 'real-time upscaling never started');
  assert.equal(started.Model, 'realesrgan-x2');
});

test('a change from the player menu keeps every setting the player does not read', async () => {
  const { P, posted } = player({ admin: true });
  await P.getPluginConfig(); // caches the player's partial copy
  await P.updatePluginConfig({ Model: 'realesrgan-x4' });
  assert.equal(posted.length, 1);
  assert.deepEqual(posted[0], { ...FULL, Model: 'realesrgan-x4' });
});

test('two quick changes from the menu both reach the server', async () => {
  const { P, api } = player({ admin: true });
  let server = plain(FULL);
  const later = (fn) => new Promise((resolve) => setTimeout(() => resolve(fn()), 5));
  api.getPluginConfiguration = () => later(() => plain(server));
  api.updatePluginConfiguration = (id, cfg) => later(() => { server = plain(cfg); });
  await Promise.all([P.updatePluginConfig({ Model: 'realesrgan-x4' }), P.updatePluginConfig({ ScaleFactor: 4 })]);
  assert.equal(server.Model, 'realesrgan-x4');
  assert.equal(server.ScaleFactor, 4);
  assert.equal(server.AiServiceApiToken, 'secret');
});

test('a user who is not an administrator is told a menu change was not saved', async () => {
  const { h, P, posted } = player({ admin: false });
  await assert.rejects(P.updatePluginConfig({ Model: 'realesrgan-x4' }), /administrator/);
  assert.equal(posted.length, 0);

  await P.setScale(4);
  const [text, kind] = h.notices[h.notices.length - 1];
  assert.match(text, /administrator/);
  assert.notEqual(kind, 'success');
});

test('a save that the server refuses is explained the same way', async () => {
  const { P, api } = player({ admin: true });
  api.updatePluginConfiguration = forbidden;
  await assert.rejects(P.updatePluginConfig({ Model: 'realesrgan-x4' }), /administrator/);
});
