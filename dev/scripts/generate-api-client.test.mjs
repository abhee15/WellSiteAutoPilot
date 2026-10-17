import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { main, waitForOpenApi } from './generate-api-client.mjs';

function serve(handler) {
  return new Promise(resolve => {
    const server = http.createServer(handler);
    server.listen(0, '127.0.0.1', () => resolve({ server, base: `http://127.0.0.1:${server.address().port}` }));
  });
}

test('waits for a populated OpenAPI document and returns it', async () => {
  let calls = 0;
  const { server, base } = await serve((request, response) => {
    calls += 1;
    if (calls < 3) {
      response.statusCode = 503;
      response.end();
      return;
    }
    response.setHeader('content-type', 'application/json');
    response.end(JSON.stringify({ paths: { '/api/v1/security/me': {} } }));
  });
  try {
    const document = await waitForOpenApi(base, 20);
    assert.ok(document.paths['/api/v1/security/me']);
    assert.ok(calls >= 3);
  } finally {
    server.close();
  }
});

test('gives up with a clear message when the Server never answers', async () => {
  await assert.rejects(waitForOpenApi('http://127.0.0.1:9', 1), /did not publish OpenAPI/);
});

test('rejects an empty document', async () => {
  const { server, base } = await serve((request, response) => {
    response.setHeader('content-type', 'application/json');
    response.end(JSON.stringify({ paths: {} }));
  });
  try {
    await assert.rejects(waitForOpenApi(base, 2), /had no paths/);
  } finally {
    server.close();
  }
});

test('writes the contract and runs the generate command from src/web', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'wsa-api-'));
  const { server, base } = await serve((request, response) => {
    response.setHeader('content-type', 'application/json');
    response.end(JSON.stringify({ paths: { '/health': {} } }));
  });
  try {
    await import('node:fs/promises').then(fs => fs.mkdir(path.join(root, 'src', 'web'), { recursive: true }));
    await main({
      WSA_API_BASE: base,
      WSA_REPO_ROOT: root,
      WSA_WAIT_SECONDS: '5',
      WSA_GENERATE_COMMAND: `node -e "require('fs').writeFileSync('ran.txt','ok')"`
    });
    const written = JSON.parse(await readFile(path.join(root, 'artifacts', 'openapi', 'v1', 'server.json'), 'utf8'));
    assert.ok(written.paths['/health']);
    assert.equal(await readFile(path.join(root, 'src', 'web', 'ran.txt'), 'utf8'), 'ok');
  } finally {
    server.close();
    await rm(root, { recursive: true, force: true });
  }
});
