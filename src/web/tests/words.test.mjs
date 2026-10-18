import assert from 'node:assert/strict';
import { test, before, after } from 'node:test';
import { createServer } from 'vite';

let vite; let words;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  words = await vite.ssrLoadModule('/src/words.ts');
});
after(async () => { await vite.close(); });

test('statuses, trust and profiles read as words; unknown values are spaced, never blank', () => {
  assert.equal(words.statusLabel('TimedOut'), 'Timed out');
  assert.equal(words.statusLabel('Completed'), 'Completed');
  assert.equal(words.statusLabel('SomethingNew'), 'Something new');
  assert.equal(words.statusLabel(null), 'Unknown');
  assert.equal(words.trustLabel('CustomerApproved'), 'Approved by your company');
  assert.equal(words.trustLabel('Untrusted'), 'Unverified');
  assert.equal(words.profileLabel('ScheduledOneShot'), 'Runs on a schedule');
});

test('outcome codes become sentences and the original code is preserved', () => {
  assert.deepEqual(words.outcomeLabel('EXECUTION_INPUT_STALE'), { text: 'An input was too old', code: 'EXECUTION_INPUT_STALE' });
  assert.equal(words.outcomeLabel('NEW_UNKNOWN_CODE').text, 'New unknown code');
  assert.deepEqual(words.outcomeLabel(null), { text: '', code: null });
});

test('every closed server value has its own plain-language wording', () => {
  assert.deepEqual(['Untrusted', 'TrustedPublisher', 'CustomerApproved', 'SignatureInvalid'].map(words.trustLabel),
    ['Unverified', 'Trusted publisher', 'Approved by your company', 'Signature invalid']);
  assert.deepEqual(['ScheduledOneShot', 'BoundedIterative', 'Reactive', 'ContinuousController', 'DurableWorkflow']
    .map(words.profileLabel),
  ['Runs on a schedule', 'Repeats until done', 'Reacts to changes', 'Runs continuously', 'Long-running workflow']);
  for (const value of ['Shadow', 'Recommendation', 'Autonomous']) {
    assert.match(words.modeSentence(value), new RegExp('^' + value + ': '), value);
  }
  assert.equal(words.triggerLabel('Manual'), 'Started by a person');
  assert.equal(words.triggerLabel('Scheduled'), 'Started by its run schedule');
  assert.equal(words.triggerLabel('Event'), 'Event', 'a value the server does not declare is spaced, not given invented wording');
});
