import assert from 'node:assert/strict';
import { test, before, after } from 'node:test';
import { createServer } from 'vite';

let vite; let routing;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  routing = await vite.ssrLoadModule('/src/routing.ts');
});
after(async () => { await vite.close(); });

const user = permissions => ({ userId: 'u', identityName: 'x', isActive: true, permissions });

test('landing priority follows section order and permissions', () => {
  assert.equal(routing.firstOpenableSection(user(['executions.read', 'assets.read'])), 'operations');
  assert.equal(routing.firstOpenableSection(user(['recommendations.read', 'assets.read'])), 'recommendations');
  assert.equal(routing.firstOpenableSection(user(['configured-logic.read'])), 'logic');
  assert.equal(routing.firstOpenableSection(user(['security.manage'])), 'access');
  assert.equal(routing.firstOpenableSection(user([])), null);
  assert.equal(routing.firstOpenableSection(null), null);
});
