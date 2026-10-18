import test, { before, after } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'vite';

let vite, recentRunsFor, logicStatusSentence;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  ({ recentRunsFor, logicStatusSentence } = await vite.ssrLoadModule('/src/views/logicSummary.ts'));
});
after(async () => { await vite?.close(); });

const run = (executionId, logicInstanceId, requestedAtUtc) => ({ executionId, logicInstanceId, requestedAtUtc, status: 'Completed' });

test('recent runs are this logic only, newest first, capped', () => {
  const runs = [run('1', 'c1', '2026-10-01T00:00:00Z'), run('2', 'c2', '2026-10-05T00:00:00Z'),
    run('3', 'c1', '2026-10-03T00:00:00Z'), run('4', 'c1', 'bad')];
  assert.deepEqual(recentRunsFor(runs, 'c1').map(r => r.executionId), ['3', '1']);
  assert.deepEqual(recentRunsFor(runs, 'c1', 1).map(r => r.executionId), ['3']);
  assert.deepEqual(recentRunsFor(runs, null), []);
});

test('status sentence says what is live, where, and whether it has run', () => {
  assert.equal(logicStatusSentence({ revisions: [] }), 'No revision has been saved yet.');
  assert.equal(
    logicStatusSentence({ revisions: [{ id: 'r1', revisionNumber: 2, status: 'Draft' }] }),
    'Not live yet. Revision 2 is draft.');
  const live = { activeRevisionId: 'r1', revisions: [{ id: 'r1', revisionNumber: 3, mode: 'Shadow',
    assetBindings: [{ assetId: 'a' }, { assetId: 'a' }, { assetId: 'b' }] }] };
  assert.equal(logicStatusSentence(live), 'Revision 3 is live in Shadow mode on 2 assets. No recorded run yet.');
  assert.match(logicStatusSentence(live, { requestedAtUtc: '2026-10-01T00:00:00Z', status: 'Failed' }), /Last recorded run .*: failed\.$/);
});
