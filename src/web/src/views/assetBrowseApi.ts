import type { AssetResponse } from '../api/generated/v1/types.gen';

/**
 * The workspace snapshot loads at most this many assets. When a deployment has that many or more
 * the flat list is incomplete, and the Assets page browses the hierarchy from the server instead.
 */
export const SNAPSHOT_ASSET_LIMIT = 100;
export const BROWSE_PAGE_SIZE = 100;
export const SEARCH_PAGE_SIZE = 50;
const MAXIMUM_PATH_DEPTH = 32;

/** One authorized asset in the hierarchy and how many authorized assets sit directly below it. */
export interface BrowseNode {
  id: string;
  assetTypeId: string;
  name: string;
  parentAssetId: string | null;
  isActive: boolean;
  childCount: number;
}

export interface SearchHit extends Omit<BrowseNode, 'childCount'> {
  path: { id: string; name: string }[];
}

const isObject = (value: unknown): value is Record<string, unknown> =>
  !!value && typeof value === 'object' && !Array.isArray(value);

function toNode(value: unknown): BrowseNode | null {
  if (!isObject(value) || typeof value.id !== 'string' || typeof value.name !== 'string') return null;
  return {
    id: value.id,
    assetTypeId: typeof value.assetTypeId === 'string' ? value.assetTypeId : '',
    name: value.name,
    parentAssetId: typeof value.parentAssetId === 'string' ? value.parentAssetId : null,
    isActive: value.isActive !== false,
    childCount: typeof value.childCount === 'number' ? value.childCount : 0,
  };
}

async function getJson(url: string, signal: AbortSignal): Promise<unknown> {
  const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', signal });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error('HTTP ' + response.status);
  return response.json();
}

function nodes(value: unknown): BrowseNode[] {
  if (!Array.isArray(value)) throw new Error('Unexpected response');
  return value.map(toNode).filter((node): node is BrowseNode => node !== null);
}

/** The top level (parentId null) or the children of one asset. */
export async function fetchChildren(
  parentId: string | null, offset: number, signal: AbortSignal,
): Promise<BrowseNode[]> {
  const query = `limit=${BROWSE_PAGE_SIZE}&offset=${offset}` +
    (parentId ? `&parentAssetId=${encodeURIComponent(parentId)}` : '');
  return nodes(await getJson(`/api/v1/assets/children?${query}`, signal));
}

export async function fetchSearch(text: string, offset: number, signal: AbortSignal): Promise<SearchHit[]> {
  const value = await getJson(
    `/api/v1/assets/search?q=${encodeURIComponent(text)}&limit=${SEARCH_PAGE_SIZE}&offset=${offset}`, signal);
  if (!Array.isArray(value)) throw new Error('Unexpected response');
  return value.flatMap(item => {
    const node = toNode(item);
    if (!node || !isObject(item)) return [];
    const path = Array.isArray(item.path)
      ? item.path.flatMap(step => isObject(step) && typeof step.id === 'string' && typeof step.name === 'string'
        ? [{ id: step.id, name: step.name }] : [])
      : [];
    return [{ id: node.id, assetTypeId: node.assetTypeId, name: node.name, parentAssetId: node.parentAssetId,
      isActive: node.isActive, path }];
  });
}

/** One asset, or null when it does not exist or this person may not see it. */
export async function fetchAsset(id: string, signal: AbortSignal): Promise<AssetResponse | null> {
  const value = await getJson(`/api/v1/assets/${encodeURIComponent(id)}`, signal);
  return isObject(value) && typeof value.id === 'string' ? value as unknown as AssetResponse : null;
}

/** Visible ancestors, top first. Stops at the first ancestor this person may not see. */
export async function fetchAncestors(asset: AssetResponse, signal: AbortSignal): Promise<AssetResponse[]> {
  const chain: AssetResponse[] = [];
  const seen = new Set<string>([asset.id ?? '']);
  let parentId = asset.parentAssetId;
  while (parentId && !seen.has(parentId) && chain.length < MAXIMUM_PATH_DEPTH) {
    seen.add(parentId);
    const parent = await fetchAsset(parentId, signal);
    if (!parent) break;
    chain.unshift(parent);
    parentId = parent.parentAssetId;
  }
  return chain;
}

/** A hierarchy node in the shape the asset page already understands. */
export function nodeAsAsset(node: BrowseNode): AssetResponse {
  return { id: node.id, assetTypeId: node.assetTypeId, name: node.name, parentAssetId: node.parentAssetId,
    attributeValuesJson: '{}', isActive: node.isActive, createdAtUtc: '' } as unknown as AssetResponse;
}
