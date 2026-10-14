import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createServer } from 'vite';

let vite, alerts;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true },
    appType: 'custom', logLevel: 'silent' });
  alerts = await vite.ssrLoadModule('/src/views/operatorAlerts.ts');
});
after(async () => { await vite?.close(); });

const systemInfo = { components: [
  { name: 'Integration Gateway', status: 'Degraded' },
  { name: 'NATS Server', status: 'Unavailable' },
  { name: 'PostgreSQL', status: 'Healthy' },
  { name: 'Integration Gateway', status: 'Degraded' },
] };
const executions = [
  { executionId: '1', assetExternalId: 'WELL-01', moduleId: 'wfd.fill',
    status: 'Failed', failureCode: 'PROVIDER_TIMEOUT',
    completedAtUtc: '2026-10-14T10:10:00Z' },
  { executionId: '2', assetExternalId: 'WELL-02', moduleId: 'wfd.pressure',
    status: 'Completed', completedAtUtc: '2026-10-14T10:30:00Z' },
  { executionId: '3', moduleId: 'wfd.fill',
    status: 'TimedOut', completedAtUtc: '2026-10-14T10:20:00Z' },
];

test('alerts retain source authorization and do not invent additional records', () => {
  const result = alerts.collectOperatorAlerts(systemInfo, executions);
  assert.equal(result.length, 4);
  assert.deepEqual(result.map(x => x.id),
    ['runtime:NATS Server', 'runtime:Integration Gateway',
      'execution:3', 'execution:1']);
  assert.equal(result[0].kind, 'unavailable');
  assert.equal(result[1].kind, 'attention');
  assert.equal(result[2].label, 'wfd.fill');
  assert.equal(result[3].detail, 'wfd.fill · Failed · PROVIDER_TIMEOUT');
  assert.equal(executions.length, 3);
});

test('healthy components and unrelated execution statuses never create alerts', () => {
  assert.deepEqual(alerts.collectOperatorAlerts(
    { components: [{ name: 'Server', status: 'Healthy' }] },
    [{ executionId: 'x', status: 'Running' }, { executionId: 'y', status: 'Completed' }],
  ), []);
  assert.deepEqual(alerts.collectOperatorAlerts(null, []), []);
});

test('duplicate and malformed inputs are handled deterministically', () => {
  const repeated = [
    executions[0], executions[0],
    { executionId: null, status: 'Failed' },
    { executionId: '4', status: 'Suspended', requestedAtUtc: 'invalid' },
  ];
  assert.deepEqual(alerts.collectOperatorAlerts(null, repeated).map(x => x.id),
    ['execution:1', 'execution:4']);
});

test('only the first twenty recent authorized alerts enter the panel', () => {
  const many = Array.from({ length: 45 }, (_, i) => ({
    executionId: String(i), status: 'Failed',
    requestedAtUtc: new Date(Date.UTC(2026, 9, 14, 0, i)).toISOString(),
  }));
  const items = alerts.collectOperatorAlerts(null, many);
  assert.equal(items.length, 20);
  assert.equal(items[0].id, 'execution:44');
  assert.equal(items[19].id, 'execution:25');
});
