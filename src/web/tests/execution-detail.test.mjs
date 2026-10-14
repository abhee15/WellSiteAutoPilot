import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';

let vite;
let detail;

before(async () => {
  vite = await createServer({
    server: { middlewareMode: true },
    appType: 'custom',
    logLevel: 'silent',
  });
  detail = await vite.ssrLoadModule('/src/views/executionDetail.ts');
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
