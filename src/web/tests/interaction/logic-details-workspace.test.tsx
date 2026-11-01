import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfiguredLogicWorkspace } from '../../src/views/ConfiguredLogicWorkspace';
import { LiveUpdatesProvider } from '../../src/live/LiveUpdates';
import { ChangeSignalClient } from '../../src/live/changeSignal';
import type { HubConnectionLike } from '../../src/live/changeSignal';
import { accessibilityViolations } from './axe';

const id = (n: number) => `77777777-7777-4777-8777-${String(n).padStart(12, '0')}`;
const manifest = { moduleId: 'sample.complex', version: '1.0.0', executionProfile: 'Shadow',
  assetRequirements: [{ role: 'well', minimumCount: 1, maximumCount: 2, requiredAssetTypeKey: 'well' }],
  parameterSchema: { type: 'object', properties: { nested: { type: 'object' }, sequence: { type: 'array' } } },
  dataRequirements: [{ id: 'fillage', assetRole: 'well', quantity: 'PumpFillage' }, { id: 'calc', assetRole: 'well', quantity: 'CalculatedValue' }] };
const parametersJson = '{"nested":{"limits":[30,40],"strategy":{"enabled":true}},"sequence":[[1,2],[3,4]]}';
const revision = { id: id(3), revisionNumber: 3, mode: 'Shadow' as const, status: 'Active' as const,
  moduleId: 'sample.complex', moduleVersion: '1.0.0', moduleManifestJson: JSON.stringify(manifest), parametersJson,
  schedule: { enabled: true, cadenceSeconds: 300, startAtUtc: '2026-11-01T12:00:00Z', timeZoneId: 'America/Chicago' },
  assetBindings: [{ role: 'well', assetId: id(1), parameterOverridesJson: '{}' }],
  dataBindings: [{ requirementId: 'fillage', assetId: id(1), providerId: 'foresite', providerAssetExternalId: '42', providerMappingJson: '{"hidden":"MAPPING_NOT_FOR_DISPLAY"}' },
    { requirementId: 'calc', assetId: id(1), providerId: 'wami', providerAssetExternalId: 'calc-1', providerMappingJson: '{}' }] };
const configuration = { id: id(2), name: 'Complex observer', canManage: true, canRequestShadow: true, activeRevisionId: id(3),
  revisions: [revision, { ...revision, id: id(4), revisionNumber: 4, status: 'Draft' as const }] };
const assets = [{ id: id(1), assetTypeId: id(50), name: 'Well 101', isActive: true }];
const run = (n: number, status = 'Running') => ({ executionId: id(n), logicInstanceId: id(2), configurationRevisionId: id(3),
  status, mode: 'Shadow', requestedAtUtc: '2026-11-01T12:10:00Z' });
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
const props = { selectedId: id(2), canManage: true, userId: id(90), canRequestShadow: true,
  modules: [{ id: id(60), moduleId: manifest.moduleId, version: manifest.version, isEnabled: true, manifestJson: JSON.stringify(manifest) }],
  assets, assetTypes: [{ id: id(50), key: 'well', isActive: true }], executions: [], onClose: vi.fn(), onChanged: async () => {} };

function fixture() {
  const state = { config: configuration, runs: [run(11), run(10, 'Completed')], activityStatus: 200, activeRevisionId: id(3), historyRequests: [] as string[] };
  const http = vi.fn(async (url: string) => {
    if (url.endsWith('/activity')) return response({ activeRevisionId: state.activeRevisionId, observedAtUtc: '2026-11-01T12:12:00Z',
      nextScheduledAtUtc: '2026-11-01T12:15:00Z', recentRuns: state.runs }, state.activityStatus);
    if (url.startsWith('/api/v1/executions/history?')) { state.historyRequests.push(url); return response({ items: state.runs, nextCursor: null }); }
    if (url.startsWith('/api/v1/executions/')) return response(state.runs.find(r => r.executionId === url.split('/').at(-1)) ?? run(11));
    if (url === '/api/v1/configured-logic/' + id(2)) return response(state.config);
    return response([]);
  });
  vi.stubGlobal('fetch', http); return { state, http };
}
function liveHarness() {
  let emit: (...args: unknown[]) => void = () => {};
  let reconnect: () => void = () => {};
  const client = new ChangeSignalClient(() => ({ start: async () => {}, stop: async () => {},
    on: (_method, handler) => { emit = handler; }, onclose: () => {}, onreconnecting: () => {},
    onreconnected: handler => { reconnect = handler; } } as HubConnectionLike), { set: () => 0, clear: () => {} });
  return { client, emit: (area: string) => emit(area), reconnect: () => reconnect() };
}
afterEach(() => { cleanup(); vi.unstubAllGlobals(); sessionStorage.clear(); });

