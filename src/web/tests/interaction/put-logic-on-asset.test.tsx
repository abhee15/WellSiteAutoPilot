import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from '../../src/App';

const userId = '55555555-5555-4555-8555-555555555555';
const assetId = '11111111-1111-4111-8111-111111111111';
const typeId = '22222222-2222-4222-8222-222222222222';
const manifest = { moduleId: 'sample', version: '1.0.0', executionProfile: 'Shadow',
  assetRequirements: [{ role: 'well', minimumCount: 1, maximumCount: 1, requiredAssetTypeKey: 'well' }], dataRequirements: [] };
const response = (value: unknown) => new Response(JSON.stringify(value), { status: 200 });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

function signIn(permissions: string[]) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => response(
    url === '/api/v1/security/me' ? { userId, identityName: 'FIELD\\x', isActive: true, roles: [], permissions, assetScopeIds: [] }
      : url.startsWith('/api/v1/asset-types') ? [{ id: typeId, key: 'well', displayName: 'Well', attributeSchemaJson: '{}', isActive: true }]
        : url.startsWith('/api/v1/assets/capabilities') ? { canCreateRoot: false, canCreateUnderAuthorizedParents: false }
          : url.startsWith(`/api/v1/assets/${assetId}/logic?`) ? { isGroupingNode: false, items: [], hasMore: false }
          : url.startsWith('/api/v1/assets?') || url === '/api/v1/assets' ? [{ id: assetId, assetTypeId: typeId, name: 'Well 101', isActive: true }]
            : url.startsWith('/api/v1/logic-modules') ? [{ id: 'm', moduleId: 'sample', version: '1.0.0', displayName: 'Sample logic',
              isEnabled: true, manifestJson: JSON.stringify(manifest) }]
              : url.startsWith('/api/v1/system/health') ? {} : [])));
}

it('offers "Put logic on this asset" only to someone who can configure logic', async () => {
  window.location.hash = '#assets/' + assetId;
  signIn(['assets.read']);
  render(<App />);
  await screen.findByText('Selected asset');
  expect(screen.queryByRole('link', { name: 'Put logic on this asset' })).toBeNull();
  cleanup();
  signIn(['assets.read', 'configured-logic.read', 'configured-logic.manage']);
  render(<App />);
  const link = await screen.findByRole('link', { name: 'Put logic on this asset' });
  expect(link.getAttribute('href')).toBe('#logic/new%3A' + assetId);
});

it('starts setup from the asset and selects it once a fitting logic is chosen', async () => {
  const user = userEvent.setup();
  window.location.hash = '#logic/new%3A' + assetId;
  signIn(['logic.read', 'assets.read', 'configured-logic.read', 'configured-logic.manage']);
  render(<App />);
  expect((await screen.findByRole('note')).textContent).toContain('Starting from Well 101');
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Approved module version' }), JSON.stringify(['sample', '1.0.0']));
  const list = await screen.findByRole('listbox', { name: 'Assets for well' }) as HTMLSelectElement;
  await waitFor(() => expect(Array.from(list.selectedOptions).map(o => o.value)).toEqual([assetId]));
});
