import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { useState } from 'react';
import userEvent from '@testing-library/user-event';
import { ConnectionInventory } from '../../src/views/ConnectionInventory';
import { ConnectionImport } from '../../src/views/ConnectionImport';
import * as api from '../../src/views/connections';
import type { Adapter, Connection, Discovery, ImportSelection } from '../../src/views/connections';
vi.mock('../../src/views/connections', async original => ({ ...await original<typeof import('../../src/views/connections')>(),
  searchDiscovered: vi.fn(), previewImport: vi.fn(), importFromConnection: vi.fn() }));
afterEach(() => { cleanup(); vi.resetAllMocks(); });
const snapshotId = '11111111-1111-4111-8111-111111111111';
const connection: Connection = { id: '22222222-2222-4222-8222-222222222222', name: 'Production ForeSite', adapterKey: 'foresite', settingsVersion: 1,
  settings: {}, isEnabled: true, revision: 1, createdAtUtc: '2026-10-22T10:00:00Z', updatedAtUtc: '2026-10-22T10:00:00Z', lastTest: null, lastTestIsCurrent: false };
const adapter: Adapter = { key: 'foresite', displayName: 'ForeSite', description: '', settingsVersion: 1, fields: [], capabilities: [], canDiscover: true,
  groupingFields: [{ key: 'assetName', label: 'ForeSite asset' }, { key: 'wellType', label: 'Well type' }, { key: 'deviceType', label: 'Device type' }] };
const wells = Array.from({ length: 1200 }, (_, index) => ({ kind: 'foresite.well', externalId: String(index + 1),
  displayName: `Well ${String(index + 1).padStart(4, '0')}`, metadata: { assetName: index < 100 ? 'North' : 'South', wellType: 'ESP' } }));
const discovery: Discovery = { complete: true, actingAs: 'CORP\\gateway', warnings: [], totalCount: 1200, sourceAssetCount: 2,
  snapshotId, expiresAtUtc: '2026-10-22T10:15:00Z', items: wells.slice(0, 1000),
  facets: [{ key: 'deviceType', label: 'Device type', hasMore: false, knownCount: 0, values: [{ value: 'Unspecified device', count: 1200 }] }] };
const preview: api.ImportPreview = { fingerprint: 'reviewed', levels: ['assetName', 'wellType', 'deviceType'], objectCount: 2, groupCount: 4,
  rootObjectCount: 0, fallbackObjectCount: 2, groups: [], warnings: [], conflicts: [], typesToCreate: [], canImport: true, alreadyImported: 0, actingAs: 'CORP\\gateway' };
