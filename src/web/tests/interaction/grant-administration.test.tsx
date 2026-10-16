import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { GrantAdministrationPage } from '../../src/views/GrantAdministrationPage';
import { loadSecurityUsers } from '../../src/views/accessManagement';
import { loadRoleCatalog } from '../../src/views/roleCatalog';
import { loadGrantAssetDirectory } from '../../src/views/grantAssetDirectory';
import { createGrant, deactivateGrant, loadGrants } from '../../src/views/grantAdministration';

import { loadDeploymentParity } from '../../src/views/deploymentMigrationPreflight';
import { materializeLegacyGrants } from '../../src/views/materializeLegacyGrants';
vi.mock('../../src/views/deploymentMigrationPreflight', () => ({ loadDeploymentParity: vi.fn() }));
vi.mock('../../src/views/materializeLegacyGrants', () => ({ materializeLegacyGrants: vi.fn() }));

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
  userId, groupSid: null, roleId, scopeKind: 'ExactAssets' as const,
  assetIds: [assetId], isActive: true, revision: 1,
};

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(loadSecurityUsers).mockResolvedValue([
    { userId: adminId, identityName: 'FIELD\\admin', isActive: true, roles: ['Admin'], assetScopeIds: [] },
    { userId, identityName: 'FIELD\\operator', isActive: true, roles: ['Operator'], assetScopeIds: [] },
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
afterEach(cleanup);

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
    await user.click(screen.getByRole('button', { name: 'Record and audit grant' }));
    await screen.findByText('Grant recorded, audited, and verified. Live user access remains unchanged.');
    expect(createGrant).toHaveBeenCalledTimes(1);
    expect(loadGrants).toHaveBeenCalledTimes(2);
  });

  it('blocks a mismatched readback and never retries an uncertain write', async () => {
    vi.mocked(loadGrants).mockResolvedValueOnce([]).mockResolvedValueOnce([{ ...row, revision: 2 }]);
    const user = await prepareGrant();
    await user.click(screen.getByRole('button', { name: 'Record and audit grant' }));
    expect((await screen.findByRole('alert')).textContent).toContain('could not be verified');
    expect(createGrant).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button', { name: 'Record and audit grant' })).toBeNull();
    expect(screen.queryByText('Grant recorded, audited, and verified. Live user access remains unchanged.')).toBeNull();
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
    await screen.findByText('Grant deactivated and verified. Current live user access remains unchanged.');
    expect(deactivateGrant).toHaveBeenCalledExactlyOnceWith(row, true, expect.any(AbortSignal));
  });
});


it('clears selected-user grants and blocks administration while a deployment write is uncertain', async () => {
  vi.mocked(loadGrants).mockResolvedValue([row]);
  vi.mocked(loadDeploymentParity).mockResolvedValue({ activeUserCount: 1, activeAssetCount: 1,
    matchingUserCount: 0, expansionCount: 0, reductionCount: 1, users: [{ userId,
      expectedLegacyGrantCount: 1, recordedGrantCount: 0, expansionCount: 0, reductionCount: 1,
      recordedGrantsMatchLegacy: false }], allActiveUsersMatchRecordedGrants: false, isDiagnosticOnly: true });
  let reject!: (reason: Error) => void;
  vi.mocked(materializeLegacyGrants).mockImplementation(() => new Promise((_, fail) => { reject = fail; }));
  const user = userEvent.setup();
  render(<GrantAdministrationPage currentUserId={adminId} />);
  const picker = await screen.findByRole('combobox', { name: 'User to administer' });
  await user.selectOptions(picker, userId);
  await screen.findByRole('button', { name: 'Deactivate grant' });
  await user.click(screen.getByRole('button', { name: 'Check deployment parity' }));
  await user.click(await screen.findByRole('checkbox', { name: /records grants for all active users/ }));
  await user.type(screen.getByRole('textbox', { name: /Type RECORD LEGACY GRANTS/ }), 'RECORD LEGACY GRANTS');
  await user.click(screen.getByRole('button', { name: 'Record legacy grants' }));
  expect(screen.queryByRole('button', { name: 'Deactivate grant' })).toBeNull();
  expect((picker as HTMLSelectElement).value).toBe('');
  expect((picker as HTMLSelectElement).disabled).toBe(true);
  expect((screen.getByRole('button', { name: 'Reload users' }) as HTMLButtonElement).disabled).toBe(true);
  expect(screen.queryByRole('button', { name: 'Compare access' })).toBeNull();
  reject(new Error('write may have committed'));
  await screen.findByRole('alert');
  expect((picker as HTMLSelectElement).disabled).toBe(false);
  expect(screen.queryByRole('button', { name: 'Deactivate grant' })).toBeNull();
  await user.selectOptions(picker, userId);
  await screen.findByRole('button', { name: 'Deactivate grant' });
  expect(loadGrants).toHaveBeenCalledTimes(2);
  expect(materializeLegacyGrants).toHaveBeenCalledTimes(1);
});

