import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from '../../src/App';

const userId = '66666666-6666-4666-8666-666666666666';
const assetId = '77777777-7777-4777-8777-777777777777';
const typeId = '88888888-8888-4888-8888-888888888888';
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

function signIn(permissions: string[]) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\link', isActive: true, roles: [], permissions, assetScopeIds: [] });
    if (url.includes('system/info')) return response({});
    if (url.includes('capabilities')) return response({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
    if (url.includes('/asset-types')) return response([{ id: typeId, key: 'well', displayName: 'Well', schemaVersion: 1, isActive: true }]);
    if (url.includes('/assets?')) return response([{ id: assetId, assetTypeId: typeId, name: 'North Well', isActive: true, createdAtUtc: '2026-10-01T00:00:00Z' }]);
    return response([]);
  }));
}

it('an address to an asset opens it once the list loads, and clearing it returns to the section address', async () => {
  window.location.hash = '#assets/' + assetId;
  signIn(['assets.read']);
  const user = userEvent.setup(); render(<App />);
  await screen.findByText('Selected Asset');
  expect(window.location.hash).toBe('#assets/' + assetId);
  await user.click(screen.getByRole('button', { name: 'Clear selection' }));
  expect(window.location.hash).toBe('#assets');
  expect(screen.queryByText('Selected Asset')).toBeNull();
});

it('selecting an asset puts it in the address so it can be shared', async () => {
  window.location.hash = '#assets';
  signIn(['assets.read']);
  const user = userEvent.setup(); render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Select Asset North Well' }));
  await waitFor(() => expect(window.location.hash).toBe('#assets/' + assetId));
  await screen.findByText('Selected Asset');
});

it('an unknown record address is kept and shows no selection', async () => {
  window.location.hash = '#assets/00000000-0000-4000-8000-000000000000';
  signIn(['assets.read']);
  render(<App />);
  await screen.findByRole('button', { name: 'Select Asset North Well' });
  expect(screen.queryByText('Selected Asset')).toBeNull();
});

it('the administration tab is part of the address', async () => {
  window.location.hash = '#access/roles';
  signIn(['security.manage']);
  const user = userEvent.setup(); render(<App />);
  const roles = await screen.findByRole('tab', { name: 'Roles and permissions' });
  expect(roles.getAttribute('aria-selected')).toBe('true');
  await user.click(screen.getByRole('tab', { name: 'User assignments' }));
  expect(window.location.hash).toBe('#access');
});
