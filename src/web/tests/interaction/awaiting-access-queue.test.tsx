import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { accessibilityViolations } from './axe';
import { AdministrationWorkspace } from '../../src/views/AdministrationWorkspace';
import { loadSecurityUsers, loadSecurityUserPage } from '../../src/views/accessManagement';
import { loadRoleCatalog } from '../../src/views/roleCatalog';
import { loadGrantAssetDirectory } from '../../src/views/grantAssetDirectory';
import { loadGrants, GrantApiError } from '../../src/views/grantAdministration';

vi.mock('../../src/views/accessManagement', async importOriginal => ({
  ...await importOriginal<typeof import('../../src/views/accessManagement')>(),
  loadSecurityUsers: vi.fn(),
  loadSecurityUserPage: vi.fn(),
}));
vi.mock('../../src/views/roleCatalog', async importOriginal => ({
  ...await importOriginal<typeof import('../../src/views/roleCatalog')>(),
  loadRoleCatalog: vi.fn(),
}));
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

  it('opens read-only user details with an honest not-evaluated group state', async () => {
    const user = userEvent.setup();
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await user.click(await screen.findByRole('button', { name: 'View access details for FIELD\\newhire' }));
    expect(await screen.findByText('No direct assignments are configured for this user.')).toBeTruthy();
    expect(screen.getByRole('region', { name: 'Selected user details' })).toBeTruthy();
    expect(screen.getByText('Group-derived access')).toBeTruthy();
    expect(screen.getByText('Not evaluated')).toBeTruthy();
    expect(screen.queryByText(/no effective access/i)).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Manage assignments' }));
    const picker = await screen.findByRole('combobox', { name: 'User to administer' });
    await vi.waitFor(() => expect((picker as HTMLSelectElement).value).toBe(waitingId));
  });

  it('shows exact scopes, repeated role assignments and retired grants without merging permissions', async () => {
    const roleId = 'a1303dbb-7bfe-4ded-ac73-3ca932ce4f31';
    const assets = [
      { id: '25e72410-9114-43d2-a96e-9ae37d91d1de', name: 'Well 101', isActive: true },
      { id: 'b8f5d615-6c30-40b0-a09c-405e3e7ed7f2', name: 'Well 102', isActive: true },
    ];
    const common = {
      subjectKind: 'User', userId: grantedId, groupId: null, directoryId: null, roleId,
      revision: 1, assetIds: [], scopeKind: 'DeploymentWide',
    };
    vi.mocked(loadGrants).mockResolvedValue([
      { ...common, grantId: 'd486a946-c161-43a2-9a35-e2c61e6738f4', isActive: true, scopeKind: 'ExactAssets', assetIds: assets.map(a => a.id) },
      { ...common, grantId: 'ea82c29b-9ac3-4bc2-8020-313638683f33', isActive: true },
      { ...common, grantId: 'dd9b9a59-ac88-4549-a0f2-8e693f34a70d', isActive: false },
    ] as never);
    vi.mocked(loadRoleCatalog).mockResolvedValue({ roles: [{
      roleId, key: 'operator', name: 'Field Operator', description: '',
      isActive: true, isSystem: false, revision: 1,
      createdAtUtc: stamp, updatedAtUtc: stamp, permissions: ['assets.read'],
    }], permissions: [] });
    vi.mocked(loadGrantAssetDirectory).mockResolvedValue(assets);
    const user = userEvent.setup();
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await user.click(await screen.findByRole('button', { name: 'View access details for FIELD\\operator' }));
    expect(await screen.findByText('3 assignment records, including retired records')).toBeTruthy();
    expect(screen.getAllByText('Field Operator')).toHaveLength(3);
    expect(screen.getAllByText('Deployment-wide non-Asset permissions')).toHaveLength(2);
    await user.click(screen.getByText('Assigned assets (2)'));
    expect(screen.getByText('Well 101')).toBeTruthy();
    expect(screen.getByText('Well 102')).toBeTruthy();
    expect(screen.getByText(/configured grants, not verified effective access/)).toBeTruthy();
  });

  it('keeps detail selection across list filtering, without equating no row with denied access', async () => {
    const user = userEvent.setup();
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await user.click(await screen.findByRole('button', { name: 'View access details for FIELD\\operator' }));
    await screen.findByText('No direct assignments are configured for this user.');
    await user.click(screen.getByRole('button', { name: 'Filters' }));
    await user.selectOptions(screen.getByRole('combobox', { name: 'Direct assignments' }), 'without');
    await vi.waitFor(() => expect(screen.queryByRole('button', { name: 'View access details for FIELD\\operator' })).toBeNull());
    expect(screen.getByRole('region', { name: 'Selected user details' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Manage assignments' })).toBeTruthy();
    await user.click(screen.getByRole('button', { name: 'Close user details' }));
    expect(screen.queryByRole('button', { name: 'Manage assignments' })).toBeNull();
  });

  it('does not expose manage actions for own or disabled users', async () => {
    const user = userEvent.setup();
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await user.click(await screen.findByRole('button', { name: 'View access details for FIELD\\admin' }));
    await screen.findByText('No direct assignments are configured for this user.');
    expect(screen.queryByRole('button', { name: 'Manage assignments' })).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Close user details' }));
    await user.click(screen.getByRole('button', { name: 'View access details for FIELD\\left' }));
    await screen.findByText('No direct assignments are configured for this user.');
    expect(screen.queryByRole('button', { name: 'Manage assignments' })).toBeNull();
  });

  it('clears the detail on a denied protected grant read and refreshes the directory', async () => {
    const user = userEvent.setup();
    vi.mocked(loadGrants).mockRejectedValueOnce(new GrantApiError('denied'));
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await user.click(await screen.findByRole('button', { name: 'View access details for FIELD\\newhire' }));
    await vi.waitFor(() => expect(screen.queryByRole('button', { name: 'Close user details' })).toBeNull());
    expect(vi.mocked(loadSecurityUserPage).mock.calls.length).toBeGreaterThan(1);
  });

  it('keeps the user detail inspector accessible with native disclosure and labelled controls', async () => {
    const user = userEvent.setup();
    const view = render(<AdministrationWorkspace currentUserId={adminId} />);
    await user.click(await screen.findByRole('button', { name: 'View access details for FIELD\\newhire' }));
    await screen.findByText('No direct assignments are configured for this user.');
    expect(await accessibilityViolations(view.container)).toEqual([]);
  });

  it('ignores a late grants read from a previously selected user', async () => {
    const user = userEvent.setup();
    let finishFirst: (value: Awaited<ReturnType<typeof loadGrants>>) => void = () => undefined;
    vi.mocked(loadGrants).mockImplementation((id) => id === grantedId
      ? new Promise<Awaited<ReturnType<typeof loadGrants>>>(resolve => { finishFirst = resolve; })
      : Promise.resolve([]));
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await user.click(await screen.findByRole('button', { name: 'View access details for FIELD\\operator' }));
    await user.click(screen.getByRole('button', { name: 'View access details for FIELD\\newhire' }));
    await screen.findByText('No direct assignments are configured for this user.');
    finishFirst([]);
    expect(screen.getByRole('region', { name: 'Selected user details' }).textContent).toContain('FIELD\\newhire');
    expect(screen.getByRole('region', { name: 'Selected user details' }).textContent).not.toContain('FIELD\\operator');
  });

  it('shows grant identifiers rather than inventing role or Asset names when enrichment fails', async () => {
    const grantId = 'd486a946-c161-43a2-9a35-e2c61e6738f4';
    const roleId = 'a1303dbb-7bfe-4ded-ac73-3ca932ce4f31';
    const assetId = '25e72410-9114-43d2-a96e-9ae37d91d1de';
    vi.mocked(loadGrants).mockResolvedValue([{ grantId, roleId, scopeKind: 'ExactAssets',
      assetIds: [assetId], isActive: true, userId: grantedId, groupId: null,
      directoryId: null, subjectKind: 'User', revision: 1 }] as never);
    vi.mocked(loadRoleCatalog).mockRejectedValue(new Error('catalog temporarily unavailable'));
    vi.mocked(loadGrantAssetDirectory).mockRejectedValue(new Error('asset directory temporarily unavailable'));
    const user = userEvent.setup();
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await user.click(await screen.findByRole('button', { name: 'View access details for FIELD\\operator' }));
    expect(await screen.findByText(/Some role or asset names are unavailable/)).toBeTruthy();
    expect(screen.getByText('Role ID: ' + roleId)).toBeTruthy();
    await user.click(screen.getByText('Assigned assets (1)'));
    expect(screen.getByText('Asset ID: ' + assetId)).toBeTruthy();
    expect(screen.getByText('Not evaluated')).toBeTruthy();
  });

  it('does not preselect a user when the Grants tab is opened directly', async () => {
    const user = userEvent.setup();
    render(<AdministrationWorkspace currentUserId={adminId} />);
    await screen.findByRole('button', { name: 'Assign a role to FIELD\\newhire' });
    await user.click(screen.getByRole('link', { name: 'Scoped grants' }));
    const picker = await screen.findByRole('combobox', { name: 'User to administer' });
    expect((picker as HTMLSelectElement).value).toBe('');
    expect(loadGrants).not.toHaveBeenCalled();
  });
});
