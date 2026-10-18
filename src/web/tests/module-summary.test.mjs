import test, { before, after } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'vite';

let vite, describeModule;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  ({ describeModule } = await vite.ssrLoadModule('/src/views/moduleSummary.ts'));
});
after(async () => { await vite?.close(); });

const manifest = {
  assetRequirements: [{ role: 'well', minimumCount: 1, maximumCount: 1, requiredAssetTypeKey: 'well' }],
  dataRequirements: [{ id: 'f', assetRole: 'well', quantity: 'PumpFillage', canonicalUnit: '%', maximumAgeSeconds: 60, allowUncertainQuality: false }],
  commandRequirements: [],
};
const module = { moduleId: 'w.sample', version: '1.0.0', displayName: 'Sample', publisher: 'Weatherford',
  executionProfile: 'ScheduledOneShot', trustStatus: 'CustomerApproved', manifestJson: JSON.stringify(manifest) };

test('a module is described from its manifest in plain words', () => {
  const s = describeModule(module, [
    { name: 'B', revisions: [{ moduleId: 'w.sample' }] }, { name: 'A', revisions: [{ moduleId: 'w.sample' }, { moduleId: 'w.sample' }] },
    { name: 'C', revisions: [{ moduleId: 'other' }] }]);
  assert.equal(s.sentence, 'Reads pump fillage for 1 well.');
  assert.deepEqual(s.needs, ['1 well for the “well” role', 'pump fillage (%), no older than 1 min, good quality only']);
  assert.equal(s.declaresCommands, false);
  assert.equal(s.provenance, 'Weatherford · Runs on a schedule · Approved by your company');
  assert.deepEqual(s.usedBy, ['A', 'B']);
});

test('commands are flagged and an unreadable manifest says so', () => {
  const withCommands = describeModule({ ...module, manifestJson: JSON.stringify({ ...manifest, commandRequirements: [{}] }) });
  assert.equal(withCommands.declaresCommands, true);
  assert.equal(describeModule({ ...module, manifestJson: 'nope' }).sentence, 'This module’s description could not be read.');
  assert.equal(describeModule({ ...module, manifestJson: '{}' }).sentence, 'Does not declare any data it reads.');
});
