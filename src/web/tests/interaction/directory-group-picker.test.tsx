import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { DirectoryGroupPicker } from '../../src/views/DirectoryGroupPicker';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
const group = { directoryId: 'tenant-a', externalId: 'OpaqueCaseID', displayName: 'SoftwareEngineers', domain: 'example' };
function setup(supported = true, assigned: unknown[] = []) {
  const fetcher = vi.fn().mockImplementation(async (url: string) => new Response(JSON.stringify(
    url === '/api/v1/security/directory' ? { directoryId: 'tenant-a', supportsGroupSearch: supported } :
      url === '/api/v1/security/groups' ? assigned : { groups: [group], hasMore: false })));
  vi.stubGlobal('fetch', fetcher);
  return fetcher;
}
it('selects a directory-qualified group by name without asking for a SID', async () => {
  const fetcher = setup(); const select = vi.fn(); const user = userEvent.setup();
  render(<DirectoryGroupPicker disabled={false} onSelect={select} />);
  await user.type(await screen.findByRole('textbox', { name: 'Group name starts with' }), 'Software');
  await user.click(screen.getByRole('button', { name: 'Search groups' }));
  await user.selectOptions(screen.getByRole('combobox', { name: 'Directory groups' }), '0');
  expect(select).toHaveBeenLastCalledWith(group);
  expect(fetcher).toHaveBeenCalledWith('/api/v1/security/directory/groups?prefix=Software', expect.any(Object));
  await user.selectOptions(screen.getByRole('combobox', { name: 'Directory groups' }), '');
  expect(select).toHaveBeenLastCalledWith(null);
});
it('keeps saved assignments inspectable when directory search is unavailable', async () => {
  setup(false, [group]); const select = vi.fn(); const user = userEvent.setup();
  render(<DirectoryGroupPicker disabled={false} onSelect={select} />);
  await screen.findByText('Group search is not configured for this deployment.');
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Previously assigned groups' }), '0');
  expect(select).toHaveBeenLastCalledWith(group);
  expect(screen.queryByRole('textbox')).toBeNull();
});
it('clears a selection when the search changes and reports a failed lookup', async () => {
  const fetcher = setup(); const select = vi.fn(); const user = userEvent.setup();
  render(<DirectoryGroupPicker disabled={false} onSelect={select} />);
  const input = await screen.findByRole('textbox');
  await user.type(input, 'Software');
  fetcher.mockResolvedValueOnce(new Response('{}', { status: 503 }));
  await user.click(screen.getByRole('button', { name: 'Search groups' }));
  await screen.findByRole('alert');
  expect(select).toHaveBeenLastCalledWith(null);
  expect((screen.getByRole('combobox', { name: 'Directory groups' }) as HTMLSelectElement).disabled).toBe(true);
});
