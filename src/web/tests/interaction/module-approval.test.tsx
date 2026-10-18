import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from '../../src/App';

const userId = '66666666-6666-4666-8666-666666666666';
const manifestJson = JSON.stringify({ assetRequirements: [], dataRequirements: [], commandRequirements: [] });
const stored = { id: 'm1', moduleId: 'sample.module', version: '1.0.0', displayName: 'Fillage monitor', publisher: 'Weatherford',
  runtime: 'DotNet', executionProfile: 'ScheduledOneShot', manifestJson, installedAtUtc: '2026-10-01T00:00:00Z' };
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });

/** A server that remembers the module's state and records every write. */
function fixture(permissions: string[], initial: object, postStatus = 200, postBody?: object) {
  let module = { ...stored, ...initial };
  const writes: { url: string; body: string }[] = [];
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') {
      writes.push({ url, body: String(init.body) });
      if (postStatus !== 200) return json(postBody ?? {}, postStatus);
      module = { ...module, ...(url.endsWith('/approve') ? { trustStatus: 'CustomerApproved', isEnabled: true } : { isEnabled: false }) };
      return json(module);
    }
    if (url === '/api/v1/security/me') return json({ userId, identityName: 'FIELD\\x', isActive: true, roles: [], assetScopeIds: [], permissions });
    if (url.startsWith('/api/v1/logic-modules')) return json([module]);
    return json([]);
  }));
  window.location.hash = '#logic';
  return writes;
}

const open = async (user: ReturnType<typeof userEvent.setup>) =>
  user.click(await screen.findByRole('button', { name: 'About Fillage monitor' }));

it('says where a version stands but offers no change to someone who cannot manage modules', async () => {
  fixture(['logic.read'], { trustStatus: 'Untrusted', isEnabled: false });
  render(<App />);
  await open(userEvent.setup());
  expect(await screen.findByText(/has not been approved, so it cannot be used/)).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Approve this version…' })).toBeNull();
});

it('approving needs a reason, explains what changes, and shows the new state afterwards', async () => {
  const user = userEvent.setup();
  const writes = fixture(['logic.read', 'logic.manage'], { trustStatus: 'Untrusted', isEnabled: false });
  render(<App />);
  await open(user);
  await user.click(await screen.findByRole('button', { name: 'Approve this version…' }));
  expect(screen.getByText(/Nothing runs because of the approval/)).toBeTruthy();
  const confirm = screen.getByRole('button', { name: 'Approve version' }) as HTMLButtonElement;
  expect(confirm.disabled).toBe(true);
  expect(screen.getByText('Enter a reason to continue.')).toBeTruthy();
  await user.type(screen.getByRole('textbox', { name: /Reason/ }), '  Reviewed the package  ');
  expect(confirm.disabled).toBe(false);
  await user.click(confirm);
  expect(await screen.findByText('Version approved and recorded.')).toBeTruthy();
  expect(writes).toEqual([{ url: '/api/v1/logic-modules/sample.module/1.0.0/approve', body: JSON.stringify({ reason: 'Reviewed the package' }) }]);
  expect(await screen.findByText(/is approved and can be used in configurations/)).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Retire this version…' })).toBeTruthy();
});

it('retiring warns that it cannot be undone and then offers nothing more', async () => {
  const user = userEvent.setup();
  const writes = fixture(['logic.read', 'logic.manage'], { trustStatus: 'CustomerApproved', isEnabled: true });
  render(<App />);
  await open(user);
  await user.click(await screen.findByRole('button', { name: 'Retire this version…' }));
  expect(screen.getByText(/cannot be undone/)).toBeTruthy();
  await user.type(screen.getByRole('textbox', { name: /Reason/ }), 'Replaced by 1.1.0');
  await user.click(screen.getByRole('button', { name: 'Retire version' }));
  expect(await screen.findByText('Version retired and recorded.')).toBeTruthy();
  expect(writes[0].url).toBe('/api/v1/logic-modules/sample.module/1.0.0/retire');
  await waitFor(() => expect(screen.getByText(/was retired\. It cannot be used again/)).toBeTruthy());
  expect(screen.queryByRole('button', { name: 'Retire this version…' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Approve this version…' })).toBeNull();
});

it('a version that is already retired offers no change even to a manager', async () => {
  fixture(['logic.read', 'logic.manage'], { trustStatus: 'CustomerApproved', isEnabled: false });
  render(<App />);
  await open(userEvent.setup());
  expect(await screen.findByText(/was retired\. It cannot be used again/)).toBeTruthy();
  expect(screen.queryByRole('button', { name: /this version/ })).toBeNull();
});

it('a refusal is explained in words and the choice is closed, with no second write', async () => {
  const user = userEvent.setup();
  const writes = fixture(['logic.read', 'logic.manage'], { trustStatus: 'Untrusted', isEnabled: false }, 409, { code: 'MODULE_RETIRED' });
  render(<App />);
  await open(user);
  await user.click(await screen.findByRole('button', { name: 'Approve this version…' }));
  await user.type(screen.getByRole('textbox', { name: /Reason/ }), 'Reviewed');
  await user.click(screen.getByRole('button', { name: 'Approve version' }));
  const approval = await screen.findByRole('group', { name: 'Approval of Fillage monitor' });
  expect((await within(approval).findByRole('alert')).textContent).toContain('already retired and cannot be approved again');
  expect(writes).toHaveLength(1);
});
