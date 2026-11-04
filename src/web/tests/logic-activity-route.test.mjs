import { test } from 'node:test';
import assert from 'node:assert/strict';
import { hashFor, parseHash } from '../src/routing.ts';
import { logicActivityDetail, parseLogicActivityDetail } from '../src/views/logicActivityRoute.ts';

const id = '22222222-2222-4222-8222-222222222222';
const run = '55555555-5555-4555-8555-555555555555';
test('Activity route preserves configured-logic selection and optional older run', () => {
  const value = logicActivityDetail(id, run);
  const parsed = parseHash(hashFor('logic', value));
  assert.deepEqual(parseLogicActivityDetail(parsed.detail), { configurationId: id, activity: true, runId: run });
  assert.deepEqual(parseLogicActivityDetail(logicActivityDetail(id)), { configurationId: id, activity: true, runId: null });
});
test('existing configuration and reserved Logic links retain their original meaning', () => {
  for (const value of [id, 'modules', 'schedules', 'new:' + id]) {
    assert.deepEqual(parseLogicActivityDetail(value), { configurationId: value, activity: false, runId: null });
  }
});
test('untrusted runIds do not become run detail requests', () => {
  assert.deepEqual(parseLogicActivityDetail(id + '?tab=activity&runId=not-a-guid'),
    { configurationId: id, activity: true, runId: null });
});
