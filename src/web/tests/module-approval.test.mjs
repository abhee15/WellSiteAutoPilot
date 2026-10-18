import test from 'node:test';
import assert from 'node:assert/strict';
import { before, after } from 'node:test';
import { createServer } from 'vite';

let vite, helpers;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  helpers = await vite.ssrLoadModule('/src/views/moduleApproval.ts');
});
after(async () => { await vite?.close(); });

test('a module is approved, not approved, or retired for good', () => {
  assert.equal(helpers.moduleState({ trustStatus: 'CustomerApproved', isEnabled: true }), 'approved');
  assert.equal(helpers.moduleState({ trustStatus: 'TrustedPublisher', isEnabled: true }), 'approved');
  assert.equal(helpers.moduleState({ trustStatus: 'Untrusted', isEnabled: false }), 'notApproved');
  assert.equal(helpers.moduleState({ trustStatus: 'CustomerApproved', isEnabled: false }), 'retired');
  assert.equal(helpers.moduleState({ trustStatus: 'CustomerApproved' }), 'approved');
});

test('the label and colour of a retired version are neutral and say so', () => {
  const retired = { trustStatus: 'CustomerApproved', isEnabled: false };
  assert.equal(helpers.moduleStateLabel(retired), 'Retired');
  assert.equal(helpers.moduleStateToken(retired), 'Retired');
  assert.equal(helpers.moduleStateLabel({ trustStatus: 'CustomerApproved', isEnabled: true }), 'Approved by your company');
  assert.equal(helpers.moduleStateToken({ trustStatus: 'Untrusted', isEnabled: false }), 'Untrusted');
});

test('only one change is offered in each state, and none once retired', () => {
  assert.equal(helpers.availableApproval('notApproved'), 'approve');
  assert.equal(helpers.availableApproval('approved'), 'retire');
  assert.equal(helpers.availableApproval('retired'), null);
});

test('a reason must be plain, non-blank and within the limit', () => {
  assert.equal(helpers.reasonProblem(''), 'empty');
  assert.equal(helpers.reasonProblem('   '), 'empty');
  assert.equal(helpers.reasonProblem('x'.repeat(501)), 'long');
  assert.equal(helpers.reasonProblem('x'.repeat(500)), null);
  assert.equal(helpers.reasonProblem('two\nlines'), 'characters');
  assert.equal(helpers.reasonProblem('  Reviewed by operations  '), null);
});

test('the address escapes the module id and version', () => {
  assert.equal(helpers.approvalPath({ moduleId: 'acme/ESP optimizer', version: '1.0.0+b1' }, 'approve'),
    '/api/v1/logic-modules/acme%2FESP%20optimizer/1.0.0%2Bb1/approve');
});

test('a reply only counts when it is about the same module and shows the new state', () => {
  const before = { id: 'm1' };
  assert.equal(helpers.approvalTookEffect(before, { id: 'm1', isEnabled: true }, 'approve'), true);
  assert.equal(helpers.approvalTookEffect(before, { id: 'm1', isEnabled: false }, 'retire'), true);
  assert.equal(helpers.approvalTookEffect(before, { id: 'm1', isEnabled: false }, 'approve'), false);
  assert.equal(helpers.approvalTookEffect(before, { id: 'other', isEnabled: true }, 'approve'), false);
  assert.equal(helpers.approvalTookEffect(before, null, 'approve'), false);
  assert.equal(helpers.approvalTookEffect(before, 'ok', 'approve'), false);
});
