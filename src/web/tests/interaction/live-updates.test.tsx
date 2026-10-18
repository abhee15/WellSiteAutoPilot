import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { App } from '../../src/App';
import { LiveStatus } from '../../src/components/LiveStatus';
import { ChangeSignalClient } from '../../src/live/changeSignal';
import type { HubConnectionLike } from '../../src/live/changeSignal';
import { LiveUpdatesProvider, useChangeRefresh, usePollingRefresh } from '../../src/live/LiveUpdates';
import { t } from '../../src/localization';

const userId = '99999999-9999-4999-8999-999999999999';
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });

type FakeHub = HubConnectionLike & {
  settle(fail?: boolean): void; emit(area: unknown): void; drop(): void; stops: number;
};

/** A hub whose start() the test settles by hand, so connection state is fully controlled. */
function liveHarness() {
  const hubs: FakeHub[] = [];
  const client = new ChangeSignalClient(() => {
    let onChanged: (...args: unknown[]) => void = () => undefined;
    let onClosed: (error?: Error) => void = () => undefined;
    let settle: (fail?: boolean) => void = () => undefined;
    const hub: FakeHub = {
      stops: 0,
      start: () => new Promise<void>((resolve, reject) => { settle = fail => (fail ? reject(new Error('down')) : resolve()); }),
      stop: () => { hub.stops += 1; return Promise.resolve(); },
      on: (_method, handler) => { onChanged = handler; },
      onclose: handler => { onClosed = handler; },
      onreconnecting: () => undefined,
      onreconnected: () => undefined,
      settle: fail => settle(fail),
      emit: area => onChanged(area),
      drop: () => onClosed(new Error('closed')),
    };
    hubs.push(hub);
    return hub;
  }, { set: () => 0, clear: () => undefined });
  return { client, hub: () => hubs[hubs.length - 1]! };
}

function Probe({ onPoll, onAssets }: { onPoll: () => void; onAssets: () => void }) {
  usePollingRefresh(onPoll);
  useChangeRefresh(['assets'], onAssets);
  return <LiveStatus />;
}

afterEach(() => {
  cleanup(); vi.useRealTimers(); vi.unstubAllGlobals(); vi.restoreAllMocks(); window.location.hash = ''; localStorage.clear();
});

/** Fake only the polling timers: React's act and Testing Library's waitFor need the rest real. */
const fakePollingTimers = () => vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });

it('polls every 10 seconds until connected, then about once a minute, and falls back again', async () => {
  fakePollingTimers();
  const { client, hub } = liveHarness();
  const onPoll = vi.fn(); const onAssets = vi.fn();
  render(<LiveUpdatesProvider client={client}><Probe onPoll={onPoll} onAssets={onAssets} /></LiveUpdatesProvider>);
  expect(screen.queryByRole('status')).toBeNull();

  await act(async () => { vi.advanceTimersByTime(10_000); });
  expect(onPoll).toHaveBeenCalledTimes(1);

  await act(async () => { client.setEnabled(true); });
  expect(screen.getByRole('status').textContent).toContain(t('live.connecting'));
  await act(async () => { hub().settle(); });
  expect(screen.getByRole('status').textContent).toContain(t('live.connected'));
  expect(onAssets).toHaveBeenCalledTimes(1); // catch-up after connecting
  onPoll.mockClear();
  await act(async () => { vi.advanceTimersByTime(59_000); });
  expect(onPoll).not.toHaveBeenCalled();
  await act(async () => { vi.advanceTimersByTime(1_000); });
  expect(onPoll).toHaveBeenCalledTimes(1);

  await act(async () => { hub().emit('assets'); hub().emit('executions'); });
  expect(onAssets).toHaveBeenCalledTimes(2); // only the subscribed area

  await act(async () => { hub().drop(); });
  expect(screen.getByRole('status').textContent).toContain(t('live.polling', { seconds: 10 }));
  onPoll.mockClear();
  await act(async () => { vi.advanceTimersByTime(10_000); });
  expect(onPoll).toHaveBeenCalledTimes(1);
});

it('a hub that cannot start leaves the fallback in place with no error text', async () => {
  const { client, hub } = liveHarness();
  render(<LiveUpdatesProvider client={client}><Probe onPoll={() => undefined} onAssets={() => undefined} /></LiveUpdatesProvider>);
  await act(async () => { client.setEnabled(true); });
  await act(async () => { hub().settle(true); });
  const status = screen.getByRole('status');
  expect(status.textContent).toBe(t('live.polling', { seconds: 10 }));
  expect(status.getAttribute('data-state')).toBe('disconnected');
  expect(screen.queryByRole('alert')).toBeNull();
});

function stubApi(counts: { executions: number }) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === '/api/v1/security/me') {
      return response({ userId, identityName: 'FIELD\\live', isActive: true, permissions: ['executions.read'] });
    }
    if (url.startsWith('/api/v1/executions')) { counts.executions += 1; return response([]); }
    if (url.includes('system/info')) return response({});
    return response([]);
  }));
}

/** Records every polling interval the page asks for, while still running it. */
function recordIntervals() {
  const delays: number[] = [];
  const real = window.setInterval.bind(window);
  vi.spyOn(window, 'setInterval').mockImplementation(((handler: TimerHandler, delay?: number, ...args: unknown[]) => {
    if (typeof delay === 'number') delays.push(delay);
    return real(handler, delay, ...args);
  }) as typeof window.setInterval);
  return delays;
}

it('refetches the workspace when the Server names an area it shows, and ignores others', { timeout: 30_000 }, async () => {
  const counts = { executions: 0 };
  stubApi(counts);
  const delays = recordIntervals();
  const { client, hub } = liveHarness();
  window.location.hash = '#operations';
  render(<LiveUpdatesProvider client={client}><App /></LiveUpdatesProvider>);
  await waitFor(() => expect(counts.executions).toBe(1));
  // The hub is only started once a session is verified, and until then polling stays at 10 s.
  await waitFor(() => expect(hub()).toBeDefined());
  expect(delays).toContain(10_000);
  expect(delays).not.toContain(60_000);
  await act(async () => { hub().settle(); });
  await waitFor(() => expect(screen.getByRole('status').textContent).toContain(t('live.connected')));
  await waitFor(() => expect(counts.executions).toBe(2)); // catch-up refetch on connect
  expect(delays).toContain(60_000);

  await act(async () => { hub().emit('executions'); });
  await waitFor(() => expect(counts.executions).toBe(3));
  await act(async () => { hub().emit('recommendations'); });
  await new Promise(resolve => setTimeout(resolve, 100));
  expect(counts.executions).toBe(3);

  // Losing the connection returns to the 10-second fallback.
  delays.length = 0;
  await act(async () => { hub().drop(); });
  await waitFor(() => expect(screen.getByRole('status').textContent).toBe(t('live.polling', { seconds: 10 })));
  expect(delays).toContain(10_000);
});

it('without the live-update provider the page polls every 10 seconds and shows no connection state', { timeout: 30_000 }, async () => {
  const counts = { executions: 0 };
  stubApi(counts);
  const delays = recordIntervals();
  window.location.hash = '#operations';
  render(<App />);
  await waitFor(() => expect(counts.executions).toBe(1));
  expect(screen.queryByRole('status')).toBeNull();
  expect(delays).toContain(10_000);
  expect(delays).not.toContain(60_000);
});
