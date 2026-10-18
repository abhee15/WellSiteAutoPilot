import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import { App } from '../../src/App';

const userId = '66666666-6666-4666-8666-666666666666';
const parentId = '77777777-7777-4777-8777-777777777771';
const assetId = '77777777-7777-4777-8777-777777777772';
const typeId = '88888888-8888-4888-8888-888888888888';
const runId = '99999999-9999-4999-8999-999999999999';
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

it('the asset page explains where the asset sits, what runs on it and what it has done', async () => {
  window.location.hash = '#assets/' + assetId;
  const calls: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    calls.push(url);
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\link', isActive: true, roles: [], permissions: ['assets.read', 'executions.read', 'recommendations.read', 'configured-logic.read'], assetScopeIds: [] });
    if (url.includes('capabilities')) return response({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
    if (url.includes('/asset-types')) return response([{ id: typeId, key: 'well', displayName: 'Well', schemaVersion: 1, isActive: true }]);
    if (url.includes('/assets?')) return response([
      { id: parentId, assetTypeId: typeId, name: 'Pad 7', isActive: true },
      { id: assetId, assetTypeId: typeId, parentAssetId: parentId, name: 'North Well', isActive: true },
    ]);
    if (url.includes('/configured-logic')) return response([{ id: 'c1', name: 'Pressure watch', moduleId: 'sample.module', revisions: [{ id: 'r1', revisionNumber: 1, assetBindings: [{ assetId }], dataBindings: [] }] }]);
    if (url.startsWith('/api/v1/executions?assetId=')) return response([{ executionId: runId, status: 'Succeeded', requestedAtUtc: '2026-10-01T00:00:00Z' }]);
    if (url.startsWith('/api/v1/recommendations?assetId=')) return response([]);
    return response([]);
  }));
  render(<App />);
  const page = (await screen.findByText('Selected Asset')).closest('.asset-page') as HTMLElement;
  expect(within(page).getByText('Located under Pad 7')).toBeTruthy();
  expect(await within(page).findByRole('link', { name: 'Pressure watch' })).toBeTruthy();
  expect(await within(page).findByText('Succeeded')).toBeTruthy();
  expect(await within(page).findByText(/No recommendations/)).toBeTruthy();
  expect(calls.some(url => url.startsWith(`/api/v1/executions?assetId=${assetId}`))).toBe(true);
});

it('the hierarchy tree shows children under their parent and opens an asset on click', async () => {
  window.location.hash = '#assets';
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\link', isActive: true, roles: [], permissions: ['assets.read'], assetScopeIds: [] });
    if (url.includes('capabilities')) return response({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
    if (url.includes('/asset-types')) return response([{ id: typeId, key: 'well', displayName: 'Well', schemaVersion: 1, isActive: true }]);
    if (url.includes('/assets?')) return response([
      { id: parentId, assetTypeId: typeId, name: 'Pad 7', isActive: true },
      { id: assetId, assetTypeId: typeId, parentAssetId: parentId, name: 'North Well', isActive: true },
    ]);
    return response([]);
  }));
  render(<App />);
  const tree = await screen.findByRole('navigation', { name: 'Asset hierarchy' });
  expect(within(tree).getByText('1 below')).toBeTruthy();
  within(tree).getByRole('button', { name: 'Open North Well from the hierarchy' }).click();
  expect(await screen.findByText('Selected Asset')).toBeTruthy();
  expect(window.location.hash).toBe('#assets/' + assetId);
});

it('an empty asset list says what to do about it', async () => {
  window.location.hash = '#assets';
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\link', isActive: true, roles: [], permissions: ['assets.read'], assetScopeIds: [] });
    if (url.includes('capabilities')) return response({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
    return response([]);
  }));
  render(<App />);
  expect(await screen.findByText('No Assets are configured.')).toBeTruthy();
  expect(screen.getByText(/Assets are the wells and equipment logic runs on/)).toBeTruthy();
});
