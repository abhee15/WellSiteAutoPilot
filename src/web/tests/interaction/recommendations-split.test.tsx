import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RecommendationInboxTable, RecommendationsPage } from '../../src/views/RecommendationsPage';
import { accessibilityViolations } from './axe';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });

const userId = '11111111-1111-4111-8111-111111111111';
const soon = () => new Date(Date.now() + 3_600_000).toISOString();
const row = (id: string, extra: Record<string, unknown> = {}) => ({
  id, assetId: `asset-${id}`, status: 'Created', canDecide: true, command: `Command ${id}`, suggestedValue: 82, unit: '%',
  moduleId: `module-${id}`, reasonCode: 'pump_efficiency_low', createdAtUtc: new Date().toISOString(), expiresAtUtc: soon(), ...extra });
const items = [row('a'), row('b', { canDecide: false }), row('c', { status: 'Approved', decisionBy: 'Sri', decisionAtUtc: new Date().toISOString() })];

function show() {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(items))));
  return render(<RecommendationsPage userId={userId} permissions={['recommendations.read', 'recommendations.decide']} />);
}
const layout = (container: HTMLElement) => container.querySelector('.recs-layout') as HTMLElement;

it('opens the decision beside the list when a row that can be reviewed is clicked, marks it, and closes with Back', async () => {
  const user = userEvent.setup();
  const { container } = show();
  await screen.findByText('Command a');
  expect(layout(container).getAttribute('data-has-selection')).toBe('false');

  await user.click(screen.getByText('Command a'));
  expect(await screen.findByRole('region', { name: 'Recommendation decision review' })).toBeTruthy();
  expect(layout(container).getAttribute('data-has-selection')).toBe('true');
  expect(container.querySelector('tbody tr[data-selected="true"]')?.textContent).toContain('Command a');
  // The review sits in the detail pane, not under the table.
  expect(layout(container).querySelector('.split-selected')?.contains(
    screen.getByRole('region', { name: 'Recommendation decision review' }))).toBe(true);

  await user.click(screen.getByRole('button', { name: 'Back to recommendations' }));
  expect(layout(container).getAttribute('data-has-selection')).toBe('false');
  expect(screen.queryByRole('region', { name: 'Recommendation decision review' })).toBeNull();
});

it('does nothing when a row that cannot be reviewed is clicked (no permission on that recommendation)', async () => {
  const user = userEvent.setup();
  const { container } = show();
  await user.click(await screen.findByText('Command b'));
  expect(layout(container).getAttribute('data-has-selection')).toBe('false');
  expect(screen.queryByRole('region', { name: 'Recommendation decision review' })).toBeNull();
  // Only the reviewable row is marked as one that opens (the default view lists what awaits review).
  const opens = (text: string) => [...container.querySelectorAll('tbody tr')].find(tr => tr.textContent?.includes(text))?.hasAttribute('data-reviewable');
  expect([opens('Command a'), opens('Command b')]).toEqual([true, false]);
});

it('the Review button still opens it for keyboard and screen-reader users, and clicking it does not open it twice', async () => {
  const user = userEvent.setup();
  show();
  await user.click(await screen.findByRole('button', { name: 'Review' }));
  expect(await screen.findByRole('region', { name: 'Recommendation decision review' })).toBeTruthy();
  expect(screen.getAllByRole('region', { name: 'Recommendation decision review' })).toHaveLength(1);
});

it('keeps the long columns in the list marked so they fold away beside the decision', async () => {
  const { container } = show();
  await screen.findByText('Command a');
  const secondary = [...container.querySelectorAll('thead th.run-col-secondary')].map(th => th.textContent);
  expect(secondary).toEqual(['Logic Module', 'Reason code', 'Created / expiry', 'Decision actor']);
  // The status, asset and suggested change stay in the narrow list.
  const kept = [...container.querySelectorAll('thead th:not(.run-col-secondary)')].map(th => th.textContent);
  expect(kept.slice(0, 3)).toEqual(['Status', 'Asset', 'Suggested change']);
});

it('without permission to decide the page is a plain list: no pane, no clickable rows', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(items))));
  const { container } = render(<RecommendationsPage userId={userId} permissions={['recommendations.read']} />);
  await screen.findByText('Command a');
  expect([...container.querySelectorAll('tbody tr')].some(tr => tr.hasAttribute('data-reviewable'))).toBe(false);
  await userEvent.setup().click(screen.getByText('Command a'));
  expect(layout(container).getAttribute('data-has-selection')).toBe('false');
  expect(within(container).queryByRole('button', { name: 'Review' })).toBeNull();
});

it('the page is accessible with the decision open beside the list', async () => {
  const { container } = show();
  await userEvent.setup().click(await screen.findByText('Command a'));
  await screen.findByRole('region', { name: 'Recommendation decision review' });
  expect(await accessibilityViolations(container)).toEqual([]);
});

it('shows a recommendation that still needs a decision as needing a look, and a decided one without that emphasis', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(items))));
  const { container } = render(<RecommendationsPage userId={userId} permissions={['recommendations.read']} />);
  await screen.findByText('Command a');
  expect(container.querySelector('tbody tr .status-indicator')?.getAttribute('data-tone')).toBe('warning');
  cleanup();
  // The default view lists only what awaits review, so the decided row is rendered through the table itself.
  const decided = render(<RecommendationInboxTable items={[items[2]] as never} nowUtc={Date.now()} />);
  expect(decided.container.querySelector('tbody tr .status-indicator')?.getAttribute('data-tone')).toBe('success');
});

it('offers Clear filters only when the filters differ from the default, and restores the default', async () => {
  const user = userEvent.setup();
  show();
  await screen.findByText('Command a');
  expect(screen.queryByRole('button', { name: 'Clear filters' })).toBeNull();
  await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'zzz');
  expect(screen.getByText('Showing 0 of 3 loaded recommendations (up to 50).')).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Clear filters' }));
  expect((screen.getByRole('searchbox', { name: 'Search' }) as HTMLInputElement).value).toBe('');
  expect(screen.queryByRole('button', { name: 'Clear filters' })).toBeNull();
  expect(await screen.findByText('Command a')).toBeTruthy();
});

it('shows the asset by name and the reason in words, keeping the raw values for hover only', async () => {
  const { RecommendationDecisionReviewPanel } = await import('../../src/views/RecommendationDecisionReviewPanel');
  const guid = '00000000-0000-4000-8000-000000000011';
  render(<RecommendationDecisionReviewPanel item={row('a', { assetId: guid }) as never} assetName="Well 7-12H"
    onCancel={() => {}} onSaved={() => {}} onConflict={() => {}} />);
  const asset = screen.getByText('Well 7-12H', { selector: 'dd' });
  expect(asset.getAttribute('title')).toBe(guid);
  expect(document.body.textContent).not.toContain(guid);
  const reason = screen.getByText('Pump efficiency low', { selector: 'dd' });
  expect(reason.getAttribute('title')).toBe('pump_efficiency_low');
});
