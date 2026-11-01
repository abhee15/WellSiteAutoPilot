// Accessibility of the control screens in their open states (forms, errors), where a name, role or list mistake hides.
import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { KillSwitchPanel } from '../../src/views/KillSwitchPanel';
import { ControlActionsPanel } from '../../src/views/ControlActionsPanel';
import { AutonomyPolicyPanel } from '../../src/views/AutonomyPolicyPanel';
import { SchedulesOverview } from '../../src/views/SchedulesOverview';
import { accessibilityViolations } from './axe';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
const id = '11111111-1111-4111-8111-111111111111';
const act = { id, recommendationId: '33333333-3333-4333-8333-333333333333', executionId: id, configuredLogicId: id, assetId: id,
  command: 'SET_CHOKE', quantity: null, value: 40, unit: '%', status: 'Ambiguous', gateCode: 'SINK_AMBIGUOUS', sink: 'Simulator',
  sinkReference: null, createdAtUtc: '2026-10-14T12:00:00Z', completedAtUtc: null, origin: 'Autonomous', readbackValue: null,
  verifiedAtUtc: null, resolutionNote: null, canManage: true };
const pol = { configuredLogicId: id, enabled: true, sinkKind: 'Simulator', limits: [{ command: 'SET_CHOKE', unit: '%', minimumValue: 10, maximumValue: 60 }],
  maxActionsPerWindow: 3, windowSeconds: 600, updatedBy: 'a', updatedAtUtc: '2026-10-14T12:00:00Z', allowApprovedRecommendations: false };
const ks = { engaged: true, changedBy: 'a', changedAtUtc: '2026-10-14T12:00:00Z', reason: 'x', autonomousControlEnabled: true };
const route = (url: string) => url.includes('kill-switch') ? ks : url.includes('policies') ? pol : [act];
const stub = () => vi.stubGlobal('fetch', vi.fn(async (url: string) => new Response(JSON.stringify(route(url)))));

it('kill switch with the release form open', async () => {
  stub(); const user = userEvent.setup();
  const { container } = render(<KillSwitchPanel userId={id} canManage />);
  await user.click(await screen.findByRole('button', { name: 'Release kill switch' }));
  expect(await accessibilityViolations(container)).toEqual([]);
});
it('control actions with resolve form open', async () => {
  stub(); const user = userEvent.setup();
  const { container } = render(<ControlActionsPanel userId={id} canManage />);
  expect(await accessibilityViolations(await (async () => { await screen.findByText('SET_CHOKE'); return container; })())).toEqual([]);
  await user.click(screen.getByRole('button', { name: 'Resolve SET_CHOKE' }));
  expect(await accessibilityViolations(container)).toEqual([]);
});
it('policy editor with a row and issues showing', async () => {
  stub(); const user = userEvent.setup();
  const { container } = render(<AutonomyPolicyPanel configuredLogicId={id} canManage />);
  await user.click(await screen.findByRole('button', { name: 'Edit autonomy policy' }));
  expect(await accessibilityViolations(container)).toEqual([]);
  await user.clear(screen.getByRole('textbox', { name: 'Command' }));
  await user.click(screen.getByRole('button', { name: 'Save autonomy policy' }));
  await screen.findByRole('alert');
  expect(await accessibilityViolations(container)).toEqual([]);
});
it('schedules overview', async () => {
  const { container } = render(<SchedulesOverview configuredLogic={[{ id, name: 'Pump', activeRevisionId: 'r', revisions: [{ id: 'r', revisionNumber: 1, mode: 'Shadow',
    assetBindings: [{ assetId: id }], schedule: { enabled: true, cadenceSeconds: 60, startAtUtc: '2026-10-14T12:00:00Z', timeZoneId: 'UTC' } }] }] as never}
    assets={[{ id, name: 'Well' }] as never} executions={[]} isLoaded nowMs={Date.parse('2026-10-14T12:00:30Z')} />);
  expect(await accessibilityViolations(container)).toEqual([]);
});
