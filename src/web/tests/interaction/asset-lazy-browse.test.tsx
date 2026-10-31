import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from '../../src/App';

const userId = '66666666-6666-4666-8666-666666666666';
const typeId = '88888888-8888-4888-8888-888888888888';
const guid = (n: number) => `77777777-7777-4777-8777-${String(n).padStart(12, '0')}`;
const padId = guid(500);
const wellId = guid(501);
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

// A full first page (100 assets) means the flat list may be incomplete, so the page browses on the server.
const firstPage = Array.from({ length: 100 }, (_, index) => (
  { id: guid(index), assetTypeId: typeId, name: 'Listed ' + index, isActive: true }));
const pad = { id: padId, assetTypeId: typeId, name: 'Pad 7', parentAssetId: null, isActive: true };
const well = { id: wellId, assetTypeId: typeId, name: 'North Well', parentAssetId: padId, isActive: true };

function host(calls: string[]) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    calls.push(url);
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\link', isActive: true, roles: [], permissions: ['assets.read'], assetScopeIds: [] });
    if (url.includes('capabilities')) return response({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
    if (url.includes('/asset-types')) return response([{ id: typeId, key: 'well', displayName: 'Well', schemaVersion: 1, isActive: true }]);
    if (url.startsWith('/api/v1/assets/children')) {
      return url.includes('parentAssetId=' + padId)
        ? response([{ ...well, childCount: 0 }])
        : response([{ ...pad, childCount: 1 }]);
    }
    if (url.startsWith('/api/v1/assets/search')) {
      return response(url.includes('north') ? [{ ...well, path: [{ id: padId, name: 'Pad 7' }] }] : []);
    }
    if (url === '/api/v1/assets/' + padId) return response(pad);
    if (url === '/api/v1/assets/' + wellId) return response(well);
    if (url.includes('/assets?')) return response(firstPage);
    return response([]);
  }));
}

it('a full first page switches to server browsing: levels load when opened', async () => {
  const calls: string[] = [];
  host(calls);
  window.location.hash = '#assets';
  render(<App />);
  const tree = await screen.findByRole('navigation', { name: 'Asset hierarchy' });
  await within(tree).findByRole('button', { name: 'Open Pad 7 from the hierarchy' });
  expect(within(tree).queryByText('Listed 0')).toBeNull();
  expect(calls.some(url => url.includes('parentAssetId'))).toBe(false);
  await userEvent.setup().click(within(tree).getByRole('button', { name: 'Show children of Pad 7' }));
  const child = await within(tree).findByRole('button', { name: 'Open North Well from the hierarchy' });
  await userEvent.setup().click(child);
  expect(window.location.hash).toBe('#assets/' + wellId);
  const page = (await screen.findByText('Selected asset')).closest('.asset-page') as HTMLElement;
  expect(await within(page).findByText('Located under Pad 7')).toBeTruthy();
});

it('an address to an asset outside the first page opens the asset and reveals it in the tree', async () => {
  const calls: string[] = [];
  host(calls);
  window.location.hash = '#assets/' + wellId;
  render(<App />);
  const page = (await screen.findByText('Selected asset')).closest('.asset-page') as HTMLElement;
  expect(await within(page).findByText('Located under Pad 7')).toBeTruthy();
  const tree = await screen.findByRole('navigation', { name: 'Asset hierarchy' });
  const open = await within(tree).findByRole('button', { name: 'Open North Well from the hierarchy' });
  expect(open.getAttribute('data-selected')).toBe('true');
});

it('search runs on the server and shows where each match sits', async () => {
  const calls: string[] = [];
  host(calls);
  window.location.hash = '#assets';
  render(<App />);
  await screen.findByRole('button', { name: 'Open Pad 7 from the hierarchy' });
  await userEvent.setup().type(screen.getByRole('searchbox', { name: 'Search assets' }), 'north');
  const results = await screen.findByRole('list', { name: 'Matching assets' });
  expect(await within(results).findByText('Well · Pad 7')).toBeTruthy();
  expect(calls.some(url => url.startsWith('/api/v1/assets/search?q=north'))).toBe(true);
  await userEvent.setup().click(within(results).getByRole('button', { name: 'Select asset North Well' }));
  expect(window.location.hash).toBe('#assets/' + wellId);
});

it('an asset that is not there (or not visible) is not shown', async () => {
  host([]);
  window.location.hash = '#assets/' + guid(999);
  render(<App />);
  await screen.findByRole('button', { name: 'Open Pad 7 from the hierarchy' });
  expect(screen.queryByText('Selected asset')).toBeNull();
});
