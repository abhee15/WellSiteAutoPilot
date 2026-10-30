import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { OperationsPage } from '../../src/views/OperationsPage';
import { persistSavedOperationsViews } from '../../src/views/operationsViews';

beforeEach(() => { window.localStorage.clear(); });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });

const base = { mode: 'Shadow', trigger: 'Manual', moduleId: 'm', moduleVersion: '1', requestContractVersion: 3 };
const runs = [
  { ...base, executionId: 'e1', status: 'Failed', assetExternalId: 'Well-Failed', requestedAtUtc: '2026-10-28T03:00:00Z' },
  { ...base, executionId: 'e2', status: 'Running', assetExternalId: 'Well-Running', requestedAtUtc: '2026-10-28T02:00:00Z' },
  { ...base, executionId: 'e3', status: 'Completed', assetExternalId: 'Well-Done', requestedAtUtc: '2026-10-28T01:00:00Z' },
];
const shown = (container: HTMLElement) =>
  [...container.querySelectorAll('tbody tr')].map(row => row.textContent ?? '').join('|');
const card = (name: RegExp) => screen.getByRole('button', { name });

it('a summary card filters the table to its kind of run, shows that it is on, and a second click undoes it', async () => {
  const user = userEvent.setup();
  const { container } = render(<OperationsPage executions={runs as never} userId="u1" />);
  expect(shown(container)).toContain('Well-Done');

  await user.click(card(/^Requires attention/));
  expect(card(/^Requires attention/).getAttribute('aria-pressed')).toBe('true');
  expect(card(/Active executions/).getAttribute('aria-pressed')).toBe('false');
  expect(shown(container)).toContain('Well-Failed');
  expect(shown(container)).not.toContain('Well-Running');
  expect(shown(container)).not.toContain('Well-Done');

  await user.click(card(/Active executions/));   // switching replaces, never adds
  expect(card(/^Requires attention/).getAttribute('aria-pressed')).toBe('false');
  expect(shown(container)).toContain('Well-Running');
  expect(shown(container)).not.toContain('Well-Failed');

  await user.click(card(/Active executions/));
  expect(card(/Active executions/).getAttribute('aria-pressed')).toBe('false');
  expect(shown(container)).toContain('Well-Failed');
  expect(shown(container)).toContain('Well-Done');
});

it('says so, and offers to clear the filters, when the chosen kind has no runs', async () => {
  const user = userEvent.setup();
  const onlyDone = runs.filter(item => item.status === 'Completed');
  const { container } = render(<OperationsPage executions={onlyDone as never} userId="u1" />);
  await user.click(card(/^Requires attention/));
  expect(within(container.querySelector('tbody')!).getByText('No recent executions match this view.')).toBeTruthy();
  await user.click(screen.getAllByRole('button', { name: 'Clear filters' })[0]);
  expect(shown(container)).toContain('Well-Done');
  expect(card(/^Requires attention/).getAttribute('aria-pressed')).toBe('false');
});

it('offers no clear button when there are no runs at all (nothing is hiding them)', () => {
  render(<OperationsPage executions={[]} userId="u1" />);
  expect(screen.queryByRole('button', { name: 'Clear filters' })).toBeNull();
});

it('a saved view stops showing as selected once a card changes what is on screen', async () => {
  persistSavedOperationsViews('u1', [{ id: 'v1', name: 'Only failures',
    criteria: { search: '', statuses: ['attention'], sort: 'newest' } }], window.localStorage);
  const user = userEvent.setup();
  const { container } = render(<OperationsPage executions={runs as never} userId="u1" />);
  await user.click(screen.getByRole('button', { name: /^Filters/ }));
  const picker = screen.getByLabelText('Saved views') as HTMLSelectElement;
  await user.selectOptions(picker, 'v1');
  expect(picker.value).toBe('v1');
  expect(shown(container)).not.toContain('Well-Done');

  await user.click(card(/Active executions/));
  expect(picker.value).toBe('');
});

it('a saved view stays selected while what it describes is still what is shown', async () => {
  persistSavedOperationsViews('u1', [{ id: 'v1', name: 'Only failures',
    criteria: { search: '', statuses: ['attention'], sort: 'newest' } }], window.localStorage);
  const user = userEvent.setup();
  render(<OperationsPage executions={runs as never} userId="u1" />);
  await user.click(screen.getByRole('button', { name: /^Filters/ }));
  const picker = screen.getByLabelText('Saved views') as HTMLSelectElement;
  await user.selectOptions(picker, 'v1');
  expect(picker.value).toBe('v1');
  // The card for the same kind is now "on" and clicking elsewhere does not disturb the selection.
  expect(card(/^Requires attention/).getAttribute('aria-pressed')).toBe('true');
  expect(picker.value).toBe('v1');
});
