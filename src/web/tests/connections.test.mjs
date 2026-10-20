/* global AbortController, Response */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';
let vite, api;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  api = await vite.ssrLoadModule('/src/views/connections.ts');
});
after(async () => { await vite?.close(); });

const adapter = { key: 'simulator', displayName: 'Sim', description: '', settingsVersion: 1, capabilities: [],
  fields: [{ key: 'baseUrl', label: 'Address', helpText: '', kind: 'Url', required: true, maxLength: 256 }] };
const connection = { id: '00000000-0000-4000-8000-000000000001', name: 'A', adapterKey: 'simulator', settingsVersion: 1,
  settings: { baseUrl: 'http://x' }, isEnabled: true, revision: 3, createdAtUtc: 'a', updatedAtUtc: 'b', lastTest: null,
  lastTestIsCurrent: false };
const problem = (status, code, detail) => new Response(JSON.stringify({ code, detail }), { status });

test('only settings with a value are sent, trimmed, and only the adapter declared fields', () => {
  assert.deepEqual(api.cleanSettings(adapter, { baseUrl: '  http://x ', stray: 'y' }), { baseUrl: 'http://x' });
  assert.deepEqual(api.cleanSettings(adapter, { baseUrl: '  ' }), {});
});

test('an update names the revision it was made from', async () => {
  let sent;
  await api.updateConnection(connection, adapter, { name: ' B ', settings: { baseUrl: 'http://y' } }, async (url, init) => {
    sent = { url, method: init.method, body: JSON.parse(init.body) };
    return new Response(JSON.stringify({ ...connection, revision: 4 }));
  });
  assert.equal(sent.url, '/api/v1/connections/' + connection.id);
  assert.equal(sent.method, 'PUT');
  assert.deepEqual(sent.body, { expectedRevision: 3, name: 'B', settings: { baseUrl: 'http://y' } });
});

test('server refusals become specific, safe error kinds', async () => {
  const kind = async (response) => {
    try { await api.testConnection(connection, async () => response); } catch (error) { return [error.kind, error.detail]; }
    return null;
  };
  assert.deepEqual(await kind(problem(409, 'CONNECTION_NAME_CONFLICT')), ['nameTaken', null]);
  assert.deepEqual(await kind(problem(409, 'CONNECTION_REVISION_STALE')), ['stale', null]);
  assert.deepEqual(await kind(problem(409, 'CONNECTION_DISABLED')), ['disabled', null]);
  assert.deepEqual(await kind(problem(400, 'CONNECTION_SETTINGS_INVALID', 'baseUrl: bad')), ['invalidSettings', 'baseUrl: bad']);
  assert.deepEqual(await kind(problem(400, 'VALIDATION_FAILED')), ['invalid', null]);
  assert.deepEqual(await kind(problem(403)), ['accessDenied', null]);
  assert.deepEqual(await kind(problem(404)), ['notFound', null]);
  assert.deepEqual(await kind(problem(503)), ['gatewayUnavailable', null]);
  assert.deepEqual(await kind(problem(500)), ['unavailable', null]);
});

test('malformed lists are refused rather than shown', async () => {
  const signal = new AbortController().signal;
  await assert.rejects(api.loadConnections(signal, async () => new Response('[{"id":"x"}]')), { kind: 'invalidResponse' });
  await assert.rejects(api.loadAdapters(signal, async () => new Response('[{"key":"a"}]')), { kind: 'invalidResponse' });
  assert.equal((await api.loadConnections(signal, async () => new Response('[]'))).length, 0);
});

test('status reflects enablement, testing and whether the test still applies', () => {
  assert.equal(api.connectionStatus({ ...connection, isEnabled: false }), 'disabled');
  assert.equal(api.connectionStatus(connection), 'notTested');
  const lastTest = { outcome: 'Unreachable', message: '', actingAs: null, testedAtUtc: 't', testedRevision: 3 };
  assert.equal(api.connectionStatus({ ...connection, lastTest, lastTestIsCurrent: true }), 'Unreachable');
  assert.equal(api.connectionStatus({ ...connection, lastTest, lastTestIsCurrent: false }), 'outdated');
});

test('dirty detection ignores whitespace and empty optional values', () => {
  assert.equal(api.isDirty(connection, adapter, { name: 'A ', settings: { baseUrl: ' http://x' } }), false);
  assert.equal(api.isDirty(connection, adapter, { name: 'A', settings: { baseUrl: 'http://z' } }), true);
  assert.equal(api.isDirty(connection, adapter, { name: 'B', settings: { baseUrl: 'http://x' } }), true);
});
