import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfiguredLogicWorkspace } from '../../src/views/ConfiguredLogicWorkspace';
import { accessibilityViolations } from './axe';

const id = (n: number) => `77777777-7777-4777-8777-${String(n).padStart(12, '0')}`;
const manifest = { moduleId: 'sample.edit', version: '1.0.0', executionProfile: 'ScheduledOneShot',
  assetRequirements: [{ role: 'well', minimumCount: 1, maximumCount: 1, requiredAssetTypeKey: 'well' }],
  dataRequirements: [{ id: 'pressure', assetRole: 'well', quantity: 'Pressure' }, { id: 'model', assetRole: 'well', quantity: 'Calculation' }],
  parameterSchema: { type: 'object', required: ['threshold', 'evaluation'], properties: {
    threshold: { type: 'number', title: 'Pressure threshold', minimum: 0 },
    evaluation: { type: 'object', title: 'Evaluation', required: ['window'], properties: { window: { type: 'integer', title: 'Window', minimum: 1 } } },
  } } };
const revision = { id: id(4), revisionNumber: 4, draftVersion: 7, status: 'Draft', mode: 'Shadow',
  moduleId: manifest.moduleId, moduleVersion: manifest.version, moduleManifestJson: JSON.stringify(manifest),
  parametersJson: '{"threshold":100,"evaluation":{"window":10}}',
  assetBindings: [{ role: 'well', assetId: id(1), parameterOverridesJson: '{"threshold":120}' }],
  dataBindings: [{ requirementId: 'pressure', assetId: id(1), providerId: 'foresite', providerAssetExternalId: 'WELL-01', providerMappingJson: '{"source":"pressure"}' },
    { requirementId: 'model', assetId: id(1), providerId: 'wami', providerAssetExternalId: 'model-01', providerMappingJson: '{"nested":{"keys":[1,2]}}' }],
  schedule: { enabled: true, cadenceSeconds: 600, startAtUtc: '2026-11-01T12:00:00Z', timeZoneId: 'UTC' } };
const active = { ...revision, id: id(3), revisionNumber: 3, status: 'Active' };
const props = { selectedId: id(2), canManage: true,
  modules: [{ id: id(60), moduleId: manifest.moduleId, version: manifest.version, isEnabled: true, manifestJson: JSON.stringify(manifest) }],
  assets: [{ id: id(1), assetTypeId: id(50), name: 'Well 01', isActive: true }],
  assetTypes: [{ id: id(50), key: 'well', isActive: true }], onClose: vi.fn(), onChanged: async () => {} };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
function fixture(status = 200, parameters = revision.parametersJson) {
  const config = { id: id(2), name: 'Pressure observer', activeRevisionId: active.id, canManage: true,
    revisions: [active, { ...revision, parametersJson: parameters }] };
  const http = vi.fn(async (url: string, init?: RequestInit) => {
    if (init?.method === 'PUT') {
      if (status !== 200) return response({ code: 'CONFIGURED_LOGIC_REVISION_CONFLICT' }, status);
      const body = JSON.parse(String(init.body));
      config.revisions[1] = { ...config.revisions[1], ...body, draftVersion: 8 };
      return response(config.revisions[1]);
    }
    if (url.endsWith('/validate')) return response({ code: 'VALIDATION_FAILED' }, 400);
    if (url.endsWith('/activity')) return response({ activeRevisionId: active.id, observedAtUtc: '2026-11-01T12:00:00Z', recentRuns: [] });
    return response(config);
  });
  vi.stubGlobal('fetch', http); return { http, config };
}
async function openEditor() {
  const user = userEvent.setup();
  const view = render(<ConfiguredLogicWorkspace {...props} />);
  await user.click(await screen.findByRole('tab', { name: 'Configuration', exact: true }));
  await user.selectOptions(screen.getByRole('combobox', { name: 'Saved revision' }), id(4));
  await user.click(screen.getByRole('button', { name: 'Edit draft revision 4' }));
  return { user, view };
}
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

