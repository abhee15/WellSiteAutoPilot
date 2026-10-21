import { useState } from 'react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AssetInventoryControls } from '../../src/views/AssetInventoryControls';
import { DEFAULT_ASSET_INVENTORY_CRITERIA } from '../../src/views/assetInventory';
import type { AssetInventoryCriteria } from '../../src/views/assetInventory';
import { assetViewsKey } from '../../src/views/assetSavedViews';

const primaryUserId = 'operator-alpha';

function AssetViewHarness({ userId = primaryUserId }: { userId?: string | null }) {
  const [criteria, setCriteria] = useState<AssetInventoryCriteria>({
    ...DEFAULT_ASSET_INVENTORY_CRITERIA
  });
  return (
    <AssetInventoryControls
      userId={userId}
      criteria={criteria}
      onChange={setCriteria}
      matchingCount={3}
      totalCount={5}
    />
  );
}

beforeEach(() => {
  window.localStorage.clear();
});

afterEach(() => {
  cleanup();
  window.localStorage.clear();
});

describe('Asset saved-view interaction', () => {
  it('types filters, persists a named view, and restores it from a select', async () => {
    const user = userEvent.setup();
    render(<AssetViewHarness />);

    const search = screen.getByRole('searchbox', { name: 'Search assets' });
    await user.type(search, 'pump');
    await user.type(screen.getByRole('textbox', { name: 'Filter name' }), 'Pump wells');
    await user.click(screen.getByRole('button', { name: 'Save filter' }));

    expect(screen.getByRole('status').textContent).toBe('Filter saved.');

    const raw = window.localStorage.getItem(assetViewsKey(primaryUserId));
    expect(raw).not.toBeNull();
    const stored = JSON.parse(raw!) as {
      version: number;
      views: Array<{ name: string; criteria: AssetInventoryCriteria }>;
    };
    expect(stored.version).toBe(1);
    expect(stored.views).toHaveLength(1);
    expect(stored.views[0].name).toBe('Pump wells');
    expect(stored.views[0].criteria.search).toBe('pump');

    await user.click(screen.getByRole('button', { name: 'Reset filters' }));
    expect((search as HTMLInputElement).value).toBe('');
    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Saved filters (this browser only)' }),
      screen.getByRole('option', { name: 'Pump wells' })
    );
    expect((search as HTMLInputElement).value).toBe('pump');
  });

  it('rejects an empty or duplicate view name without duplicating storage', async () => {
    const user = userEvent.setup();
    render(<AssetViewHarness />);

    await user.click(screen.getByRole('button', { name: 'Save filter' }));
    expect(screen.getByRole('status').textContent)
      .toBe('Enter a name of up to 48 characters.');
    expect(window.localStorage.getItem(assetViewsKey(primaryUserId))).toBeNull();

    await user.type(screen.getByRole('textbox', { name: 'Filter name' }), 'Working set');
    await user.click(screen.getByRole('button', { name: 'Save filter' }));
    await user.click(screen.getByRole('button', { name: 'Save filter' }));
    expect(screen.getByRole('status').textContent)
      .toBe('A view with that name already exists.');

    const raw = window.localStorage.getItem(assetViewsKey(primaryUserId));
    const stored = JSON.parse(raw!) as { views: unknown[] };
    expect(stored.views).toHaveLength(1);
  });

  it('does not permit saving or restoring preferences without an authenticated user', async () => {
    const user = userEvent.setup();
    render(<AssetViewHarness userId={null} />);

    const save = screen.getByRole('button', { name: 'Save filter' });
    const savedViews = screen.getByRole('combobox', { name: 'Saved filters (this browser only)' });
    expect((save as HTMLButtonElement).disabled).toBe(true);
    expect((savedViews as HTMLSelectElement).disabled).toBe(true);
    await user.click(screen.getByRole('button', { name: 'Reset filters' }));
    expect(window.localStorage.length).toBe(0);
  });
});
