import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RecommendationsPage } from '../../src/views/RecommendationsPage';

const userId = '11111111-1111-4111-8111-111111111111';
const item = { id: '22222222-2222-4222-8222-222222222222', assetId: '33333333-3333-4333-8333-333333333333',
  status: 'Created', canDecide: true, command: 'Review pump setting', suggestedValue: 82, unit: '%',
  createdAtUtc: new Date().toISOString(), expiresAtUtc: new Date(Date.now() + 3600000).toISOString() };
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
function fixture() {
  const state: { value: object } = { value: item };
  const http = vi.fn(async (url: string) => {
    // The kill switch panel shares the page; it is exercised in kill-switch.test.tsx.
    if (url === '/api/v1/control/kill-switch') {
      return new Response(JSON.stringify({ engaged: false, autonomousControlEnabled: true }));
    }
    expect(url).toBe('/api/v1/recommendations?limit=50');
    return new Response(JSON.stringify([state.value]));
  });
  vi.stubGlobal('fetch', http);
  render(<RecommendationsPage userId={userId} permissions={['recommendations.read', 'recommendations.decide']} />);
  return { state, http };
}
async function review() {
  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'Review' }));
  await user.click(screen.getByRole('radio', { name: 'Approve recommendation' }));
  await user.type(screen.getByRole('textbox', { name: 'Reason (required for audit)' }), 'Reviewed for this Asset');
  await user.click(screen.getByRole('checkbox', { name: 'I reviewed the asset, recommendation, proposed value, and expiry.' }));
  return user;
}

it('coarse decision permission cannot enable a row with absent, malformed or false resource capability', async () => {
  const { state } = fixture();
  for (const canDecide of [false, undefined, 'true']) {
    state.value = { ...item, canDecide };
    await userEvent.setup().click(await screen.findByRole('button', { name: 'Reload inbox' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Reload inbox' })).toHaveProperty('disabled', false));
    expect(screen.queryByRole('button', { name: 'Review' })).toBeNull();
  }
});

it('revocation clears a reviewed decision even when coarse permissions and the row ID remain unchanged', async () => {
  const { state, http } = fixture();
  const user = await review();
  expect(screen.getByRole('button', { name: 'Submit decision' })).toHaveProperty('disabled', false);
  state.value = { ...item, canDecide: false };
  await user.click(screen.getByRole('button', { name: 'Reload inbox' }));
  await waitFor(() => expect(screen.queryByRole('textbox', { name: 'Reason (required for audit)' })).toBeNull());
  expect(screen.queryByRole('button', { name: 'Submit decision' })).toBeNull();
  state.value = item;
  await user.click(screen.getByRole('button', { name: 'Reload inbox' }));
  await screen.findByRole('button', { name: 'Review' });
  expect(screen.queryByRole('button', { name: 'Submit decision' })).toBeNull();
  expect(http.mock.calls.every(([url]) => !String(url).endsWith('/decision'))).toBe(true);
});

it('an equivalent refresh preserves the reason and acknowledgement without resubmitting', async () => {
  fixture(); const user = await review();
  await user.click(screen.getByRole('button', { name: 'Reload inbox' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Reload inbox' })).toHaveProperty('disabled', false));
  expect(screen.getByRole('textbox', { name: 'Reason (required for audit)' })).toHaveProperty('value', 'Reviewed for this Asset');
  expect(screen.getByRole('button', { name: 'Submit decision' })).toHaveProperty('disabled', false);
});

it('changed proposal metadata requires a fresh review for the same recommendation ID', async () => {
  const { state } = fixture(); const user = await review();
  state.value = { ...item, suggestedValue: 71 };
  await user.click(screen.getByRole('button', { name: 'Reload inbox' }));
  await waitFor(() => expect(screen.getByRole('textbox', { name: 'Reason (required for audit)' })).toHaveProperty('value', ''));
  expect(screen.getByRole('button', { name: 'Submit decision' })).toHaveProperty('disabled', true);
});

it('names the asset when assets are loaded and keeps the id as its hover text', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify([
    { id: 'r1', assetId: 'asset-1', status: 'Created', moduleId: 'm', moduleVersion: '1', createdAtUtc: new Date().toISOString(),
      expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(), canDecide: false }]), { status: 200 })));
  render(<RecommendationsPage userId={userId} permissions={['recommendations.read']}
    assets={[{ id: 'asset-1', name: 'North Well' }] as never} />);
  expect((await screen.findByText('North Well')).getAttribute('title')).toBe('asset-1');
  expect(screen.queryByText('asset-1')).toBeNull();
});

it('the decision screen states the proposal, its context and that nothing is sent', async () => {
  const runId = '44444444-4444-4444-8444-444444444444';
  const configurationId = '55555555-5555-4555-8555-555555555555';
  const revisionId = '66666666-6666-4666-8666-666666666666';
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify([{ ...item, quantity: 'PumpSpeed',
    executionId: runId, configuredLogicId: configurationId, configurationRevisionId: revisionId }]))));
  render(<RecommendationsPage userId={userId} permissions={['recommendations.read', 'recommendations.decide']}
    assets={[{ id: item.assetId, name: 'North Well', isActive: true } as never]}
    configuredLogic={[{ id: configurationId, name: 'Pump watch', revisions: [{ id: revisionId, revisionNumber: 4 }] } as never]} />);
  await userEvent.setup().click(await screen.findByRole('button', { name: 'Review' }));
  expect(await screen.findByText('Set pump speed to 82 % on North Well.')).toBeTruthy();
  expect(screen.getByText('Approving records your decision. It does not send a command.')).toBeTruthy();
  expect(screen.getByText('From Pump watch, revision 4')).toBeTruthy();
  expect(screen.getByRole('link', { name: 'See the run that produced this' }).getAttribute('href')).toBe('#operations/' + runId);
  expect(screen.getByRole('link', { name: 'North Well' }).getAttribute('href')).toBe('#assets/' + item.assetId);
  expect(screen.getByText(/^Expires in/)).toBeTruthy();
});
