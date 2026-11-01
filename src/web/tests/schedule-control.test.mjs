/* global AbortController, Response */
import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

let vite, client, panel;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  client = await vite.ssrLoadModule('/src/views/scheduleControl.ts');
  panel = await vite.ssrLoadModule('/src/views/ConfiguredLogicSchedulePanel.tsx');
});
after(async () => { await vite?.close(); });

const LOGIC = '11111111-1111-4111-8111-111111111111';
const REVISION = '22222222-2222-4222-8222-222222222222';
const CHANGE = '33333333-3333-4333-8333-333333333333';
const shape = (enabled, cadenceSeconds) => ({ enabled, cadenceSeconds, startAtUtc: null, timeZoneId: 'UTC' });
const control = (overrides = {}) => ({
  configuredLogicId: LOGIC, activeRevisionId: REVISION, version: `${REVISION}.0`, declared: shape(true, 300),
  effective: shape(true, 300), differsFromRevision: false, nextRunAtUtc: '2026-11-01T12:05:00Z',
  minimumCadenceSeconds: 15, maximumCadenceSeconds: 604800, canManage: true, changes: [], ...overrides,
});
const paused = control({ version: `${REVISION}.1`, effective: shape(false, 300), differsFromRevision: true, nextRunAtUtc: null,
  changes: [{ id: CHANGE, version: 1, kind: 'Paused', schedule: shape(false, 300), changedBy: 'A. Rivera',
    changedAtUtc: '2026-11-01T11:42:00Z', reason: 'Sensor swap' }] });
const reply = (body, status = 200) => new Response(JSON.stringify(body), { status });
const signal = () => new AbortController().signal;

test('the live schedule is read from the governed endpoint and validated', async () => {
  const state = await client.fetchScheduleControl(LOGIC, signal(), async (url, init) => {
    assert.equal(url, `/api/v1/configured-logic/${LOGIC}/schedule`);
    assert.equal(init.credentials, 'same-origin');
    assert.equal(init.cache, 'no-store');
    return reply(paused);
  });
  assert.equal(state.version, `${REVISION}.1`);
  assert.equal(client.scheduleRunState(state), 'paused');
  assert.equal(client.scheduleRunState(control()), 'running');
  assert.equal(client.scheduleRunState(control({ activeRevisionId: null })), 'none');
  assert.equal(client.scheduleRunState(control({ effective: null })), 'none');
});

test('an unreadable schedule is never shown as running or paused', async () => {
  const bad = [[], {}, control({ configuredLogicId: 'x' }), control({ version: '' }), control({ differsFromRevision: 'no' }),
    control({ effective: { enabled: 'yes' } }), control({ effective: shape(true, 1.5) }), control({ nextRunAtUtc: 'soon' }),
    control({ minimumCadenceSeconds: 0 }), control({ maximumCadenceSeconds: 5 }), control({ changes: {} }),
    control({ changes: [{ ...paused.changes[0], kind: 'Exploded' }] }), control({ changes: [{ ...paused.changes[0], id: 'x' }] })];
  for (const body of bad) {
    await assert.rejects(client.fetchScheduleControl(LOGIC, signal(), async () => reply(body)), { kind: 'invalidResponse' }, JSON.stringify(body));
  }
  await assert.rejects(client.fetchScheduleControl(LOGIC, signal(), async () => new Response('nope')), { kind: 'invalidResponse' });
  for (const [status, kind] of [[401, 'accessDenied'], [403, 'accessDenied'], [409, 'stale'], [500, 'unavailable']]) {
    await assert.rejects(client.fetchScheduleControl(LOGIC, signal(), async () => new Response('{}', { status })), { kind });
  }
});

