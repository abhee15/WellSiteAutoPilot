import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen } from '@testing-library/react';
import { ExecutionDetailPanel } from '../../src/views/ExecutionDetailPanel';
import { ChangeSignalClient } from '../../src/live/changeSignal';
import type { HubConnectionLike } from '../../src/live/changeSignal';
import { LiveUpdatesProvider } from '../../src/live/LiveUpdates';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

const executionId = '44444444-4444-4444-8444-444444444444';
const run = (status: string) => ({ executionId, logicInstanceId: 'cfg', configurationRevisionId: 'rev', status,
  mode: 'Shadow', trigger: 'Manual', moduleId: 'sample.module', moduleVersion: '1.0.0', requestContractVersion: 3 });

/** A hub the test controls: it connects when told to and emits change signals by hand. */
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

it('reads an in-flight run again when the Server reports a run change, without replacing what is shown', async () => {
  const harness = hubHarness();
  let release: (value: Response) => void = () => undefined;
  // Call 1 is the first load, call 2 is the catch-up the client makes when it connects, call 3 is the change signal.
  const fetchMock = vi.fn()
    .mockImplementationOnce(async () => new Response(JSON.stringify(run('Running'))))
    .mockImplementationOnce(async () => new Response(JSON.stringify(run('Running'))))
    .mockImplementationOnce(() => new Promise<Response>(resolve => { release = resolve; }));
  vi.stubGlobal('fetch', fetchMock);
  render(<LiveUpdatesProvider client={harness.client}>
    <ExecutionDetailPanel executionId={executionId} onClose={() => undefined} /></LiveUpdatesProvider>);
  await screen.findByText('Running');
  await act(async () => { harness.client.setEnabled(true); harness.connect(); });
  expect(fetchMock).toHaveBeenCalledTimes(2);
  await act(async () => { harness.emit('executions'); });
  expect(fetchMock).toHaveBeenCalledTimes(3);
  // While the third answer is pending the first stays on screen; there is no loading message.
  expect(screen.getByText('Running')).toBeTruthy();
  expect(screen.queryByText('Loading execution details…')).toBeNull();
  await act(async () => { release(new Response(JSON.stringify(run('Completed')))); });
  await screen.findByText('Completed');
});

it('does not read a finished run again on a change signal', async () => {
  const harness = hubHarness();
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(run('Completed'))));
  vi.stubGlobal('fetch', fetchMock);
  render(<LiveUpdatesProvider client={harness.client}>
    <ExecutionDetailPanel executionId={executionId} onClose={() => undefined} /></LiveUpdatesProvider>);
  await screen.findByText('Completed');
  await act(async () => { harness.client.setEnabled(true); harness.connect(); });
  await act(async () => { harness.emit('executions'); });
  // A finished run is never read again on a signal, including the catch-up the client makes when it connects.
  expect(fetchMock).toHaveBeenCalledTimes(1);
});
