import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from '../../src/App';
import { t } from '../../src/localization';

const typeId = '11111111-1111-4111-8111-111111111111';
const assetId = '22222222-2222-4222-8222-222222222222';
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; localStorage.clear(); });

it('clears a selected Asset and draft when the same user loses its Asset scope', async () => {
  window.location.hash = '#assets';
  let revoked = false;
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const data = url.startsWith('/api/v1/security/me')
      ? { userId: '44444444-4444-4444-8444-444444444444', identityName: 'FIELD\\engineer',
          roles: ['Engineer'], isActive: true, assetScopeIds: revoked ? [] : [assetId] }
      : url.startsWith('/api/v1/asset-types')
      ? [{ id: typeId, key: 'well', displayName: 'Well', attributeSchemaJson: '{}', isActive: true }]
      : url.startsWith('/api/v1/assets/capabilities')
      ? { canCreateRoot: false, canCreateUnderAuthorizedParents: !revoked }
      : url.startsWith('/api/v1/assets?')
      ? revoked ? [] : [{ id: assetId, assetTypeId: typeId, name: 'Confidential Well', isActive: true }]
      : url.startsWith('/api/v1/system/info') ? {} : [];
    return new Response(JSON.stringify(data), { headers: { 'Content-Type': 'application/json' } });
  }));
  const user = userEvent.setup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: t('assets.selectNamed', { name: 'Confidential Well' }) }));
  expect(screen.getByText(assetId)).not.toBeNull();
  await user.type(screen.getByRole('textbox', { name: 'Asset name' }), 'Draft that must be cleared');
  revoked = true;
  await user.click(screen.getByRole('button', { name: 'Refresh' }));
  await screen.findByText(t('assets.createNoScope'));
  expect(screen.queryByText(assetId)).toBeNull();
  expect(screen.queryByText('Confidential Well')).toBeNull();
  expect(screen.queryByDisplayValue('Draft that must be cleared')).toBeNull();
});
