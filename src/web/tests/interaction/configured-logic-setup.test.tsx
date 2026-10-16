import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfiguredLogicSetupPanel } from '../../src/views/ConfiguredLogicSetupPanel';
import { App } from '../../src/App';
import { ConfiguredLogicWorkspace } from '../../src/views/ConfiguredLogicWorkspace';
import type { AssetResponse, AssetTypeResponse, LogicModuleResponse } from '../../src/api/generated/v1/types.gen';
import type { VerifiedConfiguration } from '../../src/views/configuredLogicSetup';
const assetId = '11111111-1111-4111-8111-111111111111';
const typeId = '22222222-2222-4222-8222-222222222222';
const configId = '33333333-3333-4333-8333-333333333333';
const revisionId = '44444444-4444-4444-8444-444444444444';
const otherRevisionId = '55555555-5555-4555-8555-555555555555';
const manifest = { moduleId: 'sample', version: '1.0.0', executionProfile: 'ScheduledOneShot',
  assetRequirements: [{ role: 'well', minimumCount: 1, maximumCount: 1, requiredAssetTypeKey: 'well' }],
  dataRequirements: [{ id: 'fillage', assetRole: 'well', quantity: 'PumpFillage' }] };
const module: LogicModuleResponse = { id: 'module', moduleId: manifest.moduleId, version: manifest.version,
  displayName: 'Approved Sample', isEnabled: true, manifestJson: JSON.stringify(manifest) };
const assets: AssetResponse[] = [{ id: assetId, assetTypeId: typeId, name: 'Well 101', isActive: true }];
const types: AssetTypeResponse[] = [{ id: typeId, key: 'well', isActive: true }];
const draft: VerifiedConfiguration = { id: configId, name: 'Observer', activeRevisionId: null,
  revisions: [{ id: revisionId, revisionNumber: 1, moduleId: 'sample', moduleVersion: '1.0.0',
    moduleManifestJson: module.manifestJson, parametersJson: '{}', mode: 'Shadow', status: 'Draft',
    assetBindings: [{ role: 'well', assetId, parameterOverridesJson: '{}' }],
    dataBindings: [{ requirementId: 'fillage', assetId, providerId: 'provider-simulator',
      providerAssetExternalId: 'WELL-101', providerMappingJson: '{}' }] }] };
const validated: VerifiedConfiguration = { ...draft, revisions: [{ ...draft.revisions[0], status: 'Validated' }] };
const active: VerifiedConfiguration = { ...draft, activeRevisionId: revisionId,
  revisions: [{ ...draft.revisions[0], status: 'Active' }] };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
