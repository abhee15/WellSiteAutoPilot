import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { DeploymentMigrationPreflightPanel } from '../../src/views/DeploymentMigrationPreflightPanel';
import { loadDeploymentParity } from '../../src/views/deploymentMigrationPreflight';
import type { DeploymentParity } from '../../src/views/deploymentMigrationPreflight';
import { materializeLegacyGrants } from '../../src/views/materializeLegacyGrants';
import { rollbackLegacyGrants } from '../../src/views/rollbackLegacyGrants';
vi.mock('../../src/views/deploymentMigrationPreflight', () => ({ loadDeploymentParity: vi.fn() }));
vi.mock('../../src/views/materializeLegacyGrants', () => ({ materializeLegacyGrants: vi.fn() }));
vi.mock('../../src/views/rollbackLegacyGrants', () => ({ rollbackLegacyGrants: vi.fn() }));
const userId = 'b86c36a2-3349-4020-9044-a3ce7ff42991';
const needsMigration: DeploymentParity = {
  activeUserCount: 1, activeAssetCount: 2, matchingUserCount: 0,
  expansionCount: 0, reductionCount: 4, allActiveUsersMatchRecordedGrants: false,
  isDiagnosticOnly: true, users: [{ userId, expectedLegacyGrantCount: 2,
    recordedGrantCount: 0, expansionCount: 0, reductionCount: 4,
    recordedGrantsMatchLegacy: false }],
};
const migrated: DeploymentParity = {
  ...needsMigration, matchingUserCount: 1, reductionCount: 0,
  allActiveUsersMatchRecordedGrants: true,
  users: [{ ...needsMigration.users[0], recordedGrantCount: 2,
    reductionCount: 0, recordedGrantsMatchLegacy: true }],
};
const receipt = { activeUsersInspected: 1, activeAssetsInspected: 2,
  grantsCreated: 2, usersAlreadyMatching: 0,
  recordedGrantsMatchLegacy: true as const, liveAuthorizationUnchanged: true as const };
beforeEach(() => { vi.resetAllMocks(); });
afterEach(cleanup);

it('requires preflight and both confirmations, suppresses duplicate writes, and invalidates the snapshot', async () => {
  vi.mocked(loadDeploymentParity).mockResolvedValue(needsMigration);
  let finish!: (value: typeof receipt) => void;
  vi.mocked(materializeLegacyGrants).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  const changed = vi.fn();
  const user = userEvent.setup();
  render(<DeploymentMigrationPreflightPanel users={[]} onWritingChange={changed} />);
  expect(screen.queryByRole('button', { name: 'Record legacy grants' })).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Check deployment parity' }));
  const save = await screen.findByRole('button', { name: 'Record legacy grants' });
  expect((save as HTMLButtonElement).disabled).toBe(true);
  await user.type(screen.getByRole('textbox'), 'record legacy grants');
  await user.click(screen.getByRole('checkbox', { name: /records grants for all active users/ }));
  expect((save as HTMLButtonElement).disabled).toBe(true);
  await user.clear(screen.getByRole('textbox'));
  await user.type(screen.getByRole('textbox'), 'RECORD LEGACY GRANTS');
  await user.dblClick(save);
  expect(materializeLegacyGrants).toHaveBeenCalledExactlyOnceWith(true, expect.any(AbortSignal));
  expect(changed).toHaveBeenCalledExactlyOnceWith(true);
  expect((screen.getByRole('button', { name: 'Check deployment parity' }) as HTMLButtonElement).disabled).toBe(true);
  finish(receipt);
  await screen.findByRole('status');
  expect(screen.queryByRole('region', { name: 'Deployment parity result' })).toBeNull();
  expect(changed.mock.calls).toEqual([[true], [false]]);
  expect(loadDeploymentParity).toHaveBeenCalledTimes(1);
});

