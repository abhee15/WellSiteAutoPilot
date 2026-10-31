import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from '../../src/App';
import { ChangeSignalClient } from '../../src/live/changeSignal';
import type { HubConnectionLike } from '../../src/live/changeSignal';
import { LiveUpdatesProvider } from '../../src/live/LiveUpdates';

// Every tree row renders exactly one icon, so counting icon renders counts row renders.
const rows = vi.hoisted(() => ({ renders: 0 }));
vi.mock('../../src/views/AssetNodeIcon', async () => {
  const actual = await vi.importActual<typeof import('../../src/views/AssetNodeIcon')>('../../src/views/AssetNodeIcon');
  return {
    ...actual,
    AssetNodeIcon: (props: Parameters<typeof actual.AssetNodeIcon>[0]) => {
      rows.renders += 1;
      return actual.AssetNodeIcon(props);
    },
  };
});

const userId = '66666666-6666-4666-8666-666666666666';
const typeId = '88888888-8888-4888-8888-888888888888';
const guid = (n: number) => `77777777-7777-4777-8777-${String(n).padStart(12, '0')}`;
const padId = guid(500);
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });

beforeEach(() => { rows.renders = 0; });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

const baseRoutes = (url: string) => {
  if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\link', isActive: true, roles: [], permissions: ['assets.read'], assetScopeIds: [] });
  if (url.includes('capabilities')) return response({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
  if (url.includes('/asset-types')) return response([{ id: typeId, key: 'well', displayName: 'Well', schemaVersion: 1, isActive: true }]);
  return null;
};

/** A hub the test controls: connect it, then emit change areas by name. */
function liveHarness() {
  let emit: (area: unknown) => void = () => undefined;
  let settle: () => void = () => undefined;
  const client = new ChangeSignalClient(() => {
    const hub: HubConnectionLike = {
      start: () => new Promise<void>(resolve => { settle = resolve; }),
      stop: () => Promise.resolve(),
      on: (_method, handler) => { emit = handler; },
      onclose: () => undefined,
      onreconnecting: () => undefined,
      onreconnected: () => undefined,
    };
    return hub;
  }, { set: () => 0, clear: () => undefined });
  return { client, connect: () => settle(), emit: (area: string) => emit(area) };
}

// ---- Non-lazy mode (fewer than 100 assets: the tree is built in the browser) ----

const wells = (name = (n: number) => 'Well ' + n) => Array.from({ length: 40 }, (_, n) => (
  { id: guid(n), assetTypeId: typeId, parentAssetId: padId, name: name(n), isActive: true }));
const pad = { id: padId, assetTypeId: typeId, name: 'Pad 7', isActive: true };

function flatHost(list: () => unknown[], counts?: { lists: number }) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const known = baseRoutes(url);
    if (known) return known;
    if (url.includes('/assets?')) { if (counts) counts.lists += 1; return response(list()); }
    return response([]);
  }));
}

it('selecting an asset re-renders only the rows on the path to the old and new selection', async () => {
  flatHost(() => [pad, ...wells()]);
  window.location.hash = '#assets';
  render(<App />);
  const tree = await screen.findByRole('navigation', { name: 'Asset hierarchy' });
  const user = userEvent.setup();
  await user.click(await within(tree).findByRole('button', { name: 'Open Well 3 from the hierarchy' }));
  await screen.findByText('Selected asset');
  await waitFor(() => expect(within(tree).getByRole('button', { name: 'Open Well 3 from the hierarchy' }).getAttribute('data-selected')).toBe('true'));

  rows.renders = 0;
  await user.click(within(tree).getByRole('button', { name: 'Open Well 9 from the hierarchy' }));
  await waitFor(() => expect(within(tree).getByRole('button', { name: 'Open Well 9 from the hierarchy' }).getAttribute('data-selected')).toBe('true'));
  expect(within(tree).getByRole('button', { name: 'Open Well 3 from the hierarchy' }).getAttribute('data-selected')).toBe('false');
  // Pad 7 (on the path), Well 3 (deselected) and Well 9 (selected), with a little room for a second pass.
  expect(rows.renders).toBeLessThanOrEqual(6);
});

it('a refresh that returns the same assets does not redraw the tree, and a real change still shows', async () => {
  let current = [pad, ...wells()];
  const counts = { lists: 0 };
  flatHost(() => structuredClone(current), counts);
  window.location.hash = '#assets';
  const live = liveHarness();
  render(<LiveUpdatesProvider client={live.client}><App /></LiveUpdatesProvider>);
  const tree = await screen.findByRole('navigation', { name: 'Asset hierarchy' });
  await within(tree).findByRole('button', { name: 'Open Well 3 from the hierarchy' });
  await act(async () => { live.connect(); });
  await waitFor(() => expect(counts.lists).toBeGreaterThanOrEqual(2)); // initial load + catch-up after connecting

  // Identical data: the server's signal makes the page re-read, and nothing in the tree is redrawn.
  let seen = counts.lists;
  rows.renders = 0;
  await act(async () => { live.emit('assets'); });
  await waitFor(() => expect(counts.lists).toBe(seen + 1));
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 50)); });
  expect(rows.renders).toBe(0);

  // Changed data: the new name appears.
  current = [pad, ...wells(n => (n === 3 ? 'Renamed Well' : 'Well ' + n))];
  seen = counts.lists;
  await act(async () => { live.emit('assets'); });
  await waitFor(() => expect(counts.lists).toBe(seen + 1));
  expect(await within(tree).findByRole('button', { name: 'Open Renamed Well from the hierarchy' })).toBeTruthy();
});

