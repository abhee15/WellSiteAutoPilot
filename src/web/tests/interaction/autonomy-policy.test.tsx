import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AutonomyPolicyPanel } from '../../src/views/AutonomyPolicyPanel';

const id = '11111111-1111-4111-8111-111111111111';
const stored = {
  configuredLogicId: id, enabled: true, sinkKind: 'Simulator',
  limits: [{ command: 'SET_CHOKE', unit: '%', minimumValue: 10, maximumValue: 60 }],
  maxActionsPerWindow: 3, windowSeconds: 600, updatedBy: 'FIELD\\admin', updatedAtUtc: '2026-10-14T12:00:00Z',
  allowApprovedRecommendations: false,
};
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

/** A policy server: GET answers the stored policy (404 when none), PUT stores what it is given. */
function server(initial: Record<string, unknown> | null) {
  const state = { value: initial };
  const puts: Array<Record<string, unknown>> = [];
  const http = vi.fn(async (url: string, init?: RequestInit) => {
    expect(url).toBe(`/api/v1/control/policies/${id}`);
    if (init?.method === 'PUT') {
      const body = JSON.parse(String(init.body)) as Record<string, unknown>;
      puts.push(body);
      state.value = { ...stored, ...body, updatedBy: 'FIELD\\operator' };
    }
    return state.value ? new Response(JSON.stringify(state.value)) : new Response('{}', { status: 404 });
  });
  vi.stubGlobal('fetch', http);
  return { puts, http };
}

it('shows the policy to readers and offers no edit without control.manage', async () => {
  server(stored);
  render(<AutonomyPolicyPanel configuredLogicId={id} canManage={false} />);
  expect(await screen.findByText('SET_CHOKE')).toBeTruthy();
  expect(screen.getByText(/At most 3 actions every 600 seconds/)).toBeTruthy();
  expect(screen.getByText(/may not be executed by a person/)).toBeTruthy();
  expect(screen.queryByRole('button')).toBeNull();
});

it('says a configuration without a policy may command nothing', async () => {
  server(null);
  render(<AutonomyPolicyPanel configuredLogicId={id} canManage />);
  expect(await screen.findByText(/has no autonomy policy, so it may not command anything/)).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Create autonomy policy' })).toBeTruthy();
});

it('creating a policy validates before sending, then sends the exact policy once', async () => {
  const { puts } = server(null);
  const user = userEvent.setup();
  render(<AutonomyPolicyPanel configuredLogicId={id} canManage />);
  await user.click(await screen.findByRole('button', { name: 'Create autonomy policy' }));
  await user.click(screen.getByRole('checkbox', { name: 'Policy enabled' }));
  await user.click(screen.getByRole('button', { name: 'Save autonomy policy' }));
  expect(await screen.findByText('An enabled policy must permit at least one command.')).toBeTruthy();
  expect(puts).toHaveLength(0);

  await user.click(screen.getByRole('button', { name: 'Add command' }));
  const row = screen.getByRole('group', { name: 'Command 1' });
  await user.type(within(row).getByRole('textbox', { name: 'Command' }), 'SET_CHOKE');
  await user.type(within(row).getByRole('textbox', { name: 'Minimum' }), '10');
  await user.click(screen.getByRole('button', { name: 'Save autonomy policy' }));
  expect(await screen.findByText('Command 1 needs both a minimum and a maximum while the policy is enabled.')).toBeTruthy();
  expect(puts).toHaveLength(0);

  await user.type(within(row).getByRole('textbox', { name: 'Maximum' }), '60');
  await user.click(screen.getByRole('button', { name: 'Save autonomy policy' }));
  expect(await screen.findByText('The autonomy policy was saved.')).toBeTruthy();
  expect(puts).toEqual([{ enabled: true, maxActionsPerWindow: 1, windowSeconds: 3600, allowApprovedRecommendations: false,
    limits: [{ command: 'SET_CHOKE', unit: null, minimumValue: 10, maximumValue: 60 }] }]);
  expect(screen.getByText('SET_CHOKE')).toBeTruthy();
  expect(screen.queryByRole('form')).toBeNull();
});

it('editing starts from the stored policy and a failed save keeps the person\'s input', async () => {
  const { puts, http } = server(stored);
  const user = userEvent.setup();
  render(<AutonomyPolicyPanel configuredLogicId={id} canManage />);
  await user.click(await screen.findByRole('button', { name: 'Edit autonomy policy' }));
  const row = screen.getByRole('group', { name: 'Command 1' });
  expect((within(row).getByRole('textbox', { name: 'Command' }) as HTMLInputElement).value).toBe('SET_CHOKE');
  http.mockImplementationOnce(async () => new Response('{}', { status: 500 }));
  await user.click(screen.getByRole('checkbox', { name: 'Allow a person to execute approved recommendations' }));
  await user.click(screen.getByRole('button', { name: 'Save autonomy policy' }));
  expect(await screen.findByText(/could not be confirmed/)).toBeTruthy();
  expect((screen.getByRole('checkbox', { name: 'Allow a person to execute approved recommendations' }) as HTMLInputElement).checked).toBe(true);
  expect(puts).toHaveLength(0);
  await user.click(screen.getByRole('button', { name: 'Save autonomy policy' }));
  await waitFor(() => expect(puts).toHaveLength(1));
  expect(puts[0].allowApprovedRecommendations).toBe(true);
});

it('never shows a policy it cannot confirm', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 500 })));
  render(<AutonomyPolicyPanel configuredLogicId={id} canManage />);
  expect(await screen.findByRole('alert')).toBeTruthy();
  expect(screen.queryByText('SET_CHOKE')).toBeNull();
  expect(screen.queryByRole('button')).toBeNull();
});
