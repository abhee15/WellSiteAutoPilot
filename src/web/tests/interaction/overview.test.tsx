import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { OverviewPage } from '../../src/views/OverviewPage';
import { SummaryCard } from '../../src/components/common';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
const response = (value: unknown) => new Response(JSON.stringify(value), { status: 200 });
const permissions = ['recommendations.read', 'executions.read', 'assets.read', 'configured-logic.read'];
const props = { permissions, systemInfo: null, isLoaded: true,
  assets: [{ id: 'a1', name: 'North Well' }], configuredLogic: [{ id: 'c1', name: 'Pressure watch' }],
  executions: [{ logicInstanceId: 'c1', status: 'Completed', requestedAtUtc: new Date().toISOString() }] };

it('puts what needs a person first, with a link to act on it', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => response([
    { id: 'r1', assetId: 'a1', executionId: 'e1', command: 'Set frequency', suggestedValue: 48, unit: 'Hz', canDecide: true, status: 'Pending', expiresAtUtc: new Date(Date.now() + 3600_000).toISOString() }])));
  render(<OverviewPage {...(props as never)} />);
  expect(await screen.findByText('Awaiting decision')).toBeTruthy();
  expect(screen.getByText('Set frequency: 48 Hz')).toBeTruthy();
  expect(screen.getByRole('link', { name: 'Review change' }).getAttribute('href')).toBe('#recommendations/r1');
});

it('says so when nothing needs attention without the generic Shadow footer', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => response([])));
  render(<OverviewPage {...(props as never)} executions={[
    { logicInstanceId: 'c1', status: 'Completed', requestedAtUtc: new Date().toISOString() }] as never} />);
  expect(await screen.findByText('Nothing needs your attention right now.')).toBeTruthy();
  expect(screen.queryByText(/nothing is sent to equipment/)).toBeNull();
});

it('guides a brand-new installation to the first asset', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => response([])));
  render(<OverviewPage {...(props as never)} assets={[]} configuredLogic={[]} executions={[]} />);
  expect(await screen.findByText(/No assets yet/)).toBeTruthy();
});

it('links each Overview KPI to its authorized page, including when the count is zero', () => {
  vi.stubGlobal('fetch', vi.fn(async () => response([])));
  render(<OverviewPage {...(props as never)} permissions={[...permissions, 'connections.read']}
    systemHealth={{ unhealthyCount: 0 } as never} />);
  const glance = within(screen.getByRole('region', { name: 'At a glance' }));
  const destinations = [
    ['Assets', '#assets'],
    ['Logic configurations', '#logic'],
    ['Recent runs', '#operations'],
    ['Services needing attention', '#status'],
  ] as const;
  for (const [label, href] of destinations) {
    const link = glance.getByRole('link', { name: new RegExp(label, 'i') });
    expect(link.getAttribute('href')).toBe(href);
    expect(link.classList.contains('summary-card-link')).toBe(true);
    expect(link.hasAttribute('aria-pressed')).toBe(false);
  }
  expect(glance.getByRole('link', { name: /Services needing attention/ }).textContent).toContain('0');
});

it('never links to an inaccessible Status page, but permits status-only readers', () => {
  vi.stubGlobal('fetch', vi.fn(async () => response([])));
  const view = render(<OverviewPage {...(props as never)} permissions={['executions.read']} />);
  expect(within(screen.getByRole('region', { name: 'At a glance' }))
    .queryByRole('link', { name: /Services needing attention/ })).toBeNull();
  view.unmount();
  render(<OverviewPage {...(props as never)} permissions={['connections.read']} />);
  const glance = within(screen.getByRole('region', { name: 'At a glance' }));
  expect(glance.getByRole('link', { name: /Services needing attention/ }).getAttribute('href')).toBe('#status');
  expect(glance.queryByRole('link', { name: /Recent runs/ })).toBeNull();
});

