import test, { before, after } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'vite';

let vite, proposalSentence, timeLeft;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  ({ proposalSentence, timeLeft } = await vite.ssrLoadModule('/src/views/decisionBrief.ts'));
});
after(async () => { await vite?.close(); });

test('the proposal reads as a sentence about a named asset', () => {
  assert.equal(proposalSentence({ quantity: 'PumpSpeed', suggestedValue: 1200.5, unit: 'rpm' }, 'North Well'),
    'Set pump speed to 1,200.5 rpm on North Well.');
  assert.equal(proposalSentence({ suggestedValue: 5, unit: '%' }), 'Suggested value 5 %.');
  assert.equal(proposalSentence({ command: 'SetSpeed' }, 'A'), 'Proposed: set speed on A.');
  assert.equal(proposalSentence({}), 'A change is proposed.');
});

test('time left is plain and never negative', () => {
  const now = Date.parse('2026-10-18T12:00:00Z');
  assert.equal(timeLeft(null, now), null);
  assert.equal(timeLeft('bad', now), null);
  assert.equal(timeLeft('2026-10-18T11:59:00Z', now), 'Expired');
  assert.equal(timeLeft('2026-10-18T12:00:30Z', now), 'Expires in less than a minute');
  assert.equal(timeLeft('2026-10-18T12:12:00Z', now), 'Expires in 12 min');
  assert.equal(timeLeft('2026-10-18T15:00:00Z', now), 'Expires in 3 h');
  assert.equal(timeLeft('2026-10-22T12:00:00Z', now), 'Expires in 4 days');
});
