import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RecommendationsPage } from '../../src/views/RecommendationsPage';
import { accessibilityViolations } from './axe';

const id = (n: number) => `88888888-8888-4888-8888-${String(n).padStart(12, '0')}`;
const permissions = ['recommendations.read', 'recommendations.decide', 'control.manage'];
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });
function fixture({ actions = true, failActions = false, readOnly = false } = {}) {
  const proposal = { id: id(1), assetId: id(2), executionId: id(3), moduleId: 'pump.watch', moduleVersion: '1.2.0',
    status: 'Created', canDecide: true, command: 'Set pump speed', suggestedValue: 48, unit: 'Hz', reasonCode: 'pump_efficiency_low',
    createdAtUtc: new Date().toISOString(), expiresAtUtc: new Date(Date.now() + 3600000).toISOString() };
  const http = vi.fn(async (url: string) => {
    if (url.startsWith('/api/v1/recommendations?')) return Response.json([proposal]);
    if (url === '/api/v1/control/kill-switch') return Response.json({ engaged: false, autonomousControlEnabled: false });
    if (url.startsWith('/api/v1/control/actions?')) return failActions ? Response.json({}, { status: 503 }) : Response.json(actions ? [{
      id: id(4), recommendationId: id(1), command: 'SET_CHOKE', status: 'Ambiguous', gateCode: 'SINK_AMBIGUOUS',
      createdAtUtc: new Date().toISOString(), canManage: true,
    }] : []);
    throw new Error('Unexpected request: ' + url);
  });
  vi.stubGlobal('fetch', http);
  const view = render(<RecommendationsPage userId={id(90)} permissions={readOnly ? ['recommendations.read'] : permissions} />);
  return { ...view, http };
}

it('keeps command state and unresolved-count attention visible without their editors competing with review', async () => {
  fixture();
  expect(await screen.findByText('Commands blocked: deployment control is off.')).toBeTruthy();
  const summary = await screen.findByRole('region', { name: 'Unconfirmed commands' });
  expect(within(summary).getByText('1 open')).toBeTruthy();
  expect(within(summary).getByRole('link', { name: 'Review unconfirmed commands' }).getAttribute('href')).toBe('#recommendations/actions');
  expect(screen.queryByRole('button', { name: 'Engage kill switch' })).toBeNull();
  expect(screen.queryByRole('button', { name: /Verify/ })).toBeNull();
  expect(await screen.findByRole('button', { name: 'Review' })).toBeTruthy();
});

it('switches addressable tasks, retains the review filter and never treats settings as a recommendation ID', async () => {
  const { http } = fixture();
  const user = userEvent.setup();
  await user.type(await screen.findByRole('searchbox', { name: 'Search' }), 'Set pump');
  const nav = screen.getByRole('navigation', { name: 'Recommendation tasks' });
  await user.click(within(nav).getByRole('link', { name: 'Command controls' }));
  expect(await screen.findByRole('button', { name: 'Engage kill switch' })).toBeTruthy();
  expect(screen.queryByRole('searchbox')).toBeNull();
  expect(http.mock.calls.some(([url]) => url.includes('/recommendations/settings'))).toBe(false);
  await user.click(within(nav).getByRole('link', { name: 'Unconfirmed commands' }));
  expect(await screen.findByRole('button', { name: /Verify.*SET_CHOKE/ })).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Engage kill switch' })).toBeNull();
  await user.click(within(nav).getByRole('link', { name: 'Review recommendations' }));
  expect(await screen.findByRole('searchbox', { name: 'Search' })).toHaveProperty('value', 'Set pump');
  expect(await accessibilityViolations(document.body)).toEqual([]);
});

it('shows an honest empty command workspace, including for read-only users', async () => {
  window.location.hash = '#recommendations/actions';
  fixture({ actions: false, readOnly: true });
  expect(await screen.findByText('No unconfirmed commands in the authorized open-command list.')).toBeTruthy();
  expect(screen.queryByRole('button', { name: /Verify|Resolve|Engage kill switch/ })).toBeNull();
});

it('keeps command-load errors visible on the review workspace', async () => {
  fixture({ failActions: true });
  await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('could not be loaded'));
  expect(await screen.findByRole('button', { name: 'Review' })).toBeTruthy();
});

it('keeps the proposal, reason and module context visible while disclosing duplicated audit metadata', async () => {
  fixture({ actions: false });
  await userEvent.setup().click(await screen.findByRole('button', { name: 'Review' }));
  const review = screen.getByRole('region', { name: 'Recommendation decision review' });
  expect(within(review).getByText('Module pump.watch, version 1.2.0')).toBeTruthy();
  expect(within(review).getByText('Reason code: Pump efficiency low')).toBeTruthy();
  const metadata = review.querySelector('details')!;
  expect(metadata.open).toBe(false);
  await userEvent.setup().click(within(review).getByText('Proposal details and timestamps'));
  expect(metadata.open).toBe(true);
  expect(within(review).getByRole('textbox', { name: 'Reason for audit' })).toBeTruthy();
});
