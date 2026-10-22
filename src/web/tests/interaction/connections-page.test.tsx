import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConnectionsPage } from '../../src/views/ConnectionsPage';
import * as api from '../../src/views/connections';
import type { Adapter, Connection } from '../../src/views/connections';

vi.mock('../../src/views/connections', async importOriginal => ({
  ...await importOriginal<typeof import('../../src/views/connections')>(),
  loadConnections: vi.fn(), loadAdapters: vi.fn(), createConnection: vi.fn(), updateConnection: vi.fn(),
  setConnectionEnabled: vi.fn(), testConnection: vi.fn(), deleteConnection: vi.fn(), discoverConnection: vi.fn(),
  previewImport: vi.fn(), importFromConnection: vi.fn(), searchDiscovered: vi.fn(),
}));

const snapshotId = '11111111-1111-4111-8111-111111111111';
const adapter: Adapter = {
  key: 'simulator', displayName: 'Provider Simulator', description: 'A built-in simulated provider.',
  settingsVersion: 1, capabilities: ['Current engineering data'], canDiscover: false, groupingFields: [],
  fields: [{ key: 'baseUrl', label: 'Simulator address', helpText: 'Where it listens.', kind: 'Url', required: true, maxLength: 256 }],
};
const saved = (extra: Partial<Connection> = {}): Connection => ({
  id: '00000000-0000-4000-8000-000000000001', name: 'Field simulator', adapterKey: 'simulator', settingsVersion: 1,
  settings: { baseUrl: 'http://127.0.0.1:5091' }, isEnabled: true, revision: 1,
  createdAtUtc: '2026-10-20T10:00:00Z', updatedAtUtc: '2026-10-20T10:00:00Z', lastTest: null, lastTestIsCurrent: false, ...extra,
});

beforeEach(() => {
  vi.resetAllMocks();
  window.location.hash = '#connections';
  vi.mocked(api.loadAdapters).mockResolvedValue([adapter]);
});
afterEach(cleanup);

