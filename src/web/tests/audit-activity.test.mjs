/* global AbortController, Response */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';

let vite, api;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  api = await vite.ssrLoadModule('/src/views/auditActivity.ts');
});
after(async () => { await vite?.close(); });

const id = n => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const event = (n, extra = {}) => ({
  id: id(n), occurredAtUtc: '2026-10-18T12:00:00Z', actorUserId: id(900), actorIdentity: 'FIELD\\admin',
  action: 'logic-module.approved', targetType: 'LogicModule', targetId: 'acme.sample@1.0.0',
  assetId: null, correlationId: null, detailsJson: '{"reason":"Reviewed"}', ...extra,
});
const ok = body => async () => new Response(JSON.stringify(body), { status: 200 });
const signal = new AbortController().signal;

test('query trims filters, omits empty ones and passes the cursor through untouched', () => {
  assert.equal(api.auditQuery({ actionPrefix: '  ', targetType: '' }, null), 'limit=50');
  assert.equal(
    api.auditQuery({ actionPrefix: ' logic-module. ', targetType: 'LogicModule' }, 'abc_-9'),
    'limit=50&actionPrefix=logic-module.&targetType=LogicModule&cursor=abc_-9');
});

test('a valid page is returned and the request is a credentialed no-store GET', async () => {
  let seen;
  const page = await api.loadAuditPage({ actionPrefix: '', targetType: '' }, null, signal, async (url, init) => {
    seen = { url, init };
    return new Response(JSON.stringify({ items: [event(1), event(2)], nextCursor: 'next-1' }), { status: 200 });
  });
  assert.equal(page.items.length, 2);
  assert.equal(page.nextCursor, 'next-1');
  assert.match(seen.url, /^\/api\/v1\/security\/audit-events\?limit=50$/);
  assert.equal(seen.init.credentials, 'same-origin');
  assert.equal(seen.init.cache, 'no-store');
});

test('malformed responses are rejected instead of shown', async () => {
  const bad = [
    { items: 'x', nextCursor: null },
    { items: [event(1, { id: 'not-a-guid' })], nextCursor: null },
    { items: [event(1), event(1)], nextCursor: null },
    { items: [event(1, { occurredAtUtc: 'yesterday' })], nextCursor: null },
    { items: [event(1, { action: '' })], nextCursor: null },
    { items: [event(1, { detailsJson: 5 })], nextCursor: null },
    { items: [], nextCursor: 'bad cursor!' },
    { items: Array.from({ length: 51 }, (_, n) => event(n + 1)), nextCursor: null },
  ];
  for (const body of bad) {
    await assert.rejects(api.loadAuditPage({ actionPrefix: '', targetType: '' }, null, signal, ok(body)),
      error => error.kind === 'invalidResponse');
  }
});

test('refusals map to stable error kinds', async () => {
  const status = code => async () => new Response('{}', { status: code });
  const kind = async code => {
    try { await api.loadAuditPage({ actionPrefix: '', targetType: '' }, null, signal, status(code)); }
    catch (error) { return error.kind; }
    return 'none';
  };
  assert.equal(await kind(401), 'accessDenied');
  assert.equal(await kind(403), 'accessDenied');
  assert.equal(await kind(400), 'invalid');
  assert.equal(await kind(500), 'unavailable');
  await assert.rejects(api.loadAuditPage({ actionPrefix: '', targetType: '' }, null, signal,
    async () => { throw new TypeError('offline'); }), error => error.kind === 'unavailable');
});

test('older pages are appended without duplicating an event already shown', () => {
  const merged = api.appendAuditEvents([event(1), event(2)], [event(2), event(3)]);
  assert.deepEqual(merged.map(item => item.id), [id(1), id(2), id(3)]);
});

test('details show only top-level scalars as text and tolerate malformed JSON', () => {
  assert.deepEqual(api.auditDetails('{"reason":"Reviewed","count":2,"ok":true,"none":null,"nested":{"a":1},"list":[1]}'),
    [{ name: 'reason', value: 'Reviewed' }, { name: 'count', value: '2' }, { name: 'ok', value: 'true' },
      { name: 'none', value: '—' }]);
  assert.deepEqual(api.auditDetails('not json'), []);
  assert.deepEqual(api.auditDetails('[1,2]'), []);
  assert.equal(api.auditDetails(JSON.stringify({ long: 'x'.repeat(500) }))[0].value.length, 201);
  const many = Object.fromEntries(Array.from({ length: 30 }, (_, n) => [`k${n}`, n]));
  assert.equal(api.auditDetails(JSON.stringify(many)).length, 12);
});

test('known actions have a label key and unknown actions fall back to the code', () => {
  assert.equal(api.auditActionLabelKey('logic-module.approved'), 'activity.action.moduleApproved');
  assert.equal(api.auditActionLabelKey('something.new'), null);
});
