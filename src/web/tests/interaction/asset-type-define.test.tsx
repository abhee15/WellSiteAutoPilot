import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App } from '../../src/App';

const userId = '66666666-6666-4666-8666-666666666666';
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

function host(permissions: string[], post: (body: Record<string, string>) => Response) {
  const posts: Record<string, string>[] = [];
  const types: unknown[] = [];
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    if (url === '/api/v1/security/me') return response({ userId, identityName: 'FIELD\\admin', isActive: true, roles: [], permissions, assetScopeIds: [] });
    if (url.includes('capabilities')) return response({ canCreateRoot: false, canCreateUnderAuthorizedParents: false });
    if (url === '/api/v1/asset-types' && init?.method === 'POST') {
      const body = JSON.parse(String(init.body));
      posts.push(body);
      return post(body);
    }
    if (url.includes('/asset-types')) return response(types);
    return response([]);
  }));
  return posts;
}

it('an Asset Type manager defines a type and the list is refreshed', async () => {
  const posts = host(['assets.read', 'assets.manage'], body => response({ id: '88888888-8888-4888-8888-888888888888', ...body, schemaVersion: 1, isActive: true }, 201));
  window.location.hash = '#assets/types';
  render(<App />);
  fireEvent.click(await screen.findByText('Define an Asset Type'));
  fireEvent.change(screen.getByLabelText('Key (short, unique)'), { target: { value: ' well ' } });
  fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'Well' } });
  fireEvent.click(screen.getByRole('button', { name: 'Create Asset Type' }));
  expect(await screen.findByText('Asset Type Well created.')).toBeTruthy();
  expect(posts).toEqual([{ key: 'well', displayName: 'Well', attributeSchemaJson: '{}' }]);
});

it('bad JSON is caught before any request and a duplicate key is explained', async () => {
  const posts = host(['assets.read', 'assets.manage'], () => response({ code: 'ASSET_TYPE_KEY_CONFLICT' }, 409));
  window.location.hash = '#assets/types';
  render(<App />);
  fireEvent.click(await screen.findByText('Define an Asset Type'));
  fireEvent.change(screen.getByLabelText('Key (short, unique)'), { target: { value: 'well' } });
  fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'Well' } });
  fireEvent.change(screen.getByLabelText('Attribute schema (JSON)'), { target: { value: '{' } });
  fireEvent.click(screen.getByRole('button', { name: 'Create Asset Type' }));
  const form = () => document.querySelector('.asset-type-define') as HTMLElement;
  await waitFor(() => expect(within(form()).getByRole('alert').textContent).toContain('not valid JSON'));
  expect(posts).toHaveLength(0);
  fireEvent.change(screen.getByLabelText('Attribute schema (JSON)'), { target: { value: '{}' } });
  fireEvent.click(screen.getByRole('button', { name: 'Create Asset Type' }));
  await waitFor(() => expect(within(form()).getByRole('alert').textContent).toContain('already exists'));
  expect(posts).toHaveLength(1);
});

it('people without the manage permission are not offered the form', async () => {
  host(['assets.read'], () => response({}, 403));
  window.location.hash = '#assets/types';
  render(<App />);
  await screen.findByText('No Asset Types are configured.');
  expect(screen.queryByText('Define an Asset Type')).toBeNull();
});
