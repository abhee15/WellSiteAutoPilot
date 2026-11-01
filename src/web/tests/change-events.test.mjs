import assert from 'node:assert/strict';
import { test, before, after } from 'node:test';
import { setImmediate } from 'node:timers';
import { createServer } from 'vite';

let vite; let signal; let apply; let notes;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  signal = await vite.ssrLoadModule('/src/live/changeSignal.ts');
  apply = await vite.ssrLoadModule('/src/live/eventApply.ts');
  notes = await vite.ssrLoadModule('/src/live/notifications.ts');
});
after(async () => { await vite.close(); });

const settle = () => new Promise(resolve => setImmediate(resolve));
const wire = (sequence, over = {}) => ({
  v: 1, epoch: 'e1', area: 'executions', sequence, entity: 'execution', id: 'run-1', kind: 'started',
  occurredAt: '2026-01-01T00:00:00Z', actor: null, correlationId: null, payload: {}, ...over,
});

async function connected() {
  const handlers = {}; const clock = { at: 0 };
  const hub = {
    start: () => Promise.resolve(), stop: () => Promise.resolve(),
    on: (method, handler) => { handlers[method] = handler; },
    onclose() {}, onreconnecting() {}, onreconnected() {},
  };
  const client = new signal.ChangeSignalClient(() => hub, { set: () => 0, clear() {} }, [5000], () => clock.at);
  const events = []; const areaCalls = []; const guarded = [];
  client.subscribeEvents(event => events.push(event));
  client.subscribe(['executions'], () => areaCalls.push(clock.at));
  client.subscribe(['executions'], () => guarded.push(clock.at), { skipWhenCovered: true });
  client.setEnabled(true); await settle();
  areaCalls.length = 0; guarded.length = 0;
  return { client, handlers, events, areaCalls, guarded, clock };
}

test('an event is parsed strictly and a malformed one is ignored', async () => {
  assert.equal(signal.parseChangeEvent({ ...wire(0) }), null);
  assert.equal(signal.parseChangeEvent({ ...wire(1), area: 'security' }), null);
  assert.equal(signal.parseChangeEvent(null), null);
  assert.equal(signal.parseChangeEvent(wire(1, { payload: { status: 'Running', n: 3 } })).payload.n, undefined);
  const h = await connected();
  h.handlers.event({ nonsense: true });
  assert.equal(h.events.length, 0);
});

test('in-order events are delivered once; the first after a connect is the baseline', async () => {
  const h = await connected();
  for (const sequence of [7, 8, 8, 9]) h.handlers.event(wire(sequence));
  assert.deepEqual(h.events.map(event => event.sequence), [7, 8, 9]);
  assert.equal(h.areaCalls.length, 0);
});

test('a gap or a new epoch is not delivered as an event but refetches the area', async () => {
  const h = await connected();
  h.handlers.event(wire(1)); h.handlers.event(wire(3));
  assert.deepEqual(h.events.map(event => event.sequence), [1]);
  assert.equal(h.areaCalls.length, 1);
  h.handlers.event(wire(1, { epoch: 'e2' }));
  assert.equal(h.events.length, 1);
  assert.equal(h.areaCalls.length, 2);
});

test('a covered area signal skips only subscriptions that asked for it, and only briefly', async () => {
  const h = await connected();
  h.handlers.event(wire(1));
  h.clock.at = 250; h.handlers.changed('executions');
  assert.equal(h.areaCalls.length, 1);
  assert.equal(h.guarded.length, 0);
  h.clock.at = signal.EVENT_COVERS_SIGNAL_MS + 1000; h.handlers.changed('executions');
  assert.equal(h.guarded.length, 1);
});

const row = (executionId, status) => ({ executionId, status });

test('run events patch the list in place, prepend is for unknown runs only, finished runs stay finished', () => {
  const list = [row('a', 'Requested'), row('b', 'Running')];
  const started = apply.applyExecutionEvent(list, signal.parseChangeEvent(wire(1, { id: 'a' })));
  assert.deepEqual(started.map(r => r.status), ['Running', 'Running']);
  assert.equal(apply.applyExecutionEvent(list, signal.parseChangeEvent(wire(2, { id: 'zzz' }))), 'unknown');
  const done = apply.applyExecutionEvent(list, signal.parseChangeEvent(wire(3, { id: 'b', kind: 'completed' })));
  assert.equal(done[1].status, 'Completed');
  const late = apply.applyExecutionEvent(done, signal.parseChangeEvent(wire(4, { id: 'b', kind: 'started' })));
  assert.equal(late[1].status, 'Completed');
  assert.equal(apply.applyExecutionEvent(list, signal.parseChangeEvent(wire(5, { area: 'recommendations', entity: 'recommendation' }))), list);
});

test('notifications follow permissions, deduplicate, resolve on a decision and cap the list', () => {
  const failed = signal.parseChangeEvent(wire(1, { kind: 'failed', payload: { code: 'timeout' } }));
  const created = signal.parseChangeEvent(wire(2, { area: 'recommendations', entity: 'recommendation', id: 'rec-1', kind: 'created' }));
  assert.equal(notes.notificationFor(failed, []), null);
  assert.equal(notes.notificationFor(failed, ['executions.read']).code, 'timeout');
  assert.equal(notes.notificationFor(created, ['executions.read']), null);
  const item = notes.notificationFor(created, ['recommendations.decide']);
  let state = notes.notificationReducer(notes.EMPTY_NOTIFICATIONS, { type: 'add', item });
  state = notes.notificationReducer(state, { type: 'add', item });
  assert.equal(state.items.length, 1);
  const decided = signal.parseChangeEvent(wire(3, { area: 'recommendations', entity: 'recommendation', id: 'rec-1', kind: 'decided' }));
  state = notes.notificationReducer(state, { type: 'resolve', id: notes.resolvedNotificationId(decided) });
  assert.equal(state.items.length, 0);
  for (let i = 0; i < 60; i += 1) state = notes.notificationReducer(state, { type: 'add', item: { ...item, id: `x${i}` } });
  assert.equal(state.items.length, notes.MAX_NOTIFICATIONS);
});
