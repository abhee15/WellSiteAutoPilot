import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { NotificationCenter } from '../../src/components/NotificationCenter';
import { ChangeSignalClient } from '../../src/live/changeSignal';
import type { HubConnectionLike } from '../../src/live/changeSignal';
import { LiveUpdatesProvider } from '../../src/live/LiveUpdates';

afterEach(() => { cleanup(); vi.useRealTimers(); });

function harness() {
  const handlers: Record<string, (...args: unknown[]) => void> = {};
  let settle: () => void = () => undefined;
  const hub: HubConnectionLike = {
    start: () => new Promise<void>(resolve => { settle = resolve; }),
    stop: () => Promise.resolve(),
    on: (method, handler) => { handlers[method] = handler; },
    onclose: () => undefined, onreconnecting: () => undefined, onreconnected: () => undefined,
  };
  const client = new ChangeSignalClient(() => hub, { set: () => 0, clear: () => undefined });
  let sequence = 0;
  const send = (entity: string, id: string, kind: string, area: string, payload: Record<string, string> = {}) => {
    sequence += 1;
    handlers.event!({ v: 1, epoch: 'e', area, sequence, entity, id, kind, occurredAt: '2026-01-01T00:00:00Z', payload });
  };
  return { client, send, connect: () => settle() };
}

async function mount(permissions: string[]) {
  const h = harness();
  render(<LiveUpdatesProvider client={h.client}><NotificationCenter permissions={permissions} /></LiveUpdatesProvider>);
  await act(async () => { h.client.setEnabled(true); h.connect(); });
  return h;
}

it('shows a toast and a list item for a failed run, with a link, and lets the person dismiss it', async () => {
  const h = await mount(['executions.read']);
  await act(async () => { h.send('execution', 'run-1', 'failed', 'executions', { code: 'timeout' }); });
  expect(screen.getAllByText(/A run failed/).length).toBeGreaterThan(0);
  expect(screen.getByRole('link', { name: 'View' }).getAttribute('href')).toContain('run-1');
  fireEvent.click(screen.getByRole('button', { name: /Notifications/ }));
  fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }));
  expect(screen.getByText('Nothing needs your attention.')).toBeTruthy();
});

it('asks for a decision only people who can decide, and clears it once decided', async () => {
  const h = await mount(['executions.read']);
  await act(async () => { h.send('recommendation', 'rec-1', 'created', 'recommendations'); });
  expect(screen.queryByRole('link', { name: 'View' })).toBeNull();
  cleanup();
  const decider = await mount(['recommendations.decide']);
  await act(async () => { decider.send('recommendation', 'rec-1', 'created', 'recommendations'); });
  expect(screen.getByRole('link', { name: 'View' }).getAttribute('href')).toContain('rec-1');
  await act(async () => { decider.send('recommendation', 'rec-1', 'decided', 'recommendations'); });
  fireEvent.click(screen.getByRole('button', { name: /Notifications/ }));
  expect(screen.getByText('Nothing needs your attention.')).toBeTruthy();
});

it('renders nothing without the live channel', () => {
  const { container } = render(<NotificationCenter permissions={['executions.read']} />);
  expect(container.textContent).toBe('');
});
