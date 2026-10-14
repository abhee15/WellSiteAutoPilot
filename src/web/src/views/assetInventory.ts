import type { AssetResponse } from '../api/generated/v1/types.gen';

export type AssetInventoryCriteria = {
  search: string;
  activity: 'all' | 'active' | 'inactive';
  hierarchy: 'all' | 'roots' | 'children';
  sort: 'name' | 'newest' | 'oldest';
};

export const DEFAULT_ASSET_INVENTORY_CRITERIA: AssetInventoryCriteria = {
  search: '', activity: 'all', hierarchy: 'all', sort: 'name',
};

// Authorization and scope are enforced by the API, never by these filters.
export function filterAssetInventory(
  authorizedAssets: readonly AssetResponse[],
  typeNames: ReadonlyMap<string | undefined, string | null | undefined>,
  criteria: AssetInventoryCriteria,
): AssetResponse[] {
  const search = criteria.search.trim().toLocaleLowerCase();
  return authorizedAssets.filter(asset => {
    if (criteria.activity === 'active' && !asset.isActive) return false;
    if (criteria.activity === 'inactive' && asset.isActive) return false;
    if (criteria.hierarchy === 'roots' && asset.parentAssetId) return false;
    if (criteria.hierarchy === 'children' && !asset.parentAssetId) return false;
    if (!search) return true;
    return [asset.name, asset.id, asset.assetTypeId, typeNames.get(asset.assetTypeId)]
      .some(value => (value ?? '').toLocaleLowerCase().includes(search));
  }).sort((left, right) => {
    if (criteria.sort === 'name') {
      const comparison = (left.name ?? '').localeCompare(right.name ?? '');
      return comparison || (left.id ?? '').localeCompare(right.id ?? '');
    }
    const leftTime = Date.parse(left.createdAtUtc ?? '') || 0;
    const rightTime = Date.parse(right.createdAtUtc ?? '') || 0;
    const comparison = criteria.sort === 'oldest'
      ? leftTime - rightTime : rightTime - leftTime;
    return comparison || (left.id ?? '').localeCompare(right.id ?? '');
  });
}
