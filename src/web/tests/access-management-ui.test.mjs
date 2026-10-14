import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';
let vite, management;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true },
    appType: 'custom', logLevel: 'silent' });
  management = await vite.ssrLoadModule('/src/views/AccessManagementPage.tsx');
});
after(async () => { await vite?.close(); });
const id = 'c8ba387d-8e96-46a4-a1da-a39cdf35ae6e';
const adminId = '4a0ac5e2-5268-4998-a81b-410e5b3cc7a9';
const user = { userId: id, identityName: 'FIELD\\operator',
  roles: ['Operator'], assetScopeIds: [], isActive: true };

test('access editor never submits without review acknowledgement', () => {
  const html = renderToStaticMarkup(createElement(management.AccessChangeEditor, {
    user, currentUserId: adminId, onCancel() {}, onSaved() {}, onRequireReload() {},
  }));
  assert.match(html, /Review access replacement/);
  assert.match(html, /FIELD\\operator/);
  assert.match(html, /scope/i);
  assert.match(html, /type="checkbox"/);
  assert.match(html, /Confirm access replacement/);
  assert.match(html, /disabled=""/);
});

test('current administrator cannot edit their own access', () => {
  const html = renderToStaticMarkup(createElement(management.AccessChangeEditor, {
    user, currentUserId: id, onCancel() {}, onSaved() {}, onRequireReload() {},
  }));
  assert.match(html, /own roles or Asset scope/);
  assert.match(html, /disabled=""/);
});

test('access page starts without exposing prior users before authorization', () => {
  const html = renderToStaticMarkup(createElement(management.AccessManagementPage, {
    currentUserId: adminId,
  }));
  assert.match(html, /Loading authorized users/);
  assert.doesNotMatch(html, /FIELD\\operator/);
});
