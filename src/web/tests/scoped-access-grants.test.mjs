import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

let vite, api, ui;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true },
    appType: 'custom', logLevel: 'silent' });
  api = await vite.ssrLoadModule('/src/views/scopedAccessGrants.ts');
  ui = await vite.ssrLoadModule('/src/views/ScopedGrantEditor.tsx');
});
after(async () => { await vite?.close(); });

const userId = 'b86c36a2-3349-4020-9044-a3ce7ff42991';
const roleId = '139e5b2f-dd2d-4630-b920-141b7bfb7a62';
const wellA = '04076374-92c5-4239-9220-d11ca7a5f400';
const wellB = 'd9fd6458-a135-47b0-ab22-3daac6cb46bb';
const role = { roleId, key: 'test-role', name: 'Field Supervisor',
  revision: 1, isActive: true, permissions: ['assets.manage', 'logic.read', 'security.manage'] };
const permissions = [
  { key: 'assets.manage', scopeKind: 'ResourceScoped', isPrivileged: false },
  { key: 'logic.read', scopeKind: 'DeploymentWide', isPrivileged: false },
  { key: 'security.manage', scopeKind: 'DeploymentWide', isPrivileged: true },
];
const assets = [
  { id: wellA, name: 'Well Alpha', isActive: true },
  { id: wellB, name: 'Well Bravo', isActive: true },
];
const draft = (scopeKind, assetIds = []) =>
  ({ subjectKind: 'User', userId, roleId, scopeKind, assetIds, expectedRoleRevision: 1 });

test('exact Asset assignments never inherit deployment permissions', () => {
  const view = api.previewGrant(draft('ExactAssets', [wellA]),
    role, permissions, assets, false);
  assert.deepEqual(view.effective, ['assets.manage']);
  assert.deepEqual(view.excluded, ['logic.read', 'security.manage']);
  assert.deepEqual(view.assetNames, ['Well Alpha']);
});
test('deployment-wide role assignments do not confer Asset management', () => {
  const view = api.previewGrant(draft('DeploymentWide'),
    role, permissions, assets, false);
  assert.deepEqual(view.effective, ['logic.read', 'security.manage']);
  assert.deepEqual(view.excluded, ['assets.manage']);
  assert.equal(view.privileged, true);
});
test('all Assets is a distinct privileged scope', () => {
  assert.throws(() => api.previewGrant(draft('AllAssets'),
    role, permissions, assets, false));
  const view = api.previewGrant(draft('AllAssets'),
    role, permissions, assets, true);
  assert.deepEqual(view.effective, ['assets.manage']);
});
test('unknown, missing or inactive Assets fail closed', () => {
  for (const candidate of [
    draft('ExactAssets'), draft('ExactAssets', [wellA, wellA]),
    draft('ExactAssets', [wellB]), draft('DeploymentWide', [wellA]),
  ]) {
    const available = candidate.assetIds.includes(wellB)
      ? assets.map(a => ({ ...a, isActive: a.id !== wellB })) : assets;
    assert.throws(() => api.previewGrant(candidate,
      role, permissions, available, true));
  }
  assert.throws(() => api.previewGrant(draft('ExactAssets', [wellA]),
    { ...role, permissions: ['untrusted.permission'] }, permissions, assets, true));
});
test('editor renders real names, preview, and disables unconfirmed writes', () => {
  const html = renderToStaticMarkup(createElement(ui.ScopedGrantEditor, {
    user: { userId, identityName: 'FIELD\\operator' }, roles: [role],
    permissions, assets, allowAllAssets: true,
    onContinue() {}, onCancel() {},
  }));
  assert.match(html, /Field Supervisor/);
  assert.match(html, /Well Alpha/);
  assert.match(html, /Well Bravo/);
  assert.match(html, /disabled=""/);
});

test('review must match the currently loaded role revision', () => {
  assert.throws(() => api.previewGrant(draft('ExactAssets', [wellA]),
    { ...role, revision: 2 }, permissions, assets, true));
});

test('scope and subject guards list exactly the server contract names', () => {
  for (const name of ['ExactAssets', 'DeploymentWide', 'AllAssets']) assert.equal(api.isScopeKind(name), true);
  for (const name of ['User', 'Group']) assert.equal(api.isSubjectKind(name), true);
  for (const bad of ['exactassets', 'ALLASSETS', 'Everything', '', ' ExactAssets', '0', 0, 1, null, undefined, {}, ['ExactAssets'],
    'toString', 'constructor', '__proto__']) {
    assert.equal(api.isScopeKind(bad), false, String(bad));
  }
  for (const bad of ['user', 'UnknownGroup', '1', 0, null, undefined, 'hasOwnProperty']) assert.equal(api.isSubjectKind(bad), false, String(bad));
  assert.throws(() => api.previewGrant(draft('1', []), role, permissions, assets, false), { name: 'Error' });
  assert.throws(() => api.previewGrant(draft(1, []), role, permissions, assets, false), { name: 'Error' });
});
