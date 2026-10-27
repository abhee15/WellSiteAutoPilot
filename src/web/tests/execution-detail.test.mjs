/* global AbortController, Response, DOMException */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

let vite;
let detail;
let panel;

before(async () => {
  vite = await createServer({
    server: { middlewareMode: true },
    appType: 'custom',
    logLevel: 'silent',
  });
  detail = await vite.ssrLoadModule('/src/views/executionDetail.ts');
  panel = await vite.ssrLoadModule('/src/views/ExecutionDetailPanel.tsx');
});

after(async () => {
  await vite?.close();
});

const id = 'd875c27a-8a9c-4f3d-85cb-24f097b956dc';
const secondId = 'c091e224-ff0e-4dbf-96d8-69303b289f07';
const response = {
  executionId: id,
  status: 'Completed',
  moduleId: 'weatherford.fillage',
  resultCode: 'SUCCESS',
};

test('execution inspection uses only the server-authorized detail endpoint', async () => {
  let called = 0;
  const controller = new AbortController();
  const loaded = await detail.fetchExecutionDetail(id, controller.signal, async (url, options) => {
    called++;
    assert.equal(url, '/api/v1/executions/' + id);
    assert.equal(options.method, 'GET');
    assert.equal(options.credentials, 'same-origin');
    assert.equal(options.cache, 'no-store');
    assert.equal(options.signal, controller.signal);
    return new Response(JSON.stringify(response), { status: 200 });
  });
  assert.deepEqual(loaded, response);
  assert.equal(called, 1);
});

test('invalid IDs never reach the server', async () => {
  let called = 0;
  await assert.rejects(detail.fetchExecutionDetail(
    '../outside', new AbortController().signal, async () => {
      called++;
      return new Response('{}');
    },
  ), { kind: 'invalidRequest' });
  assert.equal(called, 0);
});

test('expired access, missing records, and server failure fail closed', async () => {
  for (const [status, kind] of [
    [401, 'accessDenied'], [403, 'accessDenied'],
    [404, 'notFound'], [500, 'unavailable'],
  ]) {
    await assert.rejects(
      detail.fetchExecutionDetail(id, new AbortController().signal,
        async () => new Response('{}', { status })),
      { kind },
    );
  }
});

test('mismatched identity, invalid JSON and missing status never display stale data', async () => {
  for (const body of [
    JSON.stringify({ ...response, executionId: secondId }),
    JSON.stringify({ executionId: id }),
    'not-json',
    'null',
  ]) {
    await assert.rejects(
      detail.fetchExecutionDetail(id, new AbortController().signal,
        async () => new Response(body, { status: 200 })),
      { kind: 'invalidResponse' },
    );
  }
});

test('cancellation signal reaches fetch and abort propagates', async () => {
  const controller = new AbortController();
  controller.abort();
  await assert.rejects(
    detail.fetchExecutionDetail(id, controller.signal, async (_url, options) => {
      assert.equal(options.signal.aborted, true);
      throw new DOMException('Operation cancelled', 'AbortError');
    }),
    { name: 'AbortError' },
  );
});

test('output preview is bounded, plain text, and does not modify payloads', () => {
  assert.deepEqual(detail.previewExecutionOutput(null), { text: '', truncated: false });
  assert.deepEqual(detail.previewExecutionOutput('<script>x</script>'), {
    text: '<script>x</script>', truncated: false,
  });
  const longOutput = 'a'.repeat(detail.MAX_EXECUTION_OUTPUT_PREVIEW + 100);
  const preview = detail.previewExecutionOutput(longOutput);
  assert.equal(preview.text.length, detail.MAX_EXECUTION_OUTPUT_PREVIEW);
  assert.equal(preview.truncated, true);
  assert.equal(longOutput.length, detail.MAX_EXECUTION_OUTPUT_PREVIEW + 100);
  assert.equal(detail.formatDetailTimestamp('invalid'), '—');
  assert.match(detail.formatDetailTimestamp('2026-10-14T12:00:00Z'), /2026/);
});

test('panel initially displays a safe loading state, never stale detail', () => {
  const html = renderToStaticMarkup(createElement(panel.ExecutionDetailPanel, {
    executionId: id,
    onClose: () => {},
  }));
  assert.match(html, /role="region"/);
  assert.match(html, /Loading execution details/);
  assert.match(html, /Close/);
  assert.doesNotMatch(html, /SUCCESS/);
});

