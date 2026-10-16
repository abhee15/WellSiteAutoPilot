import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ProposedAssetAccessPanel } from '../../src/views/ProposedAssetAccessPanel';
import { loadProposedAssetAccess } from '../../src/views/proposedAssetAccess';
vi.mock('../../src/views/proposedAssetAccess', () => ({ loadProposedAssetAccess: vi.fn() }));
const userId = 'b86c36a2-3349-4020-9044-a3ce7ff42991';
const permissions = [
  { key: 'assets.read', category: 'Assets', scopeKind: 'ResourceScoped', isPrivileged: false },
  { key: 'assets.manage', category: 'Assets', scopeKind: 'ResourceScoped', isPrivileged: false },
  { key: 'security.manage', category: 'Security', scopeKind: 'DeploymentWide', isPrivileged: true },
];
beforeEach(() => vi.resetAllMocks());
afterEach(cleanup);

it('requests one resource permission at a time, pages server results, and clears stale results on failure', async () => {
  const assets = Array.from({ length: 100 }, (_, i) => ({ id: String(i), name: 'Well ' + i }));
  vi.mocked(loadProposedAssetAccess).mockResolvedValueOnce({ userId, permission: 'assets.read',
    offset: 0, limit: 100, assets, isDiagnosticOnly: true }).mockRejectedValueOnce(new Error('audit failed'));
  const user = userEvent.setup();
  render(<ProposedAssetAccessPanel userId={userId} permissions={permissions} />);
  expect(loadProposedAssetAccess).not.toHaveBeenCalled();
  expect(screen.queryByRole('option', { name: 'security.manage' })).toBeNull();
  await user.selectOptions(screen.getByRole('combobox'), 'assets.read');
  await user.click(screen.getByRole('button', { name: 'Compare access' }));
  await screen.findByText('Well 99');
  expect(loadProposedAssetAccess).toHaveBeenCalledExactlyOnceWith(userId, 'assets.read', 0, expect.any(AbortSignal));
  expect((screen.getByRole('button', { name: 'Previous' }) as HTMLButtonElement).disabled).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Next' }));
  await screen.findByRole('alert');
  expect(screen.queryByText('Well 99')).toBeNull();
  expect(screen.queryByRole('region', { name: 'Proposed resource access result' })).toBeNull();
  expect(loadProposedAssetAccess).toHaveBeenLastCalledWith(userId, 'assets.read', 100, expect.any(AbortSignal));
});

it('changes permission without reusing prior results and disables paging for a partial page', async () => {
  vi.mocked(loadProposedAssetAccess).mockResolvedValue({ userId, permission: 'assets.read',
    offset: 0, limit: 100, assets: [{ id: '1', name: 'Well Alpha' }], isDiagnosticOnly: true });
  const user = userEvent.setup();
  render(<ProposedAssetAccessPanel userId={userId} permissions={permissions} />);
  await user.selectOptions(screen.getByRole('combobox'), 'assets.read');
  await user.click(screen.getByRole('button', { name: 'Compare access' }));
  await screen.findByText('Well Alpha');
  expect((screen.getByRole('button', { name: 'Next' }) as HTMLButtonElement).disabled).toBe(true);
  await user.selectOptions(screen.getByRole('combobox'), 'assets.manage');
  expect(screen.queryByText('Well Alpha')).toBeNull();
  expect(loadProposedAssetAccess).toHaveBeenCalledTimes(1);
});

it('suppresses duplicate reads and aborts an in-flight preview on removal', async () => {
  vi.mocked(loadProposedAssetAccess).mockImplementation(() => new Promise(() => {}));
  const user = userEvent.setup();
  const view = render(<ProposedAssetAccessPanel userId={userId} permissions={permissions} />);
  await user.selectOptions(screen.getByRole('combobox'), 'assets.read');
  await user.dblClick(screen.getByRole('button', { name: 'Compare access' }));
  expect(loadProposedAssetAccess).toHaveBeenCalledTimes(1);
  const signal = vi.mocked(loadProposedAssetAccess).mock.calls[0][3];
  view.unmount();
  expect(signal.aborted).toBe(true);
});
