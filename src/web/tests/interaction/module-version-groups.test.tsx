import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LogicPage } from '../../src/views/LogicPage';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });
const versions = [
  { id: 'old', moduleId: 'wfd.pump', displayName: 'Pump monitor', version: '1.9.0', isEnabled: true, trustStatus: 'TrustedPublisher' },
  { id: 'new', moduleId: 'wfd.pump', displayName: 'Pump monitor', version: '1.10.0', isEnabled: false, trustStatus: 'Untrusted' },
] as never;

it('presents one module family and inspects approval and details for the exact chosen version', async () => {
  window.location.hash = '#logic/modules';
  vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 200 })));
  const user = userEvent.setup();
  render(<LogicPage modules={versions} configuredLogic={[]} userId="user" assets={[]} assetTypes={[]}
    permissions={['logic.read']} onRefresh={async () => {}} />);
  expect(screen.getAllByRole('button', { name: 'About Pump monitor' })).toHaveLength(1);
  expect(screen.getByText('Versions: 2')).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'About Pump monitor' }));
  const selector = screen.getByRole('combobox', { name: /Module version/ });
  expect((selector as HTMLSelectElement).value).toBe('new');
  expect(within(selector).getAllByRole('option').map(option => option.getAttribute('value'))).toEqual(['new', 'old']);
  await user.selectOptions(selector, 'old');
  expect((screen.getByRole('combobox', { name: /Module version/ }) as HTMLSelectElement).value).toBe('old');
  const region = screen.getByRole('region', { name: 'About Pump monitor' });
  expect(within(region).getByText(/v1.9.0/)).toBeTruthy();
  expect(screen.getAllByRole('button', { name: 'About Pump monitor' })).toHaveLength(1);
});
