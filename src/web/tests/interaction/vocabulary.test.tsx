import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import { App } from '../../src/App';

const userId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const assetId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

it('operations show plain wording, a fallback asset name and keep the raw code as a tooltip', async () => {
  window.location.hash = '#operations';
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\words', isActive: true, roles: [], permissions: ['executions.read'], assetScopeIds: [] });
    if (url.includes('system/info')) return response({});
    if (url.includes('/executions')) return response([{ executionId: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd', assetId, assetExternalId: null,
      moduleId: 'pump-monitor', moduleVersion: '1.0.0', mode: 'Shadow', status: 'TimedOut', failureCode: 'EXECUTION_INPUT_STALE',
      requestedAtUtc: '2026-10-17T12:00:00Z' }]);
    return response([]);
  }));
  render(<App />);
  const row = (await screen.findByText('pump-monitor')).closest('tr')!;
  expect(within(row).getByText('Timed out')).not.toBeNull();
  const outcome = within(row).getByText('An input was too old');
  expect(outcome.getAttribute('title')).toBe('EXECUTION_INPUT_STALE');
  expect(within(row).getByText(assetId.slice(0, 8) + '…')).not.toBeNull();
  expect(document.title).toBe('Operations · WellSite AutoPilot');
});

it('offers a skip link that moves focus to the main content without changing the address', async () => {
  window.location.hash = '#operations';
  vi.stubGlobal('fetch', vi.fn(async (url: string) => response(url === '/api/v1/security/me'
    ? { userId, identityName: 'FIELD\\words', isActive: true, roles: [], permissions: ['executions.read'], assetScopeIds: [] }
    : url.includes('system/info') ? {} : [])));
  render(<App />);
  const skip = await screen.findByRole('link', { name: 'Skip to main content' });
  skip.click();
  expect(document.activeElement).toBe(document.getElementById('main-content'));
  expect(window.location.hash).toBe('#operations');
});

it('operations name a run after its logic and the assets it covers', async () => {
  window.location.hash = '#operations';
  const configId = 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee';
  const revisionId = 'ffffffff-ffff-4fff-8fff-ffffffffffff';
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\names', isActive: true, roles: [],
      permissions: ['executions.read', 'assets.read', 'configured-logic.read'], assetScopeIds: [] });
    if (url.includes('system/info')) return response({});
    if (url.includes('/executions')) return response([{ executionId: '11111111-1111-4111-8111-111111111111', logicInstanceId: configId,
      configurationRevisionId: revisionId, assetId: null, assetExternalId: null, moduleId: 'pump-monitor', moduleVersion: '1.0.0',
      mode: 'Shadow', status: 'Completed', requestedAtUtc: '2026-10-17T12:00:00Z' }]);
    if (url.includes('/configured-logic')) return response([{ id: configId, name: 'North pumps', activeRevisionId: revisionId,
      createdAtUtc: '2026-10-01T00:00:00Z', revisions: [{ id: revisionId, revisionNumber: 3, status: 'Active',
        assetBindings: [{ role: 'well', assetId }], dataBindings: [] }] }]);
    if (url.includes('/assets?')) return response([{ id: assetId, assetTypeId: 't', name: 'North Well', isActive: true, createdAtUtc: '2026-10-01T00:00:00Z' }]);
    if (url.includes('capabilities')) return response({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
    return response([]);
  }));
  render(<App />);
  const row = (await screen.findByText('North pumps')).closest('tr')!;
  expect(within(row).getByText('North Well')).not.toBeNull();
  expect(within(row).getByText(/pump-monitor v1\.0\.0 · Revision 3/)).not.toBeNull();
});
