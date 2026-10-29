import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { GrantAdministrationPage } from '../../src/views/GrantAdministrationPage';
import { loadSecurityUsers } from '../../src/views/accessManagement';
import { loadRoleCatalog } from '../../src/views/roleCatalog';
import { loadGrantAssetDirectory } from '../../src/views/grantAssetDirectory';
import { createGrant, deactivateGrant, loadGrants } from '../../src/views/grantAdministration';

vi.mock('../../src/views/accessManagement', () => ({ loadSecurityUsers: vi.fn() }));
vi.mock('../../src/views/roleCatalog', () => ({ loadRoleCatalog: vi.fn() }));
vi.mock('../../src/views/grantAssetDirectory', async importOriginal => ({
  ...await importOriginal<typeof import('../../src/views/grantAssetDirectory')>(),
  loadGrantAssetDirectory: vi.fn(),
}));
vi.mock('../../src/views/grantAdministration', async importOriginal => ({
  ...await importOriginal<typeof import('../../src/views/grantAdministration')>(),
  createGrant: vi.fn(), deactivateGrant: vi.fn(), loadGrants: vi.fn(),
}));

const adminId = '00bc2b6e-2943-49a9-af38-dc8e7af30a1e';
const userId = 'b86c36a2-3349-4020-9044-a3ce7ff42991';
const roleId = '139e5b2f-dd2d-4630-b920-141b7bfb7a62';
const assetId = '04076374-92c5-4239-9220-d11ca7a5f400';
const row = {
  grantId: 'e4c14a68-aa15-4d6a-a9f8-e7f94d710f23', subjectKind: 'User' as const,
  userId, groupId: null, directoryId: null, roleId, scopeKind: 'ExactAssets' as const,
  assetIds: [assetId], isActive: true, revision: 1,
};

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(loadSecurityUsers).mockResolvedValue([
    { userId: adminId, identityName: 'FIELD\\admin', isActive: true },
    { userId, identityName: 'FIELD\\operator', isActive: true },
  ]);
  vi.mocked(loadRoleCatalog).mockResolvedValue({
    roles: [{ roleId, key: 'supervisor', name: 'Field Supervisor', description: '',
      isActive: true, isSystem: false, revision: 1,
      createdAtUtc: '2026-10-15T00:00:00Z', updatedAtUtc: '2026-10-15T00:00:00Z',
      permissions: ['assets.read'] }],
    permissions: [{ key: 'assets.read', category: 'Assets', scopeKind: 'ResourceScoped', isPrivileged: false }],
  });
  vi.mocked(loadGrantAssetDirectory).mockResolvedValue([{ id: assetId, name: 'Well Alpha', isActive: true }]);
  vi.mocked(loadGrants).mockResolvedValue([]);
  vi.mocked(createGrant).mockResolvedValue(row);
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

async function prepareGrant() {
  const user = userEvent.setup();
  render(<GrantAdministrationPage currentUserId={adminId} />);
  const picker = await screen.findByRole('combobox', { name: 'User to administer' });
  expect(within(picker).queryByRole('option', { name: 'FIELD\\admin' })).toBeNull();
  await user.selectOptions(picker, userId);
  await user.click(await screen.findByRole('button', { name: 'Prepare a scoped grant' }));
  await user.selectOptions(screen.getByRole('combobox', { name: 'Role' }), roleId);
  await user.click(screen.getByRole('checkbox', { name: /^Well Alpha/ }));
  const next = screen.getByRole('button', { name: 'Continue to final review' });
  expect((next as HTMLButtonElement).disabled).toBe(true);
  await user.click(screen.getByRole('checkbox', { name: 'I reviewed the role, permissions, and Asset boundaries.' }));
  await user.click(next);
  expect(createGrant).not.toHaveBeenCalled();
  return user;
}

