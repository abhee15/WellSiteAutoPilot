import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen } from '@testing-library/react';
import { AutonomousControlPanel } from '../../src/views/AutonomousControlPanel';
import { ChangeSignalClient } from '../../src/live/changeSignal';
import type { HubConnectionLike } from '../../src/live/changeSignal';
import { LiveUpdatesProvider } from '../../src/live/LiveUpdates';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

const action = (status: string) => ({ id: 'a1', status, command: 'SET_SPEED', value: '1', unit: 'rpm',
  gateCode: 'GATE_OK', sink: 'simulator', createdAtUtc: '2026-01-01T00:00:00Z' });

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

it('reads the control actions again when the Server reports a control change', async () => {
  const harness = hubHarness();
  let status = 'Dispatched';
  const fetchMock = vi.fn(async (url: string) => String(url).includes('kill-switch')
    ? new Response(JSON.stringify({ engaged: false, autonomousControlEnabled: true }))
    : new Response(JSON.stringify([action(status)])));
  vi.stubGlobal('fetch', fetchMock);
  render(<LiveUpdatesProvider client={harness.client}><AutonomousControlPanel canManage /></LiveUpdatesProvider>);
  await screen.findByText('Dispatched');
  await act(async () => { harness.client.setEnabled(true); harness.connect(); });
  const before = fetchMock.mock.calls.length;

  status = 'Blocked';
  await act(async () => { harness.emit('executions'); });
  expect(fetchMock.mock.calls.length).toBe(before);

  await act(async () => { harness.emit('control'); });
  await screen.findByText('Blocked');
  expect(fetchMock.mock.calls.length).toBeGreaterThan(before);
});
