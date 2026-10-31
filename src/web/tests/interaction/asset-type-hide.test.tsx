import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App } from '../../src/App';

const userId = '66666666-6666-4666-8666-666666666666';
const typeId = '88888888-8888-4888-8888-888888888888';
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

function host(permissions: string[], type: Record<string, unknown>, failure?: Response) {
  const calls: { url: string; method: string; body: unknown }[] = [];
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\eng', isActive: true, roles: [], permissions, assetScopeIds: [] });
    if (url.includes('capabilities')) return response({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
    if (url.startsWith(`/api/v1/asset-types/${typeId}`) && init?.method) {
      calls.push({ url, method: init.method, body: init.body ? JSON.parse(String(init.body)) : null });
      return failure ?? new Response(null, { status: 204 });
    }
    if (url.includes('/asset-types')) return response([{ id: typeId, key: 'well', displayName: 'Well', schemaVersion: 1, attributeSchemaJson: '{}', isActive: true, createdAtUtc: '2026-10-01T00:00:00Z', ...type }]);
    return response([]);
  }));
  return calls;
}

it('an administrator hides an unused-by-name type with a note, which the server receives trimmed', async () => {
  const calls = host(['assets.read', 'asset-types.hide'], {});
  window.location.hash = '#assets/types';
  render(<App />);
  fireEvent.click(await screen.findByRole('button', { name: 'Show details of Well' }));
  fireEvent.change(screen.getByLabelText('Note (optional, up to 200 characters)'), { target: { value: '  superseded  ' } });
  fireEvent.click(screen.getByRole('button', { name: 'Hide this type' }));
  await waitFor(() => expect(calls).toEqual([{ url: `/api/v1/asset-types/${typeId}/hide`, method: 'PUT', body: { note: 'superseded' } }]));
});

it('hidden types are listed only on request and a manager can restore one', async () => {
  const calls = host(['assets.read', 'asset-types.manage'], { isActive: false, hiddenBy: 'FIELD\\admin', hiddenAtUtc: '2026-10-02T00:00:00Z', hiddenNote: 'old' });
  window.location.hash = '#assets/types';
  render(<App />);
  await screen.findByText('No asset types are configured.');
  fireEvent.click(screen.getByLabelText('Show hidden types'));
  fireEvent.click(await screen.findByRole('button', { name: 'Show details of Well' }));
  expect(screen.getByText(/Note: old/)).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Restore this type' }));
  await waitFor(() => expect(calls).toEqual([{ url: `/api/v1/asset-types/${typeId}/unhide`, method: 'PUT', body: null }]));
});

it('an unused type can be deleted only after confirming, and operators are offered no actions', async () => {
  const calls = host(['assets.read', 'asset-types.manage'], {});
  window.location.hash = '#assets/types';
  render(<App />);
  fireEvent.click(await screen.findByRole('button', { name: 'Show details of Well' }));
  fireEvent.click(screen.getByRole('button', { name: 'Delete this type' }));
  expect(calls).toHaveLength(0);
  fireEvent.click(screen.getByRole('button', { name: 'Yes, delete it' }));
  await waitFor(() => expect(calls).toEqual([{ url: `/api/v1/asset-types/${typeId}`, method: 'DELETE', body: null }]));
});

it('a type that assets have used explains why it cannot be deleted', async () => {
  host(['assets.read', 'asset-types.manage'], {}, response({ code: 'ASSET_TYPE_IN_USE' }, 409));
  window.location.hash = '#assets/types';
  render(<App />);
  fireEvent.click(await screen.findByRole('button', { name: 'Show details of Well' }));
  fireEvent.click(screen.getByRole('button', { name: 'Delete this type' }));
  fireEvent.click(screen.getByRole('button', { name: 'Yes, delete it' }));
  const actions = screen.getByLabelText('Type actions');
  await waitFor(() => expect(within(actions).getByRole('alert').textContent).toContain('Hide it instead'));
});

it('without either permission no type actions appear', async () => {
  host(['assets.read'], {});
  window.location.hash = '#assets/types';
  render(<App />);
  fireEvent.click(await screen.findByRole('button', { name: 'Show details of Well' }));
  expect(screen.queryByLabelText('Type actions')).toBeNull();
  expect(screen.queryByLabelText('Show hidden types')).toBeNull();
});
