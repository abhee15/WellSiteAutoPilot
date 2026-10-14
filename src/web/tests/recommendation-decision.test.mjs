import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

let vite, client, panel;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true },
    appType: 'custom', logLevel: 'silent' });
  client = await vite.ssrLoadModule('/src/views/recommendationDecision.ts');
  panel = await vite.ssrLoadModule('/src/views/RecommendationDecisionReviewPanel.tsx');
});
after(async () => { await vite?.close(); });

const id = '66238ca2-7a40-49e6-b23c-a4e5e2701189';
const success = { id, status: 'Approved', decisionBy: 'FIELD\\reviewer',
  decisionAtUtc: '2026-10-14T12:00:00Z', decisionReason: 'Reviewed conditions' };

test('only explicit validated decisions are posted once to the governed API', async () => {
  let calls = 0;
  const controller = new AbortController();
  const result = await client.submitRecommendationDecision(
    id, 'Approved', '  Reviewed conditions  ', controller.signal, async (url, init) => {
      calls++;
      assert.equal(url, '/api/v1/recommendations/' + id + '/decision');
      assert.equal(init.method, 'POST');
      assert.equal(init.credentials, 'same-origin');
      assert.equal(init.cache, 'no-store');
      assert.equal(init.headers['Content-Type'], 'application/json');
      assert.equal(init.signal, controller.signal);
      assert.deepEqual(JSON.parse(init.body),
        { decision: 'Approved', reason: 'Reviewed conditions' });
      return new Response(JSON.stringify(success));
    },
  );
  assert.equal(calls, 1);
  assert.deepEqual(result, success);
});

test('invalid identities, actions, and audit reasons never reach the backend', async () => {
  let calls = 0;
  for (const [target, decision, reason] of [
    ['not-a-guid', 'Approved', 'reviewed'],
    [id, 'Pending', 'reviewed'],
    [id, 'Approved', ' '],
    [id, 'Rejected', 'bad\nreason'],
    [id, 'Approved', 'x'.repeat(1025)],
  ]) {
    await assert.rejects(client.submitRecommendationDecision(
      target, decision, reason, new AbortController().signal, async () => {
        calls++;
        return new Response('{}');
      },
    ), { kind: 'invalid' });
  }
  assert.equal(calls, 0);
});

test('conflicts, expired access, and unavailable runtime fail closed', async () => {
  for (const [status, kind] of [
    [401, 'accessDenied'], [403, 'accessDenied'], [404, 'notFound'],
    [409, 'conflict'], [400, 'invalid'], [500, 'unavailable'],
  ]) {
    await assert.rejects(client.submitRecommendationDecision(
      id, 'Rejected', 'Unsafe operating state', new AbortController().signal,
      async () => new Response('{}', { status }),
    ), { kind });
  }
});

test('unverified 2xx responses do not count as a saved operator decision', async () => {
  for (const body of [
    'not-json',
    JSON.stringify({ ...success, id: 'f7b43ec8-ea4a-4b2b-9f1d-465074490ef3' }),
    JSON.stringify({ ...success, status: 'Rejected' }),
    JSON.stringify({ ...success, decisionAtUtc: null }),
    JSON.stringify({ ...success, decisionBy: null }),
  ]) {
    await assert.rejects(client.submitRecommendationDecision(
      id, 'Approved', 'Reviewed conditions', new AbortController().signal,
      async () => new Response(body),
    ), { kind: 'invalidResponse' });
  }
});

test('cancellation propagates to the decision request without automatic retry', async () => {
  const controller = new AbortController();
  controller.abort();
  let attempts = 0;
  await assert.rejects(client.submitRecommendationDecision(
    id, 'Approved', 'Reviewed conditions', controller.signal, async (_url, init) => {
      attempts++;
      assert.equal(init.signal.aborted, true);
      throw new DOMException('Cancelled', 'AbortError');
    },
  ), { name: 'AbortError' });
  assert.equal(attempts, 1);
});

test('review panel requires deliberate selection, reason and acknowledgement', () => {
  const html = renderToStaticMarkup(createElement(
    panel.RecommendationDecisionReviewPanel, {
      item: { id, status: 'Pending', assetId: 'well-1', command: 'SetSpeed',
        suggestedValue: 9.5, unit: 'spm', createdAtUtc: '2026-10-14T10:00:00Z',
        expiresAtUtc: '2026-10-15T10:00:00Z' },
      onCancel: () => {}, onSaved: () => {}, onConflict: () => {},
    },
  ));
  assert.match(html, /Recommendation decision review/);
  assert.match(html, /explicit confirmation/);
  assert.match(html, /SetSpeed/);
  assert.match(html, /type="radio"/);
  assert.match(html, /type="checkbox"/);
  assert.match(html, /maxLength="1024"/);
  assert.match(html, /Submit decision/);
  assert.match(html, /disabled=""/);
});
