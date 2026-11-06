import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RecommendationDecisionReviewPanel } from '../../src/views/RecommendationDecisionReviewPanel';
import { RecommendationsPage } from '../../src/views/RecommendationsPage';
import { ControlExecutionPanel } from '../../src/views/ControlExecutionPanel';
import { accessibilityViolations } from './axe';

const item = { id: '22222222-2222-4222-8222-222222222222', assetId: '33333333-3333-4333-8333-333333333333', status: 'Created', canDecide: true,
  command: 'Set pump speed', suggestedValue: 48, unit: 'Hz', createdAtUtc: new Date().toISOString(), expiresAtUtc: new Date(Date.now() + 3600000).toISOString() };
const callbacks = () => ({ onCancel: vi.fn(), onSaved: vi.fn(), onConflict: vi.fn() });
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

async function fillDecision() {
  const user = userEvent.setup();
  await user.click(screen.getByRole('radio', { name: 'Approve recommendation' }));
  await user.type(screen.getByRole('textbox', { name: 'Reason for audit' }), 'Checked operating limits');
  await user.click(screen.getByRole('checkbox'));
  return user;
}

it('connects a short audit reason error after blur without making a decision or losing the entered value', async () => {
  const calls = callbacks();
  const fetcher = vi.fn(); vi.stubGlobal('fetch', fetcher);
  const { container } = render(<RecommendationDecisionReviewPanel item={item} {...calls} />);
  const user = userEvent.setup();
  const reason = screen.getByRole('textbox', { name: 'Reason for audit' });
  expect(reason.getAttribute('aria-invalid')).toBeNull();
  await user.type(reason, 'x'); await user.tab();
  expect(reason.getAttribute('aria-invalid')).toBe('true');
  expect(document.getElementById(reason.getAttribute('aria-describedby')!)?.textContent).toBe('Enter at least 2 characters for the audit reason.');
  expect(reason).toHaveProperty('value', 'x');
  expect(screen.getByRole('button', { name: 'Submit decision' })).toHaveProperty('disabled', true);
  expect(fetcher).not.toHaveBeenCalled();
  await user.type(reason, ' reviewed');
  expect(reason.getAttribute('aria-invalid')).toBeNull();
  expect(await accessibilityViolations(container)).toEqual([]);
});

it('blocks only the pending decision, announces it once and cannot submit twice', async () => {
  let finish!: (response: Response) => void;
  const fetcher = vi.fn<typeof fetch>(() => new Promise<Response>(resolve => { finish = resolve; }));
  vi.stubGlobal('fetch', fetcher);
  const calls = callbacks();
  render(<><a href="#assets">Assets</a><RecommendationDecisionReviewPanel item={item} {...calls} /></>);
  const user = await fillDecision();
  const submit = screen.getByRole('button', { name: 'Submit decision' });
  await user.click(submit);
  expect(screen.getAllByRole('status')).toHaveLength(1);
  expect(submit.getAttribute('aria-busy')).toBe('true');
  expect(screen.getByRole('textbox', { name: 'Reason for audit' })).toHaveProperty('disabled', true);
  expect(screen.getByRole('checkbox')).toHaveProperty('disabled', true);
  expect((screen.getByRole('radio', { name: 'Approve recommendation' }) as HTMLInputElement).matches(':disabled')).toBe(true);
  expect(screen.queryByRole('dialog')).toBeNull();
  expect(screen.getByRole('link', { name: 'Assets' }).getAttribute('href')).toBe('#assets');
  await user.click(submit);
  expect(fetcher).toHaveBeenCalledTimes(1);
  finish(json({ id: item.id, status: 'Approved', decisionBy: 'reviewer', decisionAtUtc: new Date().toISOString() }));
  await waitFor(() => expect(calls.onSaved).toHaveBeenCalledOnce());
  expect(JSON.parse(fetcher.mock.calls[0][1]!.body as string)).toEqual({ decision: 'Approved', reason: 'Checked operating limits' });
});

it('keeps the audit reason after a failed submission and routes conflicts through the existing callback', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(json({}, 503)).mockResolvedValueOnce(json({}, 409));
  vi.stubGlobal('fetch', fetcher);
  const calls = callbacks();
  render(<RecommendationDecisionReviewPanel item={item} {...calls} />);
  const user = await fillDecision();
  await user.click(screen.getByRole('button', { name: 'Submit decision' }));
  await screen.findByRole('alert');
  expect(screen.getByRole('textbox', { name: 'Reason for audit' })).toHaveProperty('value', 'Checked operating limits');
  expect(calls.onSaved).not.toHaveBeenCalled();
  await user.click(screen.getByRole('button', { name: 'Submit decision' }));
  await waitFor(() => expect(calls.onConflict).toHaveBeenCalledOnce());
});

it('execution refusal stays visible and requires fresh acknowledgement without editing the command', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => json({ executed: false, refusalCode: 'GATE_KILL_SWITCH', action: null })));
  const onExecuted = vi.fn();
  const { container } = render(<ControlExecutionPanel item={{ ...item, status: 'Approved' }} onExecuted={onExecuted} onCancel={vi.fn()} onConflict={vi.fn()} />);
  const user = userEvent.setup();
  expect(screen.queryByRole('textbox')).toBeNull();
  await user.click(screen.getByRole('checkbox'));
  await user.click(screen.getByRole('button', { name: 'Execute command' }));
  expect((await screen.findByRole('alert')).textContent).toMatch(/kill switch/i);
  expect(screen.getByRole('checkbox')).toHaveProperty('checked', false);
  expect(screen.getByRole('button', { name: 'Execute command' })).toHaveProperty('disabled', true);
  expect(onExecuted).not.toHaveBeenCalled();
  expect(await accessibilityViolations(container)).toEqual([]);
});

it('the completed decision returns to the inbox and announces the recorded outcome through the shared notice', async () => {
  let current = { ...item };
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    if (url.endsWith('/decision') && init?.method === 'POST') {
      current = { ...current, status: 'Approved' };
      return json({ id: item.id, status: 'Approved', decisionBy: 'reviewer', decisionAtUtc: new Date().toISOString() });
    }
    if (url.includes('kill-switch')) return json({ engaged: false, autonomousControlEnabled: false });
    if (url.includes('/control/actions')) return json([]);
    return json([current]);
  }));
  render(<RecommendationsPage userId="11111111-1111-4111-8111-111111111111" permissions={['recommendations.read', 'recommendations.decide']} />);
  await userEvent.setup().click(await screen.findByRole('button', { name: 'Review', exact: true }));
  const user = await fillDecision();
  await user.click(screen.getByRole('button', { name: 'Submit decision' }));
  await waitFor(() => expect(screen.queryByRole('region', { name: 'Recommendation decision review' })).toBeNull());
  const outcome = screen.getByRole('status');
  expect(outcome.classList.contains('ui-notice')).toBe(true);
  expect(outcome.textContent).toMatch(/decision.*saved|saved.*decision/i);
});
