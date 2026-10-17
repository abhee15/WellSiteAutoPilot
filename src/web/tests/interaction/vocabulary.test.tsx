import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import { App } from '../../src/App';

const userId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const assetId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

it('operations show plain wording, a fallback asset name and keep the raw code as a tooltip', async () => {
  window.location.hash = '#operations';
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\words', isActive: true, roles: [], permissions: ['executions.read'], assetScopeIds: [] });
    if (url.includes('system/info')) return response({});
    if (url.includes('/executions')) return response([{ executionId: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd', assetId, assetExternalId: null,
      moduleId: 'pump-monitor', moduleVersion: '1.0.0', mode: 'Shadow', status: 'TimedOut', failureCode: 'EXECUTION_INPUT_STALE',
      requestedAtUtc: '2026-10-17T12:00:00Z' }]);
    return response([]);
  }));
  render(<App />);
  const row = (await screen.findByText('pump-monitor')).closest('tr')!;
  expect(within(row).getByText('Timed out')).not.toBeNull();
  const outcome = within(row).getByText('An input was too old');
  expect(outcome.getAttribute('title')).toBe('EXECUTION_INPUT_STALE');
  expect(within(row).getByText(assetId.slice(0, 8) + '…')).not.toBeNull();
  expect(document.title).toBe('Operations · WellSite AutoPilot');
});

it('offers a skip link that moves focus to the main content without changing the address', async () => {
  window.location.hash = '#operations';
  vi.stubGlobal('fetch', vi.fn(async (url: string) => response(url === '/api/v1/security/me'
    ? { userId, identityName: 'FIELD\\words', isActive: true, roles: [], permissions: ['executions.read'], assetScopeIds: [] }
    : url.includes('system/info') ? {} : [])));
  render(<App />);
  const skip = await screen.findByRole('link', { name: 'Skip to main content' });
  skip.click();
  expect(document.activeElement).toBe(document.getElementById('main-content'));
  expect(window.location.hash).toBe('#operations');
});
