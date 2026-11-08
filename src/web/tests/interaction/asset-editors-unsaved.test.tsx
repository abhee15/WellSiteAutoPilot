import { act, cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { UnsavedChangesPrompt } from '../../src/components/UnsavedChangesPrompt';
import { AssetCreationPanel } from '../../src/views/AssetCreationPanel';
import { AssetTypeDefinitionPanel } from '../../src/views/AssetTypeDefinitionPanel';
import type { AssetResponse, AssetTypeResponse } from '../../src/api/generated/v1/types.gen';

const wellType: AssetTypeResponse = { id: '11111111-1111-4111-8111-111111111111', key: 'well', displayName: 'Well', schemaVersion: 1,
  attributeSchemaJson: '{"type":"object","properties":{"field":{"type":"string"}}}', isActive: true, createdAtUtc: '2026-10-15T00:00:00Z' };
const field: AssetResponse = { id: '22222222-2222-4222-8222-222222222222', assetTypeId: wellType.id, name: 'Field Alpha',
  parentAssetId: null, attributeValuesJson: '{}', isActive: true, createdAtUtc: '2026-10-15T00:00:00Z' };
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
const changeHash = async (hash: string) => { await act(async () => { window.location.hash = hash; await new Promise(resolve => setTimeout(resolve, 20)); }); };
const askedToLeave = () => screen.queryByRole('heading', { name: 'Leave without saving?' });

beforeEach(() => { window.location.hash = '#assets'; });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

describe('asset creation panel', () => {
  const panel = () => render(<><UnsavedChangesPrompt /><AssetCreationPanel userId="operator-id" assetTypes={[wellType]} assets={[field]}
    capabilities={{ canCreateRoot: false, canCreateUnderAuthorizedParents: true }} onCreated={() => {}} onRefresh={() => {}} /></>);

  it('does not ask while the form is untouched, including after only choosing a type and parent', async () => {
    const user = userEvent.setup(); panel();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Asset type' }), wellType.id);
    await user.selectOptions(screen.getByRole('combobox', { name: 'Parent asset' }), field.id);
    await changeHash('#logic');
    expect(askedToLeave()).toBeNull();
    expect(window.location.hash).toBe('#logic');
  });

  it('asks once a name has been typed, and goes clean again when it is deleted', async () => {
    const user = userEvent.setup(); panel();
    const name = screen.getByRole('textbox', { name: 'Asset name' });
    await user.type(name, 'W');
    await changeHash('#logic');
    expect(askedToLeave()).toBeTruthy();
    expect(window.location.hash).toBe('#assets');
    await user.click(screen.getByRole('button', { name: 'Stay and keep editing' }));
    await user.clear(name);
    await changeHash('#logic');
    expect(askedToLeave()).toBeNull();
  });

  it('does not ask after the asset was created', async () => {
    const user = userEvent.setup();
    const created = { ...field, id: '33333333-3333-4333-8333-333333333333', name: 'Well 101', parentAssetId: field.id };
    vi.stubGlobal('fetch', vi.fn(async (url: string) => url === '/api/v1/assets' ? json(created, 201) : json(created)));
    panel();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Asset type' }), wellType.id);
    await user.selectOptions(screen.getByRole('combobox', { name: 'Parent asset' }), field.id);
    await user.type(screen.getByRole('textbox', { name: 'Asset name' }), 'Well 101');
    await user.click(screen.getByRole('button', { name: 'Create asset' }));
    await screen.findByRole('status');
    await changeHash('#logic');
    expect(askedToLeave()).toBeNull();
  });
});

describe('asset type definition panel', () => {
  const panel = (onCreated = vi.fn()) => render(<><UnsavedChangesPrompt /><AssetTypeDefinitionPanel onCreated={onCreated} /></>);

  it('does not ask when untouched, asks after typing, and not after a successful definition', async () => {
    const user = userEvent.setup();
    const echo = { id: '88888888-8888-4888-8888-888888888888', key: 'pump', displayName: 'Pump', schemaVersion: 1,
      attributeSchemaJson: '{}', isActive: true, createdAtUtc: '2026-10-15T00:00:00Z' };
    vi.stubGlobal('fetch', vi.fn(async () => json(echo, 201)));
    const { unmount } = panel();
    await changeHash('#logic');
    expect(askedToLeave()).toBeNull();
    unmount(); window.location.hash = '#assets';

    panel();
    await user.type(screen.getByLabelText(/^Key/), 'pump');
    await user.type(screen.getByLabelText(/^Display name/), 'Pump');
    await changeHash('#logic');
    expect(askedToLeave()).toBeTruthy();
    await user.click(screen.getByRole('button', { name: 'Stay and keep editing' }));
    await user.click(screen.getByRole('button', { name: 'Create asset type' }));
    await screen.findByRole('status');
    await changeHash('#logic');
    expect(askedToLeave()).toBeNull();
  });
});
