/* global AbortController, Response, DOMException, URL */
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { before, after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

let vite, client, panel, page, localization;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  client = await vite.ssrLoadModule('/src/views/controlExecution.ts');
  panel = await vite.ssrLoadModule('/src/views/ControlExecutionPanel.tsx');
  page = await vite.ssrLoadModule('/src/views/RecommendationsPage.tsx');
  localization = await vite.ssrLoadModule('/src/localization.ts');
});
after(async () => { await vite?.close(); });

const id = '66238ca2-7a40-49e6-b23c-a4e5e2701189';
const nowUtc = Date.parse('2026-10-14T12:00:00Z');
const approved = {
  id, status: 'Approved', canDecide: true, command: 'SetSpeed', decisionBy: 'FIELD\\reviewer',
  decisionAtUtc: '2026-10-14T11:00:00Z', expiresAtUtc: '2026-10-15T12:00:00Z', controlActionId: null,
  assetId: 'asset-1', moduleId: 'wfd.fill', moduleVersion: '1.0.0', suggestedValue: 9, unit: 'spm',
  quantity: 'PumpingSpeed', reasonCode: 'OPTIMIZED', createdAtUtc: '2026-10-14T10:00:00Z',
};
const action = { id: 'f7b43ec8-ea4a-4b2b-9f1d-465074490ef3', recommendationId: id, status: 'Verified', gateCode: 'READBACK_CONFIRMED' };

// Guard rails: the browser's tables cannot drift from the server's closed lists.
function csharpConstants(file, pattern) {
  const source = readFileSync(new URL(file, import.meta.url), 'utf8');
  return [...source.matchAll(pattern)].map(match => match[1]);
}

test('every gate code the server can report has a message', () => {
  const codes = csharpConstants('../../server/WellSiteAutoPilot.Application/Control/AutonomousControlService.cs',
    /public const string \w+ = "([A-Z_]+)";/g);
  assert.ok(codes.length >= 20, 'the GateCodes list could not be read');
  assert.deepEqual([...codes].sort(), Object.keys(client.CONTROL_GATE_MESSAGES).sort());
  for (const key of Object.values(client.CONTROL_GATE_MESSAGES)) {
    assert.equal(typeof localization.englishMessages[key], 'string', key);
  }
});

test('every control action status the server can report has a message', () => {
  const source = readFileSync(new URL('../../server/WellSiteAutoPilot.Domain/Control/ControlModels.cs', import.meta.url), 'utf8');
  const body = /enum ControlActionStatus\s*\{([^}]*)\}/.exec(source)?.[1] ?? '';
  const statuses = [...body.matchAll(/^\s*([A-Z][A-Za-z]+)\s*=/gm)].map(match => match[1]);
  assert.ok(statuses.length >= 6, 'the status list could not be read');
  assert.deepEqual([...statuses].sort(), Object.keys(client.CONTROL_OUTCOME_MESSAGES).sort());
  for (const key of Object.values(client.CONTROL_OUTCOME_MESSAGES)) {
    assert.equal(typeof localization.englishMessages[key], 'string', key);
  }
});

test('the advisory command matches the server\'s reserved command', () => {
  const [reserved] = csharpConstants('../../server/WellSiteAutoPilot.Domain/Recommendations/RecommendationCommands.cs',
    /public const string Advisory = "([A-Z_]+)";/g);
  assert.equal(client.ADVISORY_COMMAND, reserved);
});

test('only a person-approved, unexecuted, non-advisory, unexpired recommendation is offered, to those who may', () => {
  const offered = (patch, canExecute = true) => client.recommendationIsExecutable({ ...approved, ...patch }, nowUtc, canExecute);
  assert.equal(offered({}), true);
  assert.equal(offered({}, false), false);
  for (const patch of [
    { canDecide: false }, { canDecide: undefined }, { status: 'Created' }, { status: 'Executed' }, { status: 'Rejected' },
    { controlActionId: 'f7b43ec8-ea4a-4b2b-9f1d-465074490ef3' }, { command: 'ADVISORY_REVIEW' },
    { decisionBy: 'system:autonomy-gate' }, { decisionBy: ' ' }, { decisionBy: null },
    { expiresAtUtc: '2026-10-14T11:59:59Z' }, { expiresAtUtc: 'not a date' },
  ]) {
    assert.equal(offered(patch), false, JSON.stringify(patch));
  }
  assert.equal(offered({ expiresAtUtc: null }), true);
});

