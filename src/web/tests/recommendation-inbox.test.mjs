/* global AbortController, Response */
import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

let vite, inbox, page, controls;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true },
    appType: 'custom', logLevel: 'silent' });
  inbox = await vite.ssrLoadModule('/src/views/recommendationInbox.ts');
  page = await vite.ssrLoadModule('/src/views/RecommendationsPage.tsx');
  controls = await vite.ssrLoadModule('/src/views/RecommendationInboxControls.tsx');
});
after(async () => { await vite?.close(); });

const nowUtc = Date.parse('2026-10-14T12:00:00Z');
const items = [
  { id: 'b', assetId: 'asset-1', status: 'Created', code: 'PUMP',
    moduleId: 'wfd.fill', createdAtUtc: '2026-10-14T10:00:00Z',
    expiresAtUtc: '2026-10-15T12:00:00Z' },
  { id: 'a', assetId: 'asset-2', status: 'Pending', code: 'PRESSURE',
    moduleId: 'wfd.pressure', createdAtUtc: '2026-10-14T11:00:00Z',
    expiresAtUtc: '2026-10-14T11:59:59Z' },
  { id: 'c', assetId: 'asset-1', status: 'Rejected', code: 'PUMP',
    moduleId: 'wfd.fill', createdAtUtc: '2026-10-14T09:00:00Z',
    expiresAtUtc: null },
  { id: 'd', assetId: 'asset-1', status: 'Pending', code: 'PUMP',
    moduleId: 'wfd.fill', createdAtUtc: '2026-10-14T08:00:00Z',
    expiresAtUtc: null },
];
const criteria = { search: '', filter: 'all', sort: 'newest' };
const ids = rows => rows.map(item => item.id);

test('filters outstanding separately from stale and terminal decisions', () => {
  assert.deepEqual(inbox.inboxCounts(items, nowUtc), {
    awaiting: 2, expired: 1, decided: 1,
  });
  assert.deepEqual(ids(inbox.filterRecommendationInbox(items,
    { ...criteria, filter: 'awaiting' }, nowUtc)), ['b', 'd']);
  assert.deepEqual(ids(inbox.filterRecommendationInbox(items,
    { ...criteria, filter: 'expired' }, nowUtc)), ['a']);
  assert.deepEqual(ids(inbox.filterRecommendationInbox(items,
    { ...criteria, filter: 'decided' }, nowUtc)), ['c']);
});

test('search and deterministic sorting never mutate the authorized subset', () => {
  assert.deepEqual(ids(inbox.filterRecommendationInbox(items,
    { ...criteria, search: 'ASSET-1' }, nowUtc)), ['b', 'c', 'd']);
  assert.deepEqual(ids(inbox.filterRecommendationInbox(items,
    { ...criteria, search: 'pressure' }, nowUtc)), ['a']);
  assert.deepEqual(ids(inbox.filterRecommendationInbox(items,
    { ...criteria, sort: 'oldest' }, nowUtc)), ['d', 'c', 'b', 'a']);
  assert.equal(items[0].id, 'b');
});

test('unknown expiry metadata cannot be considered safely awaiting', () => {
  const bad = { ...items[0], expiresAtUtc: 'not-a-date' };
  assert.equal(inbox.recommendationIsAwaiting(bad, nowUtc), false);
  assert.equal(inbox.recommendationIsExpired(bad, nowUtc), true);
});

test('inbox uses only same-origin uncached, abortable scoped endpoint', async () => {
  const controller = new AbortController();
  const received = await inbox.fetchRecommendationInbox(controller.signal, async (url, init) => {
    assert.equal(url, '/api/v1/recommendations?limit=50');
    assert.equal(init.method, 'GET');
    assert.equal(init.cache, 'no-store');
    assert.equal(init.credentials, 'same-origin');
    assert.equal(init.signal, controller.signal);
    return new Response(JSON.stringify(items));
  });
  assert.equal(received.length, 4);
});

test('unauthorized and malformed responses never restore stale recommendations', async () => {
  for (const status of [401, 403]) {
    await assert.rejects(inbox.fetchRecommendationInbox(
      new AbortController().signal, async () => new Response('{}', { status })),
    /RECOMMENDATION_INBOX_ACCESS_DENIED/);
  }
  for (const body of ['{}', 'not-json', '[{"id":"x"}]', 'null']) {
    await assert.rejects(inbox.fetchRecommendationInbox(
      new AbortController().signal, async () => new Response(body)),
    /RECOMMENDATION_INBOX_INVALID_RESPONSE/);
  }
  await assert.rejects(inbox.fetchRecommendationInbox(
    new AbortController().signal, async () => new Response('{}', { status: 500 })),
  /RECOMMENDATION_INBOX_UNAVAILABLE/);
});

test('inbox SSR is read-only and cannot trigger a decision', () => {
  const html = renderToStaticMarkup(createElement(page.RecommendationInboxTable, {
    items, nowUtc,
  }));
  assert.match(html, /asset-1/);
  assert.match(html, /wfd\.fill/);
  assert.match(html, /Expired, do not act/);
  assert.equal(html.includes('<button'), false);
});

test('filter controls show authorized-window counts and accessible labels', () => {
  const html = renderToStaticMarkup(createElement(controls.RecommendationInboxControls, {
    criteria, onChange: () => {}, matchingCount: 2, loadedCount: 4,
  }));
  assert.match(html, /Recommendation inbox filters/);
  assert.match(html, /role="group"/);
  assert.match(html, /Showing 2 of 4 loaded recommendations/);
});

test('inbox page initially shows loading without displaying old suggestions', () => {
  const html = renderToStaticMarkup(createElement(page.RecommendationsPage, {
    userId: null,
  }));
  assert.match(html, /Recommendations overview/);
  assert.match(html, /Loading recommendations/);
  assert.doesNotMatch(html, /asset-1/);
});

test('decision visibility follows server permission even for a custom role name', () => {
  const deciding = renderToStaticMarkup(createElement(page.RecommendationsPage, {
    userId: 'custom-reviewer', permissions: ['recommendations.decide'],
  }));
  const readOnly = renderToStaticMarkup(createElement(page.RecommendationsPage, {
    userId: 'named-admin', permissions: [],
  }));
  assert.match(deciding, /Approval records a governed review decision/);
  assert.match(readOnly, /Read-only review/);
});
