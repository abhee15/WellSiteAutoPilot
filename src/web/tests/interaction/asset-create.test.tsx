import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AssetCreationPanel } from '../../src/views/AssetCreationPanel';
import type {
  AssetResponse,
  AssetTypeResponse,
} from '../../src/api/generated/v1/types.gen';

const wellType: AssetTypeResponse = {
  id: '11111111-1111-4111-8111-111111111111',
  key: 'well', displayName: 'Well',
  schemaVersion: 1,
  attributeSchemaJson: '{"type":"object","properties":{"field":{"type":"string"}}}',
  isActive: true, createdAtUtc: '2026-10-15T00:00:00Z',
};
const field: AssetResponse = {
  id: '22222222-2222-4222-8222-222222222222',
  assetTypeId: wellType.id, name: 'Field Alpha',
  parentAssetId: null, attributeValuesJson: '{}',
  isActive: true, createdAtUtc: '2026-10-15T00:00:00Z',
};
const created: AssetResponse = {
  ...field, id: '33333333-3333-4333-8333-333333333333',
  name: 'Well 101', parentAssetId: field.id,
};

const success = (data: unknown, status = 200) =>
  new Response(JSON.stringify(data), {
    status, headers: { 'Content-Type': 'application/json' }
  });

beforeEach(() => { vi.unstubAllGlobals(); });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

function panel(options: {
  allowRoot?: boolean;
  allowChildren?: boolean;
  authenticated?: boolean;
  onCreated?: (asset: AssetResponse) => Promise<void> | void;
  onRefresh?: () => Promise<void> | void;
} = {}) {
  return render(<AssetCreationPanel
    userId={options.authenticated === false ? null : 'operator-id'}
    assetTypes={[wellType]}
    assets={[field]}
    capabilities={{
      canCreateRoot: options.allowRoot ?? false,
      canCreateUnderAuthorizedParents: options.allowChildren ?? true,
    }}
    onCreated={options.onCreated ?? (() => {})}
    onRefresh={options.onRefresh ?? (() => {})}
  />);
}

async function prepareForm(user: ReturnType<typeof userEvent.setup>) {
  await user.selectOptions(
    screen.getByRole('combobox', { name: 'Asset type' }), wellType.id);
  await user.selectOptions(
    screen.getByRole('combobox', { name: 'Parent Asset' }), field.id);
  await user.type(screen.getByRole('textbox', { name: 'Asset name' }), 'Well 101');
}

