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
