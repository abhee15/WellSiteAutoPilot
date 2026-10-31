import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { App } from '../../src/App';
import { canOpenSection } from '../../src/security/sessionAccess';
import { personas } from './rolePersonas';

const userId = '66666666-6666-4666-8666-666666666666';
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

const sections = ['overview', 'operations', 'recommendations', 'assets', 'logic', 'connections', 'status', 'access'] as const;
const linkName: Record<(typeof sections)[number], string> = {
  overview: 'Overview', operations: 'Runs', recommendations: 'Recommendations', assets: 'Assets', logic: 'Logic',
  connections: 'Connections', status: 'Status', access: 'Access' };

/** Which permission each API area needs: a request outside the persona's permissions means the page asked for data it may not read. */
const needs: [RegExp, string[]][] = [
  [/^\/api\/v1\/executions/, ['executions.read']],
  [/^\/api\/v1\/logic-modules/, ['logic.read']],
  [/^\/api\/v1\/configured-logic/, ['configured-logic.read']],
  [/^\/api\/v1\/recommendations/, ['recommendations.read']],
  [/^\/api\/v1\/assets|^\/api\/v1\/asset-types|^\/api\/v1\/asset-ownership/, ['assets.read']],
  [/^\/api\/v1\/connections/, ['connections.read']],
  [/^\/api\/v1\/security\/(users|roles|grants|directory|audit)/, ['security.manage']],
];

function signIn(permissions: string[]) {
  const calls: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const path = String(url);
    calls.push(`${init?.method ?? 'GET'} ${path}`);
    if (path === '/api/v1/security/me') {
      return json({ userId, identityName: 'FIELD\\persona', isActive: true, roles: [], permissions, assetScopeIds: [] });
    }
    if (path.includes('capabilities')) return json({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
    if (path.includes('system/health')) return json({});
    return json([]);
  }));
  return calls;
}

describe.each(Object.entries(personas))('as %s', (_name, permissions) => {
  it.each(sections)('%s: opens only when permitted, and never asks for data the role cannot read', async section => {
    window.location.hash = '#' + section;
    const calls = signIn(permissions);
    render(<App />);
    const user = { userId, identityName: 'x', isActive: true, permissions } as never;
    const allowed = canOpenSection(user, section);
    if (allowed) {
      await screen.findByRole('link', { name: linkName[section] });
      await waitFor(() => expect(screen.queryByText('Your current permissions do not allow this section.')).toBeNull());
    } else {
      // Access has its own message; every other section shares one.
      const denied = section === 'access' ? 'Administrator access is required to manage users.'
        : 'Your current permissions do not allow this section.';
      await screen.findByText(denied);
      expect(screen.queryByRole('link', { name: linkName[section] })).toBeNull();
    }
    await new Promise(resolve => setTimeout(resolve, 50));
    const leaks = calls.map(call => call.replace(/^\w+ /, '')).filter(path =>
      needs.some(([pattern, required]) => pattern.test(path) && !required.some(p => permissions.includes(p))));
    expect(leaks, `requests the role is not allowed to make`).toEqual([]);
  });
});

// ---- Controls: what each role is offered on a page it can open (the server decides the same way) ----
const asset = { id: '00000000-0000-4000-8000-000000000011', assetTypeId: '00000000-0000-4000-8000-000000000001', name: 'Well A',
  parentAssetId: null, attributeValuesJson: '{}', isActive: true, createdAtUtc: new Date().toISOString() };
const type = { id: asset.assetTypeId, key: 'well', displayName: 'Well', schemaVersion: 1, attributeSchemaJson: '{}', isActive: true,
  createdAtUtc: new Date().toISOString() };
const connection = { id: '00000000-0000-4000-8000-000000000950', name: 'ForeSite', adapterKey: 'foresite', settingsVersion: 1, settings: {},
  isEnabled: true, revision: 1, createdAtUtc: new Date().toISOString(), updatedAtUtc: new Date().toISOString(), lastTest: null, lastTestIsCurrent: false };

/** Answers like the server for one role: refuses what the role cannot read, and reports what it may create. */
function signInWithData(permissions: string[]) {
  const needsFor = (path: string) => needs.find(([pattern]) => pattern.test(path))?.[1][0];
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const path = String(url);
    if (path === '/api/v1/security/me') return json({ userId, identityName: 'FIELD\\persona', isActive: true, roles: [], permissions, assetScopeIds: [] });
    const required = needsFor(path.split('?')[0]!);
    if (required && !permissions.includes(required)) return json({}, 403);
    if (path.includes('/assets/capabilities')) {
      const can = permissions.includes('assets.manage'); return json({ canCreateRoot: can, canCreateUnderAuthorizedParents: can });
    }
    if (path.includes('system/health')) return json({});
    if (path.startsWith('/api/v1/asset-types')) return json([type]);
    if (path.startsWith('/api/v1/assets/owners')) return json([{ connectionId: null, name: null, isEnabled: true, assetCount: 1 }]);
    if (path.startsWith('/api/v1/assets')) return json([asset]);
    if (path.startsWith('/api/v1/connections/adapters')) return json([]);
    if (path.startsWith('/api/v1/connections')) return json([connection]);
    return json([]);
  }));
}

const has = (permissions: string[], ...required: string[]) => required.every(p => permissions.includes(p));
/** [where, control as the person sees it, who should be offered it]. */
const controls: [string, string, (p: string[]) => boolean][] = [
  ['#assets', 'Create View', p => has(p, 'asset-views.create', 'connections.read', 'assets.read')],
  ['#connections', 'New connection', p => has(p, 'connections.read', 'connections.manage')],
  // Needs an active Asset to configure, so it also needs to read Assets.
  ['#logic', 'Configure Logic', p => has(p, 'configured-logic.read', 'configured-logic.manage', 'assets.read')],
];

describe.each(Object.entries(personas))('controls as %s', (_name, permissions) => {
  it.each(controls)('%s: "%s" appears only for roles allowed to use it', async (hash, label, offered) => {
    window.location.hash = hash;
    signInWithData(permissions);
    render(<App />);
    const user = { userId, identityName: 'x', isActive: true, permissions } as never;
    const section = hash.slice(1) as (typeof sections)[number];
    if (!canOpenSection(user, section)) return; // not reachable: covered by the section matrix above
    await screen.findByRole('heading', { name: section === 'logic' ? 'Logic workspace' : linkName[section] });
    await new Promise(resolve => setTimeout(resolve, 300));
    const present = screen.queryAllByRole('button', { name: label }).length + screen.queryAllByRole('link', { name: label }).length > 0;
    expect(present, `${label} for permissions ${permissions.join(', ')}`).toBe(offered(permissions));
  });
});
