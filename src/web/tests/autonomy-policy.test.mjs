/* global AbortController, Response */
import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createServer } from 'vite';

let vite, policy, localization;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  policy = await vite.ssrLoadModule('/src/views/autonomyPolicy.ts');
  localization = await vite.ssrLoadModule('/src/localization.ts');
});
after(async () => { await vite?.close(); });

const id = '11111111-1111-4111-8111-111111111111';
const stored = {
  configuredLogicId: id, enabled: true, sinkKind: 'Simulator',
  limits: [{ command: 'SET_CHOKE', unit: '%', minimumValue: 10, maximumValue: 60 }],
  maxActionsPerWindow: 3, windowSeconds: 600, updatedBy: 'FIELD\\admin', updatedAtUtc: '2026-10-14T12:00:00Z',
  allowApprovedRecommendations: true,
};
const reply = (body, status = 200) => new Response(JSON.stringify(body), { status });
const draft = (over = {}) => ({
  enabled: true, maxActionsPerWindow: '3', windowSeconds: '600', allowApprovedRecommendations: false,
  limits: [{ command: 'SET_CHOKE', unit: '%', minimumValue: '10', maximumValue: '60' }], ...over,
});
const codes = result => result.issues.map(issue => issue.code);

test('a valid draft becomes the exact request, with numbers parsed and blank units dropped', () => {
  const result = policy.validatePolicyDraft(draft({
    limits: [{ command: '  SET_CHOKE ', unit: ' ', minimumValue: '-5.5', maximumValue: '60' }] }));
  assert.deepEqual(result.issues, []);
  assert.deepEqual(result.request, { enabled: true, maxActionsPerWindow: 3, windowSeconds: 600,
    allowApprovedRecommendations: false,
    limits: [{ command: 'SET_CHOKE', unit: null, minimumValue: -5.5, maximumValue: 60 }] });
});

test('the draft rules mirror the server rules', () => {
  const cases = [
    [draft({ limits: [] }), ['noLimits']],
    [draft({ limits: [{ command: ' ', unit: '', minimumValue: '1', maximumValue: '2' }] }), ['commandRequired']],
    [draft({ limits: [{ command: 'x'.repeat(257), unit: '', minimumValue: '1', maximumValue: '2' }] }), ['commandTooLong']],
    [draft({ limits: [{ command: 'advisory_review', unit: '', minimumValue: '1', maximumValue: '2' }] }), ['commandReserved']],
    [draft({ limits: [{ command: 'A', unit: '', minimumValue: '1', maximumValue: '2' },
      { command: 'a ', unit: '', minimumValue: '1', maximumValue: '2' }] }), ['commandDuplicate']],
    [draft({ limits: [{ command: 'A', unit: 'u'.repeat(65), minimumValue: '1', maximumValue: '2' }] }), ['unitTooLong']],
    [draft({ limits: [{ command: 'A', unit: '', minimumValue: '', maximumValue: '2' }] }), ['boundsRequired']],
    [draft({ limits: [{ command: 'A', unit: '', minimumValue: '1e3', maximumValue: '2' }] }), ['boundNotNumber']],
    [draft({ limits: [{ command: 'A', unit: '', minimumValue: '5', maximumValue: '2' }] }), ['boundsReversed']],
    [draft({ maxActionsPerWindow: '0' }), ['rateInvalid']],
    [draft({ maxActionsPerWindow: '1001' }), ['rateInvalid']],
    [draft({ maxActionsPerWindow: '1.5' }), ['rateInvalid']],
    [draft({ windowSeconds: '86401' }), ['windowInvalid']],
    [draft({ windowSeconds: '' }), ['windowInvalid']],
    [draft({ limits: Array.from({ length: 65 }, (_, at) => ({ command: 'C' + at, unit: '', minimumValue: '1', maximumValue: '2' })) }), ['tooManyLimits']],
  ];
  for (const [input, expected] of cases) {
    const result = policy.validatePolicyDraft(input);
    assert.deepEqual(codes(result), expected, JSON.stringify(input));
    assert.equal(result.request, null);
  }
});