test('execution posts once, with no body, to the governed endpoint', async () => {
  let calls = 0;
  const controller = new AbortController();
  const result = await client.executeApprovedRecommendation(id, controller.signal, async (url, init) => {
    calls++;
    assert.equal(url, '/api/v1/control/recommendations/' + id + '/execute');
    assert.equal(init.method, 'POST');
    assert.equal(init.credentials, 'same-origin');
    assert.equal(init.cache, 'no-store');
    assert.equal(init.body, undefined, 'the command must come from the stored recommendation, never the browser');
    assert.equal(init.signal, controller.signal);
    return new Response(JSON.stringify({ executed: true, refusalCode: null, action }));
  });
  assert.equal(calls, 1);
  assert.deepEqual(result, { action, refusalCode: null });
});

test('a gate refusal is a result, not an error, and carries no action', async () => {
  const result = await client.executeApprovedRecommendation(id, new AbortController().signal,
    async () => new Response(JSON.stringify({ executed: false, refusalCode: 'GATE_KILL_SWITCH', action: null })));
  assert.deepEqual(result, { action: null, refusalCode: 'GATE_KILL_SWITCH' });
});

test('a malformed identity never reaches the backend', async () => {
  let calls = 0;
  await assert.rejects(client.executeApprovedRecommendation('not-a-guid', new AbortController().signal,
    async () => { calls++; return new Response('{}'); }), { kind: 'invalid' });
  assert.equal(calls, 0);
});

test('conflicts, expired access and an unavailable gate fail closed', async () => {
  for (const [status, kind] of [[401, 'accessDenied'], [403, 'accessDenied'], [404, 'notFound'],
    [409, 'conflict'], [400, 'invalid'], [500, 'unavailable']]) {
    await assert.rejects(client.executeApprovedRecommendation(id, new AbortController().signal,
      async () => new Response('{}', { status })), { kind });
  }
});

test('an unverified 2xx never counts as an execution', async () => {
  for (const body of [
    'not-json', '[]', '{}',
    JSON.stringify({ executed: true, refusalCode: null, action: null }),
    JSON.stringify({ executed: true, refusalCode: null, action: { ...action, recommendationId: 'f7b43ec8-ea4a-4b2b-9f1d-465074490ef3' } }),
    JSON.stringify({ executed: true, refusalCode: null, action: { ...action, status: '' } }),
    JSON.stringify({ executed: false, refusalCode: null, action: null }),
    JSON.stringify({ executed: false, refusalCode: 'GATE_KILL_SWITCH', action }),
    JSON.stringify({ executed: 'yes', refusalCode: null, action }),
  ]) {
    await assert.rejects(client.executeApprovedRecommendation(id, new AbortController().signal,
      async () => new Response(body)), { kind: 'invalidResponse' }, body);
  }
});

test('cancellation propagates without automatic retry', async () => {
  const controller = new AbortController();
  controller.abort();
  let attempts = 0;
  await assert.rejects(client.executeApprovedRecommendation(id, controller.signal, async (_url, init) => {
    attempts++;
    assert.equal(init.signal.aborted, true);
    throw new DOMException('Cancelled', 'AbortError');
  }), { name: 'AbortError' });
  assert.equal(attempts, 1);
});

test('the execution panel asks for confirmation and takes no command input', () => {
  const html = renderToStaticMarkup(createElement(panel.ControlExecutionPanel, {
    item: approved, onCancel: () => {}, onExecuted: () => {}, onConflict: () => {},
  }));
  assert.match(html, /Execute approved recommendation/);
  assert.match(html, /Nothing is sent until you confirm/);
  assert.match(html, /control gate/);
  assert.match(html, /SetSpeed/);
  assert.match(html, /type="checkbox"/);
  assert.match(html, /Execute command/);
  assert.match(html, /disabled=""/);
  assert.doesNotMatch(html, /<input(?![^>]*type="checkbox")/, 'only the confirmation is an input');
  assert.doesNotMatch(html, /<textarea/);
});

test('the inbox offers Execute only for executable rows, and only with the control permission', () => {
  const rows = [approved, { ...approved, id: 'b', command: 'ADVISORY_REVIEW' }, { ...approved, id: 'c', status: 'Created' }];
  const withExecute = renderToStaticMarkup(createElement(page.RecommendationInboxTable, {
    items: rows, nowUtc, onExecute: () => {},
  }));
  assert.equal(withExecute.match(/>Execute</g)?.length, 1);
  const without = renderToStaticMarkup(createElement(page.RecommendationInboxTable, { items: rows, nowUtc }));
  assert.doesNotMatch(without, />Execute</);
});

test('the page tells those who can execute that approval and execution are separate steps', () => {
  const executing = renderToStaticMarkup(createElement(page.RecommendationsPage, {
    userId: 'operator', permissions: ['recommendations.decide', 'control.manage'],
  }));
  assert.match(executing, /Executing an approved recommendation is a separate, explicit step/);
  const decidingOnly = renderToStaticMarkup(createElement(page.RecommendationsPage, {
    userId: 'operator', permissions: ['recommendations.decide'],
  }));
  assert.doesNotMatch(decidingOnly, /separate, explicit step/);
});
