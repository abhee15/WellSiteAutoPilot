import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import { App } from '../../src/App';

const userId = '55555555-5555-4555-8555-555555555555';
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

function signIn(permissions: string[]) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => response(url === '/api/v1/security/me'
    ? { userId, identityName: 'FIELD\\nav', isActive: true, roles: [], permissions, assetScopeIds: [] }
    : url.includes('system/info') ? {} : url.includes('capabilities')
      ? { canCreateRoot: false, canCreateUnderAuthorizedParents: false } : [])));
}

it('groups navigation by intent and shows only groups the user can use', async () => {
  window.location.hash = '#assets';
  signIn(['assets.read', 'logic.read']);
  render(<App />);
  const nav = await screen.findByRole('navigation', { name: 'Primary' });
  await within(nav).findByRole('link', { name: 'Assets' });
  expect(within(nav).getByRole('group', { name: 'Understand' })).not.toBeNull();
  expect(within(nav).queryByRole('group', { name: 'Operate' })).toBeNull();
  expect(within(nav).queryByRole('group', { name: 'Administer' })).toBeNull();
});

it('shows Access in the navigation for administrators', async () => {
  window.location.hash = '#access';
  signIn(['security.manage']);
  render(<App />);
  const nav = await screen.findByRole('navigation', { name: 'Primary' });
  const link = await within(nav).findByRole('link', { name: 'Access' });
  expect(link.getAttribute('aria-current')).toBe('page');
  expect(within(nav).getByRole('group', { name: 'Administer' })).not.toBeNull();
});

it('lands on the first permitted section when no section is addressed', async () => {
  window.location.hash = '';
  signIn(['recommendations.read']);
  render(<App />);
  await waitFor(() => expect(window.location.hash).toBe('#recommendations'));
  expect((await screen.findByRole('link', { name: 'Recommendations' })).getAttribute('aria-current')).toBe('page');
});

it('respects an explicit address to a section the user cannot open', async () => {
  window.location.hash = '#logic';
  signIn(['assets.read']);
  render(<App />);
  await screen.findByText('Your current permissions do not allow this section.');
  expect(window.location.hash).toBe('#logic');
});
