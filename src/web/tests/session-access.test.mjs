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
 { ...session, isActive: 'true' }]) {
    await assert.rejects(access.loadSession(signal(), async () => response(invalid)));
  }
  for (const status of [401, 403, 500]) await assert.rejects(access.loadSession(signal(), async () => new Response('', { status })));
});

test('only a 401 is classified as sign-in required; other failures stay generic', async () => {
  for (const [status, signInRequired] of [[401, true], [403, false], [500, false]]) {
    const failure = await access.loadSession(signal(), async () => new Response('', { status })).catch(reason => reason);
    assert.ok(failure instanceof access.SessionAccessError);
    assert.equal(failure.status, status);
    assert.equal(access.isSignInRequired(failure), signInRequired);
  }
  assert.equal(access.isSignInRequired(new Error('network')), false);
  assert.equal(access.isSignInRequired(new access.SessionAccessError('Invalid session')), false);
  assert.equal(access.isSignInRequired(undefined), false);
});

test('workspace does not request APIs outside the current permissions', async () => {
  const urls = [];
  const snapshot = await workspace.loadWorkspaceSnapshot(session, signal(), async (url, init) => {
    urls.push(url); assert.equal(init.cache, 'no-store');
    return response(url.includes('system/health') ? {} : url.includes('capabilities')
      ? { canCreateRoot: false, canCreateUnderAuthorizedParents: false } : []);
  });
  assert.equal(urls.some(url => /executions|logic-modules|configured-logic/.test(url)), false);
  assert.deepEqual(snapshot.executions, []); assert.deepEqual(snapshot.configurations, []);
  assert.equal(snapshot.incomplete, false);
});

test('a refresh limited to some datasets requests only those', async () => {
  const user = { ...session, permissions: ['assets.read', 'executions.read'] };
  const urls = [];
  const snapshot = await workspace.loadWorkspaceSnapshot(user, signal(), async url => {
    urls.push(url);
    return response(url.includes('capabilities') ? { canCreateRoot: true, canCreateUnderAuthorizedParents: true } : [{ id: 'one' }]);
  }, new Set(workspace.SNAPSHOT_KEYS_BY_AREA.executions));
  assert.deepEqual(urls, ['/api/v1/executions?limit=50']);
  assert.equal(snapshot.executions.length, 1);
  assert.deepEqual(snapshot.assets, []);
  assert.equal(snapshot.incomplete, false);
  assert.deepEqual(snapshot.deniedPermissions, []);
});

test('each change area names the datasets it covers', () => {
  assert.deepEqual(workspace.SNAPSHOT_KEYS_BY_AREA.assets, ['assetTypes', 'assets', 'capabilities']);
  assert.deepEqual(workspace.SNAPSHOT_KEYS_BY_AREA.modules, ['modules']);
  assert.deepEqual(workspace.SNAPSHOT_KEYS_BY_AREA['configured-logic'], ['configurations']);
});

test('one denied API family clears its own data while retaining permitted work', async () => {
  const user = { ...session, permissions: ['assets.read', 'executions.read'] };
  const snapshot = await workspace.loadWorkspaceSnapshot(user, signal(), async url => {
    if (url === '/api/v1/asset-types') return new Response('', { status: 403 });
    return response(url.includes('system/health') ? {} : url.includes('executions') ? [{ executionId: 'allowed' }]
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
    response(url.includes('system/health') ? {} : [null, 5, 'malformed']));
  assert.equal(snapshot.incomplete, true);
  assert.deepEqual(snapshot.assets, []); assert.deepEqual(snapshot.assetTypes, []);
});

test('an active user without effective permissions is awaiting access', () => {
  const user = (isActive, permissions) => ({ userId: 'u', identityName: 'FIELD\\new', isActive, permissions });
  assert.equal(access.isAwaitingAccess(user(true, [])), true);
  assert.equal(access.isAwaitingAccess(user(true, ['assets.read'])), false);
  assert.equal(access.isAwaitingAccess(user(false, [])), false);
  assert.equal(access.isAwaitingAccess(null), false);
});

test('transient read failures are reported as unavailable so callers keep their last good data', async () => {
  const snapshot = await workspace.loadWorkspaceSnapshot(session, signal(), async url => {
    if (url.includes('/assets?')) return new Response('', { status: 500 });
    return response(url.includes('system/health') ? {} : url.includes('capabilities')
      ? { canCreateRoot: true, canCreateUnderAuthorizedParents: true } : []);
  });
  assert.deepEqual(snapshot.unavailable, ['assets']);
  assert.equal(snapshot.incomplete, true);
  const denied = await workspace.loadWorkspaceSnapshot({ ...session, permissions: ['assets.read'] }, signal(),
    async url => url === '/api/v1/asset-types' ? new Response('', { status: 403 })
      : response(url.includes('system/health') ? {} : url.includes('capabilities')
        ? { canCreateRoot: true, canCreateUnderAuthorizedParents: true } : []));
  assert.deepEqual(denied.unavailable, []);
});

test('configuration and module snapshots include records beyond the first page', async () => {
  const user = { ...session, permissions: ['configured-logic.read', 'logic.read'] };
  const first = Array.from({ length: 100 }, (_, index) => ({ id: String(index) }));
  const only = new Set(['configurations', 'modules']);
  const mock = async url => response(url.includes('offset=100') ? [{ id: '100' }] : first);
  const snapshot = await workspace.loadWorkspaceSnapshot(user, signal(), mock, only);
  assert.equal(snapshot.configurations.length, 101);
  assert.equal(snapshot.modules.at(-1).id, '100');
  assert.equal(snapshot.incomplete, false);

  const revoked = await workspace.loadWorkspaceSnapshot(user, signal(), async url =>
    url.includes('configured-logic') && url.includes('offset=100') ? new Response('', { status: 403 }) : mock(url), only);
  assert.deepEqual(revoked.configurations, []);
  assert.deepEqual(revoked.deniedPermissions, ['configured-logic.read']);
  assert.equal(revoked.modules.length, 101);
});
