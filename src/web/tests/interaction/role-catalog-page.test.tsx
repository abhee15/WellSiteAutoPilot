import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RoleCatalogPage } from '../../src/views/RoleCatalogPage';
import * as catalog from '../../src/views/roleCatalog';
import { accessibilityViolations } from './axe';

vi.mock('../../src/views/roleCatalog', async importOriginal => ({
  ...await importOriginal<typeof import('../../src/views/roleCatalog')>(),
  loadRoleCatalog: vi.fn(), saveRole: vi.fn(),
}));

const role = (n: number, extra: Partial<catalog.RoleRecord> = {}): catalog.RoleRecord => ({
  roleId: `00000000-0000-4000-8000-00000000000${n}`, key: `role-${n}`, name: `Role ${n}`, description: `About role ${n}`,
  isSystem: false, isActive: true, revision: 1, createdAtUtc: '2026-10-01T00:00:00Z', updatedAtUtc: '2026-10-01T00:00:00Z',
  permissions: ['assets.read'], ...extra });
const permissions: catalog.PermissionDefinition[] = [
  { key: 'assets.read', category: 'Assets', scopeKind: 'ResourceScoped', isPrivileged: false },
  { key: 'security.manage', category: 'Security', scopeKind: 'DeploymentWide', isPrivileged: true }];
const roles = [role(1, { isSystem: true, name: 'Administrator' }), role(2, { name: 'Field operator' }), role(3, { name: 'Retired role', isActive: false })];

beforeEach(() => {
  vi.mocked(catalog.loadRoleCatalog).mockResolvedValue({ roles, permissions } as never);
});
afterEach(cleanup);

const ready = async () => { await screen.findByText('Field operator'); };
const editor = () => screen.queryByRole('region', { name: 'Role definition editor' });
const rowOf = (name: string) => screen.getByText(name).closest('tr') as HTMLElement;

