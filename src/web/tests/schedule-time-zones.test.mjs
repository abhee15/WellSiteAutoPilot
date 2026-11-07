import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createServer } from 'vite';

let vite, model;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  model = await vite.ssrLoadModule('/src/views/assignmentSchedule.ts');
});
after(async () => { await vite?.close(); });

test('the schedule offers every zone the browser knows, not a short fixed list', () => {
  // Compare with the engine's own list, not named zones: ICU spells some zones differently by version (Asia/Calcutta).
  const choices = new Set(model.timeZoneChoices('UTC'));
  const known = Intl.supportedValuesOf('timeZone');
  assert.ok(known.length > 100, 'this engine lists its zones');
  assert.deepEqual(known.filter(zone => !choices.has(zone)), []);
});

test('the chosen zone leads the list, and UTC is always offered', () => {
  const choices = model.timeZoneChoices('America/New_York');
  assert.equal(choices[0], 'America/New_York');
  assert.ok(choices.includes('UTC'));
});

test('a saved zone the browser does not list is kept, so an existing schedule never loses its zone', () => {
  const choices = model.timeZoneChoices('Legacy/Custom_Zone');
  assert.equal(choices[0], 'Legacy/Custom_Zone');
});

test('no zone appears twice', () => {
  const choices = model.timeZoneChoices(Intl.DateTimeFormat().resolvedOptions().timeZone);
  assert.equal(new Set(choices).size, choices.length);
});

test('an older browser without a zone list still offers the chosen zone, its own zone and UTC', () => {
  const original = Intl.supportedValuesOf;
  Intl.supportedValuesOf = undefined;
  try {
    const choices = model.timeZoneChoices('Europe/Paris');
    assert.equal(choices[0], 'Europe/Paris');
    assert.ok(choices.includes('UTC'));
    assert.ok(choices.length <= 3);
  } finally { Intl.supportedValuesOf = original; }
});
