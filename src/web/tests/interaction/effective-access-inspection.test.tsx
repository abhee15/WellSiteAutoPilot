import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { EffectiveAccessInspector } from '../../src/views/EffectiveAccessInspector';
import { loadEffectiveAccess } from '../../src/views/effectiveAccessInspection';

vi.mock('../../src/views/effectiveAccessInspection', () => ({ loadEffectiveAccess: vi.fn() }));

const userId = 'b86c36a2-3349-4020-9044-a3ce7ff42991';
const assetId = '04076374-92c5-4239-9220-d11ca7a5f400';
const grantId = 'e4c14a68-aa15-4d6a-a9f8-e7f94d710f23';
const assets = [{ id: assetId, name: 'Well Alpha', isActive: true }];

beforeEach(() => { vi.resetAllMocks(); });
afterEach(cleanup);

it('displays a server-computed expansion only after an explicit diagnostic read', async () => {
  vi.mocked(loadEffectiveAccess).mockResolvedValue({
    userId, assetId, assetName: 'Well Alpha', isDiagnosticOnly: true,
    wideningCount: 1, reductionCount: 0,
    permissions: [{ permission: 'assets.manage', scopeKind: 'ResourceScoped',
      legacyAllowed: false, proposedAllowed: true,
      proposedReasonCode: 'SECURITY_PERMISSION_GRANTED', proposedGrantId: grantId }],
  });
  const user = userEvent.setup();
  render(<EffectiveAccessInspector userId={userId} assets={assets} />);
  expect(loadEffectiveAccess).not.toHaveBeenCalled();
  expect(screen.queryByText('Expanded')).toBeNull();
  await user.selectOptions(screen.getByRole('combobox', { name: 'Inspect permissions for' }), assetId);
  await user.click(screen.getByRole('button', { name: 'Compare access' }));
  await screen.findByText('Expanded');
  expect(loadEffectiveAccess).toHaveBeenCalledExactlyOnceWith(userId, assetId, expect.any(AbortSignal));
  expect(screen.queryByRole('button', { name: /save|activate|cutover|grant/i })).toBeNull();
});

it('discloses no comparison when the server cannot verify or audit it', async () => {
  vi.mocked(loadEffectiveAccess).mockRejectedValue(new Error('audit unavailable'));
  const user = userEvent.setup();
  render(<EffectiveAccessInspector userId={userId} assets={assets} />);
  await user.click(screen.getByRole('button', { name: 'Compare access' }));
  await screen.findByRole('alert');
  expect(screen.queryByRole('table')).toBeNull();
  expect(screen.queryByText('Expanded')).toBeNull();
  expect(loadEffectiveAccess).toHaveBeenCalledTimes(1);
});
