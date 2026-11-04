import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AdministrationWorkspace } from '../../src/views/AdministrationWorkspace';
import { accessibilityViolations } from './axe';

vi.mock('../../src/views/AccessManagementPage', () => ({
  AccessManagementPage: ({ onAssignRole }: { onAssignRole: (user: {
    userId: string; identityName: string; isActive: boolean;
  }) => void }) =>
    <button type="button" onClick={() => onAssignRole({
      userId: '11111111-1111-4111-8111-111111111111',
      identityName: 'FIELD\\operator',
      isActive: true,
    })}>Assign role to selected user</button>,
}));
vi.mock('../../src/views/GrantAdministrationPage', () => ({
  GrantAdministrationPage: ({ initialUserId }: { initialUserId?: string }) =>
    <p data-testid="grant-handoff">{initialUserId || 'No selected user'}</p>,
}));
vi.mock('../../src/views/RoleCatalogPage', () => ({
  RoleCatalogPage: () => <p>Roles content</p>,
}));
vi.mock('../../src/views/ActivityPage', () => ({
  ActivityPage: ({ selectedId }: { selectedId?: string | null }) =>
    <p data-testid="activity-selection">{selectedId || 'No event selected'}</p>,
}));

afterEach(() => { cleanup(); window.location.hash = ''; });

it('uses one native navigation landmark, exact addressable links, and the correct selected link', async () => {
  window.location.hash = '#access/roles';
  const user = userEvent.setup();
  const view = render(<AdministrationWorkspace currentUserId="admin" />);
  const nav = screen.getByRole('navigation', { name: 'Access administration' });
  expect(within(nav).getAllByRole('link').map(link => link.getAttribute('href')))
    .toEqual(['#access', '#access/roles', '#access/grants', '#access/activity']);
  expect(screen.queryByRole('tablist')).toBeNull();
  expect(screen.queryByRole('tabpanel')).toBeNull();
  expect(within(nav).getByRole('link', { name: 'Roles and permissions' })
    .getAttribute('aria-current')).toBe('page');
  await user.click(within(nav).getByRole('link', { name: 'User assignments' }));
  await waitFor(() => expect(window.location.hash).toBe('#access'));
  expect(screen.getByRole('link', { name: 'User assignments' }).getAttribute('aria-current')).toBe('page');
  expect(await accessibilityViolations(view.container)).toEqual([]);
});

it('carries an authorized Assign role selection to Grants, but direct Grants navigation clears it', async () => {
  window.location.hash = '#access';
  const user = userEvent.setup();
  render(<AdministrationWorkspace currentUserId="admin" />);
  await user.click(screen.getByRole('button', { name: 'Assign role to selected user' }));
  await waitFor(() => expect(window.location.hash).toBe('#access/grants'));
  expect(screen.getByTestId('grant-handoff').textContent).toBe('11111111-1111-4111-8111-111111111111');
  await user.click(screen.getByRole('link', { name: 'User assignments' }));
  await waitFor(() => expect(window.location.hash).toBe('#access'));
  await user.click(screen.getByRole('link', { name: 'Scoped grants' }));
  await waitFor(() => expect(window.location.hash).toBe('#access/grants'));
  expect(screen.getByTestId('grant-handoff').textContent).toBe('No selected user');
});

it('retains the selected audit event from a bookmark while the Activity nav item points to its section', async () => {
  const eventId = '22222222-2222-4222-8222-222222222222';
  window.location.hash = '#access/activity%2F' + eventId;
  const user = userEvent.setup();
  render(<AdministrationWorkspace currentUserId="admin" />);
  expect(screen.getByTestId('activity-selection').textContent).toBe(eventId);
  const link = screen.getByRole('link', { name: 'Activity' });
  expect(link.getAttribute('aria-current')).toBe('page');
  expect(link.getAttribute('href')).toBe('#access/activity');
  await user.click(link);
  await waitFor(() => expect(window.location.hash).toBe('#access/activity'));
  expect(screen.getByTestId('activity-selection').textContent).toBe('No event selected');
});