it('edits the same draft using its version, retaining nested settings, overrides, multiple providers and schedule', async () => {
  const { http, config } = fixture(); const { user, view } = await openEditor();
  await user.clear(screen.getByRole('textbox', { name: 'Evaluation · Window', exact: true }));
  await user.type(screen.getByRole('textbox', { name: 'Evaluation · Window', exact: true }), '15');
  await user.click(screen.getByRole('button', { name: 'Save draft changes' }));
  await screen.findByRole('button', { name: 'Edit draft revision 4' });
  const writes = http.mock.calls.filter(([, init]) => init?.method === 'PUT');
  expect(writes).toHaveLength(1);
  expect(writes[0][0]).toBe('/api/v1/configured-logic/' + id(2) + '/revisions/' + id(4));
  expect(writes[0][1]?.headers).toMatchObject({ 'If-Match': '"7"' });
  const body = JSON.parse(String(writes[0][1]?.body));
  expect(JSON.parse(body.parametersJson)).toEqual({ threshold: 100, evaluation: { window: 15 } });
  expect(body.dataBindings).toEqual(revision.dataBindings);
  expect(body.assetBindings).toEqual(revision.assetBindings);
  expect(body.schedule.cadenceSeconds).toBe(600);
  expect(config.activeRevisionId).toBe(active.id);
  expect(await accessibilityViolations(view.container)).toEqual([]);
});

it('keeps complex unmodeled values in JSON and preserves unsaved edits on a conflict', async () => {
  const parameters = '{"threshold":100,"evaluation":{"window":10},"matrix":[[1,2],[3,4]]}';
  const { http } = fixture(409, parameters); const { user } = await openEditor();
  const input = screen.getByRole('textbox', { name: 'Parameters (JSON object)' });
  expect(input).toHaveProperty('value', parameters);
  await user.clear(input); await user.paste('{"edited":true}');
  await user.click(screen.getByRole('button', { name: 'Save draft changes' }));
  await screen.findByRole('alert');
  expect(input).toHaveProperty('value', '{"edited":true}');
  expect(screen.getByRole('button', { name: 'Save draft changes' }).matches(':disabled')).toBe(true);
  expect(http.mock.calls.filter(([, init]) => init?.method === 'PUT')).toHaveLength(1);
});

it('returns to typed fields only when JSON can round-trip, and leaves failed validation editable', async () => {
  const { http } = fixture(); const { user } = await openEditor();
  // Settings and overrides use the same primitive; choose the main module settings action.
  await user.click(screen.getAllByRole('button', { name: 'Edit JSON' })[0]);
  expect(screen.getByRole('textbox', { name: 'Parameters (JSON object)' })).toHaveProperty('value', revision.parametersJson);
  await user.click(screen.getByRole('button', { name: 'Use form fields' }));
  expect(screen.getByRole('textbox', { name: 'Evaluation · Window', exact: true })).toHaveProperty('value', '10');
  await user.click(screen.getByRole('button', { name: 'Cancel editor' }));
  await user.click(screen.getByRole('button', { name: 'Validate revision 4' }));
  await screen.findByRole('alert');
  expect(http.mock.calls.find(([url]) => url.endsWith('/validate'))?.[1]?.headers).toMatchObject({ 'If-Match': '"7"' });
  expect(screen.getByRole('button', { name: 'Edit draft revision 4' })).toHaveProperty('disabled', false);
});

it('distinguishes inherited boolean overrides from explicit false without populating inherited defaults', async () => {
  const { ParameterOverridesEditor } = await import('../../src/views/ParameterOverridesEditor');
  const onChange = vi.fn(); const user = userEvent.setup();
  render(<ParameterOverridesEditor schema={{ type: 'object', properties: { enabled: { type: 'boolean', title: 'Enabled' } } }}
    value="{}" label="Overrides for Well 01" idPrefix="boolean-override-" onChange={onChange} onValidity={vi.fn()} />);
  const choice = screen.getByRole('combobox', { name: /Enabled/ });
  expect(choice).toHaveProperty('value', '');
  expect(onChange).not.toHaveBeenCalled();
  await user.selectOptions(choice, 'false');
  expect(onChange).toHaveBeenLastCalledWith('{"enabled":false}');
  await user.selectOptions(choice, '');
  expect(onChange).toHaveBeenLastCalledWith('{}');
});

it('keeps duplicate JSON property names raw for authoritative server rejection', async () => {
  fixture(); const { user } = await openEditor();
  await user.click(screen.getAllByRole('button', { name: 'Edit JSON' })[0]);
  const input = screen.getByRole('textbox', { name: 'Parameters (JSON object)' });
  await user.clear(input); await user.paste('{"threshold":50,"threshold":100,"evaluation":{"window":10}}');
  expect(screen.queryByRole('button', { name: 'Use form fields' })).toBeNull();
});
