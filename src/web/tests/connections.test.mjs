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

const adapter = { key: 'simulator', displayName: 'Sim', description: '', settingsVersion: 1, capabilities: [], canDiscover: false, groupingFields: [],
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

test('discovery is validated, filtered by name or any fact, and its refusals are classified', async () => {
  const snapshotId = '11111111-1111-4111-8111-111111111111';
  const good = { complete: true, actingAs: 'A\\b', warnings: [], totalCount: 2, snapshotId, expiresAtUtc: '2026-10-21T10:15:00Z',
    facets: [{ key: 'wellType', label: 'Well type', hasMore: false, values: [{ value: 'ESP', count: 1 }, { value: 'Unknown type', count: 1 }] }],
    items: [
      { externalId: '1', displayName: 'Alpha 1', kind: 'k', metadata: { wellType: 'ESP' } },
      { externalId: '2', displayName: 'Bravo', kind: 'k', metadata: {} }] };
  const found = await api.discoverConnection(connection, async (url, init) => {
    assert.equal(String(url), `/api/v1/connections/${connection.id}/discover`);
    assert.equal(init.method, 'POST');
    return new Response(JSON.stringify(good));
  });
  assert.equal(found.items.length, 2);
  assert.equal(found.totalCount, 2);
  assert.equal(found.snapshotId, snapshotId);
  assert.equal(found.facets[0].values[0].count, 1);
  for (const bad of [{ ...good, totalCount: 1 }, { ...good, snapshotId: 'nope' }, { ...good, facets: [{ key: 'x' }] }, { ...good, totalCount: undefined }]) {
    await assert.rejects(api.discoverConnection(connection, async () => new Response(JSON.stringify(bad))), { kind: 'invalidResponse' });
  }
  assert.deepEqual(api.filterDiscovered(found.items, ' esp ').map(i => i.externalId), ['1']);
  assert.deepEqual(api.filterDiscovered(found.items, 'brav').map(i => i.externalId), ['2']);
  assert.equal(api.filterDiscovered(found.items, '').length, 2);
  await assert.rejects(api.discoverConnection(connection, async () => new Response('{"complete":"yes"}')), { kind: 'invalidResponse' });
  const refuse = (status, code) => api.discoverConnection(connection, async () =>
    new Response(JSON.stringify({ code }), { status })).catch(e => e.kind);
  assert.equal(await refuse(409, 'CONNECTION_DISCOVERY_UNSUPPORTED'), 'unsupported');
  assert.equal(await refuse(409, 'CONNECTION_DISABLED'), 'disabled');
  assert.equal(await refuse(400, 'CONNECTION_DISCOVERY_TOO_LARGE'), 'tooLarge');
  assert.equal(await refuse(409, 'CONNECTION_HAS_IMPORTS'), 'hasImports');
  assert.equal(await refuse(409, 'CONNECTION_SNAPSHOT_EXPIRED'), 'snapshotExpired');
  assert.equal(await refuse(400, 'CONNECTION_IMPORT_TOO_LARGE'), 'importTooLarge');
  assert.equal(await refuse(503, undefined), 'gatewayUnavailable');
});

test('searching what was found sends the kept list, words and chosen values, and validates the answer', async () => {
  const selection = { snapshotId: '11111111-1111-4111-8111-111111111111', query: 'alpha', facets: { field: ['Alpha'] } };
  const seen = [];
  const found = await api.searchDiscovered(connection, selection, async (url, init) => {
    seen.push([String(url), init.method, init.body]);
    return new Response(JSON.stringify({ matchCount: 5, items: [{ externalId: '1', displayName: 'A', kind: 'k', metadata: {} }] }));
  });
  assert.equal(found.matchCount, 5);
  assert.deepEqual(seen[0], [`/api/v1/connections/${connection.id}/discover/search`, 'POST',
    '{"snapshotId":"11111111-1111-4111-8111-111111111111","query":"alpha","facets":{"field":["Alpha"]}}']);
  await api.searchDiscovered(connection, { ...selection, query: '', facets: {} }, async (_url, init) => {
    assert.equal(init.body, '{"snapshotId":"11111111-1111-4111-8111-111111111111","query":null,"facets":null}');
    return new Response(JSON.stringify({ matchCount: 0, items: [] }));
  });
  await assert.rejects(api.searchDiscovered(connection, selection, async () => new Response('{"matchCount":0,"items":[{}]}')), { kind: 'invalidResponse' });
  await assert.rejects(api.searchDiscovered(connection, selection, async () => new Response('{"matchCount":0,"items":[{"externalId":"1","displayName":"A","kind":"k","metadata":{}}]}')), { kind: 'invalidResponse' });
  await assert.rejects(api.searchDiscovered(connection, selection, async () => new Response('{}', { status: 409 })), { kind: 'stale' });
  await assert.rejects(api.searchDiscovered(connection, selection, async () =>
    new Response(JSON.stringify({ code: 'CONNECTION_SNAPSHOT_EXPIRED' }), { status: 409 })), { kind: 'snapshotExpired' });
});

test('import preview and result are validated, and refusals are classified', async () => {
  const group = { name: 'North', isFallback: false, objectCount: 2, sampleNames: ['A'], children: [] };
  const good = { fingerprint: 'F', levels: ['x'], objectCount: 2, groupCount: 1, fallbackObjectCount: 0, rootObjectCount: 0,
    groups: [group], warnings: [], conflicts: [], typesToCreate: ['well'], canImport: true, actingAs: null, alreadyImported: 0 };
  const seen = [];
  const preview = await api.previewImport(connection, ['x'], null, async (url, init) => {
    seen.push([String(url), init.method, init.body]);
    return new Response(JSON.stringify(good));
  });
  assert.equal(preview.fingerprint, 'F');
  assert.deepEqual(seen[0], [`/api/v1/connections/${connection.id}/import/preview`, 'POST', '{"levels":["x"]}']);
  await assert.rejects(api.previewImport(connection, [], null, async () => new Response(JSON.stringify({ ...good, fingerprint: '' }))), { kind: 'invalidResponse' });
  await assert.rejects(api.previewImport(connection, [], null, async () => new Response(JSON.stringify({ ...good, groups: [{ name: 1 }] }))), { kind: 'invalidResponse' });
  const chosen = { snapshotId: '11111111-1111-4111-8111-111111111111', query: 'alpha', facets: { field: ['Alpha'] } };
  await api.previewImport(connection, ['x'], chosen, async (_url, init) => {
    assert.equal(init.body, '{"levels":["x"],"snapshotId":"11111111-1111-4111-8111-111111111111","query":"alpha","facets":{"field":["Alpha"]}}');
    return new Response(JSON.stringify(good));
  });
  const done = { wellsCreated: 2, groupsCreated: 1, groupsReused: 0, alreadyImported: 0, typesCreated: [] };
  const result = await api.importFromConnection(connection, ['x'], 'F', null, async (url, init) => {
    assert.equal(init.body, '{"levels":["x"],"expectedFingerprint":"F"}');
    return new Response(JSON.stringify(done));
  });
  assert.equal(result.wellsCreated, 2);
  await api.importFromConnection(connection, ['x'], 'F', chosen, async (_url, init) => {
    assert.equal(init.body, '{"levels":["x"],"expectedFingerprint":"F","snapshotId":"11111111-1111-4111-8111-111111111111","query":"alpha","facets":{"field":["Alpha"]}}');
    return new Response(JSON.stringify(done));
  });
  await assert.rejects(api.importFromConnection(connection, [], 'F', null, async () => new Response('{"wellsCreated":-1}')), { kind: 'invalidResponse' });
  const refuse = (status, code) => api.importFromConnection(connection, [], 'F', null, async () =>
    new Response(JSON.stringify({ code }), { status })).catch(e => e.kind);
  assert.equal(await refuse(403, 'CONNECTION_IMPORT_NEEDS_ALL_ASSETS'), 'needsAllAssets');
  assert.equal(await refuse(403, undefined), 'accessDenied');
  assert.equal(await refuse(409, 'CONNECTION_IMPORT_PLAN_CHANGED'), 'planChanged');
  assert.equal(await refuse(409, 'CONNECTION_IMPORT_NOT_ALLOWED'), 'importNotAllowed');
  assert.equal(await refuse(409, 'CONNECTION_DISCOVERY_INCOMPLETE'), 'incomplete');
  assert.equal(await refuse(400, 'CONNECTION_IMPORT_LEVELS_INVALID'), 'invalidLevels');
});

test('paged inventory requests retain snapshot scope and cancellation beyond the first 1,000', async () => {
  const controller = new AbortController();
  const selection = { snapshotId: '11111111-1111-4111-8111-111111111111', query: '', facets: { deviceType: ['WFordKS'] } };
  await api.searchDiscovered(connection, selection, async (_url, init) => {
    assert.deepEqual(JSON.parse(init.body), { snapshotId: selection.snapshotId, query: null, facets: selection.facets, offset: 1100, limit: 100 });
    assert.equal(init.signal, controller.signal);
    return new Response(JSON.stringify({ matchCount: 1200, items: [{ kind: 'foresite.well', externalId: '1101', displayName: 'Beyond sample', metadata: {} }] }));
  }, { offset: 1100, limit: 100, signal: controller.signal });
});
test('explicit source identities survive preview and apply, including an intentionally empty selection', async () => {
  const objects = [{ kind: 'foresite.well', externalId: '1101' }];
  const selection = { snapshotId: '11111111-1111-4111-8111-111111111111', query: '', facets: {}, objects };
  for (const requested of [selection, { ...selection, objects: [] }]) {
    await api.previewImport(connection, [], requested, async (_url, init) => {
      assert.deepEqual(JSON.parse(init.body).objects, requested.objects);
      return new Response(JSON.stringify({ fingerprint: 'F', levels: [], objectCount: requested.objects.length, groupCount: 0,
        rootObjectCount: requested.objects.length, fallbackObjectCount: 0, groups: [], warnings: [], conflicts: [], typesToCreate: [], canImport: true, actingAs: null, alreadyImported: 0 }));
    });
    await api.importFromConnection(connection, [], 'F', requested, async (_url, init) => {
      assert.deepEqual(JSON.parse(init.body).objects, requested.objects);
      return new Response(JSON.stringify({ wellsCreated: requested.objects.length, groupsCreated: 0, groupsReused: 0, alreadyImported: 0, typesCreated: [] }));
    });
  }
});
test('invalid availability counts are refused instead of displaying misleading unknown totals', async () => {
  const body = { complete: true, actingAs: null, warnings: [], totalCount: 1, items: [], snapshotId: '11111111-1111-4111-8111-111111111111',
    expiresAtUtc: '2026-10-22T10:15:00Z', facets: [{ key: 'deviceType', label: 'Device type', hasMore: false, knownCount: 2, values: [] }] };
  await assert.rejects(api.discoverConnection(connection, async () => new Response(JSON.stringify(body))), { kind: 'invalidResponse' });
});

test('legacy adoption consent is serialized with the reviewed import selection', async () => {
  const selection = { snapshotId: '11111111-1111-4111-8111-111111111111', query: '', facets: {}, adoptExistingHierarchy: true };
  const preview = { fingerprint: 'H', levels: [], objectCount: 0, groupCount: 0, fallbackObjectCount: 0, rootObjectCount: 0,
    alreadyImported: 2, groups: [], warnings: [], conflicts: [], typesToCreate: [], canImport: true, actingAs: null,
    adoptingHierarchy: true, existingWells: 2, groupsToAdopt: 0 };
  const reviewed = await api.previewImport(connection, [], selection, async (_url, init) => {
    assert.equal(JSON.parse(init.body).adoptExistingHierarchy, true);
    return new Response(JSON.stringify(preview));
  });
  assert.equal(reviewed.adoptingHierarchy, true);
  await api.importFromConnection(connection, [], 'H', selection, async (_url, init) => {
    assert.equal(JSON.parse(init.body).adoptExistingHierarchy, true);
    return new Response(JSON.stringify({ wellsCreated: 0, groupsCreated: 0, groupsReused: 0, alreadyImported: 2, typesCreated: [], hierarchyAdopted: true }));
  });
  await assert.rejects(api.previewImport(connection, [], selection, async () => new Response(JSON.stringify({ ...preview, existingWells: -1 }))), { kind: 'invalidResponse' });
});
