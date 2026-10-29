/* global AbortController, Response */
import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
let vite, inspection, preview, download;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  inspection = await vite.ssrLoadModule('/src/views/cardInspection.ts');
  preview = await vite.ssrLoadModule('/src/views/SurfaceCardPreview.tsx');
  download = await vite.ssrLoadModule('/src/views/ExecutionOutputDownload.tsx');
});
after(async () => { await vite?.close(); });
const schema = 'foresite.surface-cards/v1';
const id = 'd875c27a-8a9c-4f3d-85cb-24f097b956dc';
const card = { id: 'current-1', kind: 'CURRENT', representation: 'SURFACE', source: 'foresite:dynacard-library',
  capturedAtUtc: '2026-10-28T12:00:00Z', positionUnit: 'in', loadUnit: 'lb', position: [0, 2, 1], load: [10, 30, 20] };
const read = cards => inspection.inspectSurfaceCards(schema, JSON.stringify(cards));

test('ragged cards retain identity, pairing, source units and original order', () => {
  const cards = [card, { ...card, id: 'full-1', kind: 'FULL', position: [0, 1], load: [2, 3] }];
  assert.deepEqual(read(cards), cards);
  const plot = inspection.surfaceCardPlot(cards[0]);
  assert.equal(plot.points, '60,230 500,30 280,130');
  assert.equal(plot.points.split(' ').length, 3);
  assert.deepEqual(cards[0].position, [0, 2, 1]);
  const flat = inspection.surfaceCardPlot({ ...card, position: [0, 0], load: [0, 0] });
  assert.equal(flat.points, '280,130 280,130');
});

test('unsupported schemas and malformed cards never produce a misleading curve', () => {
  assert.equal(inspection.inspectSurfaceCards('unknown/v1', JSON.stringify([card])), null);
  for (const json of ['null', '{}', '[null]', 'invalid', 'x'.repeat(131073)]) assert.equal(inspection.inspectSurfaceCards(schema, json), null);
  for (const invalid of [
    { ...card, position: [0] }, { ...card, load: [1, 2] }, { ...card, load: [1, null, 3] },
    { ...card, position: Array(1025).fill(0), load: Array(1025).fill(1) },
    { ...card, load: [1, 1e300, 3] }, { ...card, load: [1, '2', 3] },
    { ...card, capturedAtUtc: 'bad' }, { ...card, capturedAtUtc: '2026-10-28T12:00:00' },
    { ...card, representation: 'DOWNHOLE' }, { ...card, kind: 'REFERENCE' }, { ...card, loadUnit: 'lbf' },
    { ...card, source: '' }, { ...card, id: 'x'.repeat(129) },
  ]) assert.equal(read([invalid]), null);
  assert.equal(read([card, card]), null);
  assert.equal(read(Array.from({ length: 17 }, (_, i) => ({ ...card, id: String(i) }))), null);
});

test('preview labels units and capture time, escapes metadata, and keeps JSON for unsupported values', () => {
  const html = renderToStaticMarkup(createElement(preview.SurfaceCardPreview, { schemaId: schema,
    json: JSON.stringify([{ ...card, id: '<img src=x>', source: '<script>source</script>' }]) }));
  assert.match(html, /role="img"/);
  assert.match(html, /3 samples/);
  assert.match(html, /Load \(lb\)/);
  assert.match(html, /Position \(in\)/);
  assert.match(html, /SURFACE/);
  assert.match(html, /recorded order/);
  assert.match(html, /&lt;img src=x&gt;/);
  assert.doesNotMatch(html, /<script>|<img/);
  assert.equal(renderToStaticMarkup(createElement(preview.SurfaceCardPreview, { schemaId: 'other', json: JSON.stringify([card]) })), '');
});

test('download reauthorizes and returns exact saved JSON without rounding decimal values', async () => {
  const content = ' {"valueContractVersion":2,"value":12345.67890123456789,"unicode":"λ"}\n';
  let calls = 0;
  const output = await download.fetchExecutionOutput(id, new AbortController().signal, async (url, options) => {
    calls++;
    assert.equal(url, '/api/v1/executions/' + id);
    assert.equal(options.cache, 'no-store');
    assert.equal(options.credentials, 'same-origin');
    return new Response(JSON.stringify({ executionId: id, status: 'Completed', outputJson: content }));
  });
  assert.equal(calls, 1);
  assert.equal(output.content, content);
  assert.equal(output.filename, `execution-${id}.json`);
});

test('download rejects missing, malformed, oversized, denied and mismatched results', async () => {
  for (const content of [null, 'bad', 'null', '[]', '"text"', 'x'.repeat(524289), '{"text":"' + 'λ'.repeat(262144) + '"}']) {
    await assert.rejects(download.fetchExecutionOutput(id, new AbortController().signal,
      async () => new Response(JSON.stringify({ executionId: id, status: 'Completed', outputJson: content }))), { kind: 'invalidResponse' });
  }
  for (const status of [401, 403, 404]) await assert.rejects(download.fetchExecutionOutput(id, new AbortController().signal,
    async () => new Response('{}', { status })));
  await assert.rejects(download.fetchExecutionOutput(id, new AbortController().signal,
    async () => new Response(JSON.stringify({ executionId: 'different', status: 'Completed', outputJson: '{}' }))), { kind: 'invalidResponse' });
  const controller = new AbortController();
  await assert.rejects(download.fetchExecutionOutput(id, controller.signal, async () => {
    controller.abort();
    return new Response(JSON.stringify({ executionId: id, status: 'Completed', outputJson: '{}' }));
  }), { name: 'AbortError' });
});
