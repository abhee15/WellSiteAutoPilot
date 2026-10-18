import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

let vite;
let menu;
let navigation;
let app;

before(async () => {
  vite = await createServer({
    server: { middlewareMode: true },
    appType: 'custom',
    logLevel: 'silent'
  });
  [menu, navigation, app] = await Promise.all([
    vite.ssrLoadModule('/src/components/UserMenu.tsx'),
    vite.ssrLoadModule('/src/components/NavigationLink.tsx'),
    vite.ssrLoadModule('/src/App.tsx')
  ]);
});

after(async () => { await vite?.close(); });

const operator = {
  userId: '123', identityName: 'SITE\\operator', displayName: 'Operator',
  isActive: true, roles: ['Operator'], assetScopeIds: []
};

test('new shell modules preserve exported App compatibility', () => {
  assert.equal(app.UserMenu, menu.UserMenu);
  assert.equal(app.NavigationLink, navigation.NavigationLink);
});

test('shell user menu never exposes administration to ordinary or inactive users', () => {
  const plain = renderToStaticMarkup(
    createElement(menu.UserMenu, { user: operator, isLoading: false })
  );
  const inactive = renderToStaticMarkup(
    createElement(menu.UserMenu, {
      user: { ...operator, isActive: false, roles: ['Admin'], permissions: ['security.manage'] },
      isLoading: false
    })
  );
  assert.doesNotMatch(plain, /href="#access"/);
  assert.doesNotMatch(inactive, /href="#access"/);
  assert.match(plain, /SITE\\operator/);
  const admin = renderToStaticMarkup(
    createElement(menu.UserMenu, {
      user: { ...operator, isActive: true, roles: ['Admin'], permissions: ['security.manage'] },
      isLoading: false
    })
  );
  assert.match(admin, /href="#access"/);
});

test('navigation preserves responsive accessible and active semantics', () => {
  const html = renderToStaticMarkup(createElement(
    navigation.NavigationLink,
    { section: 'assets', current: 'assets' },
    'Assets'
  ));
  assert.match(html, /aria-current="page"/);
  assert.match(html, /aria-label="Assets"/);
  assert.match(html, /<svg class="nav-icon"[^>]*aria-hidden="true"/);
});

test('account administration visibility follows server permission rather than a role label', () => {
  const custom = renderToStaticMarkup(createElement(menu.UserMenu, {
    user: { ...operator, roles: ['Customer Security'], permissions: ['security.manage'] }, isLoading: false,
  }));
  const namedAdmin = renderToStaticMarkup(createElement(menu.UserMenu, {
    user: { ...operator, roles: ['Admin'], permissions: [] }, isLoading: false,
  }));
  assert.match(custom, /href="#access"/);
  assert.doesNotMatch(namedAdmin, /href="#access"/);
});
