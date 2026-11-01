import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ControlActionsPanel } from '../../src/views/ControlActionsPanel';

const userId = '11111111-1111-4111-8111-111111111111';
const actionId = '22222222-2222-4222-8222-222222222222';
const action = (over: Record<string, unknown> = {}) => ({
  id: actionId, recommendationId: '33333333-3333-4333-8333-333333333333',
  executionId: '44444444-4444-4444-8444-444444444444', configuredLogicId: '55555555-5555-4555-8555-555555555555',
  assetId: '66666666-6666-4666-8666-666666666666', command: 'SET_CHOKE', quantity: null, value: 40, unit: '%',
  status: 'Ambiguous', gateCode: 'SINK_AMBIGUOUS', sink: 'Simulator', sinkReference: null,
  createdAtUtc: '2026-10-14T12:00:00Z', completedAtUtc: null, origin: 'Autonomous', readbackValue: null,
  verifiedAtUtc: null, resolutionNote: null, canManage: true, ...over,
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

/** A control server: the list holds `rows`; verify and resolve answer with what the test sets. */
function server(rows: Array<Record<string, unknown>>, answers: { verify?: Record<string, unknown>; resolve?: Record<string, unknown> } = {}) {
  const state = { rows };
  const posts: Array<{ url: string; body: unknown }> = [];
  const http = vi.fn(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') {
      posts.push({ url, body: init.body ? JSON.parse(String(init.body)) : null });
      const answer = url.endsWith('/verify') ? answers.verify : answers.resolve;
      state.rows = answer ? state.rows.map(row => (row.id === answer.id ? answer : row)) : state.rows;
      return new Response(JSON.stringify(answer ?? state.rows[0]));
    }
    expect(url).toBe('/api/v1/control/actions?status=Dispatched&status=Ambiguous&order=oldest&limit=200');
    return new Response(JSON.stringify(state.rows));
  });
  vi.stubGlobal('fetch', http);
  return { posts, http };
}

it('shows nothing while no command awaits a decision', async () => {
  const { http } = server([action({ status: 'Verified' }), action({ id: '77777777-7777-4777-8777-777777777777', status: 'Failed' })]);
  const { container } = render(<ControlActionsPanel userId={userId} canManage />);
  await waitFor(() => expect(http).toHaveBeenCalled());
  expect(container.textContent).toBe('');
});

it('lists open commands for readers and offers no action without control.manage', async () => {
  server([action()]);
  render(<ControlActionsPanel userId={userId} canManage={false} />);
  expect(await screen.findByText('SET_CHOKE')).toBeTruthy();
  expect(screen.getByText(/The command was sent but its result is unknown/)).toBeTruthy();
  expect(screen.queryByRole('button')).toBeNull();
});

it('verify reports when the target gave no answer and when it settled the command', async () => {
  const { posts } = server([action()], { verify: action({ status: 'Verified', gateCode: 'READBACK_CONFIRMED' }) });
  const user = userEvent.setup();
  render(<ControlActionsPanel userId={userId} canManage />);
  await user.click(await screen.findByRole('button', { name: 'Verify SET_CHOKE' }));
  await waitFor(() => expect(posts).toHaveLength(1));
  expect(posts[0].url).toBe(`/api/v1/control/actions/${actionId}/verify`);
  // Settled: the command leaves the list.
  await waitFor(() => expect(screen.queryByText('SET_CHOKE')).toBeNull());
});

it('verify with no answer keeps the command and says so', async () => {
  server([action()]);
  const user = userEvent.setup();
  render(<ControlActionsPanel userId={userId} canManage />);
  await user.click(await screen.findByRole('button', { name: 'Verify SET_CHOKE' }));
  expect(await screen.findByText('The target gave no answer, so the command still needs a decision.')).toBeTruthy();
  expect(screen.getByText('SET_CHOKE')).toBeTruthy();
});

it('resolving needs a choice and a note, then sends the attestation once', async () => {
  const { posts } = server([action()], { resolve: action({ status: 'Failed', gateCode: 'OPERATOR_RESOLVED_NOT_APPLIED' }) });
  const user = userEvent.setup();
  render(<ControlActionsPanel userId={userId} canManage />);
  await user.click(await screen.findByRole('button', { name: 'Resolve SET_CHOKE' }));
  const confirm = screen.getByRole('button', { name: 'Record decision' });
  expect(confirm).toHaveProperty('disabled', true);
  await user.click(screen.getByRole('radio', { name: 'The command was not applied' }));
  expect(confirm).toHaveProperty('disabled', true);
  await user.type(screen.getByRole('textbox', { name: 'What did you check? (required)' }), 'Choke unchanged on site');
  expect(confirm).toHaveProperty('disabled', false);
  await user.click(confirm);
  await waitFor(() => expect(posts).toHaveLength(1));
  expect(posts[0]).toEqual({ url: `/api/v1/control/actions/${actionId}/resolve`,
    body: { applied: false, note: 'Choke unchanged on site' } });
  await waitFor(() => expect(screen.queryByText('SET_CHOKE')).toBeNull());
});

it('cancelling a resolution sends nothing and discards the draft', async () => {
  const { posts } = server([action()]);
  const user = userEvent.setup();
  render(<ControlActionsPanel userId={userId} canManage />);
  await user.click(await screen.findByRole('button', { name: 'Resolve SET_CHOKE' }));
  await user.type(screen.getByRole('textbox', { name: 'What did you check? (required)' }), 'draft');
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  await user.click(screen.getByRole('button', { name: 'Resolve SET_CHOKE' }));
  expect(screen.getByRole('textbox', { name: 'What did you check? (required)' })).toHaveProperty('value', '');
  expect(posts).toEqual([]);
});

it('never shows a list it cannot confirm', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 500 })));
  render(<ControlActionsPanel userId={userId} canManage />);
  expect(await screen.findByRole('alert')).toBeTruthy();
  expect(screen.queryByRole('button')).toBeNull();
});

it('offers no action on a command the person cannot decide, and says why', async () => {
  server([action({ canManage: false })]);
  render(<ControlActionsPanel userId={userId} canManage />);
  expect(await screen.findByText('SET_CHOKE')).toBeTruthy();
  expect(screen.getByText(/cannot decide it/)).toBeTruthy();
  expect(screen.queryByRole('button')).toBeNull();
});

it('says when the page is full so the count is not read as complete', async () => {
  const rows = Array.from({ length: 200 }, (_, index) => action({
    id: `00000000-0000-4000-8000-${String(index).padStart(12, '0')}`,
  }));
  server(rows);
  render(<ControlActionsPanel userId={userId} canManage />);
  expect(await screen.findByText('200+ open, showing the longest waiting')).toBeTruthy();
});
