import assert from 'node:assert/strict';
import { test, before, after } from 'node:test';
import { createServer } from 'vite';

let vite; let ctx;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  ctx = await vite.ssrLoadModule('/src/views/runContext.ts');
});
after(async () => { await vite.close(); });

const configuration = { id: 'cfg', name: 'ESP optimizer', revisions: [
  { id: 'rev1', revisionNumber: 1, assetBindings: [{ role: 'well', assetId: 'a1' }] },
  { id: 'rev2', revisionNumber: 2, assetBindings: [{ role: 'well', assetId: 'a1' }, { role: 'well', assetId: 'a2' }, { role: 'well', assetId: 'a1' }] },
] };
const assets = [{ id: 'a1', name: 'North Well' }, { id: 'a2', name: 'South Well' }];

test('a run is named from its configuration revision and the loaded assets', () => {
  const result = ctx.describeRunContext({ logicInstanceId: 'cfg', configurationRevisionId: 'rev2', assetId: null }, [configuration], assets);
  assert.deepEqual(result, { logicName: 'ESP optimizer', revisionNumber: 2, assetLabels: ['North Well', 'South Well'] });
});

test('a run that names its own asset uses it; unknown ids stay short rather than blank', () => {
  const own = ctx.describeRunContext({ logicInstanceId: 'cfg', configurationRevisionId: 'rev1', assetId: 'a2' }, [configuration], assets);
  assert.deepEqual(own.assetLabels, ['South Well']);
  const unknown = ctx.describeRunContext({ logicInstanceId: 'gone', configurationRevisionId: 'x', assetId: '123456789abcdef' }, [], []);
  assert.deepEqual(unknown, { logicName: null, revisionNumber: null, assetLabels: ['12345678…'] });
});

test('long asset lists are summarised', () => {
  assert.equal(ctx.summarizeAssetLabels(['A', 'B']), 'A, B');
  assert.equal(ctx.summarizeAssetLabels(['A', 'B', 'C', 'D']), 'A, B +2 more');
});

import { configurationAssetLabels, latestRunByConfiguration } from '../src/views/runContext.ts';

test('configuration asset labels cover every revision, once, by name', () => {
  const config = { revisions: [
    { assetBindings: [{ assetId: 'a1' }, { assetId: 'a2' }] },
    { assetBindings: [{ assetId: 'a1' }, { assetId: 'a3-long-unknown-identifier' }] },
  ] };
  const assets = [{ id: 'a1', name: 'North' }, { id: 'a2', name: 'South' }];
  assert.deepEqual(configurationAssetLabels(config, assets), ['North', 'South', 'a3-long-…']);
  assert.deepEqual(configurationAssetLabels({}, assets), []);
});

test('latest run per configuration ignores undated or unlinked runs', () => {
  const map = latestRunByConfiguration([
    { logicInstanceId: 'c1', requestedAtUtc: '2026-10-01T00:00:00Z', status: 'Succeeded' },
    { logicInstanceId: 'c1', requestedAtUtc: '2026-10-02T00:00:00Z', status: 'Failed' },
    { logicInstanceId: 'c1', requestedAtUtc: 'not a date', status: 'Succeeded' },
    { requestedAtUtc: '2026-10-03T00:00:00Z', status: 'Succeeded' },
  ]);
  assert.deepEqual([...map.keys()], ['c1']);
  assert.equal(map.get('c1').status, 'Failed');
});
