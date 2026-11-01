import type { AssetLogicPageResponse, ExecutionResponse, RecommendationSummaryResponse } from '../api/generated/v1/types.gen';
import { hashFor } from '../routing';
import { readDetail } from './detailRead';

export const LOGIC_PAGE_SIZE = 5;
export const RUN_PREVIEW_SIZE = 2;

export async function fetchAssetLogic(assetId: string, offset: number, signal: AbortSignal) {
  const page = await readDetail<AssetLogicPageResponse>(`/api/v1/assets/${encodeURIComponent(assetId)}/logic?offset=${offset}&limit=${LOGIC_PAGE_SIZE}`, signal);
  if (typeof page?.isGroupingNode !== 'boolean' || !Array.isArray(page.items) || typeof page.hasMore !== 'boolean' ||
    page.items.some(item => !item.configuration?.id)) throw new Error('ASSET_LOGIC_INVALID');
  return page;
}

export async function fetchLogicRuns(assetId: string, logicId: string | undefined, signal: AbortSignal) {
  const runs = await readDetail<ExecutionResponse[]>(`/api/v1/executions?${new URLSearchParams({ assetId, ...(logicId ? { configuredLogicId: logicId } : {}), limit: String(RUN_PREVIEW_SIZE) })}`, signal);
  if (!Array.isArray(runs)) throw new Error('ASSET_RUNS_INVALID');
  return runs.slice(0, RUN_PREVIEW_SIZE);
}

export async function fetchAssetRecommendations(assetId: string, signal: AbortSignal) {
  const items = await readDetail<RecommendationSummaryResponse[]>(`/api/v1/recommendations?assetId=${encodeURIComponent(assetId)}&limit=10`, signal);
  if (!Array.isArray(items)) throw new Error('ASSET_RECOMMENDATIONS_INVALID');
  return items;
}

export function runHistoryHash(assetId: string, logicId: string, runId?: string) {
  return hashFor('operations', `history?${new URLSearchParams({ assetId, configuredLogicId: logicId, ...(runId ? { runId } : {}) })}`);
}

export function parseRunHistory(detail: string | null) {
  if (!detail?.startsWith('history?')) return null;
  const params = new URLSearchParams(detail.slice('history?'.length));
  const assetId = params.get('assetId');
  const logicId = params.get('configuredLogicId');
  const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (!assetId || !logicId || !guid.test(assetId) || !guid.test(logicId)) return null;
  const runId = params.get('runId');
  return { assetId, logicId, runId: runId && guid.test(runId) ? runId : null };
}
