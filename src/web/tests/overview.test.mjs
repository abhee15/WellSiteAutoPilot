import test from 'node:test';
import assert from 'node:assert/strict';
import { before, after } from 'node:test';
import { createServer } from 'vite';

let vite, attentionItems, cappedCount, assetTotalLabel;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  ({ attentionItems, cappedCount, assetTotalLabel } = await vite.ssrLoadModule('/src/views/overview.ts'));
});
after(async () => { await vite?.close(); });

const now = Date.parse('2026-10-18T12:00:00Z');
const base = { permissions: ['recommendations.read', 'executions.read', 'assets.read', 'configured-logic.read'],
  executions: [], recommendations: [], assets: [{ id: 'a' }], configuredLogic: [{ id: 'c1' }], nowUtc: now };

test('nothing needs attention when everything is quiet', () => {
  const quiet = { ...base, executions: [{ logicInstanceId: 'c1', status: 'Completed', requestedAtUtc: '2026-10-18T11:00:00Z' }] };
  assert.deepEqual(attentionItems(quiet), []);
});

test('awaiting recommendations and recent problem runs are counted, old ones are not', () => {
  const items = attentionItems({ ...base,
    recommendations: [{ id: 'r1', status: 'Pending', expiresAtUtc: '2026-10-19T00:00:00Z' }, { id: 'r2', status: 'Approved' }],
    executions: [
      { logicInstanceId: 'c1', status: 'Failed', requestedAtUtc: '2026-10-18T10:00:00Z' },
      { logicInstanceId: 'c1', status: 'TimedOut', requestedAtUtc: '2026-10-10T10:00:00Z' }] });
  assert.deepEqual(items.map(i => [i.kind, i.count]), [['decide', 1], ['failedRuns', 1]]);
});

test('families the person cannot read never appear', () => {
  const items = attentionItems({ ...base, permissions: ['assets.read'], assets: [],
    recommendations: [{ id: 'r1', status: 'Pending' }], executions: [{ status: 'Failed', requestedAtUtc: '2026-10-18T11:00:00Z' }] });
  assert.deepEqual(items.map(i => i.kind), ['noAssets']);
});

test('setup nudges: no assets, then no logic, then logic that never ran', () => {
  assert.deepEqual(attentionItems({ ...base, assets: [] }).map(i => i.kind), ['noAssets']);
  assert.deepEqual(attentionItems({ ...base, configuredLogic: [] }).map(i => i.kind), ['noLogic']);
  assert.deepEqual(attentionItems(base).map(i => i.kind), ['idleLogic']);
});

test('counts from a cut-off list say "at least" and owner totals win over the capped list', () => {
  assert.equal(cappedCount(40, 100), '40');
  assert.equal(cappedCount(100, 100), '100+');
  assert.equal(assetTotalLabel([{ assetCount: 443 }, { assetCount: 1737 }], 100, 100), '2180');
  assert.equal(assetTotalLabel([], 100, 100), '100+');
  assert.equal(assetTotalLabel(null, 12, 100), '12');
});
