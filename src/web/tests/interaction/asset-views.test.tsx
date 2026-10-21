import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from '../../src/App';

const userId = '66666666-6666-4666-8666-666666666666';
const typeId = '88888888-8888-4888-8888-888888888888';
const connectionId = '99999999-9999-4999-8999-999999999999';
const wellId = '77777777-7777-4777-8777-000000000001';
const viewId = '55555555-5555-4555-8555-555555555555';
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

const well = { id: wellId, assetTypeId: typeId, name: 'North Well', parentAssetId: null, ownerConnectionId: connectionId, isActive: true };
const definition = { version: 1, connections: [], assetTypeIds: [], activity: 'all', search: null, grouping: ['connection'] };
const saved = { id: viewId, name: 'By owner', definition, revision: 1, createdAtUtc: '2026-10-21T10:00:00Z', updatedAtUtc: '2026-10-21T10:00:00Z' };

function host(requests: { url: string; init?: RequestInit }[], existing: unknown[]) {
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    requests.push({ url, init });
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\link', isActive: true, roles: [], permissions: ['assets.read'], assetScopeIds: [] });
    if (url.includes('capabilities')) return response({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
    if (url.includes('/asset-types')) return response([{ id: typeId, key: 'well', displayName: 'Well', schemaVersion: 1, isActive: true }]);
    if (url === '/api/v1/asset-views' && init?.method === 'POST') {
      const body = JSON.parse(String(init.body)) as { name: string; definition: unknown };
      return response({ ...saved, id: '55555555-5555-4555-8555-555555555556', name: body.name, definition: body.definition }, 201);
    }
    if (url === '/api/v1/asset-views') return response(existing);
    if (url.startsWith(`/api/v1/asset-views/${viewId}/nodes`)) {
      return url.includes('path=') ? response({ level: null, groups: [], assets: [{ ...well, childCount: 0 }] })
        : response({ level: 'connection', groups: [{ key: connectionId, label: 'Houston Production', count: 1 }], assets: [] });
    }
    if (url === '/api/v1/assets/owners') return response([{ connectionId, connectionName: 'Houston Production', isEnabled: true, topLevelCount: 1, assetCount: 1 }]);
    if (url.includes('/assets?')) return response([well]);
    if (url === '/api/v1/assets/' + wellId + '/source') return response({ connectionId, connectionName: 'Houston Production', resourceKind: 'foresite.well', externalId: 'FS-1001', importedAtUtc: '2026-10-21T10:00:00Z' });
    if (url === '/api/v1/assets/' + wellId) return response(well);
    return response([]);
  }));
}

it('opens a saved View: groups load when opened and a chosen asset opens the asset page', async () => {
  const requests: { url: string }[] = [];
  host(requests, [saved]);
  window.location.hash = '#assets';
  render(<App />);
  const selector = await screen.findByLabelText('View');
  await waitFor(() => expect(within(selector).queryByText('By owner')).toBeTruthy());
  await userEvent.setup().selectOptions(selector, viewId);
  const tree = await screen.findByRole('navigation', { name: 'Assets in this View' });
  const group = await within(tree).findByRole('button', { name: 'Open Houston Production from the hierarchy' });
  expect(requests.some(request => request.url.includes('path='))).toBe(false);
  await userEvent.setup().click(group);
  const asset = await within(tree).findByRole('button', { name: 'Open North Well from the hierarchy' });
  expect(requests.some(request => request.url.includes('path=' + connectionId))).toBe(true);
  await userEvent.setup().click(asset);
  expect(window.location.hash).toBe('#assets/' + wellId);
  // The location starts at the owning Connection, so equal names in two Connections stay distinguishable.
  expect(await screen.findByText('Located under Houston Production')).toBeTruthy();
  // And where it came from in the outside system.
  expect(await screen.findByText('FS-1001')).toBeTruthy();
  expect(screen.getByText(/Source \(foresite\.well\):/)).toBeTruthy();
});

