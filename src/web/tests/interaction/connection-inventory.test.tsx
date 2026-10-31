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
  await user.click(screen.getByRole('button', { name: 'Apply filters', exact: true }));
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
  await userEvent.click(screen.getByText('Device type', { selector: 'summary' }));
  expect(screen.getByRole('checkbox', { name: 'Unspecified device (1200)' })).toBeTruthy();
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
  await user.click(screen.getByRole('button', { name: 'Apply filters', exact: true }));
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

it('distinguishes same-named wells by source ID and reviews only the checked identity', async () => {
  const duplicates: Discovery = { ...discovery, totalCount: 2, sourceAssetCount: 1, facets: [], items: [
    { ...wells[0], displayName: 'Repeated well' }, { ...wells[1100], displayName: 'Repeated well' }] };
  vi.mocked(api.previewImport).mockResolvedValue({ ...preview, objectCount: 1 });
  render(<Workflow found={duplicates} />); const user = userEvent.setup();
  await user.click(screen.getByLabelText('Choose individual wells'));
  expect(screen.getByText('Source ID: 1')).toBeTruthy(); expect(screen.getByText('Source ID: 1101')).toBeTruthy();
  const boxes = screen.getAllByRole('checkbox', { name: 'Select Repeated well' });
  expect(document.getElementById(boxes[1].getAttribute('aria-describedby')!)?.textContent).toBe('Source ID: 1101');
  await user.click(boxes[1]); await user.click(screen.getByRole('button', { name: 'Review import' }));
  await waitFor(() => expect(api.previewImport).toHaveBeenCalledOnce());
  expect(vi.mocked(api.previewImport).mock.calls[0][2]?.objects).toEqual([{ kind: 'foresite.well', externalId: '1101' }]);
});

it('stages multiple values per field, searches listed values, and imports the applied OR/AND scope', async () => {
  const found: Discovery = { ...discovery, totalCount: 3, items: wells.slice(0, 3), facets: [
    { key: 'deviceType', label: 'Device type', knownCount: 2, hasMore: false, values: [
      { value: 'LufkinSam', count: 1 }, { value: 'WFordKS', count: 1 }, { value: 'Unspecified device', count: 1 }] },
    { key: 'wellType', label: 'Well type', hasMore: false, values: [{ value: 'ESP', count: 2 }, { value: 'RRL', count: 1 }] },
  ] };
  vi.mocked(api.searchDiscovered).mockResolvedValue({ matchCount: 2, items: wells.slice(0, 2) });
  vi.mocked(api.previewImport).mockResolvedValue(preview);
  render(<Workflow found={found} />); const user = userEvent.setup();
  await user.click(screen.getByText('Filter discovered wells'));
  await user.click(screen.getByText('Device type', { selector: 'summary' }));
  await user.click(screen.getByRole('checkbox', { name: 'LufkinSam (1)' }));
  await user.type(screen.getByRole('searchbox', { name: 'Search Device type values' }), 'WFord');
  expect(screen.queryByRole('checkbox', { name: 'LufkinSam (1)' })).toBeNull();
  await user.click(screen.getByRole('checkbox', { name: 'WFordKS (1)' }));
  await user.click(screen.getByText('Well type', { selector: 'summary' }));
  await user.click(screen.getByRole('checkbox', { name: 'ESP (2)' }));
  expect(api.searchDiscovered).not.toHaveBeenCalled();
  expect(screen.getByRole('button', { name: 'Review import' }).matches(':disabled')).toBe(true);
  const scope = { snapshotId, query: '', facets: { deviceType: ['LufkinSam', 'WFordKS'], wellType: ['ESP'] } };
  await user.click(screen.getByRole('button', { name: 'Apply filters' }));
  await screen.findByText('Showing 1–2 of 2 matching wells.');
  expect(vi.mocked(api.searchDiscovered).mock.calls[0][1]).toEqual(scope);
  await user.click(screen.getByRole('button', { name: 'Review import' }));
  await screen.findByLabelText('Import review');
  expect(vi.mocked(api.previewImport).mock.calls[0][2]).toEqual(scope);
  await user.clear(screen.getByRole('searchbox', { name: 'Search Device type values' }));
  expect((screen.getByRole('checkbox', { name: 'LufkinSam (1)' }) as HTMLInputElement).checked).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Clear field filters' }));
  expect(screen.queryByLabelText('Import review')).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Apply filters' }));
  await waitFor(() => expect(api.searchDiscovered).toHaveBeenCalledTimes(2));
  expect(vi.mocked(api.searchDiscovered).mock.calls[1][1]).toEqual({ snapshotId, query: '', facets: {} });
});

