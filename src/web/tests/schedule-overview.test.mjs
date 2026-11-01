import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createServer } from 'vite';

let vite, model;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  model = await vite.ssrLoadModule('/src/views/scheduleOverview.ts');
});
after(async () => { await vite?.close(); });

const at = text => Date.parse(text);
const schedule = (over = {}) => ({ enabled: true, cadenceSeconds: 60, startAtUtc: '2026-10-14T12:00:00Z', timeZoneId: 'UTC', ...over });

test('the next run is counted from the start time, as the server counts it', () => {
  const s = schedule();
  // Before the start: the start itself.
  assert.equal(model.nextScheduledAt(s, at('2026-10-14T11:00:00Z')), at('2026-10-14T12:00:00Z'));
  // Exactly on an occurrence: the one after it, because that one is due now.
  assert.equal(model.nextScheduledAt(s, at('2026-10-14T12:00:00Z')), at('2026-10-14T12:01:00Z'));
  assert.equal(model.nextScheduledAt(s, at('2026-10-14T12:00:59.999Z')), at('2026-10-14T12:01:00Z'));
  assert.equal(model.nextScheduledAt(s, at('2026-10-14T12:10:30Z')), at('2026-10-14T12:11:00Z'));
  // A cadence that does not divide the hour still counts from the start.
  assert.equal(model.nextScheduledAt(schedule({ cadenceSeconds: 45 }), at('2026-10-14T12:01:00Z')), at('2026-10-14T12:01:30Z'));
});

test('a switched-off or unusable schedule has no next run', () => {
  const now = at('2026-10-14T12:00:00Z');
  for (const bad of [schedule({ enabled: false }), schedule({ cadenceSeconds: 0 }), schedule({ cadenceSeconds: -5 }),
    schedule({ cadenceSeconds: null }), schedule({ startAtUtc: 'soon' }), schedule({ startAtUtc: null })]) {
    assert.equal(model.nextScheduledAt(bad, now), null, JSON.stringify(bad));
  }
  assert.equal(model.nextScheduledAt(schedule(), Number.NaN), null);
});

const logic = (id, name, active, extra = {}) => ({
  id, name, activeRevisionId: active ? `${id}-r` : null,
  revisions: active ? [{ id: `${id}-r`, revisionNumber: 2, mode: 'Shadow', status: 'Active',
    assetBindings: [{ assetId: 'a1' }], ...active }] : [],
  ...extra,
});
const assets = [{ id: 'a1', name: 'North Well' }];

test('only the live revision decides: unscheduled and not-live logic is counted, not listed', () => {
  const now = at('2026-10-14T12:00:30Z');
  const result = model.buildScheduleOverview([
    logic('c1', 'Pump watch', { schedule: schedule() }),
    logic('c2', 'No schedule', {}),
    logic('c3', 'Not live', null),
  ], assets, [], now);
  assert.deepEqual(result.rows.map(row => row.id), ['c1']);
  assert.equal(result.unscheduledCount, 2);
  assert.equal(result.rows[0].nextRunAtUtc, '2026-10-14T12:01:00.000Z');
  assert.deepEqual(result.rows[0].assetLabels, ['North Well']);
  assert.equal(result.rows[0].mode, 'Shadow');
  assert.equal(result.rows[0].revisionNumber, 2);
});

test('rows are ordered soonest first, switched-off ones last, and carry the latest run', () => {
  const now = at('2026-10-14T12:00:30Z');
  const result = model.buildScheduleOverview([
    logic('slow', 'Slow', { schedule: schedule({ cadenceSeconds: 3600 }) }),
    logic('off', 'Aaa off', { schedule: schedule({ enabled: false }) }),
    logic('fast', 'Fast', { schedule: schedule() }),
  ], assets, [
    { logicInstanceId: 'fast', requestedAtUtc: '2026-10-14T12:00:00Z', status: 'Completed' },
    { logicInstanceId: 'fast', requestedAtUtc: '2026-10-14T11:59:00Z', status: 'Failed' },
  ], now);
  assert.deepEqual(result.rows.map(row => row.id), ['fast', 'slow', 'off']);
  assert.deepEqual(result.rows[0].lastRun, { requestedAtUtc: '2026-10-14T12:00:00Z', status: 'Completed' });
  assert.equal(result.rows[1].lastRun, null);
  assert.equal(result.rows[2].enabled, false);
  assert.equal(result.rows[2].nextRunAtUtc, null);
});
