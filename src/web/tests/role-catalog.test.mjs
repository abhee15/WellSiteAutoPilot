/* global AbortController, Response */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

let vite, api, ui;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true },
    appType: 'custom', logLevel: 'silent' });
  api = await vite.ssrLoadModule('/src/views/roleCatalog.ts');
  ui = await vite.ssrLoadModule('/src/views/RoleCatalogPage.tsx');
});
after(async () => { await vite?.close(); });

const id = '7391049d-f272-4738-9173-a370c2b27a9c';
const role = {
  roleId: id, key: 'custom-foo', name: 'Field Supervisor',
  description: 'Well operations', isSystem: false, isActive: true,
  revision: 1, createdAtUtc: '2026-10-14T00:00:00Z',
  updatedAtUtc: '2026-10-14T00:00:00Z',
  permissions: ['assets.read'],
};
const permissions = [
  { key: 'assets.read', category: 'Assets',
    scopeKind: 'ResourceScoped', isPrivileged: false },
  { key: 'security.manage', category: 'Security',
    scopeKind: 'DeploymentWide', isPrivileged: true },
];
const signal = () => new AbortController().signal;

test('Admin role catalog loads only authorized, valid identities and metadata', async () => {
  const urls = [];
  const loaded = await api.loadRoleCatalog(signal(), async (url, init) => {
    urls.push(url);
    assert.equal(init.credentials, 'same-origin');
    assert.equal(init.cache, 'no-store');
    return new Response(JSON.stringify(url.includes('role-permissions') ? permissions : [role]));
  });
  assert.deepEqual(urls, ['/api/v1/security/roles?limit=100',
    '/api/v1/security/role-permissions']);
  assert.deepEqual(loaded.roles, [role]);
  assert.deepEqual(loaded.permissions, permissions);
});

test('deny malformed role rows, unknown permissions and duplicate records', async () => {
  const list = [
    [{ ...role, permissions: ['unrecognized.capability'] }],
    [{ ...role, revision: 0 }],
    [role, role],
    [{ ...role, roleId: 'invalid' }],
  ];
  for (const roles of list) {
    await assert.rejects(api.loadRoleCatalog(signal(), async url =>
      new Response(JSON.stringify(url.includes('role-permissions')
        ? permissions : roles))), { kind: 'invalidResponse' });
  }
  await assert.rejects(api.loadRoleCatalog(signal(), async () =>
    new Response('{}', { status: 403 })), { kind: 'accessDenied' });
});

test('validation rejects reserved names, fabricated permissions and duplicate values', () => {
  for (const mutation of [
    { name: 'Admin', description: '', permissions: [] },
    { name: 'A', description: '', permissions: [] },
    { name: 'Operator', description: '', permissions: [] },
    { name: 'Field Supervisor', description: '', permissions: ['unknown.permission'] },
    { name: 'Field\nSupervisor', description: '', permissions: [] },
    { name: 'Field\u0000Supervisor', description: '', permissions: [] },
    { name: 'Field Supervisor', description: 'Invalid\u001fdescription', permissions: [] },
    { name: 'Field Supervisor', description: '', permissions: ['assets.read', 'assets.read'] },
  ]) {
    assert.throws(() => api.validateRoleMutation(
      { ...mutation, isActive: true }, permissions.map(p => p.key)), { kind: 'invalid' });
  }
});

test('unconfirmed, system-owned and unchanged roles cannot submit', async () => {
  let calls = 0;
  const request = async () => { calls++; return new Response('{}'); };
  const mutation = { name: role.name, description: role.description,
    permissions: role.permissions, isActive: true };
  for (const [current, checked] of [
    [role, false], [{ ...role, isSystem: true }, true], [role, true],
  ]) {
    await assert.rejects(api.saveRole(current, mutation, ['assets.read'], checked,
      signal(), request), { kind: 'invalid' });
  }
  assert.equal(calls, 0);
});

test('create role uses a single confirmed POST and verifies the result', async () => {
  let calls = 0;
  const payload = { name: role.name, description: role.description,
    isActive: true, permissions: ['assets.read'] };
  const created = await api.saveRole(null, payload, ['assets.read'],
    true, signal(), async (url, init) => {
      calls++;
      assert.equal(url, '/api/v1/security/roles');
      assert.equal(init.method, 'POST');
      assert.deepEqual(JSON.parse(init.body), {
        name: role.name, description: role.description, permissions: ['assets.read'],
      });
      return new Response(JSON.stringify(role), { status: 201 });
    });
  assert.equal(calls, 1);
  assert.deepEqual(created, role);
});

test('edits use expected revision and require server verification; never retry', async () => {
  let calls = 0;
  const mutation = { name: role.name, description: 'New description',
    isActive: false, permissions: [] };
  const updated = { ...role, description: mutation.description,
    revision: 2, isActive: false, permissions: [] };
  assert.deepEqual(await api.saveRole(role, mutation, ['assets.read'],
    true, signal(), async (url, init) => {
      calls++;
      assert.equal(url, '/api/v1/security/roles/' + id);
      assert.equal(init.method, 'PUT');
      assert.equal(JSON.parse(init.body).expectedRevision, 1);
      return new Response(JSON.stringify(updated));
    }), updated);
  assert.equal(calls, 1);

  for (const [status, kind] of [[403, 'accessDenied'],
    [409, 'conflict'], [500, 'unavailable']]) {
    await assert.rejects(api.saveRole(role, mutation, ['assets.read'], true,
      signal(), async () => new Response('{}', { status })), { kind });
  }
  await assert.rejects(api.saveRole(role, mutation, ['assets.read'], true,
    signal(), async () => new Response(JSON.stringify({ ...updated, revision: 3 }))),
  { kind: 'invalidResponse' });
});

test('Admin role authoring UI shows protected defaults and disabled confirmation', () => {
  const html = renderToStaticMarkup(createElement(ui.RoleDefinitionEditor, {
    original: null,
    template: { ...role, name: 'Engineer', isSystem: true },
    permissions,
    onSaved() {}, onCancel() {}, onFailure() {},
  }));
  assert.match(html, /Create customer role/);
  assert.match(html, /Engineer Copy/);
  assert.match(html, /Manage security settings/);
  assert.match(html, /reviewed the permissions/);
  assert.match(html, /disabled=""/);
});

test('Role page loads with no previous user or role records exposed', () => {
  const html = renderToStaticMarkup(createElement(ui.RoleCatalogPage));
  assert.match(html, /Loading authorized role catalog/);
  assert.doesNotMatch(html, /Field Supervisor/);
});
