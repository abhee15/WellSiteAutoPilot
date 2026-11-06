import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RecommendationsPage } from '../../src/views/RecommendationsPage';

const userId = '11111111-1111-4111-8111-111111111111';
const id = (n: number) => `88888888-8888-4888-8888-${String(n).padStart(12, '0')}`;
const proposal = (n: number, run: number) => ({
  id: id(n), assetId: id(90), executionId: id(run), moduleId: 'pump.watch',
  status: 'Created', canDecide: true, command: `Proposal ${n}`, suggestedValue: n,
  createdAtUtc: new Date(Date.now() - n * 1000).toISOString(),
  expiresAtUtc: new Date(Date.now() + 3600000).toISOString(),
});
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });

function fixture() {
  const items = [proposal(1, 11), proposal(2, 11), proposal(3, 12), proposal(4, 13), proposal(5, 14)];
  const extra = proposal(6, 11);
  const state = { scopedFailure: false };
  const http = vi.fn(async (url: string) => {
    if (url.startsWith('/api/v1/recommendations?')) {
      const run = new URL(url, 'http://localhost').searchParams.get('executionId');
      return run ? state.scopedFailure ? response({}, 403) : response([...items, extra].filter(item => item.executionId === run)) : response(items);
    }
    if (url.startsWith('/api/v1/recommendations/')) return response(proposal(70, 77));
    if (url === '/api/v1/control/kill-switch') return response({ engaged: false, autonomousControlEnabled: true });
    return response([]);
  });
  vi.stubGlobal('fetch', http);
  const view = render(<RecommendationsPage userId={userId} permissions={['recommendations.read', 'recommendations.decide', 'executions.read']} />);
  return { http, state, ...view };
}

it('shows three distinct runs and fetches the selected run including proposals outside the inbox window', async () => {
  const { http } = fixture();
  const picker = await screen.findByRole('combobox', { name: 'Recommendation runs' });
  await waitFor(() => expect(within(picker).getAllByRole('option')).toHaveLength(3));
  expect(await screen.findByText('Proposal 6')).toBeTruthy();
  expect(screen.getByText('Proposal 1')).toBeTruthy();
  expect(screen.queryByText('Proposal 3')).toBeNull();
  expect(screen.getByRole('link', { name: 'View all runs' }).getAttribute('href')).toBe('#operations');
  await userEvent.setup().selectOptions(picker, id(12));
  expect(await screen.findByText('Proposal 3')).toBeTruthy();
  expect(screen.queryByText('Proposal 1')).toBeNull();
  expect((picker as HTMLSelectElement).value).toBe(id(12));
  expect(http.mock.calls.some(([url]) => url.includes('executionId=' + id(12)))).toBe(true);
});

it('preserves direct review links outside the three-run preview', async () => {
  window.location.hash = '#recommendations/' + id(70);
  fixture();
  expect(await screen.findByRole('region', { name: 'Recommendation decision review' })).toBeTruthy();
  expect(screen.getByRole('link', { name: 'See the run that produced this' }).getAttribute('href')).toBe('#operations/' + id(77));
});

it('does not fall back to stale global proposals when the selected run is denied', async () => {
  const { state } = fixture();
  await screen.findByText('Proposal 1');
  state.scopedFailure = true;
  await userEvent.setup().click(screen.getByRole('button', { name: 'Reload inbox' }));
  expect(await screen.findByText('Recommendations for this run could not be loaded.')).toBeTruthy();
  expect(screen.queryByText('Proposal 1')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Review' })).toBeNull();
});

it('routes a command deep link to its open-command context instead of attempting recommendation review', async () => {
  window.location.hash = '#recommendations/actions%2F' + id(50);
  const { http } = fixture();
  expect(await screen.findByText(/no longer in the authorized open-command list/)).toBeTruthy();
  expect(http.mock.calls.some(([url]) => url.includes('/recommendations/actions'))).toBe(false);
  expect(screen.queryByRole('region', { name: 'Recommendation decision review' })).toBeNull();
});