describe('Asset create workflow with real DOM events', () => {
  it('posts one authorized request, verifies 201 with scoped GET and selects the real Asset', async () => {
    const user = userEvent.setup();
    const onCreated = vi.fn();
    const requests: Array<{ url: string; init?: RequestInit }> = [];
    const http = vi.fn(async (url: string, init?: RequestInit) => {
      requests.push({ url, init });
      return url === '/api/v1/assets'
        ? success(created, 201)
        : success(created);
    });
    vi.stubGlobal('fetch', http);
    panel({ onCreated });
    await prepareForm(user);

    // Capability hints are restrictive: ordinary users may create children
    // but the root-admin option must never appear in the parent dropdown.
    expect(screen.queryByRole('option', {
      name: 'Root Asset (administrator only)'
    })).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Create Asset' }));

    expect(await screen.findByRole('status')).toHaveProperty(
      'textContent', 'Asset Well 101 created and verified.');
    expect(onCreated).toHaveBeenCalledTimes(1);
    expect(onCreated).toHaveBeenCalledWith(created);
    expect(requests.map(item => item.url)).toEqual([
      '/api/v1/assets', '/api/v1/assets/' + created.id
    ]);
    const body = JSON.parse(String(requests[0].init?.body));
    expect(body).toEqual({
      assetTypeId: wellType.id, name: 'Well 101',
      parentAssetId: field.id, attributeValuesJson: '{}'
    });
  });

  it('refuses root creation for a parent-only operator and never sends a POST', async () => {
    const user = userEvent.setup();
    const http = vi.fn();
    vi.stubGlobal('fetch', http);
    panel();
    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Asset type' }), wellType.id);
    await user.type(screen.getByRole('textbox', { name: 'Asset name' }), 'Root');
    expect((screen.getByRole('button', {
      name: 'Create Asset'
    }) as HTMLButtonElement).disabled).toBe(true);
    expect(http).not.toHaveBeenCalled();
  });

  it('allows the root option only when explicitly granted by server capability', async () => {
    const user = userEvent.setup();
    const http = vi.fn(async () => success({}));
    vi.stubGlobal('fetch', http);
    panel({ allowRoot: true, allowChildren: false });
    expect(screen.getByRole('option', {
      name: 'Root Asset (administrator only)'
    })).not.toBeNull();
    expect(screen.queryByRole('option', { name: 'Field Alpha' })).toBeNull();
    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Asset type' }), wellType.id);
    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Parent Asset' }), '__root__');
    await user.type(screen.getByRole('textbox', { name: 'Asset name' }), 'Root');
    await user.click(screen.getByRole('button', { name: 'Create Asset' }));
    expect(http).toHaveBeenCalledTimes(1);
    expect(JSON.parse(String(http.mock.calls[0][1]?.body)).parentAssetId).toBeNull();
  });

  it.each([
    [403, 'You do not have permission to create this Asset. Refresh your access.'],
    [409, 'The Asset conflicts with an existing value. Refresh before trying again.'],
    [400, 'The server rejected the Asset values or schema. Review the selected type and attributes.'],
    [404, 'The selected Asset Type or parent no longer exists. Refresh and try again.'],
  ])('displays stable failure feedback for HTTP %d without blind retry',
    async (status, message) => {
      const user = userEvent.setup();
      const http = vi.fn(async () => success({}, status));
      vi.stubGlobal('fetch', http);
      panel();
      await prepareForm(user);
      await user.click(screen.getByRole('button', { name: 'Create Asset' }));
      expect(await screen.findByRole('alert')).toHaveProperty('textContent', message);
      expect(http).toHaveBeenCalledTimes(1);
    });

  it('reports an authorized write confirmed by 201 but not yet in readable Asset scope', async () => {
    const user = userEvent.setup();
    const onCreated = vi.fn();
    const onRefresh = vi.fn();
    const http = vi.fn(async (url: string) =>
      url === '/api/v1/assets' ? success(created, 201) : success({}, 403));
    vi.stubGlobal('fetch', http);
    panel({ onCreated, onRefresh });
    await prepareForm(user);
    await user.click(screen.getByRole('button', { name: 'Create Asset' }));

    expect(await screen.findByRole('status')).toHaveProperty(
      'textContent',
      'Asset Well 101 was created, but you do not currently have permission to view it. Ask an administrator to grant access. Do not recreate it.'
    );
    expect(http).toHaveBeenCalledTimes(2);
    expect(onRefresh).toHaveBeenCalledTimes(1);
    expect(onCreated).not.toHaveBeenCalled();
  });

  it('treats interrupted POST as uncertain and never retries the write', async () => {
    const user = userEvent.setup();
    const http = vi.fn(async () => {
      throw new Error('connection closed after POST may have committed');
    });
    vi.stubGlobal('fetch', http);
    panel();
    await prepareForm(user);
    await user.click(screen.getByRole('button', { name: 'Create Asset' }));
    expect(await screen.findByRole('alert')).toHaveProperty(
      'textContent',
      'Creation could not be confirmed. Refresh and check the Asset list before retrying.');
    expect(http).toHaveBeenCalledTimes(1);
  });

  it('prevents local malformed JSON and unauthenticated writes', async () => {
    const user = userEvent.setup();
    const http = vi.fn();
    vi.stubGlobal('fetch', http);
    panel();
    await prepareForm(user);
    await user.click(screen.getByRole('button', { name: 'Edit these details as JSON instead' }));
    await user.clear(screen.getByRole('textbox', { name: 'Attributes (JSON object)' }));
    // Set a malformed JSON string without invoking user-event keyboard syntax.
    await user.type(screen.getByRole('textbox', { name: 'Attributes (JSON object)' }),
      'not-json');
    await user.click(screen.getByRole('button', { name: 'Create Asset' }));
    expect(await screen.findByRole('alert')).toHaveProperty(
      'textContent', 'Enter a valid JSON object for Asset attributes.');
    expect(http).not.toHaveBeenCalled();

    cleanup();
    panel({ authenticated: false });
    expect(screen.getByText('Asset creation access is unavailable for this session.'))
      .not.toBeNull();
    expect(screen.queryByRole('button', { name: 'Create Asset' })).toBeNull();
  });
});

