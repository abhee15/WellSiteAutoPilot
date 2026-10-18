import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ActivityPage } from '../../src/views/ActivityPage';
import { AuditActivityError, loadAuditPage } from '../../src/views/auditActivity';

vi.mock('../../src/views/auditActivity', async importOriginal => ({
  ...await importOriginal<typeof import('../../src/views/auditActivity')>(),
  loadAuditPage: vi.fn(),
}));

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const event = (n: number, extra: Record<string, unknown> = {}) => ({
  id: id(n), occurredAtUtc: '2026-10-18T12:00:00Z', actorUserId: id(900), actorIdentity: 'FIELD\\admin',
  action: 'logic-module.approved', targetType: 'LogicModule', targetId: `module-${n}`,
  assetId: null, correlationId: null, detailsJson: '{"reason":"Reviewed"}', ...extra,
});

beforeEach(() => { vi.resetAllMocks(); });
afterEach(cleanup);

describe('Activity page', () => {
  it('shows events with a readable action, the recorded code and expandable details', async () => {
    vi.mocked(loadAuditPage).mockResolvedValue({ items: [event(1)], nextCursor: null });
    render(<ActivityPage />);
    const row = (await screen.findByText('module-1', { exact: false })).closest('tr') as HTMLElement;
    expect(within(row).getByText('Logic module approved')).toBeTruthy();
    expect(within(row).getByText('(logic-module.approved)')).toBeTruthy();
    expect(within(row).getByText('FIELD\\admin')).toBeTruthy();
    await userEvent.click(within(row).getByText('Show details'));
    expect(within(row).getByText('Reviewed')).toBeTruthy();
    expect(screen.queryByText('Load older events')).toBeNull();
  });

  it('loads older events with the server cursor and keeps what is already shown', async () => {
    vi.mocked(loadAuditPage)
      .mockResolvedValueOnce({ items: [event(1)], nextCursor: 'cursor-1' })
      .mockResolvedValueOnce({ items: [event(1), event(2)], nextCursor: null });
    render(<ActivityPage />);
    await screen.findByText('module-1', { exact: false });
    await userEvent.click(screen.getByText('Load older events'));
    await screen.findByText('module-2', { exact: false });
    expect(screen.getAllByText('module-1', { exact: false })).toHaveLength(1);
    expect(vi.mocked(loadAuditPage).mock.calls[1][1]).toBe('cursor-1');
    expect(screen.queryByText('Load older events')).toBeNull();
  });

  it('applies and clears filters by starting again from the newest page', async () => {
    vi.mocked(loadAuditPage).mockResolvedValue({ items: [event(1)], nextCursor: null });
    render(<ActivityPage />);
    await screen.findByText('module-1', { exact: false });
    await userEvent.type(screen.getByLabelText('Action starts with'), 'security.');
    await userEvent.click(screen.getByText('Apply filters'));
    await waitFor(() => expect(vi.mocked(loadAuditPage).mock.calls.at(-1)?.[0])
      .toEqual({ actionPrefix: 'security.', targetType: '' }));
    expect(vi.mocked(loadAuditPage).mock.calls.at(-1)?.[1]).toBeNull();
    await userEvent.click(screen.getByText('Clear filters'));
    await waitFor(() => expect(vi.mocked(loadAuditPage).mock.calls.at(-1)?.[0])
      .toEqual({ actionPrefix: '', targetType: '' }));
  });

  it('says so when nothing matches and shows a stable message when access is refused', async () => {
    vi.mocked(loadAuditPage).mockResolvedValueOnce({ items: [], nextCursor: null });
    render(<ActivityPage />);
    expect(await screen.findByText('No activity matches these filters.')).toBeTruthy();
    cleanup();
    vi.mocked(loadAuditPage).mockRejectedValueOnce(new AuditActivityError('accessDenied'));
    render(<ActivityPage />);
    expect((await screen.findByRole('alert')).textContent).toContain('protected administrators only');
    expect(screen.queryByRole('table')).toBeNull();
  });

  it('shows an unrecognized action as its recorded code', async () => {
    vi.mocked(loadAuditPage).mockResolvedValue({ items: [event(1, { action: 'future.thing', detailsJson: '{}' })], nextCursor: null });
    render(<ActivityPage />);
    expect(await screen.findByText('future.thing')).toBeTruthy();
  });
});
