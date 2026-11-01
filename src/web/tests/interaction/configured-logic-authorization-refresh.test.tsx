import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from '../../src/App';

const userId = '11111111-1111-4111-8111-111111111111';
const configurationId = '22222222-2222-4222-8222-222222222222';
const revisionId = '33333333-3333-4333-8333-333333333333';
const assetId = '44444444-4444-4444-8444-444444444444';
const draft = { id: configurationId, name: 'Scoped observer', activeRevisionId: null, canManage: true, canRequestShadow: false,
  revisions: [{ id: revisionId, revisionNumber: 1, status: 'Draft', mode: 'Shadow',
    moduleId: 'sample', moduleVersion: '1.0.0', parametersJson: '{}',
    assetBindings: [{ role: 'well', assetId, parameterOverridesJson: '{}' }], dataBindings: [] }] };
const response = (value: unknown) => new Response(JSON.stringify(value));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });

function fixture(detail?: () => Promise<Response>) {
  window.location.hash = '#logic';
  const state = { visible: true, configuration: draft };
  const http = vi.fn(async (url: string) => {
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\custom',
      isActive: true, roles: ['Custom Engineer'], assetScopeIds: [],
      permissions: ['configured-logic.read', 'configured-logic.manage'] });
    if (url === '/api/v1/configured-logic/capabilities') return response({ canManage: true, canRequestShadow: false });
    if (url.startsWith('/api/v1/configured-logic?')) return response(state.visible ? [state.configuration] : []);
    if (url === '/api/v1/configured-logic/' + configurationId) return detail ? detail() : response(state.configuration);
    return response({});
  });
  vi.stubGlobal('fetch', http);
  return { state, http };
}

it('clears selected details and an editor when resource access disappears without a session permission change', async () => {
  const { state, http } = fixture();
  const user = userEvent.setup(); render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Inspect revisions of Scoped observer' }));
  await user.click(await screen.findByRole('button', { name: 'Create revision', exact: true }));
  await screen.findByRole('textbox', { name: 'Parameters (JSON object)' });
  state.visible = false;
  await user.click(screen.getByRole('button', { name: 'Refresh' }));
  await waitFor(() => expect(screen.queryByRole('region', { name: 'Configured logic setup' })).toBeNull());
  expect(screen.queryByRole('textbox', { name: 'Parameters (JSON object)' })).toBeNull();
  expect(screen.queryByText('Scoped observer')).toBeNull();
  // Restored list visibility does not silently reopen the retired selection.
  state.visible = true;
  await user.click(screen.getByRole('button', { name: 'Refresh' }));
  await screen.findByRole('button', { name: 'Inspect revisions of Scoped observer' });
  expect(screen.queryByRole('region', { name: 'Configured logic setup' })).toBeNull();
  expect(http.mock.calls.filter(([url]) => url === '/api/v1/configured-logic/' + configurationId)).toHaveLength(1);
});

it('does not discard a draft or refetch detail for an equivalent authorized refresh', async () => {
  const { http } = fixture();
  const user = userEvent.setup(); render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Inspect revisions of Scoped observer' }));
  await user.click(await screen.findByRole('button', { name: 'Create revision', exact: true }));
  const parameters = await screen.findByRole('textbox', { name: 'Parameters (JSON object)' });
  await user.clear(parameters); await user.click(parameters); await user.paste('{"reviewed":true}');
  await user.click(screen.getByRole('button', { name: 'Refresh' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Refresh' })).toHaveProperty('disabled', false));
  expect(screen.getByRole('textbox', { name: 'Parameters (JSON object)' })).toHaveProperty('value', '{"reviewed":true}');
  expect(http.mock.calls.filter(([url]) => url === '/api/v1/configured-logic/' + configurationId)).toHaveLength(1);
});

it('a late detail response cannot restore a configuration removed by the next authorized list', async () => {
  let complete: (value: Response) => void = () => {};
  const pending = new Promise<Response>(resolve => { complete = resolve; });
  const { state } = fixture(() => pending);
  const user = userEvent.setup(); render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Inspect revisions of Scoped observer' }));
  await screen.findByRole('region', { name: 'Configured logic setup' });
  state.visible = false;
  await user.click(screen.getByRole('button', { name: 'Refresh' }));
  await waitFor(() => expect(screen.queryByRole('region', { name: 'Configured logic setup' })).toBeNull());
  await act(async () => { complete(response(draft)); await pending; });
  expect(screen.queryByRole('region', { name: 'Configured logic setup' })).toBeNull();
  expect(screen.queryByText('Scoped observer')).toBeNull();
});

it('retains unsaved inputs when selected revision metadata changes; an explicit reload reviews the new snapshot', async () => {
  const { state, http } = fixture();
  const user = userEvent.setup(); render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Inspect revisions of Scoped observer' }));
  await user.click(await screen.findByRole('button', { name: 'Create revision', exact: true }));
  const parameters = await screen.findByRole('textbox', { name: 'Parameters (JSON object)' });
  await user.clear(parameters); await user.click(parameters); await user.paste('{"nested":{"value":7}}');
  state.configuration = { ...draft, revisions: [{ ...draft.revisions[0], status: 'Validated' }] };
  await user.click(screen.getByRole('button', { name: 'Refresh' }));
  expect(screen.getByRole('textbox', { name: 'Parameters (JSON object)' })).toHaveProperty('value', '{"nested":{"value":7}}');
  expect(http.mock.calls.filter(([url]) => url === '/api/v1/configured-logic/' + configurationId)).toHaveLength(1);
  await user.click(screen.getByRole('button', { name: 'Cancel editor' }));
  await user.click(screen.getByRole('button', { name: 'Reload persisted revisions' }));
  await screen.findByRole('button', { name: 'Review activation of revision 1' });
  expect(http.mock.calls.filter(([url]) => url === '/api/v1/configured-logic/' + configurationId)).toHaveLength(2);

});