function Workflow({ found = discovery }: { found?: Discovery }) {
  const [selection, setSelection] = useState<ImportSelection | null>({ snapshotId, query: '', facets: {} });
  const [count, setCount] = useState<number | null>(found.totalCount);
  const [busy, setBusy] = useState(false);
  return <><ConnectionInventory connection={connection} found={found} stale={false} disabled={busy}
    onSelect={(next, size) => { setSelection(next); setCount(next ? size : null); }} />
    <ConnectionImport connection={connection} adapter={adapter} selection={selection} selectedCount={count} totalCount={found.totalCount} onBusyChange={setBusy} /></>;
}
it('retains explicit wells across eleven pages and a filter, then reviews their complete source identity set', async () => {
  vi.mocked(api.searchDiscovered).mockImplementation(async (_connection, selection, _fetcher, page) => {
    const matching = selection.query === 'North' ? wells.filter(well => well.metadata.assetName === 'North') : wells;
    return { matchCount: matching.length, items: matching.slice(page!.offset, page!.offset + page!.limit) };
  });
  vi.mocked(api.previewImport).mockResolvedValue(preview);
  render(<Workflow />); const user = userEvent.setup();
  await user.click(screen.getByLabelText('Choose individual wells'));
  await user.click(screen.getByLabelText('Select Well 0001'));
  for (let page = 1; page <= 11; page++) {
    await user.click(screen.getByRole('button', { name: 'Next wells' }));
    await screen.findByText(`Showing ${page * 100 + 1}–${Math.min(page * 100 + 100, 1200)} of 1200 matching wells.`);
  }
  await user.click(screen.getByLabelText('Select Well 1101'));
  expect(screen.getByText('2 wells selected')).toBeTruthy();
  fireEvent.change(screen.getByLabelText('Search what was found'), { target: { value: 'North' } });
  expect(screen.getByRole('button', { name: 'Review import' }).matches(':disabled')).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Search', exact: true }));
  await screen.findByLabelText('Select Well 0001');
  expect((screen.getByLabelText('Select Well 0001') as HTMLInputElement).checked).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Review import' }));
  await waitFor(() => expect(api.previewImport).toHaveBeenCalledOnce());
  expect(vi.mocked(api.previewImport).mock.calls[0].slice(1)).toEqual([['assetName', 'wellType', 'deviceType'],
    { snapshotId, query: '', facets: {}, objects: [{ kind: 'foresite.well', externalId: '1' }, { kind: 'foresite.well', externalId: '1101' }] }]);
}, 15000);
it('keeps device coverage and a single unknown device facet visible', async () => {
  render(<Workflow />);
  expect(screen.getByText('0 known · 1200 unknown')).toBeTruthy();
  expect(screen.getByText('Mapped ForeSite assets represented')).toBeTruthy();
  await userEvent.click(screen.getByText('Filter discovered wells'));
  expect(screen.getByLabelText('Device type')).toBeTruthy();
  expect(screen.getByRole('option', { name: 'Unspecified device (1200)' })).toBeTruthy();
});
it('makes grouping presets and their path explicit and adds custom dimensions only after Add level', async () => {
  render(<Workflow />); const user = userEvent.setup();
  const path = screen.getByLabelText('Import hierarchy');
  expect(within(path).getAllByRole('listitem').map(item => item.textContent)).toEqual(['Production ForeSite', 'ForeSite asset', 'Well type', 'Device type', 'Wells']);
  await user.click(screen.getByText('Flat wells')); expect(within(path).getAllByRole('listitem')).toHaveLength(2);
  await user.click(screen.getByText('Customize grouping levels'));
  await user.selectOptions(screen.getByLabelText('Add a grouping level'), 'deviceType');
  expect(within(path).getAllByRole('listitem')).toHaveLength(2);
  await user.click(screen.getByRole('button', { name: 'Add level' }));
  expect(within(path).getAllByRole('listitem').map(item => item.textContent)).toEqual(['Production ForeSite', 'Device type', 'Wells']);
});
it('invalidates old review while searching and after failure, without reverting to an all-well import', async () => {
  vi.mocked(api.previewImport).mockResolvedValue(preview);
  let fail!: (error: Error) => void;
  vi.mocked(api.searchDiscovered).mockImplementation(() => new Promise((_resolve, reject) => { fail = reject; }));
  render(<Workflow />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Review import' })); await screen.findByLabelText('Import review');
  fireEvent.change(screen.getByLabelText('Search what was found'), { target: { value: 'ESP' } });
  expect(screen.queryByLabelText('Import review')).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Search', exact: true }));
  expect(screen.getByRole('button', { name: 'Review import' }).matches(':disabled')).toBe(true);
  fail(new api.ConnectionsError('unavailable'));
  await screen.findByRole('alert');
  expect(screen.getByRole('button', { name: 'Review import' }).matches(':disabled')).toBe(true);
  expect(api.importFromConnection).not.toHaveBeenCalled();
});
it('requires discovery and prevents oversized imports before review', async () => {
  const props = { connection, adapter, totalCount: 10000, selection: { snapshotId, query: '', facets: {} } };
  const { rerender } = render(<ConnectionImport {...props} selection={null} />);
  expect(screen.getByRole('button', { name: 'Review import' }).matches(':disabled')).toBe(true);
  rerender(<ConnectionImport {...props} selectedCount={10000} />);
  expect(screen.getByText(/One import can contain up to 5,000 wells/)).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Review import' }).matches(':disabled')).toBe(true);
});