it('never retries an uncertain migration and clears confirmations until fresh preflight', async () => {
  vi.mocked(loadDeploymentParity).mockResolvedValue(needsMigration);
  vi.mocked(materializeLegacyGrants).mockRejectedValue(new Error('lost response after commit'));
  const user = userEvent.setup();
  render(<DeploymentMigrationPreflightPanel users={[]} />);
  await user.click(screen.getByRole('button', { name: 'Check deployment parity' }));
  await user.click(await screen.findByRole('checkbox', { name: /records grants for all active users/ }));
  await user.type(screen.getByRole('textbox'), 'RECORD LEGACY GRANTS');
  await user.click(screen.getByRole('button', { name: 'Record legacy grants' }));
  expect((await screen.findByRole('alert')).textContent).toMatch(/may have committed/);
  expect(materializeLegacyGrants).toHaveBeenCalledTimes(1);
  expect(screen.queryByRole('textbox')).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Check deployment parity' }));
  const save = await screen.findByRole('button', { name: 'Record legacy grants' });
  expect((save as HTMLButtonElement).disabled).toBe(true);
  expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(false);
  expect((screen.getByRole('textbox') as HTMLInputElement).value).toBe('');
  expect(screen.queryByRole('alert')).toBeNull();
  expect(materializeLegacyGrants).toHaveBeenCalledTimes(1);
});

it('withholds materialization for expansion and matching deployments', async () => {
  vi.mocked(loadDeploymentParity).mockResolvedValueOnce({ ...needsMigration, expansionCount: 1 }).mockResolvedValueOnce(migrated);
  const user = userEvent.setup();
  render(<DeploymentMigrationPreflightPanel users={[]} />);
  await user.click(screen.getByRole('button', { name: 'Check deployment parity' }));
  await screen.findByRole('region', { name: 'Deployment parity result' });
  expect(screen.queryByRole('button', { name: 'Record legacy grants' })).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Check deployment parity' }));
  await screen.findByText(/All currently active users match/);
  expect(screen.queryByRole('button', { name: 'Record legacy grants' })).toBeNull();
  expect(materializeLegacyGrants).not.toHaveBeenCalled();
});

it('requires independent rollback confirmation and clears an uncertain outcome without retry', async () => {
  vi.mocked(loadDeploymentParity).mockResolvedValue(migrated);
  vi.mocked(rollbackLegacyGrants).mockRejectedValue(new Error('connection lost'));
  const user = userEvent.setup();
  render(<DeploymentMigrationPreflightPanel users={[]} />);
  await user.click(screen.getByRole('button', { name: 'Check deployment parity' }));
  const rollback = await screen.findByRole('button', { name: 'Rollback migrated grants' });
  expect((rollback as HTMLButtonElement).disabled).toBe(true);
  await user.type(screen.getByRole('textbox'), 'ROLLBACK MIGRATED GRANTS');
  expect((rollback as HTMLButtonElement).disabled).toBe(true);
  await user.click(screen.getByRole('checkbox', { name: /rollback affects all migrated grants/ }));
  await user.click(rollback);
  expect((await screen.findByRole('alert')).textContent).toMatch(/may have committed/);
  expect(rollbackLegacyGrants).toHaveBeenCalledExactlyOnceWith(true, expect.any(AbortSignal));
  expect(materializeLegacyGrants).not.toHaveBeenCalled();
  expect(screen.queryByRole('region', { name: 'Deployment parity result' })).toBeNull();
});

it('shows rollback receipt and aborts a pending write on removal', async () => {
  vi.mocked(loadDeploymentParity).mockResolvedValue(migrated);
  vi.mocked(rollbackLegacyGrants).mockResolvedValueOnce({ migratedGrantsInspected: 2, grantsDeactivated: 2, liveAuthorizationUnchanged: true })
    .mockImplementationOnce(() => new Promise(() => {}));
  const user = userEvent.setup();
  const view = render(<DeploymentMigrationPreflightPanel users={[]} />);
  for (let i = 0; i < 2; i++) {
    await user.click(screen.getByRole('button', { name: 'Check deployment parity' }));
    await user.click(await screen.findByRole('checkbox', { name: /rollback affects all migrated grants/ }));
    await user.type(screen.getByRole('textbox'), 'ROLLBACK MIGRATED GRANTS');
    await user.click(screen.getByRole('button', { name: 'Rollback migrated grants' }));
    if (i === 0) await screen.findByText(/2 migration-origin grants deactivated and audited/);
  }
  expect(rollbackLegacyGrants).toHaveBeenCalledTimes(2);
  const signal = vi.mocked(rollbackLegacyGrants).mock.calls[1][1];
  view.unmount();
  expect(signal.aborted).toBe(true);
});
