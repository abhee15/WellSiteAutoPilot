/* global AbortController, Response */
import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createServer } from 'vite';

let vite, client;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  client = await vite.ssrLoadModule('/src/views/controlActions.ts');
});
after(async () => { await vite?.close(); });

const id = '11111111-1111-4111-8111-111111111111';
const action = (over = {}) => ({
  id, recommendationId: '22222222-2222-4222-8222-222222222222', executionId: '33333333-3333-4333-8333-333333333333',
  configuredLogicId: '44444444-4444-4444-8444-444444444444', assetId: '55555555-5555-4555-8555-555555555555',
  command: 'SET_CHOKE', quantity: null, value: 40, unit: '%', status: 'Ambiguous', gateCode: 'SINK_AMBIGUOUS',
  sink: 'Simulator', sinkReference: null, createdAtUtc: '2026-10-14T12:00:00Z', completedAtUtc: null,
  origin: 'Autonomous', readbackValue: null, verifiedAtUtc: null, resolutionNote: null, ...over,
});
const reply = (body, status = 200) => new Response(JSON.stringify(body), { status });
const signal = () => new AbortController().signal;

test('only dispatched and ambiguous actions need a decision', () => {
  for (const [status, needs] of [['Dispatched', true], ['Ambiguous', true], ['Requested', false], ['Verified', false],
    ['Failed', false], ['Blocked', false]]) {
    assert.equal(client.controlActionNeedsDecision({ status }), needs, status);
  }
});

test('the list is read from the governed endpoint and every row is validated', async () => {
  const rows = await client.fetchControlActions(signal(), async (url, init) => {
    assert.equal(url, '/api/v1/control/actions?limit=200');
    assert.equal(init.credentials, 'same-origin');
    assert.equal(init.cache, 'no-store');
    return reply([action()]);
  });
  assert.equal(rows[0].command, 'SET_CHOKE');
  for (const body of [{}, [action({ id: 'nope' })], [action({ status: 'Mystery' })], [action({ createdAtUtc: 'soon' })],
    [action({ value: '40' })], [null]]) {
    await assert.rejects(client.fetchControlActions(signal(), async () => reply(body)),
      { kind: 'invalidResponse' }, JSON.stringify(body));
  }
  for (const [status, kind] of [[401, 'accessDenied'], [403, 'accessDenied'], [500, 'unavailable']]) {
    await assert.rejects(client.fetchControlActions(signal(), async () => reply([], status)), { kind });
  }
});

test('verify posts to the action and only accepts that action back', async () => {
  const got = await client.verifyControlAction(id, signal(), async (url, init) => {
    assert.equal(url, `/api/v1/control/actions/${id}/verify`);
    assert.equal(init.method, 'POST');
    return reply(action({ status: 'Verified', gateCode: 'READBACK_CONFIRMED' }));
  });
  assert.equal(got.status, 'Verified');
  await assert.rejects(client.verifyControlAction(id, signal(), async () =>
    reply(action({ id: '66666666-6666-4666-8666-666666666666' }))), { kind: 'invalidResponse' });
  await assert.rejects(client.verifyControlAction('nope', signal(), async () => reply(action())), { kind: 'invalid' });
  for (const [status, kind] of [[404, 'notFound'], [409, 'conflict'], [403, 'accessDenied'], [503, 'unavailable']]) {
    await assert.rejects(client.verifyControlAction(id, signal(), async () => reply({}, status)), { kind });
  }
});

test('an unchanged verify is told apart from one that settled something', () => {
  assert.equal(client.verifyChangedAction(action(), action()), false);
  assert.equal(client.verifyChangedAction(action(), action({ status: 'Verified' })), true);
  assert.equal(client.verifyChangedAction(action({ status: 'Dispatched', gateCode: 'A' }), action({ status: 'Dispatched', gateCode: 'B' })), true);
});

test('resolving needs a note, sends the attestation once, and must settle as attested', async () => {
  let calls = 0;
  const done = await client.resolveControlAction(id, true, '  Checked the choke on site  ', signal(), async (url, init) => {
    calls++;
    assert.equal(url, `/api/v1/control/actions/${id}/resolve`);
    assert.equal(init.method, 'POST');
    assert.deepEqual(JSON.parse(init.body), { applied: true, note: 'Checked the choke on site' });
    return reply(action({ status: 'Verified', gateCode: 'OPERATOR_RESOLVED_APPLIED' }));
  });
  assert.equal(calls, 1);
  assert.equal(done.status, 'Verified');
  const failed = await client.resolveControlAction(id, false, 'Not moved', signal(), async () =>
    reply(action({ status: 'Failed', gateCode: 'OPERATOR_RESOLVED_NOT_APPLIED' })));
  assert.equal(failed.status, 'Failed');
  // Attested applied but the server says failed (or the reverse): not shown as done.
  await assert.rejects(client.resolveControlAction(id, true, 'ok', signal(), async () =>
    reply(action({ status: 'Failed' }))), { kind: 'invalidResponse' });
  await assert.rejects(client.resolveControlAction(id, false, 'ok', signal(), async () =>
    reply(action({ status: 'Verified' }))), { kind: 'invalidResponse' });
});

test('a blank, oversized or control-character note never reaches the server', async () => {
  const never = async () => { throw new Error('must not be called'); };
  for (const note of ['', '   ', 'x'.repeat(1025), 'bad\u0000note', 'bad\u007fnote']) {
    await assert.rejects(client.resolveControlAction(id, true, note, signal(), never), { kind: 'invalid' }, JSON.stringify(note));
  }
  assert.equal(client.normalizeResolutionNote('line one\nline two\ttab'), 'line one\nline two\ttab');
  assert.equal(client.normalizeResolutionNote('x'.repeat(1024)).length, 1024);
});
