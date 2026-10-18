import { afterEach, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { UserMenu } from '../../src/components/UserMenu';

afterEach(cleanup);
const user = (permissions: string[]) => ({ userId: 'u', identityName: 'FIELD\\link', isActive: true, roles: [], permissions, assetScopeIds: [] });

it('offers access management only to someone who can use it, not as a disabled item', async () => {
  render(<UserMenu user={user(['assets.read']) as never} isLoading={false} />);
  expect(screen.queryByText('Manage access and roles')).toBeNull();
  cleanup();
  render(<UserMenu user={user(['security.manage']) as never} isLoading={false} />);
  expect(screen.getByText('Manage access and roles').getAttribute('href')).toBe('#access');
});

it('closes with Escape (focus returns to the button) and on a click outside', async () => {
  const actor = userEvent.setup();
  render(<div><button type="button">elsewhere</button><UserMenu user={user([]) as never} isLoading={false} /></div>);
  const summary = screen.getByText('FIELD\\link', { selector: '.user-label' }).closest('summary') as HTMLElement;
  const details = summary.closest('details') as HTMLDetailsElement;
  await actor.click(summary);
  expect(details.open).toBe(true);
  await actor.keyboard('{Escape}');
  expect(details.open).toBe(false);
  expect(document.activeElement).toBe(summary);
  await actor.click(summary);
  expect(details.open).toBe(true);
  await actor.click(screen.getByRole('button', { name: 'elsewhere' }));
  expect(details.open).toBe(false);
});