it('organizes read-only settings without limiting nested parameters or providers, and never renders mapping JSON', async () => {
  fixture(); const view = render(<ConfiguredLogicWorkspace {...props} />);
  await screen.findByText('Running');
  expect(screen.getByText('foresite, wami')).toBeTruthy();
  expect(screen.queryByRole('tab', { name: /Readiness/ })).toBeNull();
  const user = userEvent.setup();
  const overview = screen.getByRole('tab', { name: 'Overview', exact: true });
  overview.focus(); await user.keyboard('{ArrowRight}');
  expect(screen.getByRole('tab', { name: 'Configuration', exact: true }).getAttribute('aria-selected')).toBe('true');
  expect(screen.getByText('{"limits":[30,40],"strategy":{"enabled":true}}')).toBeTruthy();
  expect(screen.getByText('[[1,2],[3,4]]')).toBeTruthy();
  expect(view.container.textContent).not.toContain('MAPPING_NOT_FOR_DISPLAY');
  expect(view.container.textContent).toContain('calc');
  expect(await accessibilityViolations(view.container)).toEqual([]);
});

it('copies the chosen immutable revision into the existing editor and protects it from tab navigation', async () => {
  fixture(); render(<ConfiguredLogicWorkspace {...props} />); const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'Create revision', exact: true }));
  expect(screen.getByRole('textbox', { name: 'Parameters (JSON object)' })).toHaveProperty('value', parametersJson);
  expect(screen.getByRole('tab', { name: 'Overview', exact: true })).toHaveProperty('disabled', true);
  expect(screen.getByRole('tab', { name: 'Activity', exact: true })).toHaveProperty('disabled', true);
  await user.click(screen.getByRole('button', { name: 'Cancel editor' }));
  expect(screen.getByRole('tab', { name: 'Overview', exact: true })).toHaveProperty('disabled', false);
  expect(configuration.revisions[0]!.parametersJson).toBe(parametersJson);
});

it('refreshes only operational observations on signals/reconnect without replacing an editor or reviewed configuration', async () => {
  const { state, http } = fixture(); const live = liveHarness(); const user = userEvent.setup();
  render(<LiveUpdatesProvider client={live.client}><ConfiguredLogicWorkspace {...props} /></LiveUpdatesProvider>);
  await act(async () => live.client.setEnabled(true));
  await screen.findByText('Running');
  state.runs = [run(12, 'Starting'), run(11, 'Completed')];
  act(() => live.emit('executions'));
  await screen.findByText('Starting');
  state.runs = [run(12, 'Completed'), run(11, 'Completed')];
  act(() => live.reconnect());
  await waitFor(() => expect(screen.getAllByText('Completed')).toHaveLength(2));
  await user.click(screen.getByRole('button', { name: 'Create revision', exact: true }));
  const input = screen.getByRole('textbox', { name: 'Parameters (JSON object)' });
  await user.clear(input); await user.click(input); await user.paste('{"kept":{"provider":"wami"}}');
  state.activeRevisionId = id(4);
  act(() => live.emit('configured-logic'));
  await screen.findByText(/active revision changed elsewhere/);
  expect(input).toHaveProperty('value', '{"kept":{"provider":"wami"}}');
  expect(http.mock.calls.filter(([url]) => url === '/api/v1/configured-logic/' + id(2))).toHaveLength(1);
});

it('keeps scoped Activity independent of transient overview failures and clears protected detail after denial', async () => {
  const { state } = fixture(); const live = liveHarness(); const user = userEvent.setup();
  render(<LiveUpdatesProvider client={live.client}><ConfiguredLogicWorkspace {...props} /></LiveUpdatesProvider>);
  await act(async () => live.client.setEnabled(true)); await screen.findByText('Running');
  state.activityStatus = 503; act(() => live.emit('executions'));
  await screen.findByText(/Operational updates could not be refreshed/);
  await user.click(screen.getByRole('tab', { name: 'Activity', exact: true }));
  await screen.findAllByRole('button', { name: /Inspect run/ });
  expect(new URL(state.historyRequests[0]!, 'http://test').searchParams.get('configuredLogicId')).toBe(id(2));
  expect(new URL(state.historyRequests[0]!, 'http://test').searchParams.has('assetId')).toBe(false);
  const panel = screen.getByRole('tabpanel');
  expect(within(panel).getAllByRole('listitem')).toHaveLength(2);
  expect(await accessibilityViolations(panel)).toEqual([]);
  state.activityStatus = 403; act(() => live.emit('configured-logic'));
  await waitFor(() => expect(screen.queryByText('Complex observer')).toBeNull());
  expect(screen.queryByRole('tab', { name: 'Configuration' })).toBeNull();
});

it('can inspect other known execution modes without exposing a Shadow-only editor or run action', async () => {
  const { state } = fixture();
  state.config = { ...configuration, revisions: configuration.revisions.map(r => ({ ...r, mode: 'Recommendation' })) } as typeof configuration;
  render(<ConfiguredLogicWorkspace {...props} />);
  await screen.findByText('Complex observer');
  expect(screen.queryByRole('button', { name: 'Create revision', exact: true })).toBeNull();
  expect(screen.queryByRole('button', { name: /Run active revision/ })).toBeNull();
  await userEvent.click(screen.getByRole('tab', { name: 'Configuration', exact: true }));
  expect(screen.getByRole('button', { name: 'Copy revision 3' })).toHaveProperty('disabled', true);
});
