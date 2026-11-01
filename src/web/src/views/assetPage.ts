import type {
  AssetResponse,
} from '../api/generated/v1/types.gen';

/** Ancestors from the top of the hierarchy down to (and excluding) the asset; stops on cycles. */
export function assetAncestors(assets: readonly AssetResponse[], assetId: string): AssetResponse[] {
  const byId = new Map(assets.map(asset => [asset.id, asset]));
  const chain: AssetResponse[] = [];
  const seen = new Set<string>([assetId]);
  let parentId = byId.get(assetId)?.parentAssetId;
  while (parentId && !seen.has(parentId)) {
    seen.add(parentId);
    const parent = byId.get(parentId);
    if (!parent) break; // parent not visible to this person: show only what they may see
    chain.unshift(parent);
    parentId = parent.parentAssetId;
  }
  return chain;
}

export function assetChildren(assets: readonly AssetResponse[], assetId: string): AssetResponse[] {
  return assets
    .filter(asset => asset.parentAssetId === assetId)
    .sort((a, b) => (a.name ?? '').localeCompare(b.name ?? ''));
}
