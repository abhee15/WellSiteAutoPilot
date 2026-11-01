import type { ExecutionHistoryResponse } from '../api/generated/v1/types.gen';
import { readDetail } from './detailRead';

export async function fetchRunHistory(assetId: string | undefined, logicId: string, after: string | null, signal: AbortSignal) {
  const page = await readDetail<ExecutionHistoryResponse>(`/api/v1/executions/history?${new URLSearchParams({ ...(assetId ? { assetId } : {}), configuredLogicId: logicId, limit: '25', ...(after ? { after } : {}) })}`, signal);
  if (!Array.isArray(page?.items)) throw new Error('ASSET_HISTORY_INVALID');
  return page;
}