test('pause, resume and frequency go to their endpoints with the reviewed version', async () => {
  const seen = [];
  const send = (body) => async (url, init) => { seen.push({ url, init }); return reply(body); };
  await client.changeSchedule(control(), { kind: 'pause' }, '  Sensor swap ', signal(), send(paused));
  await client.changeSchedule(paused, { kind: 'resume' }, '', signal(), send(control({ version: `${REVISION}.2` })));
  await client.changeSchedule(control(), { kind: 'frequency', cadenceSeconds: 600 }, '', signal(),
    send(control({ effective: shape(true, 600), differsFromRevision: true })));
  assert.deepEqual(seen.map(call => [call.init.method, call.url.replace(`/api/v1/configured-logic/${LOGIC}/schedule`, '')]),
    [['POST', '/pause'], ['POST', '/resume'], ['PUT', '']]);
  assert.equal(seen[0].init.headers['If-Match'], `"${REVISION}.0"`);
  assert.equal(seen[1].init.headers['If-Match'], `"${REVISION}.1"`);
  assert.deepEqual(JSON.parse(seen[0].init.body), { reason: 'Sensor swap' });
  assert.deepEqual(JSON.parse(seen[1].init.body), { reason: null });
  assert.deepEqual(JSON.parse(seen[2].init.body), { cadenceSeconds: 600, reason: null });
});

test('an answer that does not show the requested outcome is not a success', async () => {
  await assert.rejects(client.changeSchedule(control(), { kind: 'pause' }, '', signal(), async () => reply(control())), { kind: 'invalidResponse' });
  await assert.rejects(client.changeSchedule(paused, { kind: 'resume' }, '', signal(), async () => reply(paused)), { kind: 'invalidResponse' });
  await assert.rejects(client.changeSchedule(control(), { kind: 'frequency', cadenceSeconds: 600 }, '', signal(),
    async () => reply(control())), { kind: 'invalidResponse' });
  await assert.rejects(client.changeSchedule(control(), { kind: 'pause' }, '', signal(),
    async () => reply({ ...paused, configuredLogicId: '44444444-4444-4444-8444-444444444444' })), { kind: 'invalidResponse' });
});

test('invalid input never reaches the backend, and conflicts are reported as stale', async () => {
  let calls = 0;
  const fetcher = async () => { calls++; return reply(paused); };
  for (const reason of ['x'.repeat(1025), 'bad\nreason', 'bad\u0000reason']) {
    await assert.rejects(client.changeSchedule(control(), { kind: 'pause' }, reason, signal(), fetcher), { kind: 'invalid' });
  }
  for (const cadenceSeconds of [14, 604801, 1.5, Number.NaN, -1]) {
    await assert.rejects(client.changeSchedule(control(), { kind: 'frequency', cadenceSeconds }, '', signal(), fetcher), { kind: 'invalid' }, String(cadenceSeconds));
  }
  assert.equal(calls, 0);
  for (const [status, kind] of [[400, 'invalid'], [401, 'accessDenied'], [403, 'accessDenied'], [409, 'stale'], [412, 'stale'], [500, 'unavailable']]) {
    await assert.rejects(client.changeSchedule(control(), { kind: 'pause' }, '', signal(),
      async () => new Response('{}', { status })), { kind });
  }
});

test('resume is offered only when there is a schedule to resume', () => {
  assert.equal(client.canResume(paused), true);
  assert.equal(client.canResume(control()), false);
  assert.equal(client.canResume(control({ effective: shape(false, null) })), false);
});

test('the panel is absent without a live revision and shows no state before it is confirmed', () => {
  assert.equal(renderToStaticMarkup(createElement(panel.ConfiguredLogicSchedulePanel,
    { configuredLogicId: LOGIC, activeRevisionId: null, mayManage: true, disabled: false })), '');
  const html = renderToStaticMarkup(createElement(panel.ConfiguredLogicSchedulePanel,
    { configuredLogicId: LOGIC, activeRevisionId: REVISION, mayManage: true, disabled: false }));
  assert.match(html, /Schedule control/);
  assert.doesNotMatch(html, /Running|Paused/, 'an unconfirmed schedule must not look running or paused');
  assert.doesNotMatch(html, /<button/, 'no action before the state is known');
});
