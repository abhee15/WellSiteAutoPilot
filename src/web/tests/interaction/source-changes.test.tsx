import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConnectionSourceChanges } from '../../src/views/ConnectionSourceChanges';
import * as api from '../../src/views/connections';
import type { SourceChangePage, SourceCheck } from '../../src/views/connections';
vi.mock('../../src/views/connections', async original => ({ ...await original<typeof api>(), checkSourceChanges: vi.fn(), loadSourceChanges: vi.fn() }));
const check: SourceCheck = { id: '11111111-1111-4111-8111-111111111111', revision: 1, connectionRevision: 1, status: 'Complete',
  startedAtUtc: '2026-10-22T10:00:00Z', completedAtUtc: '2026-10-22T10:01:00Z', actingAs: 'Gateway account',
  found: 10001, changed: 1, new: 0, notReturned: 0, comparisonUnavailable: 0, warnings: [], unavailableFields: [], sourceScopeCount: 3, sourceScopeFingerprint: 'A'.repeat(64) };
const page: SourceChangePage = { latestCheck: check, lastSuccessfulCheck: check, total: 26, offset: 0, items: [{
  id: '22222222-2222-4222-8222-222222222222', assetId: '33333333-3333-4333-8333-333333333333', kind: 'foresite.well', externalId: '10000',
  name: 'Well 10000', status: 'Changed', firstSeenAtUtc: check.startedAtUtc, lastSeenAtUtc: check.completedAtUtc!,
  evidence: { status: 'Changed', fingerprint: 'A'.repeat(64), differences: [{ field: 'wellType', accepted: 'ESP', observed: 'RRL' }], unknownFields: [] },
  referencesTruncated: false, views: [{ id: '44444444-4444-4444-8444-444444444444', name: 'ESP wells', revisionId: null, revisionNumber: null, status: 'Current', kind: 'View', reference: 'Member' }],
  configurations: [{ id: '55555555-5555-4555-8555-555555555555', name: 'Pinned logic', revisionId: '66666666-6666-4666-8666-666666666666', revisionNumber: 2, status: 'Superseded', kind: 'Input', reference: 'pressure' }],
}] };
const props = { connectionId: '77777777-7777-4777-8777-777777777777', connectionRevision: 1, canCheck: true, disabled: false, onBusyChange: vi.fn() };
beforeEach(() => { vi.resetAllMocks(); vi.mocked(api.checkSourceChanges).mockResolvedValue(check); vi.mocked(api.loadSourceChanges).mockResolvedValue(page); });
afterEach(cleanup);
it('waits for explicit review and shows retained evidence, Views and pinned input revisions', async () => {
  render(<ConnectionSourceChanges {...props} />);
  expect(api.loadSourceChanges).not.toHaveBeenCalled();
  expect(screen.getByText(/does not update Assets or block execution/)).toBeTruthy();
  await userEvent.click(screen.getByRole('button', { name: 'Review saved findings' }));
  await userEvent.click(await screen.findByText(/Well 10000 · Changed/));
  expect(screen.getByText('ESP')).toBeTruthy(); expect(screen.getByText('RRL')).toBeTruthy();
  expect(screen.getByText('ESP wells')).toBeTruthy();
  expect(screen.getByText(/Pinned logic · r2 · Superseded · Input: pressure/)).toBeTruthy();
  expect(api.checkSourceChanges).not.toHaveBeenCalled();
});
it('checks first and refreshes durable findings, with independent paging and applied search', async () => {
  render(<ConnectionSourceChanges {...props} />);
  await userEvent.click(screen.getByRole('button', { name: 'Check for changes' }));
  await screen.findByText('26 retained findings');
  expect(api.checkSourceChanges).toHaveBeenCalledWith(props.connectionId);
  await userEvent.type(screen.getByRole('searchbox'), 'unapplied');
  await userEvent.click(screen.getByRole('button', { name: 'Next findings' }));
  await waitFor(() => expect(api.loadSourceChanges).toHaveBeenLastCalledWith(props.connectionId, 25, '', expect.any(AbortSignal)));
});
it('retains displayed evidence after a failed check and shows missing-well wording', async () => {
  vi.mocked(api.loadSourceChanges).mockResolvedValue({ ...page, items: [{ ...page.items[0], status: 'NotReturned', evidence: { ...page.items[0].evidence, status: 'NotReturned', differences: [] } }] });
  render(<ConnectionSourceChanges {...props} />);
  await userEvent.click(screen.getByRole('button', { name: 'Review saved findings' }));
  await userEvent.click(await screen.findByText(/Well 10000 · NotReturned/));
  expect(screen.getByText(/does not confirm deletion/)).toBeTruthy();
  vi.mocked(api.checkSourceChanges).mockRejectedValue(new api.ConnectionsError('gatewayUnavailable'));
  await userEvent.click(screen.getByRole('button', { name: 'Check for changes' }));
  await screen.findByRole('alert');
  expect(screen.getByText(/Well 10000 · NotReturned/)).toBeTruthy();
});
it('disables checking dirty or read-only connections and explains denied review scope', async () => {
  vi.mocked(api.loadSourceChanges).mockRejectedValue(new api.ConnectionsError('accessDenied'));
  render(<ConnectionSourceChanges {...props} canCheck={false} />);
  expect((screen.getByRole('button', { name: 'Check for changes' }) as HTMLButtonElement).disabled).toBe(true);
  await userEvent.click(screen.getByRole('button', { name: 'Review saved findings' }));
  expect(await screen.findByText(/Review needs Connection read/)).toBeTruthy();
});
it('clears previously visible details when read scope is revoked', async () => {
  render(<ConnectionSourceChanges {...props} />);
  await userEvent.click(screen.getByRole('button', { name: 'Review saved findings' }));
  await screen.findByText(/Well 10000 · Changed/);
  vi.mocked(api.loadSourceChanges).mockRejectedValue(new api.ConnectionsError('accessDenied'));
  await userEvent.click(screen.getByRole('button', { name: 'Review saved findings' }));
  await screen.findByRole('alert');
  expect(screen.queryByText(/Well 10000 · Changed/)).toBeNull();
});

it('uses Enter to search without submitting the surrounding Connection settings form', async () => {
  const submit = vi.fn((event: React.FormEvent) => event.preventDefault());
  render(<form onSubmit={submit}><button type="submit">Save settings</button><ConnectionSourceChanges {...props} /></form>);
  await userEvent.click(screen.getByRole('button', { name: 'Review saved findings' }));
  await screen.findByText('26 retained findings');
  await userEvent.type(screen.getByRole('searchbox'), 'ESP{Enter}');
  await waitFor(() => expect(api.loadSourceChanges).toHaveBeenLastCalledWith(props.connectionId, 0, 'ESP', expect.any(AbortSignal)));
  expect(submit).not.toHaveBeenCalled();
});
