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
