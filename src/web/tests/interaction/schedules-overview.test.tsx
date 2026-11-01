import { afterEach, expect, it } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import { SchedulesOverview } from '../../src/views/SchedulesOverview';

afterEach(cleanup);
const now = Date.parse('2026-10-14T12:00:30Z');
const logic = (id: string, name: string, schedule: object | null, nextRunAtUtc: string | null = '2026-10-14T12:01:00Z') => ({
  id, name, activeRevisionId: `${id}-r`, nextRunAtUtc,
  revisions: [{ id: `${id}-r`, revisionNumber: 2, mode: 'Shadow', status: 'Active',
    assetBindings: [{ assetId: 'a1' }], schedule }],
}) as never;
const assets = [{ id: 'a1', name: 'North Well', isActive: true }] as never;
const schedule = (over: object = {}) => ({ enabled: true, cadenceSeconds: 60, startAtUtc: '2026-10-14T12:00:00Z', timeZoneId: 'UTC', ...over });

it('lists scheduled logic with frequency, next run and a link to the configuration', () => {
  render(<SchedulesOverview configuredLogic={[logic('c1', 'Pump watch', schedule()), logic('c2', 'Plain', null)]}
    assets={assets} executions={[]} isLoaded nowMs={now} />);
  const link = screen.getByRole('link', { name: 'Pump watch' });
  expect(link.getAttribute('href')).toBe('#logic/c1');
  const row = link.closest('tr') as HTMLElement;
  expect(within(row).getByText(/Every 1 min/)).toBeTruthy();
  expect(within(row).getByText('North Well')).toBeTruthy();
  expect(screen.getByText('1 without a schedule, not listed here.')).toBeTruthy();
  expect(screen.queryByText('Plain')).toBeNull();
});

it('shows a switched-off schedule as off with no next run', () => {
  render(<SchedulesOverview configuredLogic={[logic('c1', 'Pump watch', schedule({ enabled: false }), null)]}
    assets={assets} executions={[]} isLoaded nowMs={now} />);
  const row = screen.getByRole('link', { name: 'Pump watch' }).closest('tr') as HTMLElement;
  expect(within(row).getByText('Off')).toBeTruthy();
  expect(within(row).getAllByText('—').length).toBeGreaterThan(0);
});

it('hides the last-run column from people who cannot read runs', () => {
  render(<SchedulesOverview configuredLogic={[logic('c1', 'Pump watch', schedule())]} assets={assets} isLoaded nowMs={now} />);
  expect(screen.queryByRole('columnheader', { name: 'Last run' })).toBeNull();
});

it('says so when nothing is scheduled, and waits while loading', () => {
  const { rerender } = render(<SchedulesOverview configuredLogic={[]} assets={assets} executions={[]} isLoaded={false} nowMs={now} />);
  expect(screen.queryByText('No logic has a schedule yet.')).toBeNull();
  rerender(<SchedulesOverview configuredLogic={[]} assets={assets} executions={[]} isLoaded nowMs={now} />);
  expect(screen.getByText('No logic has a schedule yet.')).toBeTruthy();
});

it('shows a next run that has already passed as due now, not as a time in the past', () => {
  render(<SchedulesOverview configuredLogic={[logic('c1', 'Pump watch', schedule(), '2026-10-14T11:59:00Z')]}
    assets={assets} executions={[]} isLoaded nowMs={now} />);
  const row = screen.getByRole('link', { name: 'Pump watch' }).closest('tr') as HTMLElement;
  expect(within(row).getByText('Due now')).toBeTruthy();
});
