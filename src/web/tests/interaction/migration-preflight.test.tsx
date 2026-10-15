import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LegacyMigrationPreflightPanel } from '../../src/views/LegacyMigrationPreflightPanel';
import { DeploymentMigrationPreflightPanel } from '../../src/views/DeploymentMigrationPreflightPanel';
import { loadLegacyMigrationPreflight } from '../../src/views/legacyMigrationPreflight';
import { loadDeploymentParity } from '../../src/views/deploymentMigrationPreflight';

vi.mock('../../src/views/legacyMigrationPreflight', () => ({ loadLegacyMigrationPreflight: vi.fn() }));
vi.mock('../../src/views/deploymentMigrationPreflight', () => ({ loadDeploymentParity: vi.fn() }));
const userId = 'b86c36a2-3349-4020-9044-a3ce7ff42991';
beforeEach(() => { vi.resetAllMocks(); });
afterEach(cleanup);

it('requires explicit inspection and renders a diagnostic match without write actions', async () => {
  vi.mocked(loadLegacyMigrationPreflight).mockResolvedValue({ userId, activeAssetCount: 2,
    expectedLegacyGrantCount: 2, recordedGrantCount: 2, expansionCount: 0, reductionCount: 0,
    differenceSamples: [], recordedGrantsMatchLegacy: true, isDiagnosticOnly: true });
  const user = userEvent.setup();
  render(<LegacyMigrationPreflightPanel userId={userId} assets={[]} />);
  expect(loadLegacyMigrationPreflight).not.toHaveBeenCalled();
  await user.click(screen.getByRole('button', { name: 'Run migration preflight' }));
  await screen.findByText(/This user’s recorded grants matched legacy access/);
  expect(loadLegacyMigrationPreflight).toHaveBeenCalledExactlyOnceWith(userId, expect.any(AbortSignal));
  expect(screen.queryByRole('button', { name: /save|approve|enable|rollback|materialize/i })).toBeNull();
});

it('clears an earlier successful deployment report when a new inspection fails', async () => {
  vi.mocked(loadDeploymentParity).mockResolvedValueOnce({ activeUserCount: 0, activeAssetCount: 2,
    matchingUserCount: 0, expansionCount: 0, reductionCount: 0, users: [],
    allActiveUsersMatchRecordedGrants: true, isDiagnosticOnly: true }).mockRejectedValueOnce(new Error('audit unavailable'));
  const user = userEvent.setup();
  render(<DeploymentMigrationPreflightPanel users={[]} />);
  expect(loadDeploymentParity).not.toHaveBeenCalled();
  await user.click(screen.getByRole('button', { name: 'Check deployment parity' }));
  await screen.findByText(/All currently active users match recorded grants/);
  await user.click(screen.getByRole('button', { name: 'Check deployment parity' }));
  await screen.findByRole('alert');
  expect(screen.queryByText(/All currently active users match recorded grants/)).toBeNull();
  expect(screen.queryByRole('region', { name: 'Deployment parity result' })).toBeNull();
  expect(loadDeploymentParity).toHaveBeenCalledTimes(2);
});

it('prevents duplicate reads and aborts a pending user inspection when removed', async () => {
  vi.mocked(loadLegacyMigrationPreflight).mockImplementation(() => new Promise(() => {}));
  const user = userEvent.setup();
  const view = render(<LegacyMigrationPreflightPanel userId={userId} assets={[]} />);
  await user.click(screen.getByRole('button', { name: 'Run migration preflight' }));
  const signal = vi.mocked(loadLegacyMigrationPreflight).mock.calls[0][1];
  expect((screen.getByRole('button', { name: /loading/i }) as HTMLButtonElement).disabled).toBe(true);
  expect(loadLegacyMigrationPreflight).toHaveBeenCalledTimes(1);
  view.unmount();
  expect(signal.aborted).toBe(true);
});
