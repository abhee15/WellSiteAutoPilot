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

function host(requests: { url: string; init?: RequestInit }[], existing: unknown[], permissions: string[] = ['assets.read']) {
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    requests.push({ url, init });
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\link', isActive: true, roles: [], permissions, assetScopeIds: [] });
    if (url.includes('capabilities')) return response({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
    if (url.includes('/asset-types')) return response([{ id: typeId, key: 'well', displayName: 'Well', schemaVersion: 1, isActive: true }]);
    if (url === `/api/v1/asset-views/${viewId}/copy` && init?.method === 'POST') {
      const body = JSON.parse(String(init.body)) as { name: string };
      return response({ ...saved, id: '55555555-5555-4555-8555-555555555557', name: body.name, visibility: 'personal', isOwner: true, canEdit: true }, 201);
    }
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

it('opens a new View after Connection import with its owner preselected but requires Save', async () => {
  const requests: { url: string; init?: RequestInit }[] = [];
  host(requests, []);
  window.location.hash = '#assets/new-view-for-connection%3A' + connectionId;
  const user = userEvent.setup();
  render(<App />);

  const form = await screen.findByRole('form', { name: 'Create a View' });
  expect((within(form).getByLabelText('Houston Production') as HTMLInputElement).checked).toBe(true);
  expect(requests.some(request => request.url === '/api/v1/asset-views' && request.init?.method === 'POST'))
    .toBe(false);
  await user.type(within(form).getByLabelText('View name'), 'After ForeSite import');
  await user.click(within(form).getByRole('button', { name: 'Save View' }));

  const savedRequest = await waitFor(() => {
    const post = requests.find(request => request.url === '/api/v1/asset-views' && request.init?.method === 'POST');
    expect(post).toBeDefined();
    return post!;
  });
  const body = JSON.parse(String(savedRequest.init?.body)) as {
    name: string; definition: { connections: string[]; search: string | null; grouping: string[] }
  };
  expect(body.name).toBe('After ForeSite import');
  expect(body.definition.connections).toEqual([connectionId]);
  expect(body.definition.search).toBeNull();
  expect(body.definition.grouping).toEqual([]);
  await waitFor(() => expect(window.location.hash).toBe('#assets'));
  expect(screen.queryByRole('form', { name: 'Create a View' })).toBeNull();
});

it('can cancel a post-import View without creating or saving anything', async () => {
  const requests: { url: string; init?: RequestInit }[] = [];
  host(requests, []);
  window.location.hash = '#assets/new-view-for-connection%3A' + connectionId;
  render(<App />);
  const form = await screen.findByRole('form', { name: 'Create a View' });
  await userEvent.setup().click(within(form).getByRole('button', { name: 'Cancel' }));
  await waitFor(() => expect(window.location.hash).toBe('#assets'));
  expect(screen.queryByRole('form', { name: 'Create a View' })).toBeNull();
  expect(requests.some(request => request.url === '/api/v1/asset-views' && request.init?.method === 'POST'))
    .toBe(false);
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

it('shares a View only for people who may share, and says what that means', async () => {
  const plain: { url: string; init?: RequestInit }[] = [];
  host(plain, [], ['assets.read']);
  window.location.hash = '#assets';
  const user = userEvent.setup();
  const first = render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Create View' }));
  const form = await screen.findByRole('form', { name: 'Create a View' });
  expect(within(form).queryByLabelText(/Share with everyone/)).toBeNull();
  first.unmount();
  cleanup();

  const requests: { url: string; init?: RequestInit }[] = [];
  host(requests, [], ['assets.read', 'asset-views.share']);
  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Create View' }));
  const sharing = await screen.findByRole('form', { name: 'Create a View' });
  await user.type(within(sharing).getByLabelText('View name'), 'For the team');
  await user.click(within(sharing).getByLabelText(/Share with everyone who can read Assets/));
  await user.click(within(sharing).getByRole('button', { name: 'Save View' }));
  await waitFor(() => expect(requests.some(request => request.init?.method === 'POST')).toBe(true));
  const body = JSON.parse(String(requests.find(request => request.init?.method === 'POST')!.init?.body)) as { visibility: string };
  expect(body.visibility).toBe('shared');
});

it("someone else's shared View is read-only: it is listed under Shared views and can be copied", async () => {
  const requests: { url: string; init?: RequestInit }[] = [];
  host(requests, [{ ...saved, visibility: 'shared', ownerName: 'Pat Engineer', isOwner: false, canEdit: false, hiddenConnectionCount: 2 }]);
  window.location.hash = '#assets';
  const user = userEvent.setup();
  render(<App />);
  const selector = await screen.findByLabelText('View');
  await waitFor(() => expect(within(selector).queryByText('By owner · Pat Engineer')).toBeTruthy());
  await user.selectOptions(selector, viewId);
  expect(screen.queryByRole('button', { name: 'Edit view' })).toBeNull();
  expect((await screen.findAllByText('2 Connection(s) named in this View are not visible to you.')).length).toBeGreaterThan(0);
  await user.click(screen.getByRole('button', { name: 'Save a copy' }));
  const form = await screen.findByRole('form', { name: 'Edit view' });
  expect(within(form).queryByRole('button', { name: 'Delete View' })).toBeNull();
  expect((within(form).getByLabelText('View name') as HTMLInputElement).value).toBe('By owner (copy)');
  await user.click(within(form).getByRole('button', { name: 'Save my copy' }));
  await waitFor(() => expect(requests.some(request => request.url.endsWith('/copy'))).toBe(true));
  const body = JSON.parse(String(requests.find(request => request.url.endsWith('/copy'))!.init?.body)) as { name: string };
  expect(body.name).toBe('By owner (copy)');
});
