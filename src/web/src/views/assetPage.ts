import type {
  AssetResponse,
  ConfiguredLogicResponse,
  ExecutionResponse,
  RecommendationSummaryResponse,
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

/** Configured logic with any saved revision bound to this asset. */
export function logicOnAsset(
  configurations: readonly ConfiguredLogicResponse[],
  assetId: string,
): ConfiguredLogicResponse[] {
  return configurations
    .filter(item => (item.revisions ?? []).some(revision =>
      (revision.assetBindings ?? []).some(binding => binding.assetId === assetId)))
    .sort((a, b) => (a.name ?? '').localeCompare(b.name ?? ''));
}

const RECENT = 10;

async function readList<T>(url: string, signal: AbortSignal, fetcher: typeof fetch): Promise<T[]> {
  const response = await fetcher(url, {
    method: 'GET', credentials: 'same-origin', cache: 'no-store', signal,
  });
  if (!response.ok) throw new Error('ASSET_ACTIVITY_UNAVAILABLE');
  const payload: unknown = await response.json();
  if (!Array.isArray(payload)) throw new Error('ASSET_ACTIVITY_UNAVAILABLE');
  return payload as T[];
}

/** Server-side filtered, grant-scoped lists for one asset. */
export function fetchAssetRuns(assetId: string, signal: AbortSignal, fetcher: typeof fetch = fetch) {
  return readList<ExecutionResponse>(
    `/api/v1/executions?assetId=${encodeURIComponent(assetId)}&limit=${RECENT}`, signal, fetcher);
}

export function fetchAssetRecommendations(
  assetId: string, signal: AbortSignal, fetcher: typeof fetch = fetch,
) {
  return readList<RecommendationSummaryResponse>(
    `/api/v1/recommendations?assetId=${encodeURIComponent(assetId)}&limit=${RECENT}`, signal, fetcher);
}
