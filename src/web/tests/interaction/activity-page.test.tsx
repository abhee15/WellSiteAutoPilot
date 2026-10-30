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
  it('reads each event as a sentence with the record named, and opens its details beside the list', async () => {
    vi.mocked(loadAuditPage).mockResolvedValue({
      items: [event(1, { targetName: 'Gas lift module v1', actorName: 'Sri Mallepudi' })], nextCursor: null });
    render(<ActivityPage />);
    const row = (await screen.findByText('Gas lift module v1')).closest('tr') as HTMLElement;
    expect(row.textContent).toContain('Logic module Gas lift module v1 was approved');
    expect(within(row).getByText('Sri Mallepudi')).toBeTruthy();
    // The raw code and the details are not in the row; they belong to the opened event.
    expect(within(row).queryByText('logic-module.approved')).toBeNull();
    expect(screen.queryByText('Show details')).toBeNull();
    await userEvent.click(within(row).getByRole('button', { name: /Open event: Logic module Gas lift module v1 was approved/ }));
    const detail = within(await screen.findByRole('article'));
    expect(detail.getByText('logic-module.approved')).toBeTruthy();
    expect(detail.getByText('Reviewed')).toBeTruthy();
    expect(detail.getByText('Sri Mallepudi (FIELD\\admin)')).toBeTruthy();
    expect(screen.queryByText('Load older events')).toBeNull();
  });

  it('shows a state change as a status with what it was before, and marks automatic events as such', async () => {
    vi.mocked(loadAuditPage).mockResolvedValue({ items: [event(1, {
      action: 'connection.status_changed', targetType: 'connection', targetName: 'ForeSite',
      actorUserId: null, actorIdentity: 'system:connection-monitor',
      detailsJson: '{"AdapterKey":"foresite","From":"Unreachable","To":"Healthy"}' })], nextCursor: null });
    render(<ActivityPage />);
    const row = (await screen.findByText('ForeSite')).closest('tr') as HTMLElement;
    expect(row.textContent).toContain('Connection ForeSite status changed');
    expect(within(row).getByText('Healthy', { selector: '.status-indicator-label' })).toBeTruthy();
    expect(within(row).getByText('Was Unreachable')).toBeTruthy();
    expect(within(row).getByText('Connection monitor')).toBeTruthy();
    expect(within(row).getByText('Automatic')).toBeTruthy();
  });

  it('falls back to a short id when the record has no name, and still works against a server that sends no names', async () => {
    vi.mocked(loadAuditPage).mockResolvedValue({ items: [event(1, {
      action: 'CONFIGURED_LOGIC_CREATED', targetType: 'ConfiguredLogic',
      targetId: '387b9281-9fee-414f-beb3-034f99ed4f47' })], nextCursor: null });
    render(<ActivityPage />);
    const row = (await screen.findByText('387b9281…')).closest('tr') as HTMLElement;
    expect(row.textContent).toContain('Logic 387b9281… was created');
    expect(within(row).getByText('FIELD\\admin')).toBeTruthy();
  });

  it('goes back from an opened event to the list', async () => {
    vi.mocked(loadAuditPage).mockResolvedValue({ items: [event(1, { targetName: 'Gas lift module v1' })], nextCursor: null });
    render(<ActivityPage />);
    await userEvent.click(await screen.findByRole('button', { name: /Open event/ }));
    expect(await screen.findByRole('article')).toBeTruthy();
    await userEvent.click(screen.getByText('Back to activity'));
    expect(screen.queryByRole('article')).toBeNull();
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

  it('shows an unrecognized action as readable words, and keeps the exact code in its details', async () => {
    vi.mocked(loadAuditPage).mockResolvedValue({ items: [event(1, { action: 'future.thing', detailsJson: '{}' })], nextCursor: null });
    render(<ActivityPage />);
    expect(await screen.findByText(/Future thing/)).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: /Open event/ }));
    const detail = within(await screen.findByRole('article'));
    expect(detail.getByText('future.thing')).toBeTruthy();
    expect(detail.getByText('No further details were recorded for this event.')).toBeTruthy();
  });
});
