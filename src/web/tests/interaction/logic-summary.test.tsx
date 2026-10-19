import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from '../../src/App';

const userId = '11111111-1111-4111-8111-111111111111';
const configurationId = '22222222-2222-4222-8222-222222222222';
const revisionId = '33333333-3333-4333-8333-333333333333';
const assetId = '44444444-4444-4444-8444-444444444444';
const runId = '55555555-5555-4555-8555-555555555555';
const live = { id: configurationId, name: 'Pressure watch', activeRevisionId: revisionId, canManage: false, canRequestShadow: false,
  revisions: [{ id: revisionId, revisionNumber: 2, status: 'Active', mode: 'Shadow', moduleId: 'sample', moduleVersion: '1.0.0',
    parametersJson: '{}', assetBindings: [{ role: 'well', assetId, parameterOverridesJson: '{}' }], dataBindings: [] }] };
const response = (value: unknown) => new Response(JSON.stringify(value));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });

function fixture(permissions: string[]) {
  window.location.hash = '#logic';
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\x', isActive: true, roles: [], assetScopeIds: [], permissions });
    if (url === '/api/v1/configured-logic/capabilities') return response({ canManage: false, canRequestShadow: false });
    if (url.startsWith('/api/v1/configured-logic?')) return response([live]);
    if (url === '/api/v1/configured-logic/' + configurationId) return response(live);
    if (url.startsWith('/api/v1/executions')) return response([{ executionId: runId, logicInstanceId: configurationId, status: 'Completed', requestedAtUtc: '2026-10-01T00:00:00Z' }]);
    return response([]);
  }));
}

it('an opened logic says where it stands and links its recent runs', async () => {
  fixture(['configured-logic.read', 'executions.read']);
  render(<App />);
  await userEvent.setup().click(await screen.findByRole('button', { name: 'Inspect revisions of Pressure watch' }));
  expect(await screen.findByText(/Revision 2 is live in Shadow mode on 1 asset\./)).toBeTruthy();
  const link = await screen.findByRole('link', { name: /2026|10/ });
  expect(link.getAttribute('href')).toBe('#operations/' + runId);
});

it('without permission to read runs the runs list is absent', async () => {
  fixture(['configured-logic.read']);
  render(<App />);
  await userEvent.setup().click(await screen.findByRole('button', { name: 'Inspect revisions of Pressure watch' }));
  await screen.findByText(/Revision 2 is live/);
  expect(screen.queryByText('Recent runs of this logic')).toBeNull();
});

it('a module explains what it does, what it needs and what uses it', async () => {
  window.location.hash = '#logic/modules';
  const manifestJson = JSON.stringify({ assetRequirements: [{ role: 'well', minimumCount: 1, maximumCount: 1, requiredAssetTypeKey: 'well' }],
    dataRequirements: [{ id: 'f', assetRole: 'well', quantity: 'PumpFillage', canonicalUnit: '%', maximumAgeSeconds: 60 }], commandRequirements: [] });
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\x', isActive: true, roles: [], assetScopeIds: [], permissions: ['logic.read', 'configured-logic.read'] });
    if (url.startsWith('/api/v1/logic-modules')) return response([{ id: 'm1', moduleId: 'sample.module', version: '1.0.0', displayName: 'Fillage monitor',
      publisher: 'Weatherford', runtime: 'DotNet', executionProfile: 'ScheduledOneShot', manifestJson, trustStatus: 'CustomerApproved', isEnabled: true, installedAtUtc: '2026-10-01T00:00:00Z' }]);
    if (url.startsWith('/api/v1/configured-logic?')) return response([{ ...live, revisions: [{ ...live.revisions[0], moduleId: 'sample.module' }] }]);
    return response([]);
  }));
  render(<App />);
  await userEvent.setup().click(await screen.findByRole('button', { name: 'About Fillage monitor' }));
  const panel = await screen.findByRole('region', { name: 'About Fillage monitor' });
  expect(panel.textContent).toContain('Reads pump fillage for 1 well.');
  expect(panel.textContent).toContain('no older than 1 min');
  expect(panel.textContent).toContain('Pressure watch');
});

it('Your logic and Modules are tabs; the list sits beside the selected item', async () => {
  fixture(['configured-logic.read', 'logic.read']);
  render(<App />);
  const configured = await screen.findByRole('tab', { name: /Your logic/ });
  expect(configured.getAttribute('aria-selected')).toBe('true');
  expect(screen.getByText('Choose logic to see where it stands, or configure new logic for your assets.')).toBeTruthy();
  await userEvent.setup().click(await screen.findByRole('button', { name: 'Inspect revisions of Pressure watch' }));
  expect(window.location.hash).toBe('#logic/' + configurationId);
  await userEvent.setup().click(screen.getByRole('tab', { name: /Modules/ }));
  expect(await screen.findByText('Choose a module to see what it does and what it needs.')).toBeTruthy();
  expect(window.location.hash).toBe('#logic/modules');
});
