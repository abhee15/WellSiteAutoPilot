import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';

let vite, view;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  view = await vite.ssrLoadModule('/src/views/auditPresentation.ts');
});
after(async () => { await vite?.close(); });

const id = n => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const event = (extra = {}) => ({
  id: id(1), occurredAtUtc: '2026-10-29T17:00:00Z', actorUserId: id(900), actorIdentity: 'WFT\\E206862',
  action: 'logic-module.approved', targetType: 'logic-module', targetId: id(503),
  assetId: null, correlationId: null, detailsJson: '{}', ...extra,
});

test('the different spellings of an action and a target type collapse to one shape', () => {
  assert.equal(view.normalizeAction('CONFIGURED_LOGIC_REVISION_ACTIVATED'), 'configured.logic.revision.activated');
  assert.equal(view.normalizeAction('connection.status_changed'), 'connection.status.changed');
  assert.equal(view.normalizeAction('logic-module.package-published'), 'logic.module.package.published');
  assert.equal(view.normalizeKind('logic-module'), 'logicmodule');
  assert.equal(view.normalizeKind('Connection'), view.normalizeKind('connection'));
});

test('a known action reads as a sentence that names the record', () => {
  const summary = view.describeAuditEvent(event({ targetName: 'ForeSite well test v1.2.0' }));
  assert.equal(summary.text, 'Logic module ForeSite well test v1.2.0 was approved');
  assert.deepEqual(summary.parts.find(part => part.strong), { text: 'ForeSite well test v1.2.0', strong: true });
  assert.equal(summary.outcome, null);
});

test('a record with no name is called by its short id, or by its own id when that is not a GUID', () => {
  assert.equal(view.describeAuditEvent(event({ action: 'CONFIGURED_LOGIC_CREATED', targetType: 'ConfiguredLogic',
    targetId: '387b9281-9fee-414f-beb3-034f99ed4f47' })).text, 'Logic 387b9281… was created');
  assert.equal(view.describeAuditEvent(event({ targetId: 'acme.sample@1.0.0' })).text,
    'Logic module acme.sample@1.0.0 was approved');
  assert.equal(view.describeAuditEvent(event({ action: 'security.grant.created', targetType: 'security-access-grant',
    targetId: id(7) })).text, 'An access grant was created');
});

test('a sentence without any target does not leave a double space behind', () => {
  assert.equal(view.describeAuditEvent(event({ targetId: null, targetName: null })).text, 'Logic module was approved');
});

test('a connection status change says what it is now and what it was', () => {
  const summary = view.describeAuditEvent(event({ action: 'connection.status_changed', targetType: 'connection',
    targetName: 'ForeSite', detailsJson: '{"AdapterKey":"foresite","From":"Healthy","To":"Unreachable"}' }));
  assert.equal(summary.text, 'Connection ForeSite status changed');
  assert.deepEqual(summary.outcome, { label: 'Unreachable', state: 'Unreachable' });
  assert.equal(summary.note, 'Was Healthy');
  const healthy = view.describeAuditEvent(event({ action: 'connection.status_changed', targetType: 'connection',
    targetName: 'ForeSite', detailsJson: '{"From":"Unreachable","To":"Healthy"}' }));
  assert.deepEqual(healthy.outcome, { label: 'Healthy', state: 'Healthy' });
  // A status change recorded without its new state still reads as a sentence, just without a status beside it.
  const bare = view.describeAuditEvent(event({ action: 'connection.status_changed', targetType: 'connection',
    targetName: 'ForeSite' }));
  assert.equal(bare.text, 'Connection ForeSite status changed');
  assert.equal(bare.outcome, null);
});

test('an action the page does not know yet reads as words with the record it touched', () => {
  assert.equal(view.describeAuditEvent(event({ action: 'future.thing_happened', targetName: 'Widget' })).text,
    'Future thing happened · Widget');
  assert.equal(view.describeAuditEvent(event({ action: 'EXECUTION_NEW_THING', targetId: null })).text, 'Execution new thing');
});