describe('Connections page', () => {
  it('lists connections with their state and opens one', async () => {
    vi.mocked(api.loadConnections).mockResolvedValue([saved()]);
    render(<ConnectionsPage canManage />);
    await screen.findByText('Field simulator');
    expect(screen.getByText('Not tested')).toBeTruthy();
    await userEvent.click(screen.getByText('Field simulator'));
    expect((await screen.findByLabelText('Simulator address (required)') as HTMLInputElement).value).toBe('http://127.0.0.1:5091');
    expect(screen.getByText(/system cannot be changed/i)).toBeTruthy();
  });

  it('creates a connection from the fields the adapter declares', async () => {
    vi.mocked(api.loadConnections).mockResolvedValue([]);
    vi.mocked(api.createConnection).mockResolvedValue(saved());
    render(<ConnectionsPage canManage />);
    await screen.findByText('There are no connections yet.');
    await userEvent.click(screen.getByText('New connection'));
    await userEvent.type(await screen.findByLabelText('Name'), 'Field simulator');
    await userEvent.selectOptions(screen.getByLabelText('System'), 'simulator');
    await userEvent.type(screen.getByLabelText('Simulator address (required)'), 'http://127.0.0.1:5091');
    await userEvent.click(screen.getByText('Create connection'));
    await waitFor(() => expect(api.createConnection).toHaveBeenCalledTimes(1));
    const [chosen, draft] = vi.mocked(api.createConnection).mock.calls[0];
    expect(chosen.key).toBe('simulator');
    expect(draft).toEqual({ name: 'Field simulator', settings: { baseUrl: 'http://127.0.0.1:5091' } });
  });

  it('says so when the name is taken or the settings are refused', async () => {
    vi.mocked(api.loadConnections).mockResolvedValue([saved()]);
    vi.mocked(api.updateConnection)
      .mockRejectedValueOnce(new api.ConnectionsError('nameTaken'))
      .mockRejectedValueOnce(new api.ConnectionsError('invalidSettings', 'baseUrl: Enter a full http or https address.'));
    render(<ConnectionsPage canManage />);
    await userEvent.click(await screen.findByText('Field simulator'));
    const name = await screen.findByLabelText('Name');
    await userEvent.type(name, ' 2');
    await userEvent.click(screen.getByText('Save'));
    expect((await screen.findByRole('alert')).textContent).toBe('Another connection already uses that name.');
    await userEvent.click(screen.getByText('Save'));
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('Enter a full http or https address.'));
  });

  it('tests only saved settings and shows the outcome, the account used and when it is out of date', async () => {
    const tested = saved({
      revision: 1, lastTestIsCurrent: true,
      lastTest: { outcome: 'Healthy', message: 'The simulator answered.', actingAs: 'NT AUTHORITY\\LOCAL SERVICE',
        testedAtUtc: '2026-10-20T10:05:00Z', testedRevision: 1 },
    });
    vi.mocked(api.loadConnections).mockResolvedValue([saved()]);
    vi.mocked(api.testConnection).mockResolvedValue(tested);
    render(<ConnectionsPage canManage />);
    await userEvent.click(await screen.findByText('Field simulator'));
    const test = await screen.findByText('Test connection') as HTMLButtonElement;
    await userEvent.type(screen.getByLabelText('Name'), 'x');
    expect(test.disabled).toBe(true);
    expect(screen.getByText('Save your changes before testing.')).toBeTruthy();
    await userEvent.clear(screen.getByLabelText('Name'));
    await userEvent.type(screen.getByLabelText('Name'), 'Field simulator');
    expect(test.disabled).toBe(false);
    await userEvent.click(test);
    expect(await screen.findByText(/Reached as NT AUTHORITY/)).toBeTruthy();
    expect(screen.getByText('The simulator answered.')).toBeTruthy();
  });

  it('does not offer a test for a disabled connection', async () => {
    vi.mocked(api.loadConnections).mockResolvedValue([saved({ isEnabled: false })]);
    render(<ConnectionsPage canManage />);
    await userEvent.click(await screen.findByText('Field simulator'));
    expect((await screen.findByText('Test connection') as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getAllByText('Disabled').length).toBeGreaterThan(0);
    expect(screen.getByText('Enable')).toBeTruthy();
  });

  it('groups actions by purpose and lets unsaved edits be discarded', async () => {
    vi.mocked(api.loadConnections).mockResolvedValue([saved()]);
    render(<ConnectionsPage canManage />);
    await userEvent.click(await screen.findByText('Field simulator'));
    const name = await screen.findByLabelText('Name') as HTMLInputElement;
    expect(screen.queryByText('Discard changes')).toBeNull();
    const checks = screen.getByRole('group', { name: 'Check this connection' });
    expect(checks.textContent).toContain('Test connection');
    const manage = screen.getByRole('region', { name: 'Manage connection' });
    expect(manage.textContent).toContain('Disable');
    expect(manage.textContent).toContain('Delete');
    await userEvent.type(name, ' 2');
    expect(name.value).toBe('Field simulator 2');
    expect(checks.textContent).toContain('Save your changes before testing.');
    await userEvent.click(screen.getByText('Discard changes'));
    expect(name.value).toBe('Field simulator');
    expect(screen.queryByText('Discard changes')).toBeNull();
  });

  it('asks before deleting', async () => {
    vi.mocked(api.loadConnections).mockResolvedValueOnce([saved()]).mockResolvedValue([]);
    vi.mocked(api.deleteConnection).mockResolvedValue();
    render(<ConnectionsPage canManage />);
    await userEvent.click(await screen.findByText('Field simulator'));
    await userEvent.click(await screen.findByText('Delete'));
    expect(api.deleteConnection).not.toHaveBeenCalled();
    await userEvent.click(screen.getByText('Yes, delete'));
    await waitFor(() => expect(api.deleteConnection).toHaveBeenCalledTimes(1));
  });

  it('is read only without the manage permission', async () => {
    vi.mocked(api.loadConnections).mockResolvedValue([saved()]);
    render(<ConnectionsPage canManage={false} />);
    await userEvent.click(await screen.findByText('Field simulator'));
    expect((await screen.findByLabelText('Name') as HTMLInputElement).disabled).toBe(true);
    expect(screen.queryByText('New connection')).toBeNull();
    expect(screen.queryByText('Save')).toBeNull();
    expect(screen.queryByText('Delete')).toBeNull();
    expect(screen.getByText('You can view connections but not change them.')).toBeTruthy();
  });

  it('keeps existing connections visible when the adapter list cannot be loaded', async () => {
    vi.mocked(api.loadConnections).mockResolvedValue([saved()]);
    vi.mocked(api.loadAdapters).mockRejectedValue(new api.ConnectionsError('gatewayUnavailable'));
    render(<ConnectionsPage canManage />);
    await screen.findByText('Field simulator');
    expect(screen.getByText(/could not be loaded, so new connections cannot be created/)).toBeTruthy();
    expect((screen.getByText('New connection') as HTMLButtonElement).disabled).toBe(true);
  });

  it('shows when the found list is kept until and warns when settings changed after looking', async () => {
    const finder: Adapter = { ...adapter, key: 'foresite', displayName: 'ForeSite', canDiscover: true };
    vi.mocked(api.loadAdapters).mockResolvedValue([finder]);
    vi.mocked(api.loadConnections).mockResolvedValue([saved({ adapterKey: 'foresite' })]);
    vi.mocked(api.discoverConnection).mockResolvedValue({
      complete: true, actingAs: null, warnings: [], totalCount: 1, snapshotId, expiresAtUtc: '2026-10-21T10:15:00Z',
      facets: [], items: [{ externalId: '1', displayName: 'Alpha 1', kind: 'foresite.well', metadata: {} }],
    });
    render(<ConnectionsPage canManage />);
    await userEvent.click(await screen.findByText('Field simulator'));
    await userEvent.click(await screen.findByText('Find what it holds'));
    await screen.findByText('Alpha 1');
    expect(screen.getByText(/This list is kept until/)).toBeTruthy();
    expect(screen.getByText('Step 1 of 2 · Discover')).toBeTruthy();
    expect(screen.queryByText(/You have unsaved changes\. This list was read/)).toBeNull();
    await userEvent.type(screen.getByLabelText('Name'), ' 2');
    expect(screen.getByText(/You have unsaved changes\. This list was read/)).toBeTruthy();
  });

  it('finds what a connection holds, searchable, and says who it looked as', async () => {
    const finder: Adapter = { ...adapter, key: 'foresite', displayName: 'ForeSite', canDiscover: true,
      groupingFields: [{ key: 'wellType', label: 'Well type' }] };
    vi.mocked(api.loadAdapters).mockResolvedValue([finder]);
    vi.mocked(api.loadConnections).mockResolvedValue([saved({ adapterKey: 'foresite' })]);
    vi.mocked(api.discoverConnection).mockResolvedValue({
      complete: true, actingAs: 'CORP\\svc-wsa', warnings: ['Asset names were not available.'],
      totalCount: 2, snapshotId, expiresAtUtc: '2026-10-21T10:15:00Z',
      facets: [{ key: 'wellType', label: 'Well type', hasMore: false, values: [{ value: 'ESP', count: 1 }, { value: 'RRL', count: 1 }] }],
      items: [
        { externalId: '1', displayName: 'Alpha 1', kind: 'foresite.well', metadata: { wellType: 'RRL', field: 'Alpha' } },
        { externalId: '2', displayName: 'Bravo 1', kind: 'foresite.well', metadata: { wellType: 'ESP', field: 'Bravo' } },
      ],
    });
    render(<ConnectionsPage canManage />);
    await userEvent.click(await screen.findByText('Field simulator'));
    await userEvent.click(await screen.findByText('Find what it holds'));
    await screen.findByText('Wells discovered');
    expect(screen.getByText('Alpha 1')).toBeTruthy();
    expect(screen.getByText('Asset names were not available.')).toBeTruthy();
    expect(screen.getByText(/Found as CORP/)).toBeTruthy();
    expect(screen.queryByText(/list is incomplete/)).toBeNull();
    vi.mocked(api.searchDiscovered).mockResolvedValue({
      matchCount: 1, items: [{ externalId: '2', displayName: 'Bravo 1', kind: 'foresite.well', metadata: { wellType: 'ESP', field: 'Bravo' } }],
    });
    await userEvent.type(screen.getByLabelText('Search what was found'), 'esp');
    await userEvent.click(screen.getByRole('button', { name: 'Search' }));
    await screen.findByText('Showing 1–1 of 1 matching wells.');
    expect(vi.mocked(api.searchDiscovered).mock.calls[0][1]).toEqual({ snapshotId, query: 'esp', facets: {} });
    expect(screen.queryByText('Alpha 1')).toBeNull();
    expect(screen.getByText('Bravo 1')).toBeTruthy();
  });

  it('shows how many were found, narrows by a fact value, and imports only the chosen part', async () => {
    const finder: Adapter = { ...adapter, key: 'foresite', displayName: 'ForeSite', canDiscover: true,
      groupingFields: [{ key: 'wellType', label: 'Well type' }] };
    vi.mocked(api.loadAdapters).mockResolvedValue([finder]);
    vi.mocked(api.loadConnections).mockResolvedValue([saved({ adapterKey: 'foresite' })]);
    vi.mocked(api.discoverConnection).mockResolvedValue({
      complete: true, actingAs: 'CORP\\svc', warnings: [], totalCount: 12000, snapshotId, expiresAtUtc: '2026-10-21T10:15:00Z',
      facets: [{ key: 'wellType', label: 'Well type', hasMore: false, values: [{ value: 'ESP', count: 4000 }, { value: 'RRL', count: 8000 }] }],
      items: [{ externalId: '1', displayName: 'Alpha 1', kind: 'foresite.well', metadata: { wellType: 'RRL' } }],
    });
    vi.mocked(api.searchDiscovered).mockResolvedValue({ matchCount: 4000, items: [{ externalId: '2', displayName: 'ESP well', kind: 'foresite.well', metadata: { wellType: 'ESP' } }] });
    vi.mocked(api.previewImport).mockResolvedValue({
      fingerprint: 'F', levels: [], objectCount: 4000, groupCount: 0, fallbackObjectCount: 0, rootObjectCount: 4000, groups: [],
      warnings: [], conflicts: [], typesToCreate: [], canImport: true, actingAs: null, alreadyImported: 0,
    });
    render(<ConnectionsPage canManage />);
    await userEvent.click(await screen.findByText('Field simulator'));
    await userEvent.click(await screen.findByText('Find what it holds'));
    await screen.findByText('Wells discovered');
    expect(screen.getByText('Showing 1–1 of 12000 matching wells.')).toBeTruthy();
    expect(screen.getByText('Everything found (12000) is selected for import.')).toBeTruthy();
    await userEvent.click(screen.getByText('Filter discovered wells'));
    await userEvent.selectOptions(screen.getByLabelText('Well type'), 'ESP');
    await screen.findByText('Showing 1–1 of 4000 matching wells.');
    expect(vi.mocked(api.searchDiscovered).mock.calls[0][1]).toEqual({ snapshotId, query: '', facets: { wellType: ['ESP'] } });
    expect(screen.getByText('4000 of the 12000 found are selected for import.')).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Review import' }));
    await waitFor(() => expect(api.previewImport).toHaveBeenCalled());
    expect(vi.mocked(api.previewImport).mock.calls[0][2]).toEqual({ snapshotId, query: '', facets: { wellType: ['ESP'] } });
    // Choosing something else makes the review stale, so it is not left on screen.
    await screen.findByLabelText('Import review');
    await userEvent.selectOptions(screen.getByLabelText('Well type'), 'RRL');
    await waitFor(() => expect(screen.queryByLabelText('Import review')).toBeNull());
  });

  it('says to find again when the kept list has expired', async () => {
    const finder: Adapter = { ...adapter, key: 'foresite', displayName: 'ForeSite', canDiscover: true,
      groupingFields: [{ key: 'wellType', label: 'Well type' }] };
    vi.mocked(api.loadAdapters).mockResolvedValue([finder]);
    vi.mocked(api.loadConnections).mockResolvedValue([saved({ adapterKey: 'foresite' })]);
    vi.mocked(api.discoverConnection).mockResolvedValue({
      complete: true, actingAs: null, warnings: [], totalCount: 2, snapshotId, expiresAtUtc: '2026-10-21T10:15:00Z',
      facets: [], items: [{ externalId: '1', displayName: 'Alpha 1', kind: 'foresite.well', metadata: {} },
        { externalId: '2', displayName: 'Bravo 1', kind: 'foresite.well', metadata: {} }],
    });
    vi.mocked(api.searchDiscovered).mockRejectedValue(new api.ConnectionsError('snapshotExpired'));
    render(<ConnectionsPage canManage />);
    await userEvent.click(await screen.findByText('Field simulator'));
    await userEvent.click(await screen.findByText('Find what it holds'));
    await userEvent.type(await screen.findByLabelText('Search what was found'), 'x');
    await userEvent.click(screen.getByRole('button', { name: 'Search' }));
    expect((await screen.findByRole('alert')).textContent).toMatch(/expired/);
  });

  it('never presents an incomplete answer as "nothing there"', async () => {
    const finder: Adapter = { ...adapter, canDiscover: true };
    vi.mocked(api.loadAdapters).mockResolvedValue([finder]);
    vi.mocked(api.loadConnections).mockResolvedValue([saved()]);
    vi.mocked(api.discoverConnection).mockResolvedValue({
      complete: false, actingAs: null, warnings: ['The system could not be reached.'], items: [],
      totalCount: 0, snapshotId, facets: [], expiresAtUtc: '2026-10-21T10:15:00Z',
    });
    render(<ConnectionsPage canManage />);
    await userEvent.click(await screen.findByText('Field simulator'));
    await userEvent.click(await screen.findByText('Find what it holds'));
    expect((await screen.findByRole('alert')).textContent).toMatch(/incomplete/);
    expect(screen.getByText('The system could not be reached.')).toBeTruthy();
    expect(screen.queryByText('Nothing was found.')).toBeNull();
  });

  it('offers Find only when the system can list what it holds', async () => {
    vi.mocked(api.loadConnections).mockResolvedValue([saved()]);
    render(<ConnectionsPage canManage />);
    await userEvent.click(await screen.findByText('Field simulator'));
    await screen.findByLabelText('Name');
    expect(screen.queryByText('Find what it holds')).toBeNull();
  });

  const importer: Adapter = {
    ...adapter, key: 'foresite', displayName: 'ForeSite', canDiscover: true,
    groupingFields: [{ key: 'assetName', label: 'ForeSite asset' }, { key: 'wellType', label: 'Well type' }],
  };
  const preview = (extra: Partial<api.ImportPreview> = {}): api.ImportPreview => ({
    fingerprint: 'ABC', levels: ['assetName'], objectCount: 3, groupCount: 2, fallbackObjectCount: 1, rootObjectCount: 0,
    warnings: [], conflicts: [], typesToCreate: ['well', 'group'], canImport: true, actingAs: 'CORP\\svc', alreadyImported: 0,
    groups: [
      { name: 'North', isFallback: false, objectCount: 2, sampleNames: ['Alpha 1', 'Alpha 2'], children: [] },
      { name: 'Unassigned', isFallback: true, objectCount: 1, sampleNames: ['Orphan'], children: [] },
    ], ...extra,
  });
  const openImporter = async () => {
    vi.mocked(api.loadAdapters).mockResolvedValue([importer]);
    vi.mocked(api.loadConnections).mockResolvedValue([saved({ adapterKey: 'foresite' })]);
    render(<ConnectionsPage canManage />);
    await userEvent.click(await screen.findByText('Field simulator'));
    await screen.findByText('Import into Assets');
    vi.mocked(api.discoverConnection).mockResolvedValue({ complete: true, actingAs: null, warnings: [], totalCount: 3,
      snapshotId, expiresAtUtc: '2026-10-21T10:15:00Z', facets: [], items: [
        { externalId: '1', displayName: 'Sample well', kind: 'foresite.well', metadata: {} }] });
    await userEvent.click(screen.getByText('Find what it holds'));
    await screen.findByText('Wells discovered');
    await userEvent.click(screen.getByText('Flat wells'));
    await userEvent.click(screen.getByText('Customize grouping levels'));
  };

  it('reviews the grouped tree before anything is created, then imports exactly what was reviewed', async () => {
    vi.mocked(api.previewImport).mockResolvedValue(preview());
    vi.mocked(api.importFromConnection).mockResolvedValue({
      wellsCreated: 3, groupsCreated: 2, groupsReused: 1, alreadyImported: 0, typesCreated: ['well', 'group'],
    });
    await openImporter();
    await userEvent.selectOptions(screen.getByLabelText('Add a grouping level'), 'assetName');
    await userEvent.click(screen.getByText('Add level'));
    await userEvent.click(screen.getByText('Review import'));
    await screen.findByText('3 wells to import · 2 groups to create or reuse.');
    expect(vi.mocked(api.previewImport).mock.calls[0][1]).toEqual(['assetName']);
    expect(screen.getByText('North')).toBeTruthy();
    expect(screen.getByText(/Unassigned \(no value\)/)).toBeTruthy();
    expect(screen.getByText(/asset types will be created first: well, group/)).toBeTruthy();
    expect(api.importFromConnection).not.toHaveBeenCalled();
    await userEvent.click(screen.getByText('Import 3 wells'));
    expect(api.importFromConnection).not.toHaveBeenCalled();
    await userEvent.click(screen.getByText('Yes, import'));
    await screen.findByText('Imported 3 wells and created 2 groups.');
    expect(vi.mocked(api.importFromConnection).mock.calls[0].slice(1)).toEqual([['assetName'], 'ABC', { snapshotId, query: '', facets: {} }]);
    expect(screen.getByText('1 groups already existed and were reused.')).toBeTruthy();
    expect(screen.queryByText('3 wells to import · 2 groups to create or reuse.')).toBeNull();
  });

  it('drops a reviewed plan when the grouping changes and when the source moved on', async () => {
    vi.mocked(api.previewImport).mockResolvedValue(preview());
    vi.mocked(api.importFromConnection).mockRejectedValue(new api.ConnectionsError('planChanged'));
    await openImporter();
    await userEvent.click(screen.getByText('Review import'));
    await screen.findByText('3 wells to import · 2 groups to create or reuse.');
    await userEvent.selectOptions(screen.getByLabelText('Add a grouping level'), 'wellType');
    await userEvent.click(screen.getByText('Add level'));
    expect(screen.queryByText('3 wells to import · 2 groups to create or reuse.')).toBeNull();
    await userEvent.click(screen.getByText('Review import'));
    await userEvent.click(await screen.findByText('Import 3 wells'));
    await userEvent.click(screen.getByText('Yes, import'));
    expect((await screen.findByRole('alert')).textContent).toMatch(/has changed since you reviewed it/);
    expect(screen.queryByText('3 wells to import · 2 groups to create or reuse.')).toBeNull();
  });

  it('will not import while there are conflicts, and says why a scoped manager is refused', async () => {
    vi.mocked(api.previewImport).mockResolvedValueOnce(preview({ canImport: false, conflicts: ['The existing asset type “well” does not accept the values for 1.'] }))
      .mockRejectedValueOnce(new api.ConnectionsError('needsAllAssets'));
    await openImporter();
    await userEvent.click(screen.getByText('Review import'));
    expect((await screen.findByText(/does not accept the values/)).getAttribute('role')).toBe('alert');
    expect((screen.getByText('Import 3 wells') as HTMLButtonElement).disabled).toBe(true);
    await userEvent.click(screen.getByText('Review import'));
    await waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(/permission to manage all Assets/));
    expect(screen.queryByText('Import 3 wells')).toBeNull();
  });

  it('reorders and removes grouping levels', async () => {
    vi.mocked(api.previewImport).mockResolvedValue(preview());
    await openImporter();
    await userEvent.selectOptions(screen.getByLabelText('Add a grouping level'), 'assetName');
    await userEvent.click(screen.getByText('Add level'));
    await userEvent.selectOptions(screen.getByLabelText('Add a grouping level'), 'wellType');
    await userEvent.click(screen.getByText('Add level'));
    await userEvent.click(screen.getByLabelText('Move Well type up'));
    await userEvent.click(screen.getByText('Review import'));
    await waitFor(() => expect(api.previewImport).toHaveBeenCalled());
    expect(vi.mocked(api.previewImport).mock.calls[0][1]).toEqual(['wellType', 'assetName']);
    await userEvent.click(screen.getByLabelText('Remove ForeSite asset'));
    expect(screen.getByText('Level 1: Well type')).toBeTruthy();
  });

  it('offers no import to someone who cannot manage connections or when the connection is disabled', async () => {
    vi.mocked(api.loadAdapters).mockResolvedValue([importer]);
    vi.mocked(api.loadConnections).mockResolvedValue([saved({ adapterKey: 'foresite', isEnabled: false })]);
    render(<ConnectionsPage canManage />);
    await userEvent.click(await screen.findByText('Field simulator'));
    await screen.findByLabelText('Name');
    expect(screen.queryByText('Import into Assets')).toBeNull();
  });
});
