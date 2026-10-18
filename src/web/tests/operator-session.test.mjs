import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

let vite;
let components;
let localization;

before(async () => {
  vite = await createServer({
    server: { middlewareMode: true },
    appType: 'custom',
    logLevel: 'silent',
  });
  components = await vite.ssrLoadModule('/src/App.tsx');
  localization = await vite.ssrLoadModule('/src/localization.ts');
});

after(async () => {
  await vite?.close();
});

test('operator session renders identity and grant permission hints', () => {
  const markup = renderToStaticMarkup(
    createElement(components.UserMenu, {
      user: {
        userId: 'operator-id',
        identityName: 'SITE\\operator',
        displayName: 'Control Room Operator',
        isActive: true,
        permissions: ['assets.read', 'executions.read'],
      },
      isLoading: false,
    }),
  );

  assert.match(markup, /Control Room Operator/);
  assert.match(markup, /SITE\\operator/);
  assert.match(markup, /2 available capabilities/);
  assert.match(markup, /Server-side access rules remain authoritative/);
  assert.match(markup, /<details/);
  assert.match(markup, /<summary/);
});

test('missing session does not show roles or prior authorized data', () => {
  const markup = renderToStaticMarkup(
    createElement(components.UserMenu, { user: null, isLoading: false }),
  );
  assert.match(markup, /Session unavailable/);
  assert.match(markup, /User details are unavailable/);
  assert.doesNotMatch(markup, /Operator, Engineer/);
  assert.doesNotMatch(markup, /2 assigned/);
});

test('responsive links retain accessible full labels and active-page semantics', () => {
  const current = renderToStaticMarkup(
    createElement(components.NavigationLink, {
      section: 'operations',
      current: 'operations',
      children: 'Runs',
    }),
  );
  const other = renderToStaticMarkup(
    createElement(components.NavigationLink, {
      section: 'assets',
      current: 'operations',
      children: 'Assets',
    }),
  );

  assert.match(current, /aria-current="page"/);
  assert.match(current, /aria-label="Runs"/);
  assert.match(current, /aria-hidden="true">OP</);
  assert.match(other, /aria-label="Assets"/);
  assert.match(other, /aria-hidden="true">AS</);
  assert.doesNotMatch(other, /aria-current="page"/);
});


test('English message catalog provides typed operator copy and interpolation', () => {
  assert.equal(localization.t('session.heading'), 'Operator session');
  assert.equal(localization.t('session.assigned', { count: 2 }), '2 assigned');
  assert.equal(
    localization.t('refresh.runtimeFailed', { status: 503 }),
    'Runtime data request failed (503). Previous data may be stale.',
  );
});
