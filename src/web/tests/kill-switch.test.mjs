/* global AbortController, Response */
import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

let vite, client, panel;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  client = await vite.ssrLoadModule('/src/views/controlKillSwitch.ts');
  panel = await vite.ssrLoadModule('/src/views/KillSwitchPanel.tsx');
});
after(async () => { await vite?.close(); });

const released = { engaged: false, changedBy: null, changedAtUtc: null, reason: null, autonomousControlEnabled: true };
const engaged = { engaged: true, changedBy: 'FIELD\\admin', changedAtUtc: '2026-10-14T12:00:00Z', reason: 'Pump trip', autonomousControlEnabled: true };
const reply = (body, status = 200) => new Response(JSON.stringify(body), { status });

test('the state is read from the governed endpoint and validated', async () => {
  const state = await client.fetchKillSwitch(new AbortController().signal, async (url, init) => {
    assert.equal(url, '/api/v1/control/kill-switch');
    assert.equal(init.credentials, 'same-origin');
    assert.equal(init.cache, 'no-store');
    return reply(engaged);
  });
  assert.deepEqual(state, engaged);
  assert.deepEqual(await client.fetchKillSwitch(new AbortController().signal, async () => reply(released)), released);
});

test('an unreadable or contradictory state is never shown as released', async () => {
  for (const body of [[], {}, { ...released, engaged: 'no' }, { ...released, autonomousControlEnabled: null },
    { ...engaged, changedAtUtc: 'yesterday' }, { ...engaged, changedBy: 7 }, { ...engaged, reason: {} }]) {
    await assert.rejects(client.fetchKillSwitch(new AbortController().signal, async () => reply(body)),
      { kind: 'invalidResponse' }, JSON.stringify(body));
  }
  await assert.rejects(client.fetchKillSwitch(new AbortController().signal, async () => new Response('nope')),
    { kind: 'invalidResponse' });
  for (const [status, kind] of [[401, 'accessDenied'], [403, 'accessDenied'], [500, 'unavailable']]) {
    await assert.rejects(client.fetchKillSwitch(new AbortController().signal, async () => new Response('{}', { status })), { kind });
  }
});

test('engaging posts the requested state once, with a trimmed optional reason', async () => {
  let calls = 0;
  const state = await client.setKillSwitch(true, '  Pump trip  ', new AbortController().signal, async (url, init) => {
    calls++;
    assert.equal(url, '/api/v1/control/kill-switch');
    assert.equal(init.method, 'PUT');
    assert.equal(init.credentials, 'same-origin');
    assert.deepEqual(JSON.parse(init.body), { engaged: true, reason: 'Pump trip' });
    return reply(engaged);
  });
  assert.equal(calls, 1);
  assert.equal(state.engaged, true);
  await client.setKillSwitch(false, '   ', new AbortController().signal, async (_url, init) => {
    assert.deepEqual(JSON.parse(init.body), { engaged: false, reason: null });
    return reply(released);
  });
});

test('a response that reports the other state is not a success', async () => {
  await assert.rejects(client.setKillSwitch(true, '', new AbortController().signal, async () => reply(released)),
    { kind: 'invalidResponse' });
  await assert.rejects(client.setKillSwitch(false, '', new AbortController().signal, async () => reply(engaged)),
    { kind: 'invalidResponse' });
});

test('an invalid reason never reaches the backend, and failures fail closed', async () => {
  let calls = 0;
  for (const reason of ['x'.repeat(1025), 'bad\nreason', 'bad\u0000reason']) {
    await assert.rejects(client.setKillSwitch(true, reason, new AbortController().signal,
      async () => { calls++; return reply(engaged); }), { kind: 'invalid' });
  }
  assert.equal(calls, 0);
  for (const [status, kind] of [[401, 'accessDenied'], [403, 'accessDenied'], [400, 'invalid'], [500, 'unavailable']]) {
    await assert.rejects(client.setKillSwitch(true, '', new AbortController().signal,
      async () => new Response('{}', { status })), { kind });
  }
});

test('the panel shows nothing without a user and a checking message until the state is known', () => {
  assert.equal(renderToStaticMarkup(createElement(panel.KillSwitchPanel, { userId: null, canManage: true })), '');
  const html = renderToStaticMarkup(createElement(panel.KillSwitchPanel, { userId: 'operator', canManage: true }));
  assert.match(html, /Control kill switch/);
  assert.match(html, /Checking the control kill switch/);
  assert.doesNotMatch(html, /Released/, 'an unconfirmed state must not look released');
  assert.doesNotMatch(html, /<button/, 'no action before the state is known');
});
