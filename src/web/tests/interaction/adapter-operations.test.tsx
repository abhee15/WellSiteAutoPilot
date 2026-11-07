import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AdapterOperationsResponse, AdapterOperationResponse } from '../../src/api/generated/v1/types.gen';
import { AdaptersPanel } from '../../src/views/AdaptersPanel';
import { loadAdapterOperations, loadConnectionOperations } from '../../src/views/adapterOperations';
import { ConnectionsError, loadConnections } from '../../src/views/connections';
import { accessibilityViolations } from './axe';

vi.mock('../../src/views/adapterOperations', () => ({ loadAdapterOperations: vi.fn(), loadConnectionOperations: vi.fn() }));
vi.mock('../../src/views/connections', async original => ({ ...await original<typeof import('../../src/views/connections')>(), loadConnections: vi.fn() }));
const operation = (key: string, name: string): AdapterOperationResponse => ({
  key, displayName: name, description: 'Bounded read', capability: 'CurrentData', capabilityId: 'engineering.current',
  contractVersion: '1.0', effect: 'ReadOnly', quantity: name, timestampSemantics: 'Source timestamp in UTC', qualitySemantics: 'Source reliability',
  request: { id: 'request/v1', fields: [{ name: 'externalId', dataType: 'string', required: true, description: 'Imported object' }] },
  response: { id: 'value/v1', fields: [{ name: 'value', dataType: 'decimal', required: true, description: 'Engineering value' }] },
  limits: { maximumQuantities: 1, maximumValueBytes: 65536, maximumReplyBytes: 262144, timeoutSeconds: 6 },
});
const profiles: AdapterOperationsResponse[] = [
  { adapterKey: 'foresite', displayName: 'ForeSite', description: 'ForeSite connection', operations: [operation('pump', 'Pump fillage'), operation('cards', 'Surface cards')] },
  { adapterKey: 'petrovisor', displayName: 'PetroVisor', description: 'Workspace inventory', operations: [] },
];
beforeEach(() => {
  vi.mocked(loadAdapterOperations).mockResolvedValue(profiles);
  vi.mocked(loadConnections).mockResolvedValue([]);
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe('Adapter operations explorer', () => {
  it('selects and searches operations, shares accessible tabs and resets selection when switching providers', async () => {
    const user = userEvent.setup();
    const { container } = render(<AdaptersPanel />);
    const pump = await screen.findByRole('button', { name: /Pump fillage/ });
    await user.click(pump);
    expect(pump.getAttribute('aria-current')).toBe('true');
    expect(screen.getByText('Source timestamp in UTC')).toBeTruthy();
    await user.click(screen.getByRole('tab', { name: 'Request and response' }));
    expect(screen.getByRole('table', { name: 'Request contract' })).toBeTruthy();
    await user.click(screen.getByRole('tab', { name: 'Limits' }));
    expect(screen.getByText('6 seconds')).toBeTruthy();
    expect(await accessibilityViolations(container)).toEqual([]);
    await user.type(screen.getByRole('searchbox', { name: 'Search operations' }), 'no match');
    expect(screen.getByText('No operations match your search.')).toBeTruthy();
    expect(screen.queryByRole('tab', { name: 'Limits' })).toBeNull();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Adapter' }), 'petrovisor');
    expect(screen.getByText('This adapter has no implemented read operations to display.')).toBeTruthy();
    expect(loadConnectionOperations).not.toHaveBeenCalled();
  });

  it('shows loading, access errors and retry without retaining stale descriptors', async () => {
    vi.mocked(loadAdapterOperations).mockRejectedValueOnce(new ConnectionsError('accessDenied'));
    render(<AdaptersPanel />);
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', 'You do not have access to connection metadata.');
    await userEvent.click(screen.getByRole('button', { name: 'Refresh operations' }));
    expect(await screen.findByRole('button', { name: /Pump fillage/ })).toBeTruthy();
  });

  it('ignores an old connection readiness response after adapter selection changes', async () => {
    vi.mocked(loadConnections).mockResolvedValue([{ id: 'a', name: 'Saved ForeSite', adapterKey: 'foresite' } as Awaited<ReturnType<typeof loadConnections>>[number]]);
    let resolve!: (value: Awaited<ReturnType<typeof loadConnectionOperations>>) => void;
    vi.mocked(loadConnectionOperations).mockImplementation(() => new Promise(done => { resolve = done; }));
    render(<AdaptersPanel />);
    await screen.findByRole('button', { name: /Pump fillage/ });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Connection' }), 'a');
    expect(screen.getByText('Checking connection readiness…')).toBeTruthy();
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Adapter' }), 'petrovisor');
    resolve({ connectionId: 'a', adapterKey: 'foresite', connectionRevision: 1, state: 'ConnectionReady', reason: 'Old response', operationKeys: ['pump'] });
    await waitFor(() => expect(screen.queryByText('Old response')).toBeNull());
  });
});