it('records and retires a Windows group custom-role assignment through the shared reviewed workflow', async () => {
  const sid = 'S-1-5-21-100-200-300-400';
  const groupRow = { ...row, subjectKind: 'WindowsGroup' as const, userId: null, groupSid: sid };
  const retired = { ...groupRow, isActive: false, revision: 2 };
  vi.mocked(loadGrants).mockResolvedValueOnce([]).mockResolvedValueOnce([groupRow]).mockResolvedValueOnce([retired]);
  vi.mocked(createGrant).mockResolvedValue(groupRow);
  vi.mocked(deactivateGrant).mockResolvedValue(retired);
  const user = userEvent.setup();
  render(<GrantAdministrationPage currentUserId={adminId} />);
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Assignment target' }), 'WindowsGroup');
  expect(screen.queryByRole('combobox', { name: 'User to administer' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Check deployment parity' })).toBeNull();
  const sidInput = screen.getByRole('textbox', { name: 'Windows group SID' });
  const load = screen.getByRole('button', { name: 'Load group assignments' });
  await user.type(sidInput, 'FIELD\\Operators');
  expect((load as HTMLButtonElement).disabled).toBe(true);
  expect(loadGrants).not.toHaveBeenCalled();
  await user.clear(sidInput);
  await user.type(sidInput, sid.toLowerCase());
  await user.click(load);
  expect(loadGrants).toHaveBeenCalledWith(sid, expect.any(AbortSignal));
  await user.click(await screen.findByRole('button', { name: 'Prepare a scoped grant' }));
  await user.selectOptions(screen.getByRole('combobox', { name: 'Role' }), roleId);
  await user.click(screen.getByRole('checkbox', { name: /^Well Alpha/ }));
  await user.click(screen.getByRole('checkbox', { name: 'I reviewed the role, permissions, and Asset boundaries.' }));
  await user.click(screen.getByRole('button', { name: 'Continue to final review' }));
  expect(createGrant).not.toHaveBeenCalled();
  await user.click(screen.getByRole('button', { name: 'Record and audit grant' }));
  await screen.findByText('Grant recorded, audited, and verified. Live user access remains unchanged.');
  expect(createGrant).toHaveBeenCalledExactlyOnceWith({ subjectKind: 'WindowsGroup', userId: '', groupSid: sid,
    roleId, scopeKind: 'ExactAssets', assetIds: [assetId], expectedRoleRevision: 1 }, true, expect.any(AbortSignal));
  await user.click(screen.getByRole('button', { name: 'Deactivate grant' }));
  const review = screen.getByRole('region', { name: 'Review grant deactivation' });
  await user.click(within(review).getByRole('checkbox'));
  await user.click(within(review).getByRole('button', { name: 'Deactivate grant' }));
  await screen.findByText('Grant deactivated and verified. Current live user access remains unchanged.');
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
  await screen.findByText(kind === 'selfAccess' ? /Another administrator must change access/ : /Windows group membership could not be verified/);
  expect(deactivateGrant).toHaveBeenCalledTimes(1);
  expect(screen.queryByRole('button', { name: 'Deactivate grant' })).toBeNull();
});
