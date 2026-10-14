import type { AssetInventoryCriteria } from './assetInventory';

export type SavedAssetView = {
  id: string;
  name: string;
  criteria: AssetInventoryCriteria;
};

export const MAX_SAVED_ASSET_VIEWS = 20;
const validActivity = new Set(['all', 'active', 'inactive']);
const validHierarchy = new Set(['all', 'roots', 'children']);
const validSort = new Set(['name', 'newest', 'oldest']);

export function assetViewsKey(userId: string): string {
  return 'wsa:assets:views:v1:' + encodeURIComponent(userId);
}

function validCriteria(candidate: unknown): candidate is AssetInventoryCriteria {
  if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate)) return false;
  const criteria = candidate as Partial<AssetInventoryCriteria>;
  return typeof criteria.search === 'string' && criteria.search.length <= 120 &&
    validActivity.has(criteria.activity ?? '') &&
    validHierarchy.has(criteria.hierarchy ?? '') &&
    validSort.has(criteria.sort ?? '');
}

export function parseSavedAssetViews(raw: string | null): SavedAssetView[] {
  if (!raw || raw.length > 50_000) return [];
  try {
    const envelope: unknown = JSON.parse(raw);
    if (!envelope || typeof envelope !== 'object' || Array.isArray(envelope)) return [];
    const payload = envelope as { version?: unknown; views?: unknown };
    if (payload.version !== 1 || !Array.isArray(payload.views)) return [];
    const ids = new Set<string>(), names = new Set<string>();
    const views: SavedAssetView[] = [];
    for (const candidate of payload.views) {
      if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate)) continue;
      const item = candidate as Partial<SavedAssetView>;
      if (typeof item.id !== 'string' || !item.id.trim() || item.id.length > 80 ||
        typeof item.name !== 'string' || !item.name.trim() ||
        item.name !== item.name.trim() || item.name.length > 48 ||
        !validCriteria(item.criteria)) continue;
      const normalizedName = item.name.toLocaleLowerCase();
      if (ids.has(item.id) || names.has(normalizedName)) continue;
      ids.add(item.id);
      names.add(normalizedName);
      views.push({ id: item.id, name: item.name, criteria: { ...item.criteria } });
      if (views.length >= MAX_SAVED_ASSET_VIEWS) break;
    }
    return views;
  } catch {
    return [];
  }
}

export function loadSavedAssetViews(
  userId: string | null | undefined,
  storage: Pick<Storage, 'getItem'> | null,
): SavedAssetView[] {
  if (!userId || !storage) return [];
  try {
    return parseSavedAssetViews(storage.getItem(assetViewsKey(userId)));
  } catch {
    return [];
  }
}

export function persistSavedAssetViews(
  userId: string | null | undefined,
  views: readonly SavedAssetView[],
  storage: Pick<Storage, 'setItem'> | null,
): boolean {
  if (!userId || !storage || views.length > MAX_SAVED_ASSET_VIEWS ||
    views.some(view => !view.id || !view.name || !validCriteria(view.criteria))) return false;
  try {
    storage.setItem(assetViewsKey(userId), JSON.stringify({ version: 1, views }));
    return true;
  } catch {
    return false;
  }
}
