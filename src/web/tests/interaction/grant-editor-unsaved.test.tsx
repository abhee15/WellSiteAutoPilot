import { act, cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ComponentProps } from 'react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { UnsavedChangesPrompt } from '../../src/components/UnsavedChangesPrompt';
import { ScopedGrantEditor } from '../../src/views/ScopedGrantEditor';

const role = { roleId: '139e5b2f-dd2d-4630-b920-141b7bfb7a62', key: 'operator', name: 'Field operator', description: '', isSystem: false,
  isActive: true, revision: 1, createdAtUtc: '2026-10-01T00:00:00Z', permissions: ['assets.read'], permissionKeys: ['assets.read'] };
const permissions = [{ key: 'assets.read', category: 'Assets', scopeKind: 'ResourceScoped', isPrivileged: false }];
const assets = [{ id: '22222222-2222-4222-8222-222222222222', name: 'Well Alpha', isActive: true }];
const goTo = async (hash: string) => { await act(async () => { window.location.hash = hash; await new Promise(resolve => setTimeout(resolve, 20)); }); };
const leaveQuestion = () => screen.queryByRole('heading', { name: 'Leave without saving?' });

beforeEach(() => { window.location.hash = '#access'; });
afterEach(cleanup);

// The fixtures carry only what the editor reads; the full generated response types have many more fields.
const props = { user: { userId: 'u2', identityName: 'ops@example.com' }, roles: [role], permissions, assets, allowAllAssets: true,
  onContinue: () => {}, onCancel: () => {} } as unknown as ComponentProps<typeof ScopedGrantEditor>;
const editor = () => render(<><UnsavedChangesPrompt /><ScopedGrantEditor {...props} /></>);

describe('grant editor unsaved-changes guard', () => {
  it('does not ask while nothing has been chosen', async () => {
    editor();
    await goTo('#logic');
    expect(leaveQuestion()).toBeNull();
  });

  it('asks once a role is chosen, and is clean again when it is cleared', async () => {
    const user = userEvent.setup();
    editor();
    const select = screen.getAllByRole('combobox')[0];
    await user.selectOptions(select, role.roleId);
    await goTo('#logic');
    expect(leaveQuestion()).toBeTruthy();
    expect(window.location.hash).toBe('#access');
    await user.click(screen.getByRole('button', { name: 'Stay and keep editing' }));
    await user.selectOptions(select, '');
    await goTo('#logic');
    expect(leaveQuestion()).toBeNull();
  });
});