describe('audited grant administration interaction', () => {
  it('requires review and a matching server read before reporting one successful write', async () => {
    vi.mocked(loadGrants).mockResolvedValueOnce([]).mockResolvedValueOnce([row]);
    const user = await prepareGrant();
    await user.click(screen.getByRole('button', { name: 'Save assignment' }));
    await screen.findByText('Assignment saved and verified. Access is evaluated from current assignments.');
    expect(createGrant).toHaveBeenCalledTimes(1);
    expect(loadGrants).toHaveBeenCalledTimes(2);
  });

  it('blocks a mismatched readback and never retries an uncertain write', async () => {
    vi.mocked(loadGrants).mockResolvedValueOnce([]).mockResolvedValueOnce([{ ...row, revision: 2 }]);
    const user = await prepareGrant();
    await user.click(screen.getByRole('button', { name: 'Save assignment' }));
    expect((await screen.findByRole('alert')).textContent).toContain('could not be verified');
    expect(createGrant).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button', { name: 'Save assignment' })).toBeNull();
    expect(screen.queryByText('Assignment saved and verified. Access is evaluated from current assignments.')).toBeNull();
  });

  it('requires a separate confirmation before retiring the observed grant revision', async () => {
    const retired = { ...row, isActive: false, revision: 2 };
    vi.mocked(loadGrants).mockResolvedValueOnce([row]).mockResolvedValueOnce([retired]);
    vi.mocked(deactivateGrant).mockResolvedValue(retired);
    const user = userEvent.setup();
    render(<GrantAdministrationPage currentUserId={adminId} />);
    await user.selectOptions(await screen.findByRole('combobox', { name: 'User to administer' }), userId);
    await user.click(await screen.findByRole('button', { name: 'Deactivate grant' }));
    const review = screen.getByRole('region', { name: 'Review grant deactivation' });
    const submit = within(review).getByRole('button', { name: 'Deactivate grant' });
    expect((submit as HTMLButtonElement).disabled).toBe(true);
    expect(deactivateGrant).not.toHaveBeenCalled();
    await user.click(within(review).getByRole('checkbox'));
    await user.click(submit);
    await screen.findByText('Grant deactivated and verified. Other active assignments may still provide access.');
    expect(deactivateGrant).toHaveBeenCalledExactlyOnceWith(row, true, expect.any(AbortSignal));
  });
});


it('records and retires a Windows group custom-role assignment through the shared reviewed workflow', async () => {
  const sid = 'S-1-5-21-100-200-300-400';
  const groupRow = { ...row, subjectKind: 'Group' as const, userId: null, groupId: sid, directoryId: 'windows-ad' };
  const retired = { ...groupRow, isActive: false, revision: 2 };
  vi.mocked(loadGrants).mockResolvedValueOnce([]).mockResolvedValueOnce([groupRow]).mockResolvedValueOnce([retired]);
  vi.mocked(createGrant).mockResolvedValue(groupRow);
  vi.mocked(deactivateGrant).mockResolvedValue(retired);
  const user = userEvent.setup();
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ directoryId: 'windows-ad', supportsGroupSearch: true }))));
  render(<GrantAdministrationPage currentUserId={adminId} />);
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Assignment target' }), 'Group');
  expect(screen.queryByRole('combobox', { name: 'User to administer' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Check deployment parity' })).toBeNull();
  const fetcher = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({
    groups: [{ directoryId: 'windows-ad', externalId: sid, displayName: 'SoftwareEngineers', domain: 'FIELD' }], hasMore: false,
  })));
  vi.stubGlobal('fetch', fetcher);
  await user.type(await screen.findByRole('searchbox', { name: 'Group name starts with' }), 'Software');
  await user.click(screen.getByRole('button', { name: 'Search groups' }));
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Directory groups' }), '0');
  expect(loadGrants).toHaveBeenCalledWith('windows-ad:' + sid, expect.any(AbortSignal));
  await user.click(await screen.findByRole('button', { name: 'Prepare a scoped grant' }));
  await user.selectOptions(screen.getByRole('combobox', { name: 'Role' }), roleId);
  await user.click(screen.getByRole('checkbox', { name: /^Well Alpha/ }));
  await user.click(screen.getByRole('checkbox', { name: 'I reviewed the role, permissions, and Asset boundaries.' }));
  await user.click(screen.getByRole('button', { name: 'Continue to final review' }));
  expect(createGrant).not.toHaveBeenCalled();
  await user.click(screen.getByRole('button', { name: 'Save assignment' }));
  await screen.findByText('Assignment saved and verified. Access is evaluated from current assignments.');
  expect(createGrant).toHaveBeenCalledExactlyOnceWith({ subjectKind: 'Group', userId: '', groupId: sid, directoryId: 'windows-ad',
    roleId, scopeKind: 'ExactAssets', assetIds: [assetId], expectedRoleRevision: 1 }, true, expect.any(AbortSignal));
  await user.click(screen.getByRole('button', { name: 'Deactivate grant' }));
  const review = screen.getByRole('region', { name: 'Review grant deactivation' });
  await user.click(within(review).getByRole('checkbox'));
  await user.click(within(review).getByRole('button', { name: 'Deactivate grant' }));
  await screen.findByText('Grant deactivated and verified. Other active assignments may still provide access.');
  expect(deactivateGrant).toHaveBeenCalledExactlyOnceWith(groupRow, true, expect.any(AbortSignal));
});

