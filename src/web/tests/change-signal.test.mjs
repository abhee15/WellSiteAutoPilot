import assert from 'node:assert/strict';
import { test, before, after } from 'node:test';
import { setImmediate } from 'node:timers';
import { createServer } from 'vite';

let vite; let signal;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  signal = await vite.ssrLoadModule('/src/live/changeSignal.ts');
});
after(async () => { await vite.close(); });

/** A hand-driven HubConnection: the test decides when start() settles and what the server sends. */
function fakeHub() {
  const hub = {
    started: 0, stopped: 0, handlers: {}, closed: [], reconnecting: [], reconnected: [],
    pending: [],
    start() { hub.started += 1; return new Promise((resolve, reject) => hub.pending.push({ resolve, reject })); },
    stop() { hub.stopped += 1; return Promise.resolve(); },
    on(method, handler) { hub.handlers[method] = handler; },
    onclose(handler) { hub.closed.push(handler); },
    onreconnecting(handler) { hub.reconnecting.push(handler); },
    onreconnected(handler) { hub.reconnected.push(handler); },
    emit(...args) { hub.handlers.changed(...args); },
  };
  return hub;
}

function harness({ failFactory = false } = {}) {
  const hubs = []; const timers = []; const cleared = [];
  const client = new signal.ChangeSignalClient(() => {
    if (failFactory) throw new Error('no hub');
    const hub = fakeHub(); hubs.push(hub); return hub;
  }, {
    set: (callback, delay) => { const handle = { callback, delay }; timers.push(handle); return handle; },
    clear: handle => { cleared.push(handle); },
  }, [5000, 15000]);
  const states = []; client.onStateChange(() => states.push(client.state));
  return { client, hubs, timers, cleared, states, last: () => hubs[hubs.length - 1] };
}
const settle = () => new Promise(resolve => setImmediate(resolve));

test('polling only slows down while the hub is connected', () => {
  assert.equal(signal.pollIntervalMs('connected'), 60_000);
  for (const state of ['idle', 'connecting', 'reconnecting', 'disconnected']) {
    assert.equal(signal.pollIntervalMs(state), 10_000, state);
  }
});

test('only the five known area names are accepted', () => {
  for (const area of ['executions', 'recommendations', 'configured-logic', 'assets', 'modules']) {
    assert.equal(signal.isChangeArea(area), true, area);
  }
  for (const value of ['Executions', 'security', '', null, undefined, 3, { area: 'assets' }]) {
    assert.equal(signal.isChangeArea(value), false, String(value));
  }
});

test('does nothing until enabled, then connects and reports the state', async () => {
  const { client, hubs, states } = harness();
  assert.equal(client.state, 'idle');
  assert.equal(hubs.length, 0);
  client.setEnabled(true);
  assert.equal(client.state, 'connecting');
  hubs[0].pending[0].resolve(); await settle();
  assert.equal(client.state, 'connected');
  assert.deepEqual(states, ['connecting', 'connected']);
});

test('a signal runs only the subscribers of that area, each once, and ignores unknown names', async () => {
  const { client, last } = harness();
  const calls = [];
  client.subscribe(['assets', 'modules'], () => calls.push('workspace'));
  client.subscribe(['recommendations'], () => calls.push('inbox'));
  client.setEnabled(true);
  last().pending[0].resolve(); await settle();
  // A fresh connection refetches everything once: signals sent while offline were lost.
  assert.deepEqual(calls.splice(0), ['workspace', 'inbox']);
  last().emit('assets');
  assert.deepEqual(calls.splice(0), ['workspace']);
  last().emit('recommendations');
  assert.deepEqual(calls.splice(0), ['inbox']);
  last().emit('executions'); last().emit('security'); last().emit(); last().emit(7, 'assets');
  assert.deepEqual(calls, []);
});

