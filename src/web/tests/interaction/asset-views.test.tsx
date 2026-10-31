import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from '../../src/App';

const userId = '66666666-6666-4666-8666-666666666666';
const typeId = '88888888-8888-4888-8888-888888888888';
const connectionId = '99999999-9999-4999-8999-999999999999';
const otherConnection = '99999999-9999-4999-8999-999999999998';
const wellId = '77777777-7777-4777-8777-000000000001';
const otherWellId = '77777777-7777-4777-8777-000000000002';
const groupId = '77777777-7777-4777-8777-000000000003';
const viewId = '55555555-5555-4555-8555-555555555555';
const response = (value: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });
const well = { id: wellId, assetTypeId: typeId, name: 'North Well', parentAssetId: groupId, ownerConnectionId: connectionId, isActive: true, childCount: 0 };
const south = { ...well, id: otherWellId, name: 'South Well' };
const group = { ...well, id: groupId, assetTypeId: 'group-type', name: 'Field', parentAssetId: null, childCount: 2 };
const saved = { id: viewId, connectionId, name: 'ESP Wells', revision: 1, memberCount: 1, hasUnavailableMembers: false,
  createdAtUtc: '2026-10-21T10:00:00Z', updatedAtUtc: '2026-10-21T10:00:00Z', canEdit: true, canDelete: true };
