import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from '../../src/App';

const userId = '99999999-9999-4999-8999-999999999999';
const assetId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

function setup(assetsStatus: () => number, release?: Promise<void>) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\keep', isActive: true, roles: [], permissions: ['assets.read'], assetScopeIds: [] });
    if (url.includes('system/info')) return response({});
    if (url.includes('capabilities')) return response({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
    if (url.includes('/asset-types')) return response([]);
    if (url.includes('/assets?')) {
      if (release) await release;
      const status = assetsStatus();
      return status === 200
        ? response([{ id: assetId, assetTypeId: 't', name: 'North Well', isActive: true, createdAtUtc: '2026-10-01T00:00:00Z' }])
        : response({}, status);
    }
    return response([]);
  }));
}

it('shows Loading, not an empty-state claim, until the first read completes', async () => {
  window.location.hash = '#assets';
  let open: () => void = () => {};
  const release = new Promise<void>(resolve => { open = resolve; });
  setup(() => 200, release);
  render(<App />);
  await screen.findAllByText('Loading…');
  expect(screen.queryByText('No Assets are configured.')).toBeNull();
  open();
  await screen.findByRole('button', { name: 'Open North Well from the hierarchy' });
});

it('a transient failure keeps the last good data and says it may be out of date', async () => {
  window.location.hash = '#assets';
  let status = 200;
  setup(() => status);
  const user = userEvent.setup(); render(<App />);
  await screen.findByRole('button', { name: 'Open North Well from the hierarchy' });
  status = 500;
  await user.click(screen.getByRole('button', { name: 'Refresh' }));
  await screen.findByText(/may be out of date/);
  expect(screen.getByRole('button', { name: 'Open North Well from the hierarchy' })).not.toBeNull();
  expect(screen.queryByText('No Assets are configured.')).toBeNull();
  status = 200;
  await user.click(screen.getByRole('button', { name: 'Refresh' }));
  await waitFor(() => expect(screen.queryByText(/may be out of date/)).toBeNull());
});
