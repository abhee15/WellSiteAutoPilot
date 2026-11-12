import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LogicPage } from '../../src/views/LogicPage';
import type { ConfiguredLogicResponse } from '../../src/api/generated/v1/types.gen';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const date = '2026-11-01T12:00:00Z';
const schedule = { enabled: true, cadenceSeconds: 3600, timeZoneId: 'America/Chicago', startAtUtc: date };
const a = deployment(10, 1, 'System A deployment', '1.0.0');
const b = deployment(11, 2, 'System B deployment', '1.1.0');
const modules = [
  { id: id(1), moduleId: 'optimizer', displayName: 'Pump Optimization', version: '2.0.0', isEnabled: true },
  { id: id(2), moduleId: 'other', displayName: 'Pump Optimization', version: '3.0.0', isEnabled: false },
];
const props = {
  configuredLogic: [{ ...a, effectiveSchedule: { ...schedule, enabled: false },
    revisions: [...a.revisions!, { ...a.revisions![0], id: id(25), status: 'Draft' as const, revisionNumber: 2, moduleVersion: '2.0.0' }] },
  b, { ...deployment(12, 1, 'Separate module', '3.0.0'),
    revisions: [{ ...a.revisions![0], id: id(32), moduleId: 'other', moduleVersion: '3.0.0' }] }],
  modules, userId: id(100), assets: [{ id: id(91), name: 'Well A' }, { id: id(92), name: 'Well B' }],
  assetTypes: [], onRefresh: async () => {},
  permissions: ['configured-logic.read', 'logic.read', 'assets.read', 'connections.read'],
};

function deployment(n: number, source: number, name: string, moduleVersion: string): ConfiguredLogicResponse {
  const revisionId = id(n + 20);
  return { id: id(n), name, activeRevisionId: revisionId,
    revisions: [{ id: revisionId, revisionNumber: 1, moduleId: 'optimizer', moduleVersion,
      status: 'Active', mode: 'Shadow', parametersJson: '{}', schedule,
      view: { viewId: id(source + 80), connectionId: id(source + 60), revision: 1 },
      assetBindings: [{ assetId: id(source + 90) }], dataBindings: [] }] };
}

const json = (value: unknown) => new Response(JSON.stringify(value));
function fixture() {
  window.location.hash = '#logic';
  const request = vi.fn(async (url: string) => {
    if (url === '/api/v1/connections') return json([1, 2].map(n => ({
      id: id(n + 60), name: n === 1 ? 'ForeSite System A' : 'ForeSite System B', adapterKey: 'foresite',
      settingsVersion: 1, settings: {}, revision: 1, isEnabled: true, createdAtUtc: date, updatedAtUtc: date,
      lastTest: null, lastTestIsCurrent: false, hasImports: true,
    })));
    if (url === '/api/v1/asset-views') return json([1, 2].map(n => ({
      id: id(n + 80), connectionId: id(n + 60), name: n === 1 ? 'West wells' : 'East wells',
      revision: 2, memberCount: 8, hasUnavailableMembers: false, createdAtUtc: date, updatedAtUtc: date,
      canEdit: false, canDelete: false,
    })));
    if (url === '/api/v1/configured-logic/' + b.id) return json(b);
    return json([]);
  });
  vi.stubGlobal('fetch', request);
  return request;
}

afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });

it('keeps deployments separate, displays pinned and approved versions, and collapses an open selection by keyboard', async () => {
  fixture();
  render(<LogicPage {...props} />);
  const groups = screen.getAllByRole('button', { name: 'Pump Optimization' });
  expect(groups).toHaveLength(2);
  const optimizer = within(groups[1].closest('tbody')!);
  const disclosure = optimizer.getByRole('button', { name: 'Pump Optimization' });
  expect(optimizer.getByText(/2 configurations · 2 assigned assets · 2 live · Latest approved v2.0.0/)).toBeTruthy();
  expect(await optimizer.findByText('ForeSite System A (foresite)')).toBeTruthy();
  expect(optimizer.getByText('ForeSite System B (foresite)')).toBeTruthy();
  expect(optimizer.getByText('View West wells · revision 1')).toBeTruthy();
  expect(optimizer.getByText('View East wells · revision 1')).toBeTruthy();
  expect(optimizer.getByText(/Active v1.0.0.*Paused/)).toBeTruthy();
  expect(optimizer.getByText(/Active v1.1.0/)).toBeTruthy();

  const user = userEvent.setup();
  await user.click(optimizer.getByRole('button', { name: 'Inspect revisions of System B deployment' }));
  expect(window.location.hash).toBe('#logic/' + b.id);
  disclosure.focus();
  await user.keyboard('{Enter}');
  expect(disclosure.getAttribute('aria-expanded')).toBe('false');
  expect(optimizer.queryByRole('button', { name: /Inspect revisions/ })).toBeNull();
  expect(window.location.hash).toBe('#logic/' + b.id);
  await user.keyboard(' ');
  expect(optimizer.getByRole('button', { name: 'Inspect revisions of System B deployment' }).getAttribute('aria-expanded')).toBe('true');
  await user.type(screen.getByRole('searchbox', { name: 'Search configurations' }), 'Pump Optimization');
  expect(optimizer.getByRole('button', { name: /System A deployment/ })).toBeTruthy();
});

it('keeps saved targets and version visible without requesting restricted catalogs', () => {
  const request = fixture();
  window.location.hash = '#logic/' + a.id;
  render(<LogicPage {...props} configuredLogic={[a]} permissions={['configured-logic.read']} />);
  expect(screen.getByRole('button', { name: 'optimizer' })).toBeTruthy();
  expect(screen.getByText(/Active v1.0.0/)).toBeTruthy();
  expect(screen.queryByRole('link', { name: 'Modules' })).toBeNull();
  expect(screen.queryByText('Last run')).toBeNull();
  expect(request.mock.calls.some(([url]) => url.includes('/connections') || url.includes('/asset-views'))).toBe(false);
});
