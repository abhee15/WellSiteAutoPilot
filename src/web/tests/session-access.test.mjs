/* global AbortController, Response */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';
let vite, access, workspace;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  access = await vite.ssrLoadModule('/src/security/sessionAccess.ts');
  workspace = await vite.ssrLoadModule('/src/views/workspaceSnapshot.ts');
});
after(async () => { await vite?.close(); });
const session = { userId: '44444444-4444-4444-8444-444444444444', identityName: 'FIELD\\reader',
  roles: ['Customer Reader'], permissions: ['assets.read'], assetScopeIds: [], isActive: true };
const signal = () => new AbortController().signal;
const response = value => new Response(JSON.stringify(value));

test('session permissions come from the server independently of role names', async () => {
  const user = await access.loadSession(signal(), async (url, init) => {
    assert.equal(url, '/api/v1/security/me');
    assert.equal(init.cache, 'no-store'); assert.equal(init.credentials, 'same-origin');
    return response(session);
  });
  assert.equal(access.canOpenSection(user, 'assets'), true);
  assert.equal(access.canOpenSection(user, 'operations'), false);
  assert.equal(access.canOpenSection({ ...user, roles: ['Admin'] }, 'access'), false);
  assert.equal(access.canOpenSection({ ...user, roles: ['Custom'], permissions: ['security.manage'] }, 'access'), true);
  assert.equal(access.hasPermission({ ...user, isActive: false }, 'assets.read'), false);
  assert.equal(access.hasPermission({ ...user, permissions: undefined }, 'assets.read'), false);
});

test('missing or malformed permission/session data fails closed', async () => {
  for (const invalid of [null, {}, { ...session, permissions: undefined },
    { ...session, permissions: ['assets.read', 'assets.read'] }, { ...session, permissions: [3] },
    { ...session, permissions: ['Admin role'] }, { ...session, userId: 'not-an-id' },
    { ...session, assetScopeIds: ['*'] }, { ...session, isActive: 'true' }]) {
    await assert.rejects(access.loadSession(signal(), async () => response(invalid)));
  }
  for (const status of [401, 403, 500]) await assert.rejects(access.loadSession(signal(), async () => new Response('', { status })));
});

test('workspace does not request APIs outside the current permissions', async () => {
  const urls = [];
  const snapshot = await workspace.loadWorkspaceSnapshot(session, signal(), async (url, init) => {
    urls.push(url); assert.equal(init.cache, 'no-store');
    return response(url.includes('system/info') ? {} : url.includes('capabilities')
      ? { canCreateRoot: false, canCreateUnderAuthorizedParents: false } : []);
  });
  assert.equal(urls.some(url => /executions|logic-modules|configured-logic/.test(url)), false);
  assert.deepEqual(snapshot.executions, []); assert.deepEqual(snapshot.configurations, []);
  assert.equal(snapshot.incomplete, false);
});

test('one denied API family clears its own data while retaining permitted work', async () => {
  const user = { ...session, permissions: ['assets.read', 'executions.read'] };
  const snapshot = await workspace.loadWorkspaceSnapshot(user, signal(), async url => {
    if (url === '/api/v1/asset-types') return new Response('', { status: 403 });
    return response(url.includes('system/info') ? {} : url.includes('executions') ? [{ executionId: 'allowed' }]
      : url.includes('capabilities') ? { canCreateRoot: true, canCreateUnderAuthorizedParents: true }
      : [{ id: 'must-be-cleared' }]);
  });
  assert.deepEqual(snapshot.assets, []); assert.deepEqual(snapshot.assetTypes, []); assert.equal(snapshot.capabilities, null);
  assert.equal(snapshot.executions[0].executionId, 'allowed');
  assert.deepEqual(snapshot.deniedPermissions, ['assets.read']);
});

test('expired sessions discard the entire snapshot, and malformed area payloads remain empty', async () => {
  await assert.rejects(workspace.loadWorkspaceSnapshot(session, signal(), async url =>
    url.includes('/assets?') ? new Response('', { status: 401 }) : response({})));
  const snapshot = await workspace.loadWorkspaceSnapshot(session, signal(), async () => response({}));
  assert.equal(snapshot.incomplete, true); assert.deepEqual(snapshot.assets, []); assert.equal(snapshot.capabilities, null);
});


test('non-object resource records are discarded rather than passed into UI components', async () => {
  const snapshot = await workspace.loadWorkspaceSnapshot(session, signal(), async url =>
    response(url.includes('system/info') ? {} : [null, 5, 'malformed']));
  assert.equal(snapshot.incomplete, true);
  assert.deepEqual(snapshot.assets, []); assert.deepEqual(snapshot.assetTypes, []);
});
