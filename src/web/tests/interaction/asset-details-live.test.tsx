import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AssetDetail } from '../../src/views/AssetDetail';
import { OperationsPage } from '../../src/views/OperationsPage';
import { LiveUpdatesProvider } from '../../src/live/LiveUpdates';
import { ChangeSignalClient } from '../../src/live/changeSignal';
import type { HubConnectionLike } from '../../src/live/changeSignal';
import { accessibilityViolations } from './axe';
import { parseRunHistory, runHistoryHash } from '../../src/views/assetActivity';

vi.mock('../../src/views/ExecutionDetailPanel', () => ({
  ExecutionDetailPanel: ({ executionId }: { executionId: string }) => <p>Inspecting {executionId}</p>,
}));

const guid = (n: number) => `77777777-7777-4777-8777-${String(n).padStart(12, '0')}`;
const assetId = guid(1), logicId = guid(2);
const response = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status });
const asset = { id: assetId, name: 'Well 7', isActive: true };
const configuration = { id: logicId, name: 'Fillage control', activeRevisionId: guid(3), revisions: [
  { id: guid(3), status: 'Active', revisionNumber: 1, moduleId: 'fillage', moduleVersion: '1.0', mode: 'Shadow',
    schedule: { enabled: true }, assetBindings: [{ assetId }] },
] };
const summary = { isGroupingNode: false, items: [{ configuration, nextScheduledAtUtc: '2026-11-01T10:00:00Z' }], hasMore: false };
const run = (id: number, status = 'Running') => ({ executionId: guid(id), status, requestedAtUtc: '2026-11-01T09:00:00Z' });
const props = { asset, assets: [asset], typeName: 'Well', configuredLogic: [], canReadLogic: true,
  canReadRuns: true, canReadRecommendations: true, canConfigureLogic: true, onSelect: vi.fn(), onClose: vi.fn() };

function liveHarness() {
  let emit: (...args: unknown[]) => void = () => undefined;
  let reconnected: () => void = () => undefined;
  const client = new ChangeSignalClient(() => ({
    start: () => Promise.resolve(), stop: () => Promise.resolve(),
    on: (_method, handler) => { emit = handler; }, onclose: () => undefined,
    onreconnecting: () => undefined, onreconnected: handler => { reconnected = handler; },
  } as HubConnectionLike), { set: () => 0, clear: () => undefined });
  return { client, emit: (area: string) => emit(area), reconnect: () => reconnected() };
}

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.useRealTimers(); window.location.hash = ''; });

it('groups two scoped runs, survives a recommendation failure and keeps focus during live refresh and reconnect', async () => {
  const live = liveHarness();
  let runs = [run(10), run(9, 'Completed')];
  let block: ((value: Response) => void) | null = null;
  let deferred = false;
  const fetcher = vi.fn(async (url: string) => {
    if (url.includes('/logic?')) return response(summary);
    if (url.includes('/recommendations?')) return response({}, 503);
    if (url.includes('/executions?')) {
      const params = new URL(url, 'http://localhost').searchParams;
      expect(params.get('assetId')).toBe(assetId);
      expect(params.get('configuredLogicId')).toBe(logicId);
      expect(params.get('limit')).toBe('2');
      if (deferred) return new Promise<Response>(resolve => { block = resolve; });
      return response(runs);
    }
    return response({});
  });
  vi.stubGlobal('fetch', fetcher);
  const scroll = vi.fn();
  HTMLElement.prototype.scrollIntoView = scroll;
  render(<LiveUpdatesProvider client={live.client}><AssetDetail {...props} /></LiveUpdatesProvider>);
  await act(async () => live.client.setEnabled(true));
  const card = await screen.findByRole('article', { name: 'Fillage control' });
  await within(card).findByText('Running');
  expect(within(card).getAllByRole('listitem')).toHaveLength(2);
  expect(await screen.findByText(/Recommendation updates unavailable/)).toBeTruthy();
  expect(screen.getByText(/Next scheduled:/)).toBeTruthy();
  const history = within(card).getByRole('link', { name: /View history for/ });
  expect(history.getAttribute('href')).toBe(runHistoryHash(assetId, logicId));
  history.focus();
  const scrollCount = scroll.mock.calls.length;
  deferred = true;
  act(() => live.emit('executions'));
  expect(within(card).getByText('Running')).toBeTruthy();
  expect(within(card).queryByRole('progressbar')).toBeNull();
  await act(async () => { block!(response([run(11, 'Starting'), run(10, 'Completed')])); });
  await within(card).findByText('Starting');
  expect(document.activeElement).toBe(history);
  expect(scroll).toHaveBeenCalledTimes(scrollCount);
  deferred = false;
  runs = [run(11, 'Completed'), run(10, 'Completed')];
  act(() => live.reconnect());
  await waitFor(() => expect(within(card).getAllByText('Completed')).toHaveLength(2));
  expect(within(card).getAllByRole('listitem')).toHaveLength(2);
  delete (HTMLElement.prototype as Partial<HTMLElement>).scrollIntoView;
});

it('shows grouping-node details without requesting runs or recommendations or offering logic', async () => {
  const fetcher = vi.fn(async () => response({ isGroupingNode: true, items: [], hasMore: false }));
  vi.stubGlobal('fetch', fetcher);
  render(<AssetDetail {...props} />);
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(1));
  expect(screen.queryByRole('article')).toBeNull();
  expect(screen.queryByText('Logic and recent runs')).toBeNull();
  expect(screen.queryByRole('link', { name: /Put logic/ })).toBeNull();
  expect(screen.getByText('Technical details')).toBeTruthy();
});

