import assert from 'node:assert/strict';
import { once } from 'node:events';
import http from 'node:http';
import { after, before, test } from 'node:test';
import { createServer as createViteServer } from 'vite';

const developmentIdentity = 'LOCAL\\wsa-operator';
const savedEnvironment = Object.fromEntries(
  ['WSA_DEV_AUTH_MODE', 'WSA_DEV_TEST_IDENTITY', 'WSA_DEV_API_TARGET'].map(
    (name) => [name, process.env[name]]
  )
);

let backend;
let frontend;
let vite;
let devAuth;
let backendBase;
let frontendBase;

async function listen(server) {
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  return `http://127.0.0.1:${server.address().port}`;
}

before(async () => {
  backend = http.createServer((request, response) => {
    const identity = request.headers['x-wsa-test-identity'];
    const displayName = request.headers['x-wsa-test-display-name'] ?? null;
    response.setHeader('Content-Type', 'application/json');
    if (request.url === '/health/live') {
      response.writeHead(200);
      response.end(JSON.stringify({ identity: identity ?? null, displayName }));
      return;
    }
    if (request.url !== '/api/v1/security/me') {
      response.writeHead(404);
      response.end('{}');
      return;
    }
    if (!identity) {
      response.writeHead(401, { 'WWW-Authenticate': 'Negotiate' });
      response.end(JSON.stringify({ reason: 'missing test identity' }));
      return;
    }
    if (identity !== developmentIdentity) {
      response.writeHead(403);
      response.end(JSON.stringify({ identity, displayName }));
      return;
    }
    response.writeHead(200);
    response.end(JSON.stringify({ identity, displayName }));
  });
  backendBase = await listen(backend);

  process.env.WSA_DEV_AUTH_MODE = 'TestHeader';
  process.env.WSA_DEV_TEST_IDENTITY = developmentIdentity;
  process.env.WSA_DEV_API_TARGET = backendBase;
  // This exercises the repository Vite configuration and an ACTUAL loopback
  // HTTP proxy, not merely a mocked proxyReq handler.
  vite = await createViteServer({
    server: { middlewareMode: true },
    appType: 'custom',
    logLevel: 'silent'
  });
  devAuth = await vite.ssrLoadModule('/dev-auth-proxy.ts');
  frontend = http.createServer((request, response) => vite.middlewares(request, response));
  frontendBase = await listen(frontend);
});

after(async () => {
  if (frontend) await new Promise((resolve) => frontend.close(resolve));
  if (vite) await vite.close();
  if (backend) await new Promise((resolve) => backend.close(resolve));
  for (const [key, value] of Object.entries(savedEnvironment)) {
    if (value === undefined) delete process.env[key];
    else process.env[key] = value;
  }
});

test('direct backend request observes a 401 challenge, not authenticated success', async () => {
  const response = await fetch(backendBase + '/api/v1/security/me');
  assert.equal(response.status, 401);
  assert.equal(response.headers.get('www-authenticate'), 'Negotiate');
});

test('browser request through actual Vite HTTP proxy is authenticated', async () => {
  const response = await fetch(frontendBase + '/api/v1/security/me');
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), {
    identity: developmentIdentity,
    displayName: null
  });
});

test('browser-supplied identity and display assertions never override server identity', async () => {
  const response = await fetch(frontendBase + '/api/v1/security/me', {
    headers: {
      'X-WSA-Test-Identity': 'LOCAL\\attacker',
      'X-WSA-Test-Display-Name': 'Forged Admin'
    }
  });
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), {
    identity: developmentIdentity,
    displayName: null
  });
});

test('health checks do not receive an injected or browser-supplied identity', async () => {
  const response = await fetch(frontendBase + '/health/live', {
    headers: {
      'X-WSA-Test-Identity': 'LOCAL\\attacker',
      'X-WSA-Test-Display-Name': 'Forged Name'
    }
  });
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { identity: null, displayName: null });
});

test('no mode, Negotiate mode and nonloopback targets cannot silently proxy', () => {
  assert.equal(devAuth.buildDevelopmentProxy({}), undefined);
  assert.throws(() => devAuth.requireConfiguredDevelopmentAuth(undefined),
    /Vite cannot reliably proxy connection-bound Windows Negotiate/);
  assert.throws(() => devAuth.buildDevelopmentProxy({
    WSA_DEV_AUTH_MODE: 'Negotiate',
    WSA_DEV_TEST_IDENTITY: developmentIdentity
  }), /Vite cannot reliably proxy/);
  for (const target of [
    'https://127.0.0.1:5080',
    'http://example.com:5080',
    'http://127.0.0.1:5080/other',
    'http://evil:pass@127.0.0.1:5080'
  ]) {
    assert.throws(() => devAuth.buildDevelopmentProxy({
      WSA_DEV_AUTH_MODE: 'TestHeader',
      WSA_DEV_TEST_IDENTITY: developmentIdentity,
      WSA_DEV_API_TARGET: target
    }), /loopback HTTP origin/);
  }
});

test('missing or ambiguous identities fail configuration before serving API traffic', () => {
  for (const identity of ['', ' ', 'LOCAL\\two,LOCAL\\three',
    'LOCAL\\operator\r\nForged: yes', 'person with spaces']) {
    assert.throws(() => devAuth.buildDevelopmentProxy({
      WSA_DEV_AUTH_MODE: 'TestHeader',
      WSA_DEV_TEST_IDENTITY: identity
    }), /WSA_DEV_TEST_IDENTITY must be one local development account/);
  }
});