const allPermissions = ['assets.read', 'connections.read', 'asset-views.create', 'asset-views.edit', 'asset-views.delete'];
type Request = { url: string; init?: RequestInit };
function host(requests: Request[], existing: unknown[] = [], permissions = allPermissions, conflict = false) {
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    requests.push({ url, init });
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\link', isActive: true, roles: [], permissions, assetScopeIds: [] });
    if (url.includes('capabilities')) return response({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
    if (url.includes('/asset-types')) return response([{ id: typeId, key: 'well', displayName: 'Well', schemaVersion: 1, isActive: true },
      { id: 'group-type', key: 'group', displayName: 'Group', schemaVersion: 1, isActive: true }]);
    if (url === '/api/v1/asset-views' && init?.method === 'POST') {
      if (conflict) return response({ code: 'ASSET_VIEW_NAME_CONFLICT' }, 409);
      const body = JSON.parse(String(init.body)) as { name: string; assetIds: string[] };
      return response({ ...saved, name: body.name, memberCount: body.assetIds.length }, 201);
    }
    if (url === '/api/v1/asset-views') return response(existing);
    if (url === `/api/v1/asset-views/${viewId}` && init?.method === 'PUT') {
      const body = JSON.parse(String(init.body)) as { name: string; assetIds: string[] };
      return response({ ...saved, name: body.name, revision: 2, memberCount: body.assetIds.length });
    }
    if (url.startsWith(`/api/v1/asset-views/${viewId}?`) && init?.method === 'DELETE') return response(null, 204);
    if (url === `/api/v1/asset-views/${viewId}/selection`) return response({ revision: 1, assetIds: [wellId] });
    if (url.startsWith(`/api/v1/asset-views/${viewId}/members`)) return response([well]);
    if (url === '/api/v1/assets/owners') return response([
      { connectionId, connectionName: 'Houston Production', isEnabled: true, topLevelCount: 1, assetCount: 3 },
      { connectionId: otherConnection, connectionName: 'Other Connection', isEnabled: true, topLevelCount: 0, assetCount: 0 }]);
    if (url.startsWith('/api/v1/assets/children')) {
      if (url.includes(otherConnection)) return response([]);
      return response(url.includes('parentAssetId=' + groupId) ? [well, south] : [group]);
    }
    if (url.startsWith('/api/v1/assets/search')) return response([south]);
    if (url.includes('/assets?')) return response([group, well, south]);
    if (url === '/api/v1/assets/' + wellId) return response(well);
    return response([]);
  }));
}
async function createForm(requests: Request[], conflict = false) {
  host(requests, [], allPermissions, conflict); window.location.hash = '#assets';
  const user = userEvent.setup(); render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Create view' }));
  const form = await screen.findByRole('form', { name: 'Create a view' });
  await user.selectOptions(within(form).getByLabelText('Connection'), connectionId);
  return { user, form };
}
async function openBranch(form: HTMLElement) {
  await userEvent.setup().click(await within(form).findByRole('button', { name: 'Open Field from the hierarchy' }));
  return within(form).findByRole('checkbox', { name: 'Select North Well' });
}
it('saves one connection and the explicit wells; parents remain navigation and there is no sharing toggle', async () => {
  const requests: Request[] = []; const { user, form } = await createForm(requests);
  expect((within(form).getByLabelText('Connection') as HTMLSelectElement).multiple).toBe(false);
  expect(within(form).queryByText(/Share with everyone/)).toBeNull();
  expect(within(form).queryByLabelText('Asset types')).toBeNull();
  expect(requests.some(request => request.init?.method === 'POST')).toBe(false);
  await user.type(within(form).getByLabelText('View name'), '  Selected ESP wells ');
  await user.click(await openBranch(form));
  expect(within(form).queryByRole('checkbox', { name: 'Select Field' })).toBeNull();
  await user.click(within(form).getByRole('button', { name: 'Save view' }));
  await waitFor(() => expect(requests.some(request => request.init?.method === 'POST')).toBe(true));
  expect(JSON.parse(String(requests.find(request => request.init?.method === 'POST')!.init?.body)))
    .toEqual({ name: 'Selected ESP wells', connectionId, assetIds: [wellId] });
});
it('keeps selection across hierarchy collapse and scoped search, and clears it when changing Connection', async () => {
  const requests: Request[] = []; const { user, form } = await createForm(requests);
  await user.click(await openBranch(form));
  await user.click(within(form).getByRole('button', { name: 'Hide children of Field' }));
  await user.type(within(form).getByLabelText('Find wells in this connection'), 'South');
  await user.click(await within(form).findByRole('checkbox', { name: 'Select South Well' }));
  expect(within(form).getByText('Selected wells (2)')).toBeTruthy();
  expect(requests.some(request => request.url.includes('/assets/search?') && request.url.includes('ownerConnectionId=' + connectionId))).toBe(true);
  await user.clear(within(form).getByLabelText('Find wells in this connection'));
  expect((await openBranch(form) as HTMLInputElement).checked).toBe(true);
  await user.selectOptions(within(form).getByLabelText('Connection'), otherConnection);
  expect(within(form).getByText('Selected wells (0)')).toBeTruthy();
});
it('selects descendant wells under an unopened group and allows exceptions', async () => {
  const requests: Request[] = []; const { user, form } = await createForm(requests);
  await user.click(await within(form).findByRole('button', { name: 'Select wells under Field' }));
  await within(form).findByText('Selected wells (2)');
  await user.click(within(form).getByRole('button', { name: 'Remove South Well from selection' }));
  expect(within(form).getByText('Selected wells (1)')).toBeTruthy();
});
it('keeps the form and selection when a name conflicts', async () => {
  const requests: Request[] = []; const { user, form } = await createForm(requests, true);
  await user.type(within(form).getByLabelText('View name'), 'Taken'); await user.click(await openBranch(form));
  await user.click(within(form).getByRole('button', { name: 'Save view' }));
  expect((await within(form).findByRole('alert')).textContent).toContain('already exists in this connection');
  expect(within(form).getByText('Selected wells (1)')).toBeTruthy();
});
it('readers see Connection Views without a create or edit permission', async () => {
  const requests: Request[] = []; host(requests, [{ ...saved, canEdit: false, canDelete: false }], ['assets.read', 'connections.read']);
  window.location.hash = '#assets'; const user = userEvent.setup(); render(<App />);
  const selector = await screen.findByLabelText('View');
  await waitFor(() => expect(within(selector).queryByText('ESP Wells · Houston Production')).toBeTruthy());
  expect(screen.queryByRole('button', { name: 'Create view' })).toBeNull();
  await user.selectOptions(selector, viewId);
  expect(await screen.findByRole('navigation', { name: 'Assets in this view' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Edit view' })).toBeNull();
  expect(screen.queryByText('My views')).toBeNull();
});
it('a permitted non-creator edits explicit membership with the expected revision', async () => {
  const requests: Request[] = []; host(requests, [saved]); window.location.hash = '#assets';
  const user = userEvent.setup(); render(<App />);
  const selector = await screen.findByLabelText('View'); await waitFor(() => expect(within(selector).queryByText(/ESP Wells/)).toBeTruthy());
  await user.selectOptions(selector, viewId); await user.click(screen.getByRole('button', { name: 'Edit view' }));
  const form = await screen.findByRole('form', { name: 'Edit view' });
  await within(form).findByText('Selected wells (1)');
  expect((within(form).getByLabelText('Connection') as HTMLSelectElement).disabled).toBe(true);
  await user.click(await openBranch(form));
  expect(within(form).getByText('Selected wells (0)')).toBeTruthy();
  await user.click(within(form).getByRole('checkbox', { name: 'Select South Well' }));
  await user.click(within(form).getByRole('button', { name: 'Save view' }));
  await waitFor(() => expect(requests.some(request => request.init?.method === 'PUT')).toBe(true));
  expect(JSON.parse(String(requests.find(request => request.init?.method === 'PUT')!.init?.body)))
    .toEqual({ expectedRevision: 1, name: 'ESP Wells', assetIds: [otherWellId] });
});
it('post-import navigation preselects the Connection without selecting or saving wells', async () => {
  const requests: Request[] = []; host(requests);
  window.location.hash = '#assets/' + encodeURIComponent('new-view:' + connectionId); render(<App />);
  const form = await screen.findByRole('form', { name: 'Create a view' });
  expect((within(form).getByLabelText('Connection') as HTMLSelectElement).value).toBe(connectionId);
  expect(within(form).getByText('Selected wells (0)')).toBeTruthy();
  expect(requests.some(request => request.init?.method === 'POST')).toBe(false);
  await userEvent.setup().click(within(form).getByRole('button', { name: 'Cancel' }));
  expect(requests.some(request => request.init?.method === 'POST')).toBe(false);
});

it('allows an authorized editor to remove an unavailable well before saving', async () => {
  const requests: Request[] = []; host(requests, [{ ...saved, hasUnavailableMembers: true }]);
  const base = vi.mocked(fetch).getMockImplementation()!;
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => url.endsWith('/selection')
    ? response({ revision: 1, assetIds: [wellId, otherWellId], unavailableMembers: [{ id: otherWellId, name: 'South Well' }] }) : base(url, init)));
  window.location.hash = '#assets'; const user = userEvent.setup(); render(<App />);
  const selector = await screen.findByLabelText('View'); await waitFor(() => expect(within(selector).queryByText(/ESP Wells/)).toBeTruthy());
  await user.selectOptions(selector, viewId); await user.click(screen.getByRole('button', { name: 'Edit view' }));
  const form = await screen.findByRole('form', { name: 'Edit view' }); await within(form).findByText('Selected wells (2)');
  expect((within(form).getByRole('button', { name: 'Save view' }) as HTMLButtonElement).disabled).toBe(true);
  expect(within(form).getByText(/Inactive or ineligible: remove before saving/)).toBeTruthy();
  await user.click(within(form).getByRole('button', { name: 'Remove South Well from selection' }));
  expect((within(form).getByRole('button', { name: 'Save view' }) as HTMLButtonElement).disabled).toBe(false);
});
