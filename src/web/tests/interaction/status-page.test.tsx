import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { StatusPage } from '../../src/views/StatusPage';
import * as api from '../../src/views/connections';
import type { Connection } from '../../src/views/connections';
import { canOpenSection } from '../../src/security/sessionAccess';

vi.mock('../../src/views/connections', async importOriginal => ({
  ...await importOriginal<typeof import('../../src/views/connections')>(),
  loadConnections: vi.fn(), loadAdapters: vi.fn(),
}));

const connection: Connection = {
  id: '00000000-0000-4000-8000-000000000001', name: 'Field simulator', adapterKey: 'simulator', settingsVersion: 1,
  settings: {}, isEnabled: true, revision: 1, createdAtUtc: '2026-10-20T10:00:00Z', updatedAtUtc: '2026-10-20T10:00:00Z',
  lastTest: { outcome: 'Unreachable', message: 'No answer', actingAs: null, testedAtUtc: '2026-10-20T10:05:00Z', testedRevision: 1 },
  lastTestIsCurrent: true,
};
const info = { product: 'WellSiteAutoPilot', components: [
  { name: 'Server', version: '1.2.3', status: 'Healthy' }, { name: 'Gateway', version: '1.2.3', status: 'Unavailable' }] };

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.loadConnections).mockResolvedValue([connection]);
  vi.mocked(api.loadAdapters).mockResolvedValue([]);
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(info), { headers: { 'Content-Type': 'application/json' } })));
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

describe('Status page', () => {
  it('shows each platform service and each connection with its state', async () => {
    render(<StatusPage canSeeServices canSeeConnections />);
    await screen.findByText('Gateway');
    expect(screen.getByText('Unavailable')).toBeTruthy();
    expect(screen.getAllByText('Version 1.2.3')).toHaveLength(2);
    const link = screen.getByRole('link', { name: 'Field simulator' });
    expect(link.getAttribute('href')).toBe('#connections/00000000-0000-4000-8000-000000000001');
    expect(screen.getByText('Unreachable')).toBeTruthy();
  });

  it('shows only what the person may see', async () => {
    render(<StatusPage canSeeServices canSeeConnections={false} />);
    await screen.findByText('Server');
    expect(api.loadConnections).not.toHaveBeenCalled();
    expect(screen.queryByText('Connections to outside systems')).toBeNull();
  });

  it('says so when a part cannot be loaded instead of showing it empty', async () => {
    vi.mocked(api.loadConnections).mockRejectedValue(new Error('x'));
    render(<StatusPage canSeeServices={false} canSeeConnections />);
    expect((await screen.findByRole('alert')).textContent).toContain('Connections could not be loaded');
  });
});

describe('Status section access', () => {
  const user = (permissions: string[]) => ({ userId: 'u', identityName: 'x', isActive: true, roles: [], permissions, assetScopeIds: [] });
  it('opens for connections.read or security.manage only', () => {
    expect(canOpenSection(user(['connections.read']), 'status')).toBe(true);
    expect(canOpenSection(user(['security.manage']), 'status')).toBe(true);
    expect(canOpenSection(user(['executions.read']), 'status')).toBe(false);
  });
});
