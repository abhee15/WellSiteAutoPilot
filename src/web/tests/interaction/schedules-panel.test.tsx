import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SchedulesPanel } from '../../src/views/SchedulesPanel';
import { ChangeSignalClient } from '../../src/live/changeSignal';
import type { HubConnectionLike } from '../../src/live/changeSignal';
import { LiveUpdatesProvider } from '../../src/live/LiveUpdates';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

const row = (over: Record<string, unknown> = {}) => ({
  configuredLogicId: 'c1', name: 'Hole cleaning', activeRevisionId: 'r1', revisionNumber: 2, mode: 'ScheduledOneShot',
  targets: [{ assetId: 'a1', name: 'Well 7' }], enabled: true, cadenceSeconds: 10, effectiveCadenceSeconds: 15,
  minimumCadenceSeconds: 15, startAtUtc: '2026-01-01T00:00:00Z', nextRunAtUtc: '2026-01-01T00:01:00Z', running: false,
  lastRun: { executionId: 'e1', scheduledForUtc: '2026-01-01T00:00:45Z', status: 'Completed', resultCode: 'OK' },
  canManage: true, overlapPolicy: 'skip-if-running', ...over,
});

function hubHarness() {
  let emit: (area: unknown) => void = () => undefined;
  let settle: () => void = () => undefined;
  const hub: HubConnectionLike = {
    start: () => new Promise<void>(resolve => { settle = resolve; }),
    stop: () => Promise.resolve(),
    on: (_method, handler) => { emit = handler; },
    onclose: () => undefined, onreconnecting: () => undefined, onreconnected: () => undefined,
  };
  const client = new ChangeSignalClient(() => hub, { set: () => 0, clear: () => undefined });
  return { client, emit: (area: unknown) => emit(area), connect: () => settle() };
}

const renderPanel = (canManage: boolean, client = hubHarness().client) =>
  render(<LiveUpdatesProvider client={client}><SchedulesPanel canManage={canManage} /></LiveUpdatesProvider>);

it('shows stored and effective frequency, the minimum and the last result read-only', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify([row()]))));
  renderPanel(false);
  expect(await screen.findByText('Hole cleaning')).toBeTruthy();
  expect(screen.getByText('Every 10 s')).toBeTruthy();
  expect(screen.getByText('Runs every 15 s because of the provider minimum')).toBeTruthy();
  expect(screen.getByText('Minimum 15 s')).toBeTruthy();
  expect(screen.getByText('OK')).toBeTruthy();
  expect(screen.getByText('You can view schedules but not change them.')).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Pause' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Change frequency' })).toBeNull();
});

it('offers editing only when the user and the row both allow it', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify([row(), row({ configuredLogicId: 'c2', name: 'Locked', canManage: false })]))));
  renderPanel(true);
  await screen.findByText('Locked');
  expect(screen.getAllByRole('button', { name: 'Pause' })).toHaveLength(1);
  expect(screen.getAllByRole('button', { name: 'Change frequency' })).toHaveLength(1);
});

it('refetches on configured-logic and executions changes only', async () => {
  const harness = hubHarness();
  let name = 'Hole cleaning';
  const fetchMock = vi.fn(async () => new Response(JSON.stringify([row({ name })])));
  vi.stubGlobal('fetch', fetchMock);
  renderPanel(false, harness.client);
  await screen.findByText('Hole cleaning');
  await act(async () => { harness.client.setEnabled(true); harness.connect(); });
  const before = fetchMock.mock.calls.length;

  name = 'Renamed';
  await act(async () => { harness.emit('control'); });
  expect(fetchMock.mock.calls.length).toBe(before);

  await act(async () => { harness.emit('configured-logic'); });
  await screen.findByText('Renamed');

  name = 'After run';
  await act(async () => { harness.emit('executions'); });
  await screen.findByText('After run');
});

it('sends the active revision as If-Match and explains a below-minimum refusal', async () => {
  const calls: { url: string; init?: RequestInit }[] = [];
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    calls.push({ url: String(url), init });
    if (init?.method === 'PUT') return new Response(JSON.stringify({ code: 'CONFIGURED_LOGIC_SCHEDULE_BELOW_MINIMUM' }), { status: 400 });
    return new Response(JSON.stringify([row({ cadenceSeconds: 30, effectiveCadenceSeconds: 30 })]));
  }));
  renderPanel(true);
  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'Change frequency' }));
  const input = screen.getByLabelText('Seconds between runs');
  await user.clear(input);
  await user.type(input, '5');
  await user.click(screen.getByRole('button', { name: 'Save' }));
  expect(await screen.findByText('The frequency must be at least 15 s for the providers this configuration reads.')).toBeTruthy();
  const put = calls.find(call => call.init?.method === 'PUT');
  expect(put?.url).toBe('/api/v1/configured-logic/c1/schedule');
  expect((put?.init?.headers as Record<string, string>)['If-Match']).toBe('"r1"');
  expect(put?.init?.body).toBe(JSON.stringify({ cadenceSeconds: 5 }));
});

it('pauses and resumes through the dedicated routes', async () => {
  let enabled = true;
  const calls: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') { calls.push(String(url)); enabled = !enabled; return new Response(JSON.stringify(row({ enabled }))); }
    return new Response(JSON.stringify([row({ enabled })]));
  }));
  renderPanel(true);
  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'Pause' }));
  await user.click(await screen.findByRole('button', { name: 'Resume' }));
  expect(calls).toEqual(['/api/v1/configured-logic/c1/schedule/pause', '/api/v1/configured-logic/c1/schedule/resume']);
});
