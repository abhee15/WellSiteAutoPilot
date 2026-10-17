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
