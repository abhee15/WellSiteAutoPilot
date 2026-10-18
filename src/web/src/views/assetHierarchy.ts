import type { AssetResponse } from '../api/generated/v1/types.gen';

export interface AssetTreeNode {
  asset: AssetResponse;
  children: AssetTreeNode[];
}

const byName = (a: AssetResponse, b: AssetResponse) => (a.name ?? '').localeCompare(b.name ?? '');

/**
 * Builds the hierarchy from parentAssetId. An asset whose parent is not in the list
 * (hidden from this person, or missing) becomes a top-level entry, and cycles are cut
 * so a malformed hierarchy can never loop the page.
 */
export function buildAssetTree(assets: readonly AssetResponse[]): AssetTreeNode[] {
  const ids = new Set(assets.map(asset => asset.id));
  const childrenOf = new Map<string, AssetResponse[]>();
  const roots: AssetResponse[] = [];
  for (const asset of assets) {
    const parent = asset.parentAssetId;
    if (parent && parent !== asset.id && ids.has(parent)) {
      childrenOf.set(parent, [...(childrenOf.get(parent) ?? []), asset]);
    } else {
      roots.push(asset);
    }
  }
  const placed = new Set<string>();
  const build = (asset: AssetResponse): AssetTreeNode => {
    placed.add(asset.id as string);
    const children = (childrenOf.get(asset.id as string) ?? [])
      .filter(child => !placed.has(child.id as string))
      .sort(byName)
      .map(build);
    return { asset, children };
  };
  const tree = roots.sort(byName).map(build);
  // Assets only reachable through a cycle never attach to a root; show them at the top level.
  for (const asset of assets) {
    if (!placed.has(asset.id as string)) tree.push(build(asset));
  }
  return tree;
}

/** Ids that must be expanded so the selected asset is visible. */
export function ancestorIds(assets: readonly AssetResponse[], assetId: string | null | undefined): Set<string> {
  const byId = new Map(assets.map(asset => [asset.id, asset]));
  const open = new Set<string>();
  let parent = assetId ? byId.get(assetId)?.parentAssetId : undefined;
  while (parent && !open.has(parent) && parent !== assetId) {
    open.add(parent);
    parent = byId.get(parent)?.parentAssetId;
  }
  return open;
}