const props = { modules: [module], assets, assetTypes: types, onSaved: vi.fn(), onCancel: vi.fn() };
beforeEach(() => { vi.clearAllMocks(); });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
async function fill(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByRole('textbox', { name: 'Configuration name' }), 'Observer');
  await user.selectOptions(screen.getByRole('combobox', { name: 'Approved module version' }),
    JSON.stringify(['sample', '1.0.0']));
  await user.selectOptions(screen.getByRole('listbox', { name: 'Assets for well' }), assetId);
  await user.type(screen.getByRole('textbox', { name: 'Provider ID' }), 'provider-simulator');
  await user.type(screen.getByRole('textbox', { name: 'Provider Asset external ID' }), 'WELL-101');
}
function workspace(canManage = true) {
  return render(<ConfiguredLogicWorkspace selectedId={configId} canManage={canManage}
    modules={[module]} assets={assets} assetTypes={types} onClose={vi.fn()} onChanged={async () => {}} />);
}
describe('Configured Logic setup journey', () => {
  it('uses keyboard navigation, saves raw JSON once, verifies persistence and reports the Draft', async () => {
    const user = userEvent.setup();
    const http = vi.fn(async (_url: string, init?: RequestInit) => response(draft, init?.method === 'POST' ? 201 : 200));
    vi.stubGlobal('fetch', http);
    render(<ConfiguredLogicSetupPanel {...props} />);
    await user.tab();
    expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Configuration name' }));
    await fill(user);
    await user.click(screen.getByRole('button', { name: 'Save Draft' }));
    await waitFor(() => expect(props.onSaved).toHaveBeenCalledWith(draft));
    expect(http).toHaveBeenCalledTimes(2);
    const body = JSON.parse(http.mock.calls[0][1]?.body as string);
    expect(body.moduleManifestJson).toBe(module.manifestJson);
    expect(body.assetBindings[0]).toEqual({ role: 'well', assetId, parameterOverridesJson: '{}' });
    expect(body.schedule).toBeUndefined();
    expect(body.parametersJson).toBe('{}');
  });
  it('excludes disabled modules, inactive Assets and wrong Asset types', async () => {
    const user = userEvent.setup();
    vi.stubGlobal('fetch', vi.fn());
    render(<ConfiguredLogicSetupPanel {...props}
      modules={[module, { ...module, id: 'disabled', isEnabled: false, displayName: 'Untrusted' }]}
      assets={[...assets, { id: otherRevisionId, isActive: false, assetTypeId: typeId, name: 'Inactive' },
        { id: revisionId, isActive: true, assetTypeId: 'tank-type', name: 'Tank' }]} />);
    expect(screen.queryByRole('option', { name: /Untrusted/ })).toBeNull();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Approved module version' }), JSON.stringify(['sample', '1.0.0']));
    expect(screen.queryByRole('option', { name: /Inactive|Tank/ })).toBeNull();
    expect(screen.getByRole('option', { name: /Well 101/ })).toBeTruthy();
  });
  it('shows an actionable empty catalog without allowing submission', () => {
    render(<ConfiguredLogicSetupPanel {...props} modules={[]} />);
    expect(screen.getByRole('status').textContent).toContain('No approved');
    expect(screen.getByRole('button', { name: 'Save Draft' })).toHaveProperty('disabled', true);
  });
  it('rejects invalid parameter objects and missing role bindings locally', async () => {
    const user = userEvent.setup(); const http = vi.fn(); vi.stubGlobal('fetch', http);
    render(<ConfiguredLogicSetupPanel {...props} />); await fill(user);
    await user.clear(screen.getByRole('textbox', { name: 'Parameters (JSON object)' }));
    await user.click(screen.getByRole('textbox', { name: 'Parameters (JSON object)' }));
    await user.paste('[1]');
    await user.click(screen.getByRole('button', { name: 'Save Draft' }));
    expect(screen.getByRole('alert').textContent).toContain('Check JSON'); expect(http).not.toHaveBeenCalled();
  });
  it('preserves duplicate properties so authoritative validation can reject them', async () => {
    const user = userEvent.setup();
    const http = vi.fn(async () => response({ code: 'VALIDATION_FAILED', detail: 'secret' }, 400));
    vi.stubGlobal('fetch', http); render(<ConfiguredLogicSetupPanel {...props} />); await fill(user);
    const field = screen.getByRole('textbox', { name: 'Parameters (JSON object)' });
    await user.clear(field); await user.click(field); await user.paste('{"x":1,"x":2}');
    await user.click(screen.getByRole('button', { name: 'Save Draft' }));
    expect(JSON.parse(http.mock.calls[0][1]?.body as string).parametersJson).toBe('{"x":1,"x":2}');
    expect(screen.getByRole('alert').textContent).toContain('VALIDATION_FAILED');
    expect(screen.getByRole('alert').textContent).not.toContain('secret');
  });
  it('blocks blind retry after a lost write response', async () => {
    const user = userEvent.setup(); const http = vi.fn(async () => { throw new TypeError('network'); });
    vi.stubGlobal('fetch', http); render(<ConfiguredLogicSetupPanel {...props} />); await fill(user);
    await user.click(screen.getByRole('button', { name: 'Save Draft' }));
    expect(screen.getByRole('alert').textContent).toContain('outcome is unknown');
    await user.click(screen.getByRole('button', { name: 'Save Draft' })); expect(http).toHaveBeenCalledTimes(1);
    expect(props.onSaved).not.toHaveBeenCalled();
  });
  it('distinguishes committed creation with failed scoped readback', async () => {
    const user = userEvent.setup(); const http = vi.fn(async (_url: string, init?: RequestInit) =>
      init?.method === 'POST' ? response(draft, 201) : response({}, 403));
    vi.stubGlobal('fetch', http); render(<ConfiguredLogicSetupPanel {...props} />); await fill(user);
    await user.click(screen.getByRole('button', { name: 'Save Draft' }));
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', expect.stringContaining('server accepted'));
    expect(props.onSaved).not.toHaveBeenCalled();
  });
  it('aborts stale form requests on unmount without publishing a result', async () => {
    const user = userEvent.setup(); let resolve!: (r: Response) => void; let signal: AbortSignal | undefined;
    vi.stubGlobal('fetch', vi.fn((_url, init) => { signal = init?.signal; return new Promise<Response>(r => { resolve = r; }); }));
    const view = render(<ConfiguredLogicSetupPanel {...props} />); await fill(user);
    await user.click(screen.getByRole('button', { name: 'Save Draft' })); view.unmount();
    expect(signal?.aborted).toBe(true);
    await act(async () => { resolve(response(draft, 201)); });
    expect(props.onSaved).not.toHaveBeenCalled();
  });
  it('validates then separately confirms activation with the reviewed active-revision token', async () => {
    const user = userEvent.setup(); let current = draft;
    const http = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        current = url.endsWith('/validate') ? validated : active;
        return response(current.revisions[0]);
      }
      return response(current);
    });
    vi.stubGlobal('fetch', http); workspace();
    await user.click(await screen.findByRole('button', { name: 'Validate revision 1' }));
    expect(await screen.findByRole('status')).toHaveProperty('textContent', expect.stringContaining('validated and verified'));
    await user.click(screen.getByRole('button', { name: 'Review activation of revision 1' }));
    expect(http.mock.calls.filter(c => c[1]?.method === 'POST')).toHaveLength(1);
    await user.click(screen.getByRole('button', { name: 'Activate reviewed revision' }));
    await waitFor(() => expect(screen.getByRole('status').textContent).toContain('activated and verified'));
    expect(http.mock.calls.find(c => c[0].endsWith('/activate'))?.[1]?.headers).toEqual({ 'If-Match': '"none"' });
  });
  it('locks conflicting activation until an explicit persisted reload and never auto-retries', async () => {
    const user = userEvent.setup(); let drift = false;
    const http = vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') { drift = true; return response({ code: 'CONFIGURED_LOGIC_REVISION_CONFLICT' }, 409); }
      return response(drift ? { ...validated, activeRevisionId: otherRevisionId,
        revisions: [...validated.revisions, { ...active.revisions[0], id: otherRevisionId, revisionNumber: 2 }] } : validated);
    });
    vi.stubGlobal('fetch', http); workspace();
    await user.click(await screen.findByRole('button', { name: 'Review activation of revision 1' }));
    await user.click(screen.getByRole('button', { name: 'Activate reviewed revision' }));
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', expect.stringContaining('configuration changed'));
    expect(screen.getByRole('button', { name: 'Review activation of revision 1' })).toHaveProperty('disabled', true);
    await user.click(screen.getByRole('button', { name: 'Reload persisted revisions' }));
    expect(await screen.findByRole('status')).toHaveProperty('textContent', expect.stringContaining('refreshed'));
    expect(http.mock.calls.filter(c => c[1]?.method === 'POST')).toHaveLength(1);
  });
  it('clears detail on denied validation and removes all mutation actions', async () => {
    const user = userEvent.setup();
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) =>
      init?.method === 'POST' ? response({}, 403) : response(draft)));
    workspace(); await user.click(await screen.findByRole('button', { name: 'Validate revision 1' }));
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', expect.stringContaining('Access is unavailable'));
    expect(screen.queryByText('Observer')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Validate revision 1' })).toBeNull();
  });
  it('lets readers inspect metadata but offers no editing or lifecycle mutations', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => response(draft))); workspace(false);
    await screen.findByText('Revision 1 · Draft');
    expect(screen.queryByRole('button', { name: 'Copy revision 1' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Validate revision 1' })).toBeNull();
  });
  it('copies immutable content into a new revision without editing the existing receipt', async () => {
    const user = userEvent.setup();
    const http = vi.fn(async (_url: string, init?: RequestInit) => init?.method === 'POST'
      ? response({ ...draft.revisions[0], id: otherRevisionId, revisionNumber: 2 }, 201)
      : response({ ...draft, revisions: [...draft.revisions, { ...draft.revisions[0], id: otherRevisionId, revisionNumber: 2 }] }));
    vi.stubGlobal('fetch', http);
    render(<ConfiguredLogicSetupPanel {...props} configuration={draft} revision={draft.revisions[0]} />);
    expect(screen.getByRole('textbox', { name: 'Configuration name' })).toHaveProperty('disabled', true);
    await user.click(screen.getByRole('button', { name: 'Save Draft' }));
    await waitFor(() => expect(props.onSaved).toHaveBeenCalled());
    expect(http.mock.calls[0][0]).toBe('/api/v1/configured-logic/' + configId + '/revisions');
    expect(draft.revisions).toHaveLength(1);
  });
  it('rejects a readback with changed immutable bindings instead of enabling validation', async () => {
    const user = userEvent.setup();
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => init?.method === 'POST'
      ? response(draft, 201) : response({ ...draft, revisions: [{ ...draft.revisions[0], parametersJson: '{"changed":true}' }] })));
    render(<ConfiguredLogicSetupPanel {...props} />); await fill(user);
    await user.click(screen.getByRole('button', { name: 'Save Draft' }));
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', expect.stringContaining('server accepted'));
    expect(props.onSaved).not.toHaveBeenCalled();
  });
  it('requires explicit UTC scheduling and keeps automatic Shadow runs opt-in', async () => {
    const user = userEvent.setup(); const http = vi.fn(); vi.stubGlobal('fetch', http);
    render(<ConfiguredLogicSetupPanel {...props} />); await fill(user);
    await user.click(screen.getByText('Schedule (optional)'));
    await user.click(screen.getByRole('checkbox', { name: 'Include a cadence schedule' }));
    expect(screen.getByRole('checkbox', { name: 'Enable scheduled Shadow runs after activation' })).toHaveProperty('checked', false);
    await user.type(screen.getByRole('textbox', { name: 'Start timestamp (UTC, ending in Z)' }), '2026-10-16T12:00:00');
    await user.click(screen.getByRole('button', { name: 'Save Draft' }));
    expect(screen.getByRole('alert').textContent).toContain('explicit UTC'); expect(http).not.toHaveBeenCalled();
  });
  it('clears the draft and selected configuration when the same user loses scope in the real App', async () => {
    window.location.hash = '#logic'; let revoked = false;
    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      const value = url === '/api/v1/security/me' ? {
        userId: otherRevisionId, roles: ['Engineer'], assetScopeIds: revoked ? [] : [assetId],
      } : url === '/api/v1/configured-logic/capabilities' ? { canManage: !revoked }
        : url.startsWith('/api/v1/configured-logic?') ? revoked ? [] : [draft]
        : url.startsWith('/api/v1/logic-modules') ? [module]
        : url.startsWith('/api/v1/asset-types') ? types
        : url.startsWith('/api/v1/assets?') ? revoked ? [] : assets
        : url.startsWith('/api/v1/assets/capabilities') ? {}
        : url.startsWith('/api/v1/system/info') ? {} : [];
      return response(value);
    }));
    const user = userEvent.setup(); render(<App />);
    await user.click(await screen.findByRole('button', { name: 'Configure Logic' }));
    await user.type(screen.getByRole('textbox', { name: 'Configuration name' }), 'Sensitive draft');
    revoked = true;
    await user.click(screen.getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(screen.queryByDisplayValue('Sensitive draft')).toBeNull());
    expect(screen.queryByRole('region', { name: 'Configured Logic setup' })).toBeNull();
    window.location.hash = '';
  });

});
