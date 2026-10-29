import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';

let vite, status;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  status = await vite.ssrLoadModule('/src/components/Status.tsx');
});
after(async () => { await vite?.close(); });

test('every state the app reports has the tone it had when these were pills', () => {
  const expected = {
    success: ['Completed', 'Active', 'Healthy', 'TrustedPublisher', 'CustomerApproved'],
    warning: ['Suspended', 'Waiting', 'Untrusted', 'Draft', 'Validated', 'Degraded', 'NeedsAttention', 'outdated'],
    error: ['Failed', 'TimedOut', 'SignatureInvalid', 'Unavailable', 'Unreachable'],
    info: ['Running', 'Starting', 'Queued', 'Requested'],
  };
  for (const [tone, values] of Object.entries(expected)) {
    for (const value of values) assert.equal(status.statusTone(value), tone, `${value} should be ${tone}`);
  }
});

test('stored-but-not-a-problem states are quiet: inactive, hidden, retired, disabled and not tested are neutral', () => {
  for (const value of ['Inactive', 'Hidden', 'Retired', 'disabled', 'notTested']) {
    assert.equal(status.statusTone(value), 'neutral', value);
  }
});

test('spelling and spacing of a reported state do not change its tone', () => {
  assert.equal(status.statusTone('TIMED_OUT'), 'error');
  assert.equal(status.statusTone('needs attention'), 'warning');
  assert.equal(status.statusTone('  running '), 'info');
});

test('a state the app does not know, or none at all, is neutral rather than alarming', () => {
  assert.equal(status.statusTone('SomethingNew'), 'neutral');
  assert.equal(status.statusTone(null), 'neutral');
  assert.equal(status.statusTone(undefined), 'neutral');
  assert.equal(status.statusTone(''), 'neutral');
});