describe('Role catalog page', () => {
  it('lists roles by name with their type, permission count and state', async () => {
    render(<RoleCatalogPage />);
    await ready();
    const operator = rowOf('Field operator');
    expect(within(operator).getByText('About role 2')).toBeTruthy();
    expect(within(operator).getByText('Customer-defined')).toBeTruthy();
    expect(within(operator).getByText('Active')).toBeTruthy();
    expect(within(rowOf('Administrator')).getByText('Protected system template')).toBeTruthy();
    expect(within(rowOf('Retired role')).getByText('Inactive')).toBeTruthy();
  });

  it('shows a role that is switched off as inactive, not as a problem', async () => {
    render(<RoleCatalogPage />);
    await ready();
    expect(rowOf('Retired role').querySelector('.status-indicator')?.getAttribute('data-tone')).toBe('neutral');
    expect(rowOf('Field operator').querySelector('.status-indicator')?.getAttribute('data-tone')).toBe('success');
  });

  it('searches by name and by key, and says so when nothing matches', async () => {
    const user = userEvent.setup();
    render(<RoleCatalogPage />);
    await ready();
    await user.type(screen.getByRole('searchbox', { name: 'Search roles' }), 'field');
    expect(screen.queryByText('Administrator')).toBeNull();
    expect(screen.getByText('Field operator')).toBeTruthy();
    await user.clear(screen.getByRole('searchbox', { name: 'Search roles' }));
    await user.type(screen.getByRole('searchbox', { name: 'Search roles' }), 'role-3');
    expect(screen.getByText('Retired role')).toBeTruthy();
    expect(screen.queryByText('Field operator')).toBeNull();
    await user.clear(screen.getByRole('searchbox', { name: 'Search roles' }));
    await user.type(screen.getByRole('searchbox', { name: 'Search roles' }), 'zzz');
    expect(screen.getByText('No matching roles.')).toBeTruthy();
  });

  it('opens an empty editor for Create role, and closes it on Cancel', async () => {
    const user = userEvent.setup();
    render(<RoleCatalogPage />);
    await ready();
    expect(editor()).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Create role' }));
    expect(editor()).toBeTruthy();
    expect(within(editor()!).getByRole('heading', { name: 'Create customer role' })).toBeTruthy();
    expect((within(editor()!).getByLabelText('Role name') as HTMLInputElement).value).toBe('');
    await user.click(within(editor()!).getByRole('button', { name: 'Cancel' }));
    expect(editor()).toBeNull();
  });

  it('Clone starts a copy of any role, including a system role', async () => {
    const user = userEvent.setup();
    render(<RoleCatalogPage />);
    await ready();
    await user.click(within(rowOf('Administrator')).getByRole('button', { name: 'Clone' }));
    expect((within(editor()!).getByLabelText('Role name') as HTMLInputElement).value).toBe('Administrator Copy');
  });

  it('Edit exists only for customer roles, and opens that role', async () => {
    const user = userEvent.setup();
    render(<RoleCatalogPage />);
    await ready();
    expect(within(rowOf('Administrator')).queryByRole('button', { name: 'Edit' })).toBeNull();
    await user.click(within(rowOf('Field operator')).getByRole('button', { name: 'Edit' }));
    expect(within(editor()!).getByRole('heading', { name: 'Edit customer role' })).toBeTruthy();
    expect((within(editor()!).getByLabelText('Role name') as HTMLInputElement).value).toBe('Field operator');
  });

  it('switching to another role replaces the editor instead of keeping the first role\'s text', async () => {
    const user = userEvent.setup();
    render(<RoleCatalogPage />);
    await ready();
    await user.click(within(rowOf('Field operator')).getByRole('button', { name: 'Edit' }));
    await user.type(within(editor()!).getByLabelText('Role name'), ' edited');
    await user.click(within(rowOf('Retired role')).getByRole('button', { name: 'Edit' }));
    expect((within(editor()!).getByLabelText('Role name') as HTMLInputElement).value).toBe('Retired role');
  });

  it('saves only after the change is acknowledged, then closes, says so and reloads', async () => {
    const user = userEvent.setup();
    vi.mocked(catalog.saveRole).mockResolvedValue(role(2, { name: 'Field operator 2' }));
    render(<RoleCatalogPage />);
    await ready();
    await user.click(within(rowOf('Field operator')).getByRole('button', { name: 'Edit' }));
    const save = () => within(editor()!).getByRole('button', { name: 'Save role definition' });
    await user.type(within(editor()!).getByLabelText('Role name'), ' 2');
    expect(save()).toHaveProperty('disabled', true);
    await user.click(within(editor()!).getByRole('checkbox', { name: /I reviewed the permissions/ }));
    expect(save()).toHaveProperty('disabled', false);
    await user.click(save());
    await waitFor(() => expect(editor()).toBeNull());
    expect(screen.getByRole('status').textContent).toContain('Role saved and verified');
    expect(catalog.saveRole).toHaveBeenCalledTimes(1);
    expect(catalog.loadRoleCatalog).toHaveBeenCalledTimes(2);
  });

  it('a refused save closes the editor and shows the reason', async () => {
    const user = userEvent.setup();
    vi.mocked(catalog.saveRole).mockRejectedValue(new catalog.RoleCatalogError('conflict'));
    render(<RoleCatalogPage />);
    await ready();
    await user.click(within(rowOf('Field operator')).getByRole('button', { name: 'Edit' }));
    await user.type(within(editor()!).getByLabelText('Role name'), 'x');
    await user.click(within(editor()!).getByRole('checkbox', { name: /I reviewed the permissions/ }));
    await user.click(within(editor()!).getByRole('button', { name: 'Save role definition' }));
    await waitFor(() => expect(editor()).toBeNull());
    expect(screen.getByRole('status').textContent).toMatch(/changed|conflict|reload/i);
  });

  it('without access it says so and offers no way to create or edit', async () => {
    vi.mocked(catalog.loadRoleCatalog).mockRejectedValue(new catalog.RoleCatalogError('accessDenied'));
    render(<RoleCatalogPage />);
    expect((await screen.findByRole('alert')).textContent).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Create role' })).toHaveProperty('disabled', true);
    expect(screen.queryByRole('button', { name: 'Edit' })).toBeNull();
  });

  it('is accessible as a list and with the editor open', async () => {
    const user = userEvent.setup();
    const { container } = render(<RoleCatalogPage />);
    await ready();
    expect(await accessibilityViolations(container)).toEqual([]);
    await user.click(within(rowOf('Field operator')).getByRole('button', { name: 'Edit' }));
    expect(await accessibilityViolations(container)).toEqual([]);
  });

  it('opens the editor beside the list, marks the role being edited, and Back returns to the full list', async () => {
    const user = userEvent.setup();
    const { container } = render(<RoleCatalogPage />);
    await ready();
    const layout = container.querySelector('.roles-layout') as HTMLElement;
    expect(layout.getAttribute('data-has-selection')).toBe('false');
    await user.click(within(rowOf('Field operator')).getByRole('button', { name: 'Edit' }));
    expect(layout.getAttribute('data-has-selection')).toBe('true');
    expect(layout.querySelector('.split-selected')?.contains(editor())).toBe(true);
    expect(rowOf('Field operator').getAttribute('data-selected')).toBe('true');
    expect(rowOf('Retired role').getAttribute('data-selected')).toBe('false');
    await user.click(screen.getByRole('button', { name: 'Back to roles' }));
    expect(editor()).toBeNull();
    expect(layout.getAttribute('data-has-selection')).toBe('false');
  });

  it('marks the long columns so they fold away beside the editor', async () => {
    const { container } = render(<RoleCatalogPage />);
    await ready();
    expect([...container.querySelectorAll('thead th.run-col-secondary')].map(th => th.textContent))
      .toEqual(['Type', 'Permissions']);
  });
});
