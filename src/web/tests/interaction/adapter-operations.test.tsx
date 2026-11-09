import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AdapterOperationsResponse, AdapterOperationResponse } from '../../src/api/generated/v1/types.gen';
import { AdaptersPanel } from '../../src/views/AdaptersPanel';
import { loadAdapterOperations } from '../../src/views/adapterOperations';
import { ConnectionsError, loadConnections } from '../../src/views/connections';
import { accessibilityViolations } from './axe';

vi.mock('../../src/views/adapterOperations', () => ({ loadAdapterOperations: vi.fn() }));
vi.mock('../../src/views/connections', async original => ({ ...await original<typeof import('../../src/views/connections')>(), loadConnections: vi.fn() }));
const operation = (key: string, name: string): AdapterOperationResponse => ({
  key, displayName: name, description: 'Bounded read', capability: 'CurrentData', capabilityId: 'engineering.current',
  contractVersion: '1.0', effect: 'ReadOnly', quantity: name, timestampSemantics: 'Source timestamp in UTC', qualitySemantics: 'Source reliability',
  request: { id: 'request/v1', fields: [{ name: 'externalId', dataType: 'string', required: true, description: 'Imported object' }] },
  response: { id: 'value/v1', fields: [{ name: 'value', dataType: 'decimal', required: true, description: 'Engineering value' }] },
  limits: { maximumQuantities: 1, maximumValueBytes: 65536, maximumReplyBytes: 262144, timeoutSeconds: 6 },
  documentation: { invocation: 'ICurrentDataProvider.GetCurrentAsync(externalId, quantity, cancellationToken)',
    executionBoundary: 'Adapter contract in the Integration Gateway; runtime authorization applies.',
    requestExampleJson: '{"externalId":"101","quantity":"PumpFillage"}', responseExampleJson: '{"value":62.5}' },
});
const profiles: AdapterOperationsResponse[] = [
  { adapterKey: 'foresite', displayName: 'ForeSite', description: 'ForeSite adapter reference', operations: [operation('pump', 'PumpFillage'), operation('average', 'AveragePumpFillage'),
    operation('cards', 'SurfaceCards'), operation('well', 'Well details'), operation('trend', 'PumpFillage history')], capabilities: [
    { key: 'surveillance', displayName: 'Surveillance data', description: 'Supported current surveillance fields.', contractVersion: '1.0', operationKeys: ['pump', 'average'],
      workflows: [{ key: 'applicable', displayName: 'Read an applicable point', purpose: 'Discover then read.', readiness: 'ReferenceOnly',
        contractVersion: '1.0', operationKeys: ['pump', 'average'],
        steps: [{ key: 'discover', description: 'Check applicable quantities', providerApi: 'GET Surveillance/AvailableQuantities/{wellId}',
          requirement: 'Map WellQuantity ids', onFailure: 'Stop when not applicable' },
          { key: 'read', description: 'Fetch well status', providerApi: 'GET Surveillance/well/{wellId}' }] }] },
    { key: 'configuration', displayName: 'Well configuration data', description: 'Supported well details.', contractVersion: '1.0', operationKeys: ['well'] },
    { key: 'trends', displayName: 'Well trend data', description: 'Bounded time-range reads.', contractVersion: '1.0', operationKeys: ['trend'] },
    { key: 'cards', displayName: 'Card data', description: 'Structured surface cards.', contractVersion: '1.0', operationKeys: ['cards'] },
  ] },
  { adapterKey: 'petrovisor', displayName: 'PetroVisor', description: 'Workspace inventory', operations: [], capabilities: [] },
];
beforeEach(() => { vi.mocked(loadAdapterOperations).mockResolvedValue(profiles); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe('Adapter capability reference', () => {
  it('prefers ForeSite, puts the simulator last, and opens the first capability without extra clicks', async () => {
    const simulator: AdapterOperationsResponse = {
      adapterKey: 'simulator', displayName: 'Provider Simulator', description: 'A built-in simulated provider for development and demonstration.',
      operations: [], capabilities: [],
    };
    vi.mocked(loadAdapterOperations).mockResolvedValue([simulator, profiles[1]!, profiles[0]!]);
    const { container } = render(<AdaptersPanel />);
    const picker = screen.getByRole('combobox', { name: 'Adapter' }) as HTMLSelectElement;
    await waitFor(() => expect(picker.value).toBe('foresite'));
    expect(Array.from(picker.options, option => option.value)).toEqual(['foresite', 'petrovisor', 'simulator']);
    const initialCapability = await screen.findByRole('button', { name: /Surveillance data/ });
    expect(initialCapability.getAttribute('aria-current')).toBe('true');
    expect(screen.getByRole('tab', { name: 'Overview' })).toBeTruthy();
    expect(screen.getByText('PumpFillage')).toBeTruthy();
    expect(container.querySelector('.operation-controls.panel')).toBeTruthy();
    expect(container.querySelector('.adapter-operations')?.getAttribute('data-has-selection')).toBe('false');
    expect(screen.queryByText(/Explore the APIs and data capabilities/)).toBeNull();
    expect(screen.queryByText('ForeSite adapter reference')).toBeNull();
    expect(screen.queryByText('A built-in simulated provider for development and demonstration.')).toBeNull();
  });

  it('prefers a non-simulator fallback and retains a deliberate simulator selection on refresh', async () => {
    const user = userEvent.setup();
    const simulator: AdapterOperationsResponse = {
      adapterKey: 'simulator', displayName: 'Provider Simulator', description: 'Development fixture', operations: [], capabilities: [],
    };
    vi.mocked(loadAdapterOperations).mockResolvedValue([simulator, profiles[1]!]);
    render(<AdaptersPanel />);
    const picker = screen.getByRole('combobox', { name: 'Adapter' }) as HTMLSelectElement;
    await waitFor(() => expect(picker.value).toBe('petrovisor'));
    await screen.findByText('This adapter has no implemented data capabilities to display.');
    await user.selectOptions(picker, 'simulator');
    await user.click(screen.getByRole('button', { name: 'Refresh capabilities' }));
    await waitFor(() => expect(loadAdapterOperations).toHaveBeenCalledTimes(2));
    expect(picker.value).toBe('simulator');
    expect(await screen.findByText('This adapter has no implemented data capabilities to display.')).toBeTruthy();
  });

  it('presents capability families and keeps supported quantities inside details, without loading Connections or live data', async () => {
    const user = userEvent.setup();
    const fetch = vi.spyOn(globalThis, 'fetch');
    const { container } = render(<AdaptersPanel />);
    const surveillance = await screen.findByRole('button', { name: /Surveillance data/ });
    expect(surveillance.getAttribute('aria-current')).toBe('true');
    expect(screen.getByRole('tab', { name: 'Overview' })).toBeTruthy();
    expect(screen.queryByRole('combobox', { name: 'Connection' })).toBeNull();
    expect(screen.queryByRole('button', { name: /^PumpFillage/ })).toBeNull();
    expect(screen.getByRole('button', { name: /Well configuration data/ })).toBeTruthy();
    await user.click(surveillance);
    expect(surveillance.getAttribute('aria-current')).toBe('true');
    expect(screen.getByRole('region', { name: 'Workflow readiness' })).toBeTruthy();
    expect(screen.getByText(/An adapter operation is not automatically available to a hosted logic module/)).toBeTruthy();
    expect(screen.getByRole('table', { name: 'Supported data and operations' })).toBeTruthy();
    expect(screen.getByText('PumpFillage')).toBeTruthy();
    await user.click(screen.getByRole('tab', { name: 'API and examples' }));
    expect(screen.getByText(/ICurrentDataProvider.GetCurrentAsync/)).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Recommended provider workflow' })).toBeTruthy();
    expect(screen.getByText('GET Surveillance/AvailableQuantities/{wellId}')).toBeTruthy();
    expect(screen.getByText('GET Surveillance/well/{wellId}')).toBeTruthy();
    expect(screen.getByRole('complementary', { name: 'For logic module authors' })).toBeTruthy();
    expect(screen.getByText(/Do not call provider URLs from a module/)).toBeTruthy();
    expect(screen.getByText(/some provider checks are not yet connected/)).toBeTruthy();
    expect(screen.getByText(/Synthetic example only/)).toBeTruthy();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Supported data example' }), 'average');
    await user.click(screen.getByRole('tab', { name: 'Contracts' }));
    expect(screen.getByRole('table', { name: 'Request contract' })).toBeTruthy();
    expect(screen.getByRole('region', { name: 'Data validity and provenance' })).toBeTruthy();
    expect(screen.getByText('Source timestamp in UTC')).toBeTruthy();
    expect(screen.getByText(/Retrieval time must not replace/)).toBeTruthy();
    await user.click(screen.getByRole('tab', { name: 'Limits' }));
    expect(screen.getByText('6 seconds')).toBeTruthy();
    expect(await accessibilityViolations(container)).toEqual([]);
    expect(loadConnections).not.toHaveBeenCalled();
    expect(fetch).not.toHaveBeenCalled();
  });

  it('searches capability names and supported fields, and resets selection when switching adapters', async () => {
    const user = userEvent.setup();
    render(<AdaptersPanel />);
    await user.click(await screen.findByRole('button', { name: /Card data/ }));
    await user.type(screen.getByRole('searchbox', { name: 'Search capabilities' }), 'PumpFillage');
    expect(screen.getByRole('button', { name: /Surveillance data/ })).toBeTruthy();
    expect(screen.getByRole('button', { name: /Well trend data/ })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Card data/ })).toBeNull();
    expect(screen.getByRole('button', { name: /Surveillance data/ }).getAttribute('aria-current')).toBe('true');
    expect(screen.getByRole('tab', { name: 'Limits' })).toBeTruthy();
    await user.clear(screen.getByRole('searchbox', { name: 'Search capabilities' }));
    expect(screen.getByRole('button', { name: /Card data/ }).getAttribute('aria-current')).toBe('true');
    await user.type(screen.getByRole('searchbox', { name: 'Search capabilities' }), 'no match');
    expect(screen.queryByRole('tab', { name: 'Limits' })).toBeNull();
    expect(screen.getByText('No capabilities match your search.')).toBeTruthy();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Adapter' }), 'petrovisor');
    expect(screen.getByText('This adapter has no implemented data capabilities to display.')).toBeTruthy();
  });

  it('renders a new adapter-defined capability without application-specific inventory changes', async () => {
    vi.mocked(loadAdapterOperations).mockResolvedValue([{ adapterKey: 'fixture', displayName: 'Fixture', description: 'Calculation fixture',
      operations: [{ ...operation('calculate', 'Pressure calculation'), capability: 'Calculation' }],
      capabilities: [{ key: 'pressure-model', displayName: 'Engineering calculations', description: 'Typed calculations.', contractVersion: '1.0', operationKeys: ['calculate'] }] }]);
    render(<AdaptersPanel />);
    await userEvent.click(await screen.findByRole('button', { name: /Engineering calculations/ }));
    expect(screen.getByText('Pressure calculation')).toBeTruthy();
  });

  it('shows loading/access failure and retries without retaining stale descriptors', async () => {
    vi.mocked(loadAdapterOperations).mockRejectedValueOnce(new ConnectionsError('accessDenied'));
    render(<AdaptersPanel />);
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', 'You do not have access to adapter metadata.');
    await userEvent.click(screen.getByRole('button', { name: 'Refresh capabilities' }));
    expect(await screen.findByRole('button', { name: /Surveillance data/ })).toBeTruthy();
  });

  it('distinguishes a metadata authorization failure from an uninstalled adapter', async () => {
    const user = userEvent.setup();
    render(<AdaptersPanel />);
    await screen.findByRole('button', { name: /Surveillance data/ });
    vi.mocked(loadAdapterOperations).mockRejectedValue(new ConnectionsError('accessDenied'));
    await user.click(screen.getByRole('button', { name: 'Refresh capabilities' }));
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', 'You do not have access to adapter metadata.');
    expect(screen.getByRole('option', { name: 'foresite (unavailable)' })).toBeTruthy();
    expect(screen.queryByText(/no longer installed/)).toBeNull();
  });

  it('does not substitute another adapter when a refresh removes the selected adapter', async () => {
    const user = userEvent.setup();
    render(<AdaptersPanel />);
    await user.click(await screen.findByRole('button', { name: /Surveillance data/ }));
    vi.mocked(loadAdapterOperations).mockResolvedValue([{ ...profiles[0], adapterKey: 'other', displayName: 'Another adapter' }]);
    await user.click(screen.getByRole('button', { name: 'Refresh capabilities' }));
    expect(await screen.findByText(/The selected adapter is no longer installed/)).toBeTruthy();
    expect(screen.getByRole('combobox', { name: 'Adapter' })).toHaveProperty('value', 'foresite');
    expect(screen.queryByRole('button', { name: /Surveillance data/ })).toBeNull();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Adapter' }), 'other');
    expect(screen.getByRole('button', { name: /Surveillance data/ })).toBeTruthy();
  });

  it('refreshes metadata while preserving the selected adapter and capability', async () => {
    const user = userEvent.setup();
    render(<AdaptersPanel />);
    await user.click(await screen.findByRole('button', { name: /Card data/ }));
    await user.click(screen.getByRole('button', { name: 'Refresh capabilities' }));
    await waitFor(() => expect(screen.getByRole('tab', { name: 'Overview' })).toBeTruthy());
    expect(screen.getByRole('button', { name: /Card data/ }).getAttribute('aria-current')).toBe('true');
    expect(loadAdapterOperations).toHaveBeenCalledTimes(2);
  });
});
