import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App } from '../../src/App';

const userId = '66666666-6666-4666-8666-666666666666';
const typeId = '88888888-8888-4888-8888-888888888888';
const otherId = '99999999-9999-4999-8999-999999999999';
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

function host(permissions: string[]) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\eng', isActive: true, roles: [], permissions, assetScopeIds: [] });
    if (url.includes('capabilities')) return response({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
    if (url.includes('/asset-types')) return response([
      { id: typeId, key: 'well', displayName: 'Well', schemaVersion: 1, attributeSchemaJson: '{}', isActive: true, createdAtUtc: '2026-10-01T00:00:00Z' },
      { id: otherId, key: 'pad', displayName: 'Pad', schemaVersion: 1, attributeSchemaJson: '{}', isActive: true, createdAtUtc: '2026-10-01T00:00:00Z' },
    ]);
    return response([]);
  }));
}

it('opens an asset type in the details pane beside the list, like the other pages', async () => {
  host(['assets.read', 'asset-types.hide']);
  window.location.hash = '#assets/types';
  const { container } = render(<App />);
  fireEvent.click(await screen.findByRole('button', { name: 'Show details of Well' }));

  const browse = container.querySelector('.split-panes > .split-browse') as HTMLElement;
  const selected = container.querySelector('.split-panes > .split-selected') as HTMLElement;
  expect(browse && selected).toBeTruthy();
  expect(container.querySelector('.split-layout')?.getAttribute('data-has-selection')).toBe('true');
  // The details are in their own pane, not under the table, and the list keeps the selected row marked.
  expect(within(browse).queryByRole('heading', { name: 'Well' })).toBeNull();
  expect(within(selected).getByRole('heading', { name: 'Well' })).toBeTruthy();
  expect(selected.getAttribute('aria-label')).toBe('Selected asset type');
  expect(browse.querySelector('tr[data-selected="true"]')?.textContent).toContain('Well');
  expect(screen.getByRole('button', { name: 'Show details of Well' }).getAttribute('aria-current')).toBe('true');
  expect(screen.getByRole('button', { name: 'Show details of Pad' }).getAttribute('aria-current')).toBeNull();
  // Actions use the shared section and form-action row, not the creation form's grid.
  const actions = within(selected).getByLabelText('Type actions');
  expect(within(actions).getByRole('button', { name: 'Hide this type' })).toBeTruthy();
  expect(selected.querySelector('.asset-create-form')).toBeNull();
  expect(selected.querySelector('.ui-form-actions')).toBeTruthy();
});

it('keeps the selected type in the address, so Back and shared links reopen it', async () => {
  host(['assets.read']);
  window.location.hash = '#assets/types';
  render(<App />);
  fireEvent.click(await screen.findByRole('button', { name: 'Show details of Well' }));
  await waitFor(() => expect(decodeURIComponent(window.location.hash)).toBe(`#assets/types:${typeId}`));

  fireEvent.click(screen.getByRole('button', { name: 'Close details' }));
  await waitFor(() => expect(window.location.hash).toBe('#assets/types'));
  expect(screen.queryByRole('heading', { name: 'Well' })).toBeNull();

  cleanup();
  window.location.hash = `#assets/types:${otherId}`;
  render(<App />);
  expect(await screen.findByRole('heading', { name: 'Pad' })).toBeTruthy();
});

it('opens a type from anywhere on its row except the other buttons', async () => {
  host(['assets.read']);
  window.location.hash = '#assets/types';
  render(<App />);
  const row = (await screen.findByRole('button', { name: 'Show details of Pad' })).closest('tr') as HTMLElement;
  fireEvent.click(within(row).getByText('pad'));
  expect(await screen.findByRole('heading', { name: 'Pad' })).toBeTruthy();
});

it('asks before deleting, in the shared confirmation, and Keep it goes back', async () => {
  host(['assets.read', 'asset-types.manage']);
  window.location.hash = '#assets/types';
  render(<App />);
  fireEvent.click(await screen.findByRole('button', { name: 'Show details of Well' }));
  fireEvent.click(screen.getByRole('button', { name: 'Delete this type' }));
  expect(screen.getByRole('heading', { name: 'Delete Well?' })).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Keep it' }));
  expect(screen.queryByRole('heading', { name: 'Delete Well?' })).toBeNull();
  expect(screen.getByRole('button', { name: 'Delete this type' })).toBeTruthy();
});