test('a disabled policy may be saved without bounds or commands, but never with reversed bounds', () => {
  assert.deepEqual(policy.validatePolicyDraft(draft({ enabled: false, limits: [] })).issues, []);
  const open = policy.validatePolicyDraft(draft({ enabled: false,
    limits: [{ command: 'A', unit: '', minimumValue: '', maximumValue: '' }] }));
  assert.deepEqual(open.issues, []);
  assert.deepEqual(open.request.limits, [{ command: 'A', unit: null, minimumValue: null, maximumValue: null }]);
  assert.deepEqual(codes(policy.validatePolicyDraft(draft({ enabled: false,
    limits: [{ command: 'A', unit: '', minimumValue: '9', maximumValue: '2' }] }))), ['boundsReversed']);
});

test('every issue the validator can raise has a message', () => {
  for (const code of ['noLimits', 'tooManyLimits', 'commandRequired', 'commandTooLong', 'commandReserved',
    'commandDuplicate', 'unitTooLong', 'boundsRequired', 'boundNotNumber', 'boundsReversed', 'rateInvalid', 'windowInvalid']) {
    assert.ok(localization.t(`autonomyPolicy.issue.${code}`, { row: 1 }) !== `autonomyPolicy.issue.${code}`, code);
  }
});

test('a stored policy round-trips into a draft', () => {
  const read = { ...stored };
  const result = policy.validatePolicyDraft(policy.draftFromPolicy({
    enabled: read.enabled, limits: read.limits, maxActionsPerWindow: 3, windowSeconds: 600,
    allowApprovedRecommendations: true, updatedBy: 'a', updatedAtUtc: read.updatedAtUtc }));
  assert.equal(result.request.limits[0].maximumValue, 60);
  assert.equal(result.request.allowApprovedRecommendations, true);
  assert.deepEqual(policy.draftFromPolicy(null).limits, []);
  assert.equal(policy.draftFromPolicy(null).enabled, false);
});

test('reading: the policy of the asked-for logic, null when none exists, errors otherwise', async () => {
  const got = await policy.fetchAutonomyPolicy(id, new AbortController().signal, async (url, init) => {
    assert.equal(url, `/api/v1/control/policies/${id}`);
    assert.equal(init.credentials, 'same-origin');
    assert.equal(init.cache, 'no-store');
    return reply(stored);
  });
  assert.equal(got.limits[0].command, 'SET_CHOKE');
  assert.equal(await policy.fetchAutonomyPolicy(id, new AbortController().signal, async () => reply({}, 404)), null);
  for (const [status, kind] of [[401, 'accessDenied'], [403, 'accessDenied'], [500, 'unavailable']]) {
    await assert.rejects(policy.fetchAutonomyPolicy(id, new AbortController().signal, async () => reply({}, status)), { kind });
  }
  for (const body of [[], { ...stored, configuredLogicId: '22222222-2222-4222-8222-222222222222' },
    { ...stored, enabled: 'yes' }, { ...stored, windowSeconds: 1.5 }, { ...stored, updatedAtUtc: 'soon' },
    { ...stored, limits: 'none' }, { ...stored, limits: [{ command: 7 }] },
    { ...stored, limits: [{ command: 'A', minimumValue: '1' }] }]) {
    await assert.rejects(policy.fetchAutonomyPolicy(id, new AbortController().signal, async () => reply(body)),
      { kind: 'invalidResponse' }, JSON.stringify(body));
  }
});

test('saving PUTs the request and only accepts an answer that matches it', async () => {
  const request = policy.validatePolicyDraft(draft({ allowApprovedRecommendations: true })).request;
  const saved = await policy.saveAutonomyPolicy(id, request, new AbortController().signal, async (url, init) => {
    assert.equal(url, `/api/v1/control/policies/${id}`);
    assert.equal(init.method, 'PUT');
    assert.deepEqual(JSON.parse(init.body), request);
    return reply(stored);
  });
  assert.equal(saved.windowSeconds, 600);
  for (const different of [{ ...stored, enabled: false }, { ...stored, maxActionsPerWindow: 4 },
    { ...stored, allowApprovedRecommendations: false },
    { ...stored, limits: [{ ...stored.limits[0], maximumValue: 70 }] }, { ...stored, limits: [] }]) {
    await assert.rejects(policy.saveAutonomyPolicy(id, request, new AbortController().signal, async () => reply(different)),
      { kind: 'invalidResponse' }, JSON.stringify(different));
  }
  await assert.rejects(policy.saveAutonomyPolicy(id, request, new AbortController().signal, async () => reply({}, 400)), { kind: 'invalid' });
  await assert.rejects(policy.saveAutonomyPolicy(id, request, new AbortController().signal, async () => reply({}, 403)), { kind: 'accessDenied' });
});