it('preserves SummaryCard filter buttons when a card does not navigate', () => {
  const onSelect = vi.fn();
  render(<SummaryCard label="Active executions" value="3" detail="Running" onSelect={onSelect} selected />);
  const button = screen.getByRole('button', { name: /Active executions/ });
  expect(button.getAttribute('aria-pressed')).toBe('true');
  fireEvent.click(button);
  expect(onSelect).toHaveBeenCalledOnce();
  expect(screen.queryByRole('link')).toBeNull();
});

const run = (executionId: string, extra: Record<string, unknown> = {}) => ({
  executionId, assetId: 'a1', moduleId: 'esp-optimizer', status: 'Completed',
  requestedAtUtc: new Date(Date.now() - 60_000).toISOString(), ...extra });

it('lists the latest five runs, newest first, naming the asset and linking to the run', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => response([])));
  const runs = Array.from({ length: 7 }, (_, index) => run(`e${index}`, {
    requestedAtUtc: new Date(Date.now() - (index + 1) * 600_000).toISOString() }));
  render(<OverviewPage {...(props as never)} executions={[...runs].reverse() as never} />);
  const list = (await screen.findByRole('region', { name: 'Latest runs' })).querySelectorAll('li');
  expect(list).toHaveLength(5);
  expect(list[0].textContent).toContain('North Well');
  expect(list[0].querySelector('a')?.getAttribute('href')).toBe('#operations/e0');
  expect(list[4].querySelector('a')?.getAttribute('href')).toBe('#operations/e4');
});

it('shows the bound assets for configured multi-asset runs that have no single asset ID', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => response([])));
  render(<OverviewPage {...(props as never)}
    assets={[{ id: 'a1', name: 'North Well' }, { id: 'a2', name: 'South Well' }] as never}
    configuredLogic={[{ id: 'c1', name: 'Pressure watch', revisions: [
      { id: 'rev2', revisionNumber: 2, assetBindings: [
        { role: 'primary', assetId: 'a1' }, { role: 'secondary', assetId: 'a2' },
      ] },
    ] }] as never}
    executions={[run('e1', { assetId: null, assetExternalId: null,
      logicInstanceId: 'c1', configurationRevisionId: 'rev2' })] as never} />);
  const panel = await screen.findByRole('region', { name: 'Latest runs' });
  expect(panel.querySelector('a[href="#operations/e1"]')?.textContent).toBe('North Well, South Well');
});

it('still lists a run that has no request time, without a time, and never throws', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => response([])));
  render(<OverviewPage {...(props as never)} executions={[run('e1', { requestedAtUtc: undefined })] as never} />);
  const item = (await screen.findByRole('region', { name: 'Latest runs' })).querySelector('li') as HTMLElement;
  expect(item.querySelector('time')).toBeNull();
  expect(item.textContent).toContain('North Well');
});

it('leaves the panel out when there are no runs to show', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => response([])));
  render(<OverviewPage {...(props as never)} executions={[]} />);
  await screen.findByText(/no recorded runs yet/);
  expect(screen.queryByRole('region', { name: 'Latest runs' })).toBeNull();
});

it('resolves missing configured run asset names outside the 100-asset snapshot with owning Connection', async () => {
  const well = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
  const owner = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
  const http = vi.fn(async (url: string) => {
    if (url === '/api/v1/assets/' + well) return response({ id: well, name: 'RPOC_00002', ownerConnectionId: owner });
    if (url === '/api/v1/assets/owners') return response([{ connectionId: owner, connectionName: 'ForeSite Production', assetCount: 1500 }]);
    return response([]);
  });
  vi.stubGlobal('fetch', http);
  render(<OverviewPage {...(props as never)} assets={[]}
    userId="11111111-1111-4111-8111-111111111111"
    configuredLogic={[{ id: 'cfg', revisions: [{ id: 'rev', assetBindings: [{ assetId: well }] }] }] as never}
    executions={[run('e1', { assetId: null, assetExternalId: null,
      logicInstanceId: 'cfg', configurationRevisionId: 'rev' })] as never} />);
  expect(await screen.findByText('RPOC_00002 (ForeSite Production)')).toBeTruthy();
  expect(http.mock.calls.some(([url]) => url === '/api/v1/assets/' + well)).toBe(true);
});