it('retains runs with a local warning on transient failure and clears them when access is denied', async () => {
  const live = liveHarness();
  let status = 200;
  vi.stubGlobal('fetch', vi.fn(async (url: string) => url.includes('/logic?') ? response(summary)
    : url.includes('/executions?') ? response([run(10)], status) : response([])));
  render(<LiveUpdatesProvider client={live.client}><AssetDetail {...props} /></LiveUpdatesProvider>);
  await act(async () => live.client.setEnabled(true));
  await screen.findByText('Running');
  status = 503;
  act(() => live.emit('executions'));
  await screen.findByText(/Run updates unavailable/);
  expect(screen.getByText('Running')).toBeTruthy();
  status = 403;
  act(() => live.emit('executions'));
  await waitFor(() => expect(screen.queryByText('Running')).toBeNull());
});

it('ignores an obsolete asset response after selection changes', async () => {
  let old: ((value: Response) => void) | null = null;
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url.includes(`/assets/${assetId}/logic`)) return new Promise<Response>(resolve => { old = resolve; });
    return response({ isGroupingNode: true, items: [], hasMore: false });
  }));
  const view = render(<AssetDetail {...props} />);
  view.rerender(<AssetDetail {...props} asset={{ ...asset, id: guid(99), name: 'Device type' }} />);
  await act(async () => { old!(response(summary)); });
  expect(screen.getByText('Device type')).toBeTruthy();
  expect(screen.queryByText('Fillage control')).toBeNull();
});

it('pages configurations on the server and keeps the add-logic action', async () => {
  const fetcher = vi.fn(async (url: string) => {
    if (url.includes('/logic?')) return response({ ...summary, hasMore: url.includes('offset=0') });
    return response([]);
  });
  vi.stubGlobal('fetch', fetcher);
  render(<AssetDetail {...props} />);
  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'More logic' }));
  await screen.findByRole('button', { name: 'Previous logic' });
  expect(fetcher.mock.calls.some(([url]) => url.includes('offset=5&limit=5'))).toBe(true);
  expect(screen.getByRole('link', { name: /Put logic/ })).toBeTruthy();
});

it('opens scoped history, pages older runs and preserves the inspected run through live replacement', async () => {
  const live = liveHarness();
  window.location.hash = runHistoryHash(assetId, logicId);
  let latest = [run(10), run(9)];
  const fetcher = vi.fn(async (url: string) => {
    expect(url).toContain('/executions/history?');
    const params = new URL(url, 'http://localhost').searchParams;
    expect(params.get('assetId')).toBe(assetId);
    expect(params.get('configuredLogicId')).toBe(logicId);
    return response(params.has('after') ? { items: [run(8)], nextCursor: null } : { items: latest, nextCursor: guid(9) });
  });
  vi.stubGlobal('fetch', fetcher);
  render(<LiveUpdatesProvider client={live.client}><OperationsPage executions={[]} userId="reader" /></LiveUpdatesProvider>);
  await act(async () => live.client.setEnabled(true));
  const inspect = await screen.findAllByRole('button', { name: /Inspect run/ });
  await userEvent.click(inspect[0]!);
  expect(screen.getByText('Inspecting ' + guid(10))).toBeTruthy();
  latest = [run(12), run(11)];
  act(() => live.emit('executions'));
  await waitFor(() => expect(fetcher.mock.calls.length).toBeGreaterThan(2));
  expect(screen.getByText('Inspecting ' + guid(10))).toBeTruthy();
  await userEvent.click(screen.getByRole('button', { name: 'Older runs' }));
  await screen.findByRole('button', { name: 'Newest runs' });
  expect(fetcher.mock.calls.some(([url]) => url.includes('after=' + guid(9)))).toBe(true);
});

it('does not interpret malformed history addresses as asset/configuration filters', () => {
  expect(parseRunHistory('history?assetId=x&configuredLogicId=y')).toBeNull();
  expect(parseRunHistory('history?assetId=' + assetId)).toBeNull();
});

it('keeps the grouped details accessible and retains run visibility for run-only readers', async () => {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => url.includes('/logic?') ? response(summary) : url.includes('/executions?') ? response([run(10)]) : response([])));
  const view = render(<AssetDetail {...props} canReadLogic={false} />);
  await screen.findByText('Running');
  expect(screen.queryByRole('article')).toBeNull();
  expect(await accessibilityViolations(view.container)).toEqual([]);
  view.rerender(<AssetDetail {...props} />);
  await screen.findByRole('article');
  expect(await accessibilityViolations(view.container)).toEqual([]);
});

it('refreshes the two-run preview through polling when SignalR is unavailable', async () => {
  vi.useFakeTimers();
  let status = 'Running';
  vi.stubGlobal('fetch', vi.fn(async (url: string) => url.includes('/logic?') ? response(summary)
    : url.includes('/executions?') ? response([run(10, status)]) : response([])));
  await act(async () => { render(<AssetDetail {...props} />); });
  expect(screen.getByText('Running')).toBeTruthy();
  status = 'Completed';
  await act(async () => { await vi.advanceTimersByTimeAsync(10_000); });
  expect(screen.getByText('Completed')).toBeTruthy();
});