test('detail renderer escapes untrusted output and displays bounded operational metadata', () => {
  const html = renderToStaticMarkup(createElement(panel.ExecutionDetailContent, {
    detail: {
      ...response,
      correlationId: 'corr-123',
      trigger: 'Scheduled',
      mode: 'Shadow',
      requestContractVersion: 2,
      outputJson: '<img src=x onerror=alert(1)>',
    },
  }));
  assert.match(html, /corr-123/);
  assert.match(html, /Scheduled/);
  assert.match(html, /Shadow/);
  assert.match(html, /weatherford\.fillage/);
  assert.match(html, /&lt;img src=x onerror=alert\(1\)&gt;/);
  assert.doesNotMatch(html, /<img src=x/);
  const oversized = renderToStaticMarkup(createElement(panel.ExecutionDetailContent, {
    detail: { ...response, outputJson: 'a'.repeat(detail.MAX_EXECUTION_OUTPUT_PREVIEW + 200) },
  }));
  assert.match(oversized, /Preview limited to the first 4,096 characters/);
});


test('input observations fail closed on malformed or oversized output and cap visible telemetry', () => {
  assert.deepEqual(detail.executionInputObservations('null'), []);
  assert.deepEqual(detail.executionInputObservations('not json'), []);
  assert.deepEqual(detail.executionInputObservations(' '.repeat(1048577)), []);
  const row = { requirementId: 'fillage', assetId: id, quantity: 'PumpFillage',
    value: 82, unit: '%', quality: 'Good', timestampUtc: '2026-10-16T12:00:00Z', source: 'simulator' };
  assert.equal(detail.executionInputObservations(JSON.stringify({ inputObservations: Array(120).fill(row) })).length, 100);
  assert.deepEqual(detail.executionInputObservations(JSON.stringify({ inputObservations: [
    { ...row, value: null }, { ...row, source: 'x'.repeat(513) }, null
  ] })), []);
});


test('failed input diagnostics show bounded missing/uncertain evidence without treating scan time as freshness', () => {
  const row = { requirementId: 'fillage', assetId: id, connectionId: secondId, quantity: 'PumpFillage',
    value: null, unit: null, quality: 'Unknown', timestampUtc: '2026-10-27T05:00:00Z',
    retrievedAtUtc: '2026-10-27T05:01:00Z', timestampKind: 'UnverifiedScan',
    outcomeCode: 'EXECUTION_INPUT_TIMESTAMP_UNVERIFIED', private: 'SECRET' };
  const outputJson = JSON.stringify({ inputDiagnostics: Array(100).fill(row) });
  const rows = detail.executionInputObservations(outputJson);
  assert.equal(rows.length, 64);
  assert.equal(rows[0].value, null);
  assert.equal(rows[0].source, secondId);
  assert.equal(rows[0].private, undefined);
  assert.deepEqual(detail.executionInputObservations(JSON.stringify({ inputDiagnostics: [{ ...row, outcomeCode: '<script>' }] })), []);
  const html = renderToStaticMarkup(createElement(panel.ExecutionDetailContent, {
    detail: { ...response, status: 'Failed', failureCode: row.outcomeCode, outputJson: JSON.stringify({ inputDiagnostics: [row] }) },
  }));
  assert.match(html, /Unverified scan time; measurement freshness is unknown/);
  assert.match(html, /EXECUTION_INPUT_TIMESTAMP_UNVERIFIED/);
  assert.match(html, /Retrieved at/);
});


test('execution detail displays pinned package and saved connection revision rather than current catalog identity', () => {
  const provenance = { packageSha256: 'a'.repeat(64), connectionsTruncated: false,
    connections: [{ connectionId: secondId, revision: 7, assetCount: 2 }] };
  assert.equal(detail.executionProvenance({ ...response, provenance }).packageSha256, 'a'.repeat(64));
  assert.equal(detail.executionProvenance({ ...response, provenance: { ...provenance, packageSha256: '<script>' } }), null);
  const html = renderToStaticMarkup(createElement(panel.ExecutionDetailContent, { detail: { ...response, provenance } }));
  assert.match(html, /Pinned package SHA-256/);
  assert.match(html, /Saved source connections/);
  assert.match(html, /revision 7/);
  assert.match(html, /2 assets/);
  assert.match(html, new RegExp(secondId));
});