it('a collapsed branch opens to show the selection, and goes back to collapsed once the selection leaves it', async () => {
  const padTwo = { id: guid(600), assetTypeId: typeId, name: 'Pad 8', isActive: true };
  const inTwo = { id: guid(601), assetTypeId: typeId, parentAssetId: guid(600), name: 'East Well', isActive: true };
  flatHost(() => [pad, ...wells().slice(0, 3), padTwo, inTwo]);
  window.location.hash = '#assets';
  render(<App />);
  const tree = await screen.findByRole('navigation', { name: 'Asset hierarchy' });
  const user = userEvent.setup();
  const open = (name: string) => within(tree).queryByRole('button', { name: `Open ${name} from the hierarchy` });

  await user.click(await within(tree).findByRole('button', { name: 'Hide children of Pad 7' }));
  expect(open('Well 1')).toBeNull();
  await user.click(within(tree).getByRole('button', { name: 'Hide children of Pad 8' }));
  expect(open('East Well')).toBeNull();

  // Choosing an asset inside a collapsed branch (here by its address) opens that branch and no other.
  window.location.hash = '#assets/' + inTwo.id;
  await waitFor(() => expect(open('East Well')).toBeTruthy());
  expect(open('East Well')!.getAttribute('data-selected')).toBe('true');
  expect(open('Well 1')).toBeNull();

  // Moving the selection out puts the branch back as the person left it.
  window.location.hash = '#assets/' + padId;
  await waitFor(() => expect(open('Pad 7')!.getAttribute('data-selected')).toBe('true'));
  expect(open('East Well')).toBeNull();
  expect(open('Well 1')).toBeNull();
});

// ---- Lazy mode (a full first page of 100: levels and the selection are read from the server) ----

const firstPage = Array.from({ length: 100 }, (_, index) => (
  { id: guid(1000 + index), assetTypeId: typeId, name: 'Listed ' + index, isActive: true }));
const lazyPad = { id: padId, assetTypeId: typeId, name: 'Pad 7', parentAssetId: null, isActive: true };
const wellA = { id: guid(501), assetTypeId: typeId, name: 'North Well', parentAssetId: padId, isActive: true };
const wellB = { id: guid(502), assetTypeId: typeId, name: 'South Well', parentAssetId: padId, isActive: true };

function lazyHost(calls: string[]) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    calls.push(url);
    const known = baseRoutes(url);
    if (known) return known;
    if (url.startsWith('/api/v1/assets/children')) {
      return url.includes('parentAssetId=' + padId)
        ? response([{ ...wellA, childCount: 0 }, { ...wellB, childCount: 0 }])
        : response([{ ...lazyPad, childCount: 2 }]);
    }
    if (url === '/api/v1/assets/' + padId) return response(lazyPad);
    if (url === '/api/v1/assets/' + wellA.id) return response(wellA);
    if (url === '/api/v1/assets/' + wellB.id) return response(wellB);
    if (url.includes('/assets?')) return response(firstPage);
    return response([]);
  }));
}

it('selecting siblings reads their shared ancestors once, and the server signal clears what was kept', async () => {
  const calls: string[] = [];
  lazyHost(calls);
  window.location.hash = '#assets';
  const live = liveHarness();
  render(<LiveUpdatesProvider client={live.client}><App /></LiveUpdatesProvider>);
  const user = userEvent.setup();
  const tree = await screen.findByRole('navigation', { name: 'Asset hierarchy' });
  await act(async () => { live.connect(); });
  await user.click(await within(tree).findByRole('button', { name: 'Show children of Pad 7' }));
  const padReads = () => calls.filter(url => url === '/api/v1/assets/' + padId).length;

  await user.click(await within(tree).findByRole('button', { name: 'Open North Well from the hierarchy' }));
  const page = () => screen.getByText('Selected asset').closest('.asset-page') as HTMLElement;
  await waitFor(() => expect(within(page()).getByText('North Well')).toBeTruthy());
  expect(padReads()).toBe(1);

  await user.click(within(tree).getByRole('button', { name: 'Open South Well from the hierarchy' }));
  await waitFor(() => expect(within(page()).getByText('South Well')).toBeTruthy());
  expect(within(page()).getByText('Located under Pad 7')).toBeTruthy();
  expect(padReads()).toBe(1); // Pad 7 was kept from the first selection

  // The server says assets changed: nothing kept may be trusted, so the next selection reads it again.
  await act(async () => { live.emit('assets'); });
  await user.click(within(tree).getByRole('button', { name: 'Open North Well from the hierarchy' }));
  await waitFor(() => expect(within(page()).getByText('North Well')).toBeTruthy());
  await waitFor(() => expect(padReads()).toBe(2));
});
