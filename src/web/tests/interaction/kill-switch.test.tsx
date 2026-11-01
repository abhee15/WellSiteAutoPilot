import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { KillSwitchPanel } from '../../src/views/KillSwitchPanel';

const userId = '11111111-1111-4111-8111-111111111111';
const released = { engaged: false, changedBy: null, changedAtUtc: null, reason: null, autonomousControlEnabled: true };
const engaged = { engaged: true, changedBy: 'FIELD\\admin', changedAtUtc: '2026-10-14T12:00:00Z', reason: 'Pump trip', autonomousControlEnabled: true };
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

/** A kill switch server: GET reports the current state, PUT applies it. */
function server(initial: object) {
  const state = { value: initial as Record<string, unknown> };
  const puts: Array<Record<string, unknown>> = [];
  const http = vi.fn(async (url: string, init?: RequestInit) => {
    expect(url).toBe('/api/v1/control/kill-switch');
    if (init?.method === 'PUT') {
      const body = JSON.parse(String(init.body)) as { engaged: boolean; reason: string | null };
      puts.push(body);
      state.value = { ...state.value, engaged: body.engaged, reason: body.reason, changedBy: 'FIELD\\operator',
        changedAtUtc: '2026-10-14T13:00:00Z' };
    }
    return new Response(JSON.stringify(state.value));
  });
  vi.stubGlobal('fetch', http);
  return { state, puts, http };
}

it('shows the state to everyone who can read it, and offers no change without control.manage', async () => {
  server(engaged);
  render(<KillSwitchPanel userId={userId} canManage={false} />);
  expect(await screen.findByText('The kill switch is engaged. No control command will be sent until it is released.')).toBeTruthy();
  expect(screen.getByText(/Last changed by FIELD\\admin/)).toBeTruthy();
  expect(screen.getByText(/Reason: Pump trip/)).toBeTruthy();
  expect(screen.queryByRole('button')).toBeNull();
});

it('says plainly when control is switched off for the whole deployment', async () => {
  server({ ...released, autonomousControlEnabled: false });
  render(<KillSwitchPanel userId={userId} canManage={false} />);
  expect(await screen.findByText(/Control is switched off for this deployment/)).toBeTruthy();
});

it('engaging needs no confirmation, sends the optional reason once, and shows the new state', async () => {
  const { puts } = server(released);
  const user = userEvent.setup();
  render(<KillSwitchPanel userId={userId} canManage />);
  await user.click(await screen.findByRole('button', { name: 'Engage kill switch' }));
  expect(screen.queryByRole('checkbox')).toBeNull();
  await user.type(screen.getByRole('textbox', { name: 'Reason (optional)' }), 'Pump trip');
  await user.click(screen.getByRole('button', { name: 'Engage now' }));
  expect(await screen.findByText('The kill switch is engaged. No control command will be sent until it is released.')).toBeTruthy();
  expect(puts).toEqual([{ engaged: true, reason: 'Pump trip' }]);
  expect(screen.getByRole('button', { name: 'Release kill switch' })).toBeTruthy();
});

it('releasing needs an explicit acknowledgement before it can be sent', async () => {
  const { puts } = server(engaged);
  const user = userEvent.setup();
  render(<KillSwitchPanel userId={userId} canManage />);
  await user.click(await screen.findByRole('button', { name: 'Release kill switch' }));
  const confirm = screen.getByRole('button', { name: 'Release now' });
  expect(confirm).toHaveProperty('disabled', true);
  await user.click(screen.getByRole('checkbox', { name: 'I understand that approved commands can be sent again.' }));
  expect(confirm).toHaveProperty('disabled', false);
  await user.click(confirm);
  expect(await screen.findByText('The kill switch is released. Approved commands can be sent through the control gate.')).toBeTruthy();
  expect(puts).toEqual([{ engaged: false, reason: null }]);
});

it('cancelling sends nothing and discards the draft', async () => {
  const { puts } = server(released);
  const user = userEvent.setup();
  render(<KillSwitchPanel userId={userId} canManage />);
  await user.click(await screen.findByRole('button', { name: 'Engage kill switch' }));
  await user.type(screen.getByRole('textbox', { name: 'Reason (optional)' }), 'draft');
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  await user.click(screen.getByRole('button', { name: 'Engage kill switch' }));
  expect(screen.getByRole('textbox', { name: 'Reason (optional)' })).toHaveProperty('value', '');
  expect(puts).toEqual([]);
});

it('never shows released when the state cannot be confirmed', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 500 })));
  render(<KillSwitchPanel userId={userId} canManage />);
  await waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(/could not be confirmed/));
  expect(screen.queryByText('Released')).toBeNull();
  expect(screen.queryByRole('button')).toBeNull();
});

it('a failed change keeps the form open, says so, and reloads the true state', async () => {
  let reads = 0;
  vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => {
    if (init?.method === 'PUT') return new Response('{}', { status: 403 });
    reads++;
    return new Response(JSON.stringify(released));
  }));
  const user = userEvent.setup();
  render(<KillSwitchPanel userId={userId} canManage />);
  await user.click(await screen.findByRole('button', { name: 'Engage kill switch' }));
  await user.click(screen.getByRole('button', { name: 'Engage now' }));
  expect((await screen.findByRole('alert')).textContent).toMatch(/do not have permission/);
  await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2));
});