it('creates a View from the dedicated flow and selects it', async () => {
  const requests: { url: string; init?: RequestInit }[] = [];
  host(requests, []);
  window.location.hash = '#assets';
  const user = userEvent.setup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Create View' }));
  const form = await screen.findByRole('form', { name: 'Create a View' });
  await user.type(within(form).getByLabelText('View name'), '  Wells by owner ');
  await user.click(within(form).getByLabelText('Houston Production'));
  await user.click(within(form).getByRole('button', { name: 'Add Connection' }));
  await user.click(within(form).getByRole('button', { name: 'Add Status' }));
  await user.click(within(form).getByRole('button', { name: 'Move Status up' }));
  await user.click(within(form).getByRole('button', { name: 'Save View' }));
  await waitFor(() => expect(requests.some(request => request.init?.method === 'POST')).toBe(true));
  const post = requests.find(request => request.init?.method === 'POST')!;
  const body = JSON.parse(String(post.init?.body)) as { name: string; definition: { connections: string[]; grouping: string[] } };
  expect(body.name).toBe('Wells by owner');
  expect(body.definition.connections).toEqual([connectionId]);
  expect(body.definition.grouping).toEqual(['status', 'connection']);
  await waitFor(() => expect(screen.queryByRole('form', { name: 'Create a View' })).toBeNull());
  expect((screen.getByLabelText('View') as HTMLSelectElement).selectedOptions[0].textContent).toBe('Wells by owner');
});

it('says so when the name is already used and keeps what was typed', async () => {
  const requests: { url: string; init?: RequestInit }[] = [];
  host(requests, []);
  const base = vi.mocked(fetch).getMockImplementation()!;
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) =>
    url === '/api/v1/asset-views' && init?.method === 'POST'
      ? response({ code: 'ASSET_VIEW_NAME_CONFLICT' }, 409) : base(url, init)));
  window.location.hash = '#assets';
  const user = userEvent.setup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Create View' }));
  const form = await screen.findByRole('form', { name: 'Create a View' });
  await user.type(within(form).getByLabelText('View name'), 'Taken');
  await user.click(within(form).getByRole('button', { name: 'Save View' }));
  expect((await within(form).findByRole('alert')).textContent).toBe('You already have a View with that name.');
  expect((within(form).getByLabelText('View name') as HTMLInputElement).value).toBe('Taken');
});

it('turns the browser-local filters into a View: only what can carry over, and nothing saved until confirmed', async () => {
  const requests: { url: string; init?: RequestInit }[] = [];
  host(requests, []);
  window.location.hash = '#assets';
  const user = userEvent.setup();
  render(<App />);
  await user.click(await screen.findByText('Filters and saved views'));
  await user.type(await screen.findByLabelText('Filter name'), 'Pumps');
  await user.selectOptions(screen.getByLabelText('Status'), 'active');
  await user.selectOptions(screen.getByLabelText('Hierarchy'), 'roots');
  await user.click(screen.getByRole('button', { name: 'Save as View…' }));
  const form = await screen.findByRole('form', { name: 'Create a View' });
  expect((within(form).getByLabelText('View name') as HTMLInputElement).value).toBe('Pumps');
  expect((within(form).getByLabelText('Show') as HTMLSelectElement).value).toBe('active');
  expect(within(form).getByRole('note').textContent).toContain('carry over');
  expect(requests.some(request => request.init?.method === 'POST')).toBe(false);
  await user.click(within(form).getByRole('button', { name: 'Save View' }));
  await waitFor(() => expect(requests.some(request => request.init?.method === 'POST')).toBe(true));
  const post = requests.find(request => request.init?.method === 'POST')!;
  const body = JSON.parse(String(post.init?.body)) as { name: string; definition: { activity: string; grouping: string[] } };
  expect(body.name).toBe('Pumps');
  expect(body.definition.activity).toBe('active');
  expect(body.definition.grouping).toEqual([]);
});