test('people show their display name with the sign-in name beneath; automatic events say so', () => {
  assert.deepEqual(view.describeActor(event({ actorName: 'Sri Mallepudi' })),
    { name: 'Sri Mallepudi', identity: 'WFT\\E206862', isSystem: false });
  assert.deepEqual(view.describeActor(event()), { name: 'WFT\\E206862', identity: null, isSystem: false });
  assert.deepEqual(view.describeActor(event({ actorUserId: null, actorIdentity: 'system:connection-monitor' })),
    { name: 'Connection monitor', identity: 'system:connection-monitor', isSystem: true });
  assert.equal(view.describeActor(event({ actorIdentity: '  ' })).name, 'Unknown person');
});

test('time is relative when recent and a date when old', () => {
  const now = Date.parse('2026-10-29T18:00:00Z');
  assert.equal(view.relativeTime('2026-10-29T17:59:40Z', now), 'Just now');
  assert.match(view.relativeTime('2026-10-29T17:55:00Z', now), /5 minutes ago/);
  assert.match(view.relativeTime('2026-10-29T15:00:00Z', now), /3 hours ago/);
  assert.match(view.relativeTime('2026-10-28T17:00:00Z', now), /yesterday/);
  assert.match(view.relativeTime('2026-09-01T12:00:00Z', now), /2026/);
  assert.equal(view.relativeTime('not a date', now), 'not a date');
});

test('recorded details get readable labels and the exact record stays available', () => {
  assert.deepEqual(view.readableDetails('{"AdapterKey":"foresite","revisionNumber":2}'),
    [{ label: 'Adapter key', value: 'foresite' }, { label: 'Revision number', value: '2' }]);
  assert.equal(view.recordedJson('{"a":1}'), '{\n  "a": 1\n}');
  assert.equal(view.recordedJson('not json'), 'not json');
});

test('every sentence template names its record unless the event has no single record', () => {
  const noRecord = ['security.grant.created', 'security.grant.deactivated', 'security.bootstrap.admin.assigned',
    'security.bootstrap.grants.assigned'];
  const known = ['connection.imported', 'connection.updated', 'connection.tested', 'configured.logic.created',
    'configured.logic.revision.created', 'configured.logic.revision.validated', 'configured.logic.revision.activated',
    'logic.module.registered', 'logic.module.approved', 'logic.module.retired', 'logic.module.package.uploaded',
    'logic.module.package.publication.queued', 'logic.module.package.published',
    'logic.module.package.publication.skipped', 'asset.created', 'asset.type.created', 'recommendation.approved',
    'recommendation.rejected', 'execution.shadow.requested', 'execution.result.conflict', 'security.role.created',
    'security.role.updated', 'security.role.assigned', 'security.user.provisioned', 'security.user.access.replaced'];
  for (const action of [...known, ...noRecord]) {
    const text = view.describeAuditEvent(event({ action, targetName: 'NAME_X' })).text;
    assert.equal(text.includes('NAME_X'), !noRecord.includes(action), `${action}: ${text}`);
    assert.ok(!text.includes('·'), `${action} fell back to the generic wording`);
  }
});

test('events are grouped under Today, Yesterday, then the date', () => {
  const now = new Date(2026, 9, 29, 15, 0, 0).getTime();
  const at = (days, hour) => new Date(2026, 9, 29 - days, hour, 0, 0).toISOString();
  assert.equal(view.dayKey(at(0, 9)), view.dayKey(at(0, 14)));
  assert.notEqual(view.dayKey(at(0, 9)), view.dayKey(at(1, 9)));
  assert.equal(view.dayLabel(at(0, 9), now), 'Today');
  assert.equal(view.dayLabel(at(1, 20), now), 'Yesterday');
  assert.match(view.dayLabel(at(3, 12), now), /Sunday|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday/);
});
