import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConnectionsPage } from '../../src/views/ConnectionsPage';
import * as api from '../../src/views/connections';
import type { Adapter, Connection } from '../../src/views/connections';

vi.mock('../../src/views/connections', async importOriginal => ({
  ...await importOriginal<typeof import('../../src/views/connections')>(),
  loadConnections: vi.fn(), loadAdapters: vi.fn(), createConnection: vi.fn(), updateConnection: vi.fn(),
  setConnectionEnabled: vi.fn(), testConnection: vi.fn(), deleteConnection: vi.fn(),
}));

const adapter: Adapter = {
  key: 'simulator', displayName: 'Provider Simulator', description: 'A built-in simulated provider.',
  settingsVersion: 1, capabilities: ['Current engineering data'],
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
});
