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
const adminId = '4a0ac5e2-5268-4998-a81b-410e5b3cc7a9';

test('access page starts without exposing prior users before authorization', () => {
  const html = renderToStaticMarkup(createElement(management.AccessManagementPage, {
    currentUserId: adminId,
  }));
  assert.match(html, /Loading authorized users/);
  assert.doesNotMatch(html, /FIELD\\operator/);
});
