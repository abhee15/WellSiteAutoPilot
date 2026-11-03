import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AdministrationWorkspace } from '../../src/views/AdministrationWorkspace';
import { loadSecurityUsers, loadSecurityUserPage } from '../../src/views/accessManagement';
import { loadRoleCatalog } from '../../src/views/roleCatalog';
import { loadGrantAssetDirectory } from '../../src/views/grantAssetDirectory';
import { loadGrants } from '../../src/views/grantAdministration';

vi.mock('../../src/views/accessManagement', async importOriginal => ({
  ...await importOriginal<typeof import('../../src/views/accessManagement')>(),
  loadSecurityUsers: vi.fn(),
  loadSecurityUserPage: vi.fn(),
}));
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
  vi.mocked(loadSecurityUsers).mockResolvedValue(directory);
  vi.mocked(loadSecurityUserPage).mockImplementation(async (criteria, cursor) => {
    const selected = directory.filter(item =>
      (criteria.accountStatus === 'all' || item.isActive === (criteria.accountStatus === 'active')) &&
      (criteria.directAssignments === 'all' ||
        (item.activeDirectGrantCount > 0) === (criteria.directAssignments === 'with')) &&
      (!criteria.search || item.identityName.toLowerCase().includes(criteria.search.toLowerCase())));
    return { items: cursor ? [] : selected, nextCursor: null };
  });
  vi.mocked(loadRoleCatalog).mockResolvedValue({ roles: [], permissions: [] });
  vi.mocked(loadGrantAssetDirectory).mockResolvedValue([]);
  vi.mocked(loadGrants).mockResolvedValue([]);
});
afterEach(() => { cleanup(); window.location.hash = ''; });

describe('user access directory', () => {
  it('shows direct role counts and offers assignment only for other active users', async () => {
    render(<AdministrationWorkspace currentUserId={adminId} />);
    expect(await screen.findByRole('button', { name: 'Assign a role to FIELD\\newhire' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Assign a role to FIELD\\operator' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Assign a role to FIELD\\admin' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Assign a role to FIELD\\left' })).toBeNull();
    expect(screen.getByText(/does not mean no access/)).toBeTruthy();
    expect(screen.getByRole('columnheader', { name: 'Direct assignments' })).toBeTruthy();
  });

  it('filters direct assignments independently from active status, without claiming no access', async () => {
    const user = userEvent.setup();
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await screen.findByRole('button', { name: 'Assign a role to FIELD\\operator' });
    expect(screen.queryByRole('checkbox', { name: 'Only users with no direct role' })).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Filters' }));
    await user.selectOptions(screen.getByRole('combobox', { name: 'Direct assignments' }), 'without');
    await screen.findByText('FIELD\\newhire');
    expect(screen.queryByRole('button', { name: 'Assign a role to FIELD\\operator' })).toBeNull();
    expect(screen.getByText('FIELD\\left')).toBeTruthy(); // independent from account status
    expect(vi.mocked(loadSecurityUserPage).mock.calls.at(-1)?.[0].directAssignments).toBe('without');

    await user.selectOptions(screen.getByRole('combobox', { name: 'Account status' }), 'active');
    await vi.waitFor(() => expect(screen.queryByText('FIELD\\left')).toBeNull());
    expect(vi.mocked(loadSecurityUserPage).mock.calls.at(-1)?.[0].accountStatus).toBe('active');

    vi.mocked(loadSecurityUserPage).mockResolvedValue({ items: [], nextCursor: null });
    await user.click(screen.getByRole('button', { name: 'Reload users' }));
    expect(await screen.findByText(/No users match these filters/)).toBeTruthy();
  });

  it('searches on the server, clears chips, and can fetch older pages', async () => {
    const user = userEvent.setup();
    vi.mocked(loadSecurityUserPage).mockImplementation(async (criteria, cursor) => {
      if (cursor) return { items: [directory[2]], nextCursor: null };
      if (criteria.search === 'newhire') return { items: [directory[1]], nextCursor: null };
      return { items: [directory[1]], nextCursor: 'YWJj' };
    });
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await screen.findByRole('button', { name: 'Assign a role to FIELD\\newhire' });
    await user.click(screen.getByRole('button', { name: 'Load more users' }));
    await screen.findByRole('button', { name: 'Assign a role to FIELD\\operator' });
    expect(vi.mocked(loadSecurityUserPage).mock.calls.at(-1)?.[1]).toBe('YWJj');

    await user.type(screen.getByRole('searchbox', { name: 'Search users by name or account' }), 'newhire');
    await vi.waitFor(() => expect(vi.mocked(loadSecurityUserPage).mock.calls.at(-1)?.[0].search).toBe('newhire'));
    expect(screen.queryByRole('button', { name: 'Assign a role to FIELD\\operator' })).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Clear filters' }));
    await vi.waitFor(() => expect(vi.mocked(loadSecurityUserPage).mock.calls.at(-1)?.[0].search).toBe(''));
  });

  it('opens the grants tab with the chosen user already selected', async () => {
    const user = userEvent.setup();
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await user.click(await screen.findByRole('button', { name: 'Assign a role to FIELD\\newhire' }));
    const picker = await screen.findByRole('combobox', { name: 'User to administer' });
    await vi.waitFor(() => expect((picker as HTMLSelectElement).value).toBe(waitingId));
    await vi.waitFor(() => expect(loadGrants).toHaveBeenCalledWith(waitingId, expect.anything()));
  });

  it('keeps the grant handoff for users found beyond the legacy 100-row grants picker', async () => {
    const user = userEvent.setup();
    // Simulate a searched/paged directory user who is not in the Grants tab's first 100 rows.
    vi.mocked(loadSecurityUsers).mockResolvedValue(directory.filter(item => item.userId !== waitingId));
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await user.click(await screen.findByRole('button', { name: 'Assign a role to FIELD\\newhire' }));
    const picker = await screen.findByRole('combobox', { name: 'User to administer' });
    await vi.waitFor(() => expect((picker as HTMLSelectElement).value).toBe(waitingId));
    expect(Array.from((picker as HTMLSelectElement).options).some(option => option.value === waitingId)).toBe(true);
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
