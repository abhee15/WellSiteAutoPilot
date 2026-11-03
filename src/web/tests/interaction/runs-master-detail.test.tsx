import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { OperationsPage } from '../../src/views/OperationsPage';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });

const executionId = '55555555-5555-4555-8555-555555555555';
const run = { executionId, status: 'Succeeded', mode: 'Shadow', trigger: 'Manual', moduleId: 'sample.module',
  moduleVersion: '1.0.0', requestContractVersion: 3, requestedAtUtc: '2026-10-28T03:00:00Z' };

it('opens a run beside the list when its row is clicked, marks the row, and closes back to the full list', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(run))));
  const user = userEvent.setup();
  const { container } = render(<OperationsPage executions={[run] as never} userId="u1" />);
  const layout = container.querySelector('.runs-layout');
  expect(layout?.getAttribute('data-has-selection')).toBe('false');

  await user.click(within(container.querySelector('tbody')!).getByText('sample.module'));
  expect(await screen.findByRole('heading', { name: 'Run details' })).toBeTruthy();
  expect(layout?.getAttribute('data-has-selection')).toBe('true');
  expect(container.querySelector('tbody tr')?.getAttribute('data-selected')).toBe('true');

  await user.click(screen.getByRole('button', { name: 'Back to runs' }));
  expect(layout?.getAttribute('data-has-selection')).toBe('false');
});

it('shows the owning connection in run history and run summary when the asset is outside the snapshot', async () => {
  const well = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
  const owner = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
  const config = [{ id: 'cfg', name: 'Pressure observer', revisions: [{ id: 'rev', revisionNumber: 1,
    assetBindings: [{ assetId: well }] }] }];
  const item = { ...run, logicInstanceId: 'cfg', configurationRevisionId: 'rev', assetId: null };
  const http = vi.fn(async (url: string) => {
    if (url === '/api/v1/assets/owners') return new Response(JSON.stringify([
      { connectionId: owner, connectionName: 'CygNet Test', assetCount: 1 }]));
    if (url === '/api/v1/assets/' + well) return new Response(JSON.stringify({
      id: well, name: 'RPOC_00002', ownerConnectionId: owner }));
    return new Response(JSON.stringify(item));
  });
  vi.stubGlobal('fetch', http);
  render(<OperationsPage executions={[item] as never} configuredLogic={config as never}
    assets={[]} userId="11111111-1111-4111-8111-111111111111" canReadAssets />);
  expect(await screen.findByText('RPOC_00002 (CygNet Test)')).toBeTruthy();
  await userEvent.setup().click(screen.getByRole('button', { name: /Inspect run/ }));
  const panel = await screen.findByRole('region', { name: 'Run details' });
  expect(within(panel).getByRole('link', { name: 'RPOC_00002 (CygNet Test)' })).toBeTruthy();
});
