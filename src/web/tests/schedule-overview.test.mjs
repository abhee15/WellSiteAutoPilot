import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createServer } from 'vite';

let vite, model;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  model = await vite.ssrLoadModule('/src/views/scheduleOverview.ts');
});
after(async () => { await vite?.close(); });

const schedule = (over = {}) => ({ enabled: true, cadenceSeconds: 60, startAtUtc: '2026-10-14T12:00:00Z', timeZoneId: 'UTC', ...over });

// The server owns the cadence: a configuration arrives with nextRunAtUtc already counted (null when off or unusable).
const logic = (id, name, active, extra = {}) => ({
  id, name, activeRevisionId: active ? `${id}-r` : null,
  revisions: active ? [{ id: `${id}-r`, revisionNumber: 2, mode: 'Shadow', status: 'Active',
    assetBindings: [{ assetId: 'a1' }], ...active }] : [],
  ...extra,
});
const assets = [{ id: 'a1', name: 'North Well' }];

test('only the live revision decides: unscheduled and not-live logic is counted, not listed', () => {
  const result = model.buildScheduleOverview([
    logic('c1', 'Pump watch', { schedule: schedule() }, { nextRunAtUtc: '2026-10-14T12:01:00Z' }),
    logic('c2', 'No schedule', {}),
    logic('c3', 'Not live', null),
  ], assets, []);
  assert.deepEqual(result.rows.map(row => row.id), ['c1']);
  assert.equal(result.unscheduledCount, 2);
  assert.equal(result.rows[0].nextRunAtUtc, '2026-10-14T12:01:00.000Z');
  assert.deepEqual(result.rows[0].assetLabels, ['North Well']);
  assert.equal(result.rows[0].mode, 'Shadow');
  assert.equal(result.rows[0].revisionNumber, 2);
});

test('rows are ordered soonest first, switched-off ones last, and carry the latest run', () => {
  const result = model.buildScheduleOverview([
    logic('slow', 'Slow', { schedule: schedule({ cadenceSeconds: 3600 }) }, { nextRunAtUtc: '2026-10-14T13:00:00Z' }),
    logic('off', 'Aaa off', { schedule: schedule({ enabled: false }) }),
    logic('fast', 'Fast', { schedule: schedule() }, { nextRunAtUtc: '2026-10-14T12:01:00Z' }),
  ], assets, [
    { logicInstanceId: 'fast', requestedAtUtc: '2026-10-14T12:00:00Z', status: 'Completed' },
    { logicInstanceId: 'fast', requestedAtUtc: '2026-10-14T11:59:00Z', status: 'Failed' },
  ]);
  assert.deepEqual(result.rows.map(row => row.id), ['fast', 'slow', 'off']);
  assert.deepEqual(result.rows[0].lastRun, { requestedAtUtc: '2026-10-14T12:00:00Z', status: 'Completed' });
  assert.equal(result.rows[1].lastRun, null);
  assert.equal(result.rows[2].enabled, false);
  assert.equal(result.rows[2].nextRunAtUtc, null);
});

test('an unreadable server next run is shown as none rather than guessed', () => {
  for (const nextRunAtUtc of [undefined, null, 'soon', 12]) {
    const result = model.buildScheduleOverview([logic('c1', 'Pump', { schedule: schedule() }, { nextRunAtUtc })], assets, []);
    assert.equal(result.rows[0].nextRunAtUtc, null, String(nextRunAtUtc));
  }
});

test('the schedule in effect decides, so a paused or re-timed logic is not shown as declared', () => {
  const result = model.buildScheduleOverview([
    logic('p', 'Paused', { schedule: schedule() }, { effectiveSchedule: schedule({ enabled: false }) }),
    logic('r', 'Retimed', { schedule: schedule() }, { effectiveSchedule: schedule({ cadenceSeconds: 600 }), nextRunAtUtc: '2026-10-14T12:10:00Z' }),
    logic('o', 'Declared off', { schedule: schedule({ enabled: false }) }, { effectiveSchedule: schedule({ enabled: false }) }),
    logic('u', 'Untouched', { schedule: schedule() }, { effectiveSchedule: schedule(), nextRunAtUtc: '2026-10-14T12:01:00Z' }),
    logic('l', 'Legacy', { schedule: schedule() }, { nextRunAtUtc: '2026-10-14T12:02:00Z' }),
  ], assets, []);
  const row = id => result.rows.find(item => item.id === id);
  assert.deepEqual([row('p').enabled, row('p').paused, row('p').changed], [false, true, true]);
  assert.deepEqual([row('r').enabled, row('r').paused, row('r').changed, row('r').cadenceSeconds], [true, false, true, 600]);
  assert.deepEqual([row('o').enabled, row('o').paused, row('o').changed], [false, false, false]);
  assert.deepEqual([row('u').paused, row('u').changed], [false, false]);
  assert.deepEqual([row('l').paused, row('l').changed, row('l').cadenceSeconds], [false, false, 60]);
});

test('a schedule below its adapter minimum is flagged from the server answer only', () => {
  const result = model.buildScheduleOverview([
    logic('b', 'Too fast', { schedule: schedule() }, { belowMinimum: true, nextRunAtUtc: '2026-10-14T12:01:00Z' }),
    logic('n', 'Fine', { schedule: schedule() }, { belowMinimum: false, nextRunAtUtc: '2026-10-14T12:02:00Z' }),
    logic('m', 'Missing', { schedule: schedule() }, { nextRunAtUtc: '2026-10-14T12:03:00Z' }),
  ], assets, []);
  assert.deepEqual(result.rows.map(row => row.belowMinimum), [true, false, false]);
});