it('preserves raw duplicate attribute keys for authoritative server rejection', async () => {
  const user = userEvent.setup();
  const raw = '{"field":"Alpha","field":"Beta"}';
  const http = vi.fn<typeof fetch>(async () => success({}, 400));
  vi.stubGlobal('fetch', http);
  panel();
  await prepareForm(user);
  await user.click(screen.getByRole('button', { name: 'Edit these details as JSON instead' }));
  const editor = screen.getByRole('textbox', { name: 'Attributes (JSON object)' });
  await user.clear(editor);
  await user.paste(raw);
  await user.click(screen.getByRole('button', { name: 'Create Asset' }));
  await screen.findByRole('alert');
  expect(JSON.parse(String(http.mock.calls[0][1]?.body)).attributeValuesJson).toBe(raw);
  expect(http).toHaveBeenCalledTimes(1);
});


it.each([
  [202, created],
  [200, { ...created, id: '55555555-5555-4555-8555-555555555555' }],
  [200, { ...created, parentAssetId: null }],
  [200, { ...created, isActive: false }],
])('refuses unverified readback status or identity (%d)', async (status, result) => {
  const user = userEvent.setup();
  const onCreated = vi.fn();
  const http = vi.fn(async (url: string) => url === '/api/v1/assets'
    ? success(created, 201) : success(result, status));
  vi.stubGlobal('fetch', http);
  panel({ onCreated });
  await prepareForm(user);
  await user.click(screen.getByRole('button', { name: 'Create Asset' }));
  await screen.findByRole('alert');
  expect(onCreated).not.toHaveBeenCalled();
  expect(http).toHaveBeenCalledTimes(2);
});

it('aborts a pending create on removal and never starts stale readback or selection', async () => {
  const user = userEvent.setup();
  let finish!: (response: Response) => void;
  const http = vi.fn<typeof fetch>(() => new Promise(resolve => { finish = resolve; }));
  const onCreated = vi.fn();
  const onRefresh = vi.fn();
  vi.stubGlobal('fetch', http);
  const view = panel({ onCreated, onRefresh });
  await prepareForm(user);
  await user.click(screen.getByRole('button', { name: 'Create Asset' }));
  const signal = http.mock.calls[0][1]?.signal;
  view.unmount();
  expect(signal?.aborted).toBe(true);
  await act(async () => { finish(success(created, 201)); });
  expect(http).toHaveBeenCalledTimes(1);
  expect(onCreated).not.toHaveBeenCalled();
  expect(onRefresh).not.toHaveBeenCalled();
});

describe('Asset create form generated from the type schema', () => {
  const typed: AssetTypeResponse = {
    ...wellType,
    attributeSchemaJson: JSON.stringify({ type: 'object', required: ['depthFt'], properties: {
      depthFt: { type: 'integer', title: 'Depth (ft)', minimum: 0 },
      lift: { type: 'string', enum: ['ESP', 'Rod'] },
    } }),
  };
  const render_ = (onCreated = () => {}) => render(<AssetCreationPanel userId="operator-id" assetTypes={[typed]}
    assets={[field]} capabilities={{ canCreateRoot: false, canCreateUnderAuthorizedParents: true }}
    onCreated={onCreated} onRefresh={() => {}} />);

  it('shows labelled fields, names the problem, and sends typed values', async () => {
    const user = userEvent.setup();
    const http = vi.fn(async (url: string, init?: RequestInit) => init?.method === 'POST'
      ? success(created, 201) : success(created));
    vi.stubGlobal('fetch', http);
    render_();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Asset type' }), typed.id);
    await user.selectOptions(screen.getByRole('combobox', { name: 'Parent Asset' }), field.id);
    await user.type(screen.getByRole('textbox', { name: 'Asset name' }), 'Well 101');
    expect(screen.queryByRole('textbox', { name: 'Attributes (JSON object)' })).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Create Asset' }));
    expect(await screen.findByText('This detail is required.')).toBeTruthy();
    expect(http).not.toHaveBeenCalled();
    await user.type(screen.getByRole('textbox', { name: 'Depth (ft)' }), '1200');
    await user.selectOptions(screen.getByRole('combobox', { name: 'Lift (optional)' }), 'Rod');
    await user.click(screen.getByRole('button', { name: 'Create Asset' }));
    await vi.waitFor(() => expect(http).toHaveBeenCalled());
    const body = JSON.parse(String(http.mock.calls[0][1]?.body));
    expect(JSON.parse(body.attributeValuesJson)).toEqual({ depthFt: 1200, lift: 'Rod' });
  });
});
