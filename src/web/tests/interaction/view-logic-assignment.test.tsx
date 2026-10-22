import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfiguredLogicSetupPanel } from '../../src/views/ConfiguredLogicSetupPanel';

const id = (value: number) => `00000000-0000-4000-8000-${String(value).padStart(12, '0')}`;
const connectionId = id(1), viewId = id(2), wellId = id(3), secondId = id(4), typeId = id(5), configId = id(6), revisionId = id(7);
const manifest = { moduleId: 'view-module', version: '1.0.0', executionProfile: 'ScheduledOneShot', requiredAdapterKey: 'foresite',
  assetRequirements: [{ role: 'well', minimumCount: 1, maximumCount: 2, requiredAssetTypeKey: 'well' }], dataRequirements: [] };
const module = { id: id(8), moduleId: manifest.moduleId, version: manifest.version, displayName: 'ForeSite group module', isEnabled: true, manifestJson: JSON.stringify(manifest) };
const first = { id: wellId, name: 'Loaded well', assetTypeId: typeId, ownerConnectionId: connectionId, isActive: true };
const second = { ...first, id: secondId, name: 'Well beyond workspace snapshot' };
const view = { id: viewId, connectionId, name: 'ESP group', revision: 2, memberCount: 2, hasUnavailableMembers: false,
  createdAtUtc: '2026-10-21T10:00:00Z', updatedAtUtc: '2026-10-21T10:00:00Z', canEdit: false, canDelete: false };
const connection = { id: connectionId, name: 'ForeSite Production', adapterKey: 'foresite', settingsVersion: 1, settings: {},
  isEnabled: true, revision: 1, createdAtUtc: view.createdAtUtc, updatedAtUtc: view.updatedAtUtc, lastTest: null, lastTestIsCurrent: false };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
type Request = { url: string; init?: RequestInit };
function host(requests: Request[], selectionRevision = 2, receiptRevision = 2) {
  let saved: unknown;
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    requests.push({ url, init });
    if (url === '/api/v1/asset-views') return response([view, { ...view, id: id(20), name: 'Too many wells', memberCount: 100 },
      { ...view, id: id(21), name: 'Wrong adapter', connectionId: id(22) }]);
    if (url === '/api/v1/connections') return response([connection, { ...connection, id: id(22), adapterKey: 'petrovisor' }]);
    if (url.endsWith('/selection')) return response({ revision: selectionRevision, assetIds: [wellId, secondId] });
    if (url.includes('/members?')) return response([first, second]);
    if (url === '/api/v1/configured-logic' && init?.method === 'POST') {
      const body = JSON.parse(String(init.body));
      saved = { id: configId, name: body.name, activeRevisionId: null, revisions: [{ id: revisionId, revisionNumber: 1,
        moduleId: module.moduleId, moduleVersion: module.version, moduleManifestJson: body.moduleManifestJson,
        parametersJson: body.parametersJson, mode: 'Shadow', status: 'Draft', assetBindings: body.assetBindings,
        dataBindings: [], view: { viewId, revision: receiptRevision, connectionId, role: 'well' } }] };
      return response(saved, 201);
    }
    if (url === '/api/v1/configured-logic/' + configId) return response(saved);
    throw new Error('Unexpected request ' + url);
  }));
}
function editor(onSaved = vi.fn(), canReadViews = true) {
  render(<ConfiguredLogicSetupPanel modules={[module]} assets={[first]} assetTypes={[{ id: typeId, key: 'well', isActive: true }]}
    onSaved={onSaved} onCancel={vi.fn()} canReadViews={canReadViews} />);
}
async function chooseModule() {
  const user = userEvent.setup();
  await user.selectOptions(screen.getByRole('combobox', { name: 'Approved module version' }), JSON.stringify([module.moduleId, module.version]));
  await user.type(screen.getByRole('textbox', { name: 'Configuration name' }), 'Group assignment');
  return user;
}
it('assigns the complete saved selection beyond the workspace snapshot and verifies its pinned revision', async () => {
  const requests: Request[] = []; host(requests); const onSaved = vi.fn(); editor(onSaved);
  const user = await chooseModule(); const selector = await screen.findByLabelText('Target View');
  await waitFor(() => expect(within(selector).queryByText(/ESP group/)).toBeTruthy());
  await user.selectOptions(selector, viewId);
  await screen.findByText(/View revision 2 · 2 selected wells/);
  await user.click(screen.getByRole('button', { name: 'Save Draft' }));
  await waitFor(() => expect(onSaved).toHaveBeenCalledOnce());
  const body = JSON.parse(String(requests.find(request => request.init?.method === 'POST')!.init?.body));
  expect(body.view).toEqual({ viewId, expectedRevision: 2, role: 'well' });
  expect(body.assetBindings.map((binding: { assetId: string }) => binding.assetId)).toEqual([wellId, secondId]);
});
it('disables incompatible adapter and cardinality choices instead of changing the module requirements', async () => {
  const requests: Request[] = []; host(requests); editor(); await chooseModule();
  const selector = await screen.findByLabelText('Target View');
  await waitFor(() => expect(within(selector).queryByText(/Too many wells/)).toBeTruthy());
  expect((within(selector).getByRole('option', { name: /Too many wells/ }) as HTMLOptionElement).disabled).toBe(true);
  expect((within(selector).getByRole('option', { name: /Wrong adapter/ }) as HTMLOptionElement).disabled).toBe(true);
});
it('rejects a stale selection before assigning any subset', async () => {
  const requests: Request[] = []; host(requests, 3); editor(); const user = await chooseModule();
  const selector = await screen.findByLabelText('Target View'); await waitFor(() => expect(within(selector).queryByText(/ESP group/)).toBeTruthy());
  await user.selectOptions(selector, viewId);
  expect((await screen.findByRole('alert')).textContent).toContain('complete View selection could not be loaded');
  expect(requests.some(request => request.init?.method === 'POST')).toBe(false);
});
it('does not enable validation when the persisted View revision differs from the request', async () => {
  const requests: Request[] = []; host(requests, 2, 3); const onSaved = vi.fn(); editor(onSaved); const user = await chooseModule();
  const selector = await screen.findByLabelText('Target View'); await waitFor(() => expect(within(selector).queryByText(/ESP group/)).toBeTruthy());
  await user.selectOptions(selector, viewId); await screen.findByText(/View revision 2 · 2 selected wells/);
  await user.click(screen.getByRole('button', { name: 'Save Draft' }));
  await screen.findByRole('alert'); expect(onSaved).not.toHaveBeenCalled();
  expect(screen.getByRole('button', { name: 'Save Draft' }).matches(':disabled')).toBe(true);
});
it('does not fetch or offer Views without Connection read access', async () => {
  const requests: Request[] = []; host(requests); editor(vi.fn(), false); await chooseModule();
  expect(screen.queryByLabelText('Target View')).toBeNull(); expect(requests).toHaveLength(0);
});