it('explains last direct administrator refusal and requires a refresh without retrying', async () => {
  const { GrantApiError } = await import('../../src/views/grantAdministration');
  vi.mocked(loadGrants).mockResolvedValue([row]);
  vi.mocked(deactivateGrant).mockRejectedValue(new GrantApiError('lastDirectAdmin'));
  const user = userEvent.setup();
  render(<GrantAdministrationPage currentUserId={adminId} />);
  await user.selectOptions(await screen.findByRole('combobox', { name: 'User to administer' }), userId);
  await user.click(await screen.findByRole('button', { name: 'Deactivate grant' }));
  const review = screen.getByRole('region', { name: 'Review grant deactivation' });
  await user.click(within(review).getByRole('checkbox'));
  await user.click(within(review).getByRole('button', { name: 'Deactivate grant' }));
  await screen.findByText(/This assignment was not deactivated/);
  expect(screen.queryByRole('button', { name: 'Deactivate grant' })).toBeNull();
  expect(deactivateGrant).toHaveBeenCalledTimes(1);
  expect(loadGrants).toHaveBeenCalledTimes(1);
  await user.click(screen.getByRole('button', { name: 'Reload users' }));
  await screen.findByRole('combobox', { name: 'User to administer' });
  expect(deactivateGrant).toHaveBeenCalledTimes(1);
});

it.each(['selfAccess', 'groupProofRequired'] as const)('explains %s refusal without retrying a grant change', async kind => {
  const { GrantApiError } = await import('../../src/views/grantAdministration');
  vi.mocked(loadGrants).mockResolvedValue([row]);
  vi.mocked(deactivateGrant).mockRejectedValue(new GrantApiError(kind));
  const user = userEvent.setup();
  render(<GrantAdministrationPage currentUserId={adminId} />);
  await user.selectOptions(await screen.findByRole('combobox', { name: 'User to administer' }), userId);
  await user.click(await screen.findByRole('button', { name: 'Deactivate grant' }));
  const review = screen.getByRole('region', { name: 'Review grant deactivation' });
  await user.click(within(review).getByRole('checkbox'));
  await user.click(within(review).getByRole('button', { name: 'Deactivate grant' }));
  await screen.findByText(kind === 'selfAccess' ? /Another administrator must change access/ : /Group membership could not be verified/);
  expect(deactivateGrant).toHaveBeenCalledTimes(1);
  expect(screen.queryByRole('button', { name: 'Deactivate grant' })).toBeNull();
});