test('an unsubscribed or throwing handler never blocks the others', async () => {
  const { client, last } = harness();
  const calls = [];
  client.subscribe(['assets'], () => { throw new Error('refetch failed'); });
  const off = client.subscribe(['assets'], () => calls.push('removed'));
  client.subscribe(['assets'], () => calls.push('kept'));
  off();
  client.setEnabled(true);
  last().pending[0].resolve(); await settle();
  calls.length = 0;
  last().emit('assets');
  assert.deepEqual(calls, ['kept']);
});

test('a hub that cannot start falls back to polling and retries slowly, with backoff', async () => {
  const { client, hubs, timers } = harness();
  client.setEnabled(true);
  hubs[0].pending[0].reject(new Error('404')); await settle();
  assert.equal(client.state, 'disconnected');
  assert.equal(hubs[0].stopped, 1);
  assert.equal(timers.length, 1);
  assert.equal(timers[0].delay, 5000);
  // The background retry keeps showing the fallback state instead of flickering.
  timers[0].callback();
  assert.equal(client.state, 'disconnected');
  assert.equal(hubs.length, 2);
  hubs[1].pending[0].reject(new Error('404')); await settle();
  assert.equal(timers[1].delay, 15000);
  timers[1].callback();
  hubs[2].pending[0].reject(new Error('404')); await settle();
  assert.equal(timers[2].delay, 15000, 'the last delay repeats');
  timers[2].callback();
  hubs[3].pending[0].resolve(); await settle();
  assert.equal(client.state, 'connected');
});

test('a factory that throws degrades to polling without raising', async () => {
  const { client, timers } = harness({ failFactory: true });
  client.setEnabled(true);
  await settle();
  assert.equal(client.state, 'disconnected');
  assert.equal(timers.length, 1);
});

test('reconnecting and reconnected follow the library, and a reconnect refetches once', async () => {
  const { client, last } = harness();
  let refetches = 0;
  client.subscribe(['executions'], () => { refetches += 1; });
  client.setEnabled(true);
  last().pending[0].resolve(); await settle();
  refetches = 0;
  last().reconnecting[0](new Error('lost'));
  assert.equal(client.state, 'reconnecting');
  assert.equal(refetches, 0);
  last().reconnected[0]('id');
  assert.equal(client.state, 'connected');
  assert.equal(refetches, 1);
});

test('a closed connection (for example after a permission change) falls back and retries', async () => {
  const { client, hubs, timers } = harness();
  client.setEnabled(true);
  hubs[0].pending[0].resolve(); await settle();
  hubs[0].closed[0](new Error('server closed'));
  assert.equal(client.state, 'disconnected');
  assert.equal(timers.length, 1);
  timers[0].callback();
  hubs[1].pending[0].resolve(); await settle();
  assert.equal(client.state, 'connected');
});

test('disabling stops the connection, cancels the retry and ignores late callbacks', async () => {
  const { client, hubs, timers, cleared } = harness();
  client.setEnabled(true);
  hubs[0].pending[0].reject(new Error('down')); await settle();
  assert.equal(timers.length, 1);
  client.setEnabled(false);
  assert.equal(client.state, 'idle');
  assert.deepEqual(cleared, [timers[0]]);
  timers[0].callback();
  assert.equal(hubs.length, 1, 'a cancelled retry must not reconnect');

  client.setEnabled(true);
  const second = hubs[1];
  client.setEnabled(false);
  assert.equal(second.stopped, 1);
  second.pending[0].resolve(); await settle();
  assert.equal(client.state, 'idle', 'a start that finishes after disabling stays idle');
  assert.equal(second.stopped, 2, 'and its connection is closed again');
  let ran = false;
  client.subscribe(['assets'], () => { ran = true; });
  second.emit('assets');
  assert.equal(ran, false, 'a stale connection can no longer trigger refetches');
});

test('state listeners hear each change once and can unsubscribe', async () => {
  const { client, hubs } = harness();
  let heard = 0;
  const off = client.onStateChange(() => { heard += 1; });
  client.setEnabled(true);
  hubs[0].pending[0].resolve(); await settle();
  assert.equal(heard, 2);
  off();
  client.setEnabled(false);
  assert.equal(heard, 2);
});
