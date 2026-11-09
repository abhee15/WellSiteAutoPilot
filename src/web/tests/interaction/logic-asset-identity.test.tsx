import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LogicPage } from '../../src/views/LogicPage';

const assetA = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const assetB = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const oldAsset = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const configuration = {
  id: '11111111-1111-4111-8111-111111111111',
  name: 'Two-source observer',
  activeRevisionId: '22222222-2222-4222-8222-222222222222',
  revisions: [
    { id: '33333333-3333-4333-8333-333333333333', revisionNumber: 1, status: 'Superseded',
      assetBindings: [{ assetId: oldAsset }] },
    { id: '22222222-2222-4222-8222-222222222222', revisionNumber: 2, status: 'Active',
      assetBindings: [{ assetId: assetA }, { assetId: assetB }] },
  ],
};
const ownerA = '44444444-4444-4444-8444-444444444444';
const ownerB = '55555555-5555-4555-8555-555555555555';
const response = (value: unknown) => new Response(JSON.stringify(value));
const props = { configuredLogic: [configuration], modules: [], assets: [], assetTypes: [],
  userId: '99999999-9999-4999-8999-999999999999', onRefresh: async () => {},
  permissions: ['configured-logic.read', 'assets.read'] };

afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });

it('resolves names outside the capped asset snapshot and distinguishes same-named assets by their Connection', async () => {
  window.location.hash = '#logic';
  const http = vi.fn(async (url: string) => {
    if (url === '/api/v1/assets/owners') return response([
      { connectionId: ownerA, connectionName: 'ForeSite Production', assetCount: 1500 },
      { connectionId: ownerB, connectionName: 'CygNet Test', assetCount: 50 },
    ]);
    if (url === '/api/v1/assets/' + assetA) return response({ id: assetA, name: 'RPOC_00002', ownerConnectionId: ownerA });
    if (url === '/api/v1/assets/' + assetB) return response({ id: assetB, name: 'RPOC_00002', ownerConnectionId: ownerB });
    return new Response('', { status: 404 });
  });
  vi.stubGlobal('fetch', http);
  render(<LogicPage {...(props as never)} />);
  expect(await screen.findByText('RPOC_00002 (ForeSite Production), RPOC_00002 (CygNet Test)')).toBeTruthy();
  const called = http.mock.calls.map(([url]) => url);
  expect(called).toContain('/api/v1/assets/' + assetA);
  expect(called).toContain('/api/v1/assets/' + assetB);
  expect(called).not.toContain('/api/v1/assets/' + oldAsset);
});

it('uses shortened IDs without making unauthorized asset directory requests', () => {
  window.location.hash = '#logic';
  const http = vi.fn(async () => response([]));
  vi.stubGlobal('fetch', http);
  render(<LogicPage {...(props as never)} permissions={['configured-logic.read']} />);
  expect(screen.getByText('aaaaaaaa…, bbbbbbbb…')).toBeTruthy();
  expect(http).not.toHaveBeenCalled();
});

it('keeps existing grid names visible while a selection-only historical asset is unresolved', async () => {
  window.location.hash = '#logic';
  let resolveHistorical: ((value: Response) => void) | undefined;
  const http = vi.fn(async (url: string) => {
    if (url === '/api/v1/assets/owners') return response([
      { connectionId: ownerA, connectionName: 'ForeSite Production', assetCount: 1500 },
      { connectionId: ownerB, connectionName: 'CygNet Test', assetCount: 50 },
    ]);
    if (url === '/api/v1/assets/' + assetA) return response({ id: assetA, name: 'Well A', ownerConnectionId: ownerA });
    if (url === '/api/v1/assets/' + assetB) return response({ id: assetB, name: 'Well B', ownerConnectionId: ownerB });
    if (url === '/api/v1/assets/' + oldAsset) return new Promise<Response>(resolve => { resolveHistorical = resolve; });
    if (url.startsWith('/api/v1/configured-logic/')) return new Response('', { status: 503 });
    return response([]);
  });
  vi.stubGlobal('fetch', http);
  const view = render(<LogicPage {...(props as never)} />);
  const label = 'Well A (ForeSite Production), Well B (CygNet Test)';
  expect(await screen.findByText(label)).toBeTruthy();

  // The selected configuration has a historical target that is not in either grid label.
  await userEvent.click(screen.getByRole('button', { name: /Two-source observer/ }));
  await waitFor(() => expect(http.mock.calls.some(([url]) => url === '/api/v1/assets/' + oldAsset)).toBe(true));
  expect(screen.getByText(label)).toBeTruthy();
  expect(screen.queryByText('aaaaaaaa…, bbbbbbbb…')).toBeNull();

  // Revoking asset read access must still remove authorized labels immediately.
  view.rerender(<LogicPage {...(props as never)} permissions={['configured-logic.read']} />);
  expect(screen.getByText('aaaaaaaa…, bbbbbbbb…')).toBeTruthy();
  resolveHistorical?.(response({ id: oldAsset, name: 'Historical Well', ownerConnectionId: ownerA }));
  expect(screen.queryByText(label)).toBeNull();
});
