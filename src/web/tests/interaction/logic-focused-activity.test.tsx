import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LogicPage } from '../../src/views/LogicPage';
import { logicActivityDetail } from '../../src/views/logicActivityRoute';
import { hashFor } from '../../src/routing';
import { accessibilityViolations } from './axe';

const userId = '11111111-1111-4111-8111-111111111111';
const configurationId = '22222222-2222-4222-8222-222222222222';
const revisionId = '33333333-3333-4333-8333-333333333333';
const assetId = '44444444-4444-4444-8444-444444444444';
const runId = '55555555-5555-4555-8555-555555555555';
const olderRunId = '66666666-6666-4666-8666-666666666666';
const configuration = { id: configurationId, name: 'Pressure observer', canManage: false, canRequestShadow: false,
  activeRevisionId: revisionId, revisions: [{ id: revisionId, revisionNumber: 2, status: 'Active', mode: 'Shadow',
    moduleId: 'sample', moduleVersion: '1.0.0', parametersJson: '{}',
    assetBindings: [{ role: 'well', assetId, parameterOverridesJson: '{}' }], dataBindings: [] }] };
const asset = { id: assetId, name: 'Well 1', assetTypeId: '77777777-7777-4777-8777-777777777777', isActive: true };
const run = { executionId: runId, logicInstanceId: configurationId, configurationRevisionId: revisionId,
  status: 'Completed', mode: 'Shadow', requestedAtUtc: '2026-11-01T12:00:00Z' };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
const baseProps = { modules: [], configuredLogic: [configuration], assets: [asset],
  assetTypes: [{ id: asset.assetTypeId, key: 'well', isActive: true }],
  userId, onRefresh: async () => {}, permissions: ['configured-logic.read', 'executions.read'],
  executions: [run], isLoaded: true };
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });

function fixture() {
  const historyRequests: string[] = [];
  const http = vi.fn(async (url: string) => {
    if (url === '/api/v1/configured-logic/' + configurationId) return response(configuration);
    if (url === '/api/v1/configured-logic/' + configurationId + '/activity') return response({
      activeRevisionId: revisionId, observedAtUtc: '2026-11-01T12:00:00Z',
      nextScheduledAtUtc: null, recentRuns: [run],
    });
    if (url.startsWith('/api/v1/executions/history?')) {
      historyRequests.push(url);
      return response({ items: [run], nextCursor: null });
    }
    if (url === '/api/v1/executions/' + olderRunId) return response({ ...run, executionId: olderRunId });
    if (url === '/api/v1/executions/' + runId) return response(run);
    return response([]);
  });
  vi.stubGlobal('fetch', http);
  return { http, historyRequests };
}

it('focuses Activity without changing configured identity and retains its mounted workspace on return', async () => {
  window.location.hash = hashFor('logic', configurationId);
  const { historyRequests } = fixture();
  const user = userEvent.setup();
  const { container } = render(<LogicPage {...baseProps as never} />);
  await screen.findByText(/Revision 2 is live/);
  const workspace = container.querySelector('.configured-detail');
  await user.click(screen.getByRole('tab', { name: 'Activity' }));
  expect(container.querySelector('.logic-layout')?.getAttribute('data-activity-focus')).toBe('true');
  expect(container.querySelector('.execution-history-focus')).toBeTruthy();
  await waitFor(() => expect(window.location.hash).toContain('runId'));
  expect(historyRequests).toHaveLength(1);
  expect(new URL(historyRequests[0]!, 'http://test').searchParams.get('configuredLogicId')).toBe(configurationId);
  expect(new URL(historyRequests[0]!, 'http://test').searchParams.has('assetId')).toBe(false);
  expect(await screen.findByRole('heading', { name: 'Run details' })).toBeTruthy();
  await user.click(screen.getByRole('tab', { name: 'Configuration' }));
  expect(container.querySelector('.configured-detail')).toBe(workspace);
  expect(container.querySelector('.logic-layout')?.getAttribute('data-activity-focus')).toBe('false');
  expect(window.location.hash).toBe(hashFor('logic', configurationId));
  expect(screen.getByText('Saved revision')).toBeTruthy();
});

it('a deep link directly inspects an older authorized run absent from first history page', async () => {
  window.location.hash = hashFor('logic', logicActivityDetail(configurationId, olderRunId));
  fixture();
  const { container } = render(<LogicPage {...baseProps as never} />);
  await screen.findByText('This run is outside the current history page.');
  expect(container.querySelector('.logic-layout')?.getAttribute('data-activity-focus')).toBe('true');
  expect(await screen.findByRole('heading', { name: 'Run details' })).toBeTruthy();
  expect(window.location.hash).toContain(olderRunId);
  expect(await accessibilityViolations(container)).toEqual([]);
});

it('without executions.read the Activity inspector cannot be opened', async () => {
  window.location.hash = hashFor('logic', configurationId);
  fixture();
  render(<LogicPage {...baseProps as never} permissions={['configured-logic.read']} executions={undefined} />);
  await screen.findByText(/Revision 2 is live/);
  expect(screen.queryByRole('tab', { name: 'Activity' })).toBeNull();
});
