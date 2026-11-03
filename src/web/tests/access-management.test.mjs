/* global AbortController, Response */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';

let vite, access;
before(async () => {
  vite = await createServer({
    server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent',
  });
  access = await vite.ssrLoadModule('/src/views/accessManagement.ts');
});
after(async () => { await vite?.close(); });

const userId = '725d06d3-e773-4534-b3e7-a4607c727196';
const wellId = '01468723-b10f-4107-8281-2d61c9b1b3e7';
const record = {
  userId, identityName: 'FIELD\\operator', displayName: 'Operator',
  isActive: true, activeDirectGrantCount: 1, roles: ['Operator'], assetScopeIds: [wellId],
  createdAtUtc: '2026-10-14T10:00:00Z', lastSeenAtUtc: '2026-10-14T11:00:00Z',
};

test('admin-only user listing uses uncached abortable same-origin API', async () => {
  const controller = new AbortController();
  const result = await access.loadSecurityUsers(controller.signal, async (url, init) => {
    assert.equal(url, '/api/v1/security/users?limit=100');
    assert.equal(init.credentials, 'same-origin');
    assert.equal(init.cache, 'no-store');
    assert.equal(init.signal, controller.signal);
    return new Response(JSON.stringify([record]));
  });
  assert.deepEqual(result, [record]);
});

test('asks the server for only waiting users when requested', async () => {
  const waiting = { ...record, activeDirectGrantCount: 0 };
  const result = await access.loadSecurityUsers(new AbortController().signal, async url => {
    assert.equal(url, '/api/v1/security/users?limit=100&awaitingAccess=true');
    return new Response(JSON.stringify([waiting]));
  }, { awaitingAccessOnly: true });
  assert.equal(result[0].activeDirectGrantCount, 0);
});

test('rejects a user whose direct grant count is missing, negative or fractional', async () => {
  for (const activeDirectGrantCount of [undefined, -1, 1.5, '1', null]) {
    const bad = { ...record, activeDirectGrantCount };
    await assert.rejects(
      access.loadSecurityUsers(new AbortController().signal, async () => new Response(JSON.stringify([bad]))),
      { kind: 'invalidResponse' },
      `count ${String(activeDirectGrantCount)} must be refused`);
  }
});

test('filters the protected user directory before paging and preserves opaque continuation', async () => {
  const filters = { search: ' %New_ ', accountStatus: 'inactive', directAssignments: 'without' };
  const cursor = 'YWJj';
  const result = await access.loadSecurityUserPage(filters, cursor, new AbortController().signal,
    async (url, init) => {
      const parsed = new URL(url, 'http://localhost');
      assert.equal(parsed.pathname, '/api/v1/security/users/page');
      assert.equal(parsed.searchParams.get('limit'), '50');
      assert.equal(parsed.searchParams.get('search'), '%New_');
      assert.equal(parsed.searchParams.get('accountStatus'), 'inactive');
      assert.equal(parsed.searchParams.get('directAssignments'), 'without');
      assert.equal(parsed.searchParams.get('cursor'), cursor);
      assert.equal(init.cache, 'no-store');
      assert.equal(init.credentials, 'same-origin');
      return new Response(JSON.stringify({ items: [{ ...record, isActive: false, activeDirectGrantCount: 0 }], nextCursor: null }));
    });
  assert.equal(result.items.length, 1);
  assert.equal(result.nextCursor, null);
});

test('default directory filters do not imply waiting or group-access verdicts', async () => {
  const query = access.userDirectoryQuery(access.DEFAULT_USER_DIRECTORY_FILTERS, null);
  assert.equal(query, 'limit=50');
  const result = await access.loadSecurityUserPage(access.DEFAULT_USER_DIRECTORY_FILTERS, null,
    new AbortController().signal, async url => {
      assert.equal(url, '/api/v1/security/users/page?limit=50');
      return new Response(JSON.stringify({ items: [{ ...record, activeDirectGrantCount: 0 }], nextCursor: null }));
    });
  assert.equal(result.items[0].activeDirectGrantCount, 0);
});

test('rejects malformed or duplicative directory pages and invalid filter/cursor input', async () => {
  const filters = access.DEFAULT_USER_DIRECTORY_FILTERS;
  for (const value of [
    { items: [], nextCursor: '!' },
    { items: [record, record], nextCursor: null },
    { items: [record], nextCursor: 'YWJj' },
    { items: [record] },
    { items: [], nextCursor: 'YWJj' },
  ]) {
    await assert.rejects(
      access.loadSecurityUserPage(filters, null, new AbortController().signal,
        async () => new Response(JSON.stringify(value))),
      { kind: 'invalidResponse' });
  }
  assert.throws(() => access.userDirectoryQuery({ ...filters, search: 'x'.repeat(101) }, null), { kind: 'invalid' });
  assert.throws(() => access.userDirectoryQuery(filters, 'bad=cursor'), { kind: 'invalid' });
});

test('server denial does not fall back to a client-cached directory result', async () => {
  await assert.rejects(
    access.loadSecurityUserPage(access.DEFAULT_USER_DIRECTORY_FILTERS, null, new AbortController().signal,
      async () => new Response('', { status: 403 })),
    { kind: 'accessDenied' });
});
