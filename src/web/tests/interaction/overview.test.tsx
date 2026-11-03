import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { OverviewPage } from '../../src/views/OverviewPage';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
const response = (value: unknown) => new Response(JSON.stringify(value), { status: 200 });
const permissions = ['recommendations.read', 'executions.read', 'assets.read', 'configured-logic.read'];
const props = { permissions, systemInfo: null, isLoaded: true,
  assets: [{ id: 'a1', name: 'North Well' }], configuredLogic: [{ id: 'c1', name: 'Pressure watch' }],
  executions: [{ logicInstanceId: 'c1', status: 'Completed', requestedAtUtc: new Date().toISOString() }] };

it('puts what needs a person first, with a link to act on it', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => response([
    { id: 'r1', assetId: 'a1', status: 'Pending', expiresAtUtc: new Date(Date.now() + 3600_000).toISOString() }])));
  render(<OverviewPage {...(props as never)} />);
  expect(await screen.findByText('1 recommendation is waiting for a decision.')).toBeTruthy();
  expect(screen.getAllByRole('link', { name: 'Open' })[0].getAttribute('href')).toBe('#recommendations');
});

it('says so when nothing needs attention, and states that nothing is sent to equipment', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => response([])));
  render(<OverviewPage {...(props as never)} executions={[
    { logicInstanceId: 'c1', status: 'Completed', requestedAtUtc: new Date().toISOString() }] as never} />);
  expect(await screen.findByText('Nothing needs your attention right now.')).toBeTruthy();
  expect(screen.getByText(/nothing is sent to equipment/)).toBeTruthy();
});

it('guides a brand-new installation to the first asset', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => response([])));
  render(<OverviewPage {...(props as never)} assets={[]} configuredLogic={[]} executions={[]} />);
  expect(await screen.findByText(/No assets yet/)).toBeTruthy();
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