it('still requires Apply after clearing unapplied field choices back to Any', async () => {
  vi.mocked(api.searchDiscovered).mockResolvedValue({ matchCount: 1200, items: wells.slice(0, 100) });
  render(<Workflow />); const user = userEvent.setup();
  await user.click(screen.getByText('Filter discovered wells'));
  await user.click(screen.getByText('Device type', { selector: 'summary' }));
  await user.click(screen.getByRole('checkbox', { name: 'Unspecified device (1200)' }));
  await user.click(screen.getByRole('button', { name: 'Clear field filters' }));
  expect(screen.getByRole('button', { name: 'Review import' }).matches(':disabled')).toBe(true);
  expect(screen.getByText('Apply filters to update matching wells before reviewing or changing the selection.')).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Apply filters' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Review import' }).matches(':disabled')).toBe(false));
});

it('uses the saved canonical hierarchy and hides ordinary regrouping controls', async () => {
  vi.mocked(api.previewImport).mockResolvedValue({ ...preview, levels: [] });
  render(<ConnectionImport connection={connection} adapter={adapter} hierarchy={{ status: 'Saved', levels: [] }}
    selection={{ snapshotId, query: '', facets: {} }} selectedCount={2} totalCount={2} />);
  expect(screen.getByText(/This connection has one saved hierarchy/)).toBeTruthy();
  expect(screen.queryByText('Recommended hierarchy')).toBeNull();
  expect(screen.queryByText('Customize grouping levels')).toBeNull();
  expect(within(screen.getByLabelText('Import hierarchy')).getAllByRole('listitem').map(item => item.textContent)).toEqual(['Production ForeSite', 'Wells']);
  await userEvent.click(screen.getByRole('button', { name: 'Review import' }));
  await screen.findByLabelText('Import review');
  expect(vi.mocked(api.previewImport).mock.calls[0][1]).toEqual([]);
});

it('explicitly reviews and adopts a legacy hierarchy without replacing already-imported wells', async () => {
  vi.mocked(api.previewImport).mockResolvedValue({ ...preview, objectCount: 0, groupCount: 0, alreadyImported: 2,
    adoptingHierarchy: true, existingWells: 2, groupsToAdopt: 4 });
  vi.mocked(api.importFromConnection).mockResolvedValue({ wellsCreated: 0, groupsCreated: 0, groupsReused: 0,
    alreadyImported: 2, typesCreated: [], hierarchyAdopted: true });
  render(<ConnectionImport connection={connection} adapter={adapter} hierarchy={{ status: 'Legacy', levels: null }}
    selection={{ snapshotId, query: '', facets: {} }} selectedCount={2} totalCount={2} />);
  const user = userEvent.setup();
  await user.click(screen.getByRole('checkbox', { name: 'Review these levels as the saved hierarchy for all existing imported wells.' }));
  await user.click(screen.getByRole('button', { name: 'Review import' }));
  await screen.findByText(/Adopt the verified hierarchy for 2 existing wells and 4 groups/);
  expect(vi.mocked(api.previewImport).mock.calls[0][2]?.adoptExistingHierarchy).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Adopt hierarchy', exact: true }));
  expect(api.importFromConnection).not.toHaveBeenCalled();
  await user.click(screen.getByRole('button', { name: 'Yes, import' }));
  await screen.findByText('Existing hierarchy adopted. Well IDs and current paths were preserved.');
  expect(vi.mocked(api.importFromConnection).mock.calls[0][3]?.adoptExistingHierarchy).toBe(true);
  expect(screen.queryByText('Customize grouping levels')).toBeNull();
});

it('distinguishes same-named preview groups with their source identities', async () => {
  vi.mocked(api.previewImport).mockResolvedValue({ ...preview, groups: [
    { name: 'Repeated asset', sourceIdentity: '101', key: 'A'.repeat(64), isFallback: false, objectCount: 1, sampleNames: ['Well A'], children: [] },
    { name: 'Repeated asset', sourceIdentity: '202', key: 'B'.repeat(64), isFallback: false, objectCount: 1, sampleNames: ['Well B'], children: [] }] });
  render(<Workflow />);
  await userEvent.click(screen.getByRole('button', { name: 'Review import' }));
  const review = await screen.findByLabelText('Import review');
  expect(within(review).getAllByText('Repeated asset')).toHaveLength(2);
  expect(within(review).getByText(/Source ID: 101/)).toBeTruthy();
  expect(within(review).getByText(/Source ID: 202/)).toBeTruthy();
});
