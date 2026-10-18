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
  // Anyone who can read anything lands on the Overview, which links onward to what they may open.
  assert.equal(routing.firstOpenableSection(user(['executions.read', 'assets.read'])), 'overview');
  assert.equal(routing.firstOpenableSection(user(['recommendations.read', 'assets.read'])), 'overview');
  assert.equal(routing.firstOpenableSection(user(['configured-logic.read'])), 'overview');
  assert.equal(routing.firstOpenableSection(user(['security.manage'])), 'access');
  assert.equal(routing.firstOpenableSection(user([])), null);
  assert.equal(routing.firstOpenableSection(null), null);
});

test('addresses carry a section and an optional encoded detail', () => {
  assert.deepEqual(routing.parseHash('#assets'), { section: 'assets', detail: null });
  assert.deepEqual(routing.parseHash('#Assets/abc%2F1'), { section: 'assets', detail: 'abc/1' });
  assert.deepEqual(routing.parseHash('#logic/'), { section: 'logic', detail: null });
  assert.deepEqual(routing.parseHash('#assets/%E0%A4%A'), { section: 'assets', detail: null });
  assert.deepEqual(routing.parseHash(''), { section: '', detail: null });
  assert.equal(routing.hashFor('assets', 'a b'), '#assets/a%20b');
  assert.equal(routing.hashFor('assets', null), '#assets');
});
