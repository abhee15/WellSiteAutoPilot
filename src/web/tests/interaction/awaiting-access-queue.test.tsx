import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AdministrationWorkspace } from '../../src/views/AdministrationWorkspace';
import { loadSecurityUsers } from '../../src/views/accessManagement';
import { loadRoleCatalog } from '../../src/views/roleCatalog';
import { loadGrantAssetDirectory } from '../../src/views/grantAssetDirectory';
import { loadGrants } from '../../src/views/grantAdministration';

vi.mock('../../src/views/accessManagement', () => ({ loadSecurityUsers: vi.fn() }));
vi.mock('../../src/views/roleCatalog', () => ({ loadRoleCatalog: vi.fn() }));
vi.mock('../../src/views/grantAssetDirectory', async importOriginal => ({
  ...await importOriginal<typeof import('../../src/views/grantAssetDirectory')>(),
  loadGrantAssetDirectory: vi.fn(),
}));
vi.mock('../../src/views/grantAdministration', async importOriginal => ({
  ...await importOriginal<typeof import('../../src/views/grantAdministration')>(),
  loadGrants: vi.fn(),
}));

const adminId = '00bc2b6e-2943-49a9-af38-dc8e7af30a1e';
const waitingId = 'b86c36a2-3349-4020-9044-a3ce7ff42991';
const grantedId = '2f6b5d0e-9a9b-4d1c-8d0e-6a9d3f1d7c11';
const disabledId = '8e3a8e0b-4a52-4f7e-a8c4-3d4d3d1a52b0';
const stamp = '2026-10-17T10:00:00Z';
const directory = [
  { userId: adminId, identityName: 'FIELD\\admin', isActive: true, activeDirectGrantCount: 1, createdAtUtc: stamp, lastSeenAtUtc: stamp },
  { userId: waitingId, identityName: 'FIELD\\newhire', isActive: true, activeDirectGrantCount: 0, createdAtUtc: stamp, lastSeenAtUtc: stamp },
  { userId: grantedId, identityName: 'FIELD\\operator', isActive: true, activeDirectGrantCount: 2, createdAtUtc: stamp, lastSeenAtUtc: stamp },
  { userId: disabledId, identityName: 'FIELD\\left', isActive: false, activeDirectGrantCount: 0, createdAtUtc: stamp, lastSeenAtUtc: stamp },
];

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(loadSecurityUsers).mockImplementation(async (_signal, _fetcher, options) =>
    options?.awaitingAccessOnly ? [directory[1]] : directory);
  vi.mocked(loadRoleCatalog).mockResolvedValue({ roles: [], permissions: [] });
  vi.mocked(loadGrantAssetDirectory).mockResolvedValue([]);
  vi.mocked(loadGrants).mockResolvedValue([]);
});
afterEach(() => { cleanup(); window.location.hash = ''; });

describe('waiting-for-access queue', () => {
  it('shows direct role counts and offers assignment only for other active users', async () => {
    render(<AdministrationWorkspace currentUserId={adminId} />);
    expect(await screen.findByRole('button', { name: 'Assign a role to FIELD\\newhire' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Assign a role to FIELD\\operator' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Assign a role to FIELD\\admin' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Assign a role to FIELD\\left' })).toBeNull();
    expect(screen.getByText(/Windows group is checked when they sign in/)).toBeTruthy();
  });

  it('asks the server for waiting users only and explains an empty queue', async () => {
    const user = userEvent.setup();
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await screen.findByRole('button', { name: 'Assign a role to FIELD\\operator' });
    await user.click(screen.getByRole('checkbox', { name: 'Only users with no direct role' }));
    await screen.findByRole('button', { name: 'Assign a role to FIELD\\newhire' });
    expect(screen.queryByRole('button', { name: 'Assign a role to FIELD\\operator' })).toBeNull();
    expect(vi.mocked(loadSecurityUsers).mock.calls.at(-1)?.[2]).toEqual({ awaitingAccessOnly: true });

    vi.mocked(loadSecurityUsers).mockResolvedValue([]);
    await user.click(screen.getByRole('button', { name: 'Reload users' }));
    expect(await screen.findByText('No active users are waiting for a direct role.')).toBeTruthy();
  });

  it('opens the grants tab with the chosen user already selected', async () => {
    const user = userEvent.setup();
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await user.click(await screen.findByRole('button', { name: 'Assign a role to FIELD\\newhire' }));
    const picker = await screen.findByRole('combobox', { name: 'User to administer' });
    await vi.waitFor(() => expect((picker as HTMLSelectElement).value).toBe(waitingId));
    await vi.waitFor(() => expect(loadGrants).toHaveBeenCalledWith(waitingId, expect.anything()));
  });

  it('does not preselect a user when the Grants tab is opened directly', async () => {
    const user = userEvent.setup();
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await screen.findByRole('button', { name: 'Assign a role to FIELD\\newhire' });
    await user.click(screen.getByRole('tab', { name: 'Scoped grants' }));
    const picker = await screen.findByRole('combobox', { name: 'User to administer' });
    expect((picker as HTMLSelectElement).value).toBe('');
    expect(loadGrants).not.toHaveBeenCalled();
  });
});
