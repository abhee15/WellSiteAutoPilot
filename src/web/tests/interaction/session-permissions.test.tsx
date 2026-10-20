import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from '../../src/App';

const userId = '44444444-4444-4444-8444-444444444444';
const assetId = '22222222-2222-4222-8222-222222222222';
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

it('custom-role sessions request and show only permitted work areas', async () => {
  window.location.hash = '#assets';
  const http = vi.fn(async (url: string) => response(url === '/api/v1/security/me'
    ? { userId, identityName: 'FIELD\\custom', isActive: true, roles: ['Well Reader'], permissions: ['assets.read'], assetScopeIds: [assetId] }
    : url.includes('system/health') ? {} : url.includes('capabilities')
    ? { canCreateRoot: false, canCreateUnderAuthorizedParents: false } : []));
  vi.stubGlobal('fetch', http); render(<App />);
  await screen.findByRole('link', { name: 'Assets' });
  expect(screen.queryByRole('link', { name: 'Runs' })).toBeNull();
  expect(screen.queryByRole('link', { name: 'Logic' })).toBeNull();
  expect(http.mock.calls.some(([url]) => /executions|logic-modules|configured-logic/.test(url))).toBe(false);
});

it('an unrelated 403 clears that feature while preserving allowed work and the session', async () => {
  window.location.hash = '#operations';
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\custom', isActive: true,
      roles: ['Custom'], permissions: ['assets.read', 'executions.read'], assetScopeIds: [] });
    if (url === '/api/v1/asset-types') return response({}, 403);
    return response(url.includes('system/health') ? {} : url.includes('capabilities')
      ? { canCreateRoot: false, canCreateUnderAuthorizedParents: false } : []);
  }));
  render(<App />);
  await screen.findByText(/Access changed during refresh/);
  expect(screen.getByRole('link', { name: 'Runs' })).not.toBeNull();
  expect(screen.queryByRole('link', { name: 'Assets' })).toBeNull();
  expect(screen.getAllByText('FIELD\\custom').length).toBeGreaterThan(0);
});

it('permission loss removes the page and clears old asset data even when the role name remains Admin', async () => {
  window.location.hash = '#assets'; let revoked = false;
  vi.stubGlobal('fetch', vi.fn(async (url: string) => response(url === '/api/v1/security/me'
    ? { userId, identityName: 'FIELD\\named-admin', isActive: true, roles: ['Admin'],
        permissions: revoked ? [] : ['assets.read'], assetScopeIds: [assetId] }
    : url.includes('system/health') ? {} : url.includes('capabilities')
    ? { canCreateRoot: false, canCreateUnderAuthorizedParents: false }
    : url.includes('/assets?') ? [{ id: assetId, name: 'Confidential Asset', isActive: true }] : [])));
  const user = userEvent.setup(); render(<App />);
  await screen.findByRole('button', { name: 'Open Confidential Asset from the hierarchy' });
  revoked = true; await user.click(screen.getByRole('button', { name: 'Refresh' }));
  await screen.findByRole('heading', { name: 'Waiting for access' });
  expect(screen.queryByText('Confidential Asset')).toBeNull();
  expect(screen.queryByRole('link', { name: 'Assets' })).toBeNull();
});

it('catalog-only access does not load configuration capabilities or expose configuration controls', async () => {
  window.location.hash = '#logic';
  const http = vi.fn(async (url: string) => response(url === '/api/v1/security/me'
    ? { userId, identityName: 'FIELD\\catalog', isActive: true, roles: ['Catalog Reader'], permissions: ['logic.read'], assetScopeIds: [] }
    : url.includes('system/health') ? {} : []));
  vi.stubGlobal('fetch', http); render(<App />);
  await screen.findByText('Logic Modules');
  expect(screen.queryByText('Configured Logic')).toBeNull();
  expect(http.mock.calls.some(([url]) => url.includes('/configured-logic'))).toBe(false);
});

it('a 401 pauses automatic refresh and explains sign-in; manual Refresh retries and recovery resumes polling', async () => {
  window.location.hash = '#assets'; let authenticated = false;
  const sessionCalls = () => http.mock.calls.filter(([url]) => url === '/api/v1/security/me').length;
  const http = vi.fn(async (url: string) => url === '/api/v1/security/me'
    ? (authenticated ? response({ userId, identityName: 'FIELD\\reader', isActive: true, roles: [], permissions: ['assets.read'], assetScopeIds: [] })
      : response({}, 401))
    : response(url.includes('system/health') ? {} : url.includes('capabilities')
      ? { canCreateRoot: false, canCreateUnderAuthorizedParents: false } : []));
  vi.stubGlobal('fetch', http);
  vi.useFakeTimers({ shouldAdvanceTime: true });
  try {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    render(<App />);
    await screen.findByText(/Windows sign-in is required/);
    expect(sessionCalls()).toBe(1);
    await vi.advanceTimersByTimeAsync(60_000);
    expect(sessionCalls()).toBe(1);
    await user.click(screen.getByRole('button', { name: 'Refresh' }));
    await vi.waitFor(() => expect(sessionCalls()).toBe(2));
    await vi.advanceTimersByTimeAsync(60_000);
    expect(sessionCalls()).toBe(2);
    authenticated = true;
    await user.click(screen.getByRole('button', { name: 'Refresh' }));
    await screen.findByRole('link', { name: 'Assets' });
    const afterRecovery = sessionCalls();
    await vi.advanceTimersByTimeAsync(10_000);
    expect(sessionCalls()).toBeGreaterThan(afterRecovery);
  } finally { vi.useRealTimers(); }
});

it('non-401 session failures keep the generic message and automatic refresh', async () => {
  window.location.hash = '#assets';
  const http = vi.fn(async () => response({}, 500));
  vi.stubGlobal('fetch', http);
  vi.useFakeTimers({ shouldAdvanceTime: true });
  try {
    render(<App />);
    await screen.findByText(/session/i);
    expect(screen.queryByText(/Windows sign-in is required/)).toBeNull();
    const first = http.mock.calls.length;
    await vi.advanceTimersByTimeAsync(10_000);
    expect(http.mock.calls.length).toBeGreaterThan(first);
  } finally { vi.useRealTimers(); }
});

it('shows the waiting-for-access page with the sign-in name when the user has no permissions', async () => {
  window.location.hash = '#assets';
  vi.stubGlobal('fetch', vi.fn(async (url: string) => url === '/api/v1/security/me'
    ? response({ userId, identityName: 'FIELD\\newcomer', isActive: true, permissions: [] })
    : response([])));
  render(<App />);
  expect(await screen.findByRole('heading', { name: 'Waiting for access' })).toBeTruthy();
  expect(screen.getByText('FIELD\\newcomer', { selector: 'code' })).toBeTruthy();
  expect(screen.queryByText(/not available for this session|sectionDenied/i)).toBeNull();
  expect(screen.queryByRole('link', { name: 'Assets' })).toBeNull();
});
