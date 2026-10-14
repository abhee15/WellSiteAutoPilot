import type { RecommendationSummaryResponse } from '../api/generated/v1/types.gen';

export type RecommendationInboxFilter = 'all' | 'awaiting' | 'expired' | 'decided';
export type RecommendationInboxSort = 'newest' | 'oldest';
export type RecommendationInboxCriteria = {
  search: string;
  filter: RecommendationInboxFilter;
  sort: RecommendationInboxSort;
};

export const DEFAULT_RECOMMENDATION_INBOX_CRITERIA: RecommendationInboxCriteria = {
  search: '', filter: 'awaiting', sort: 'newest',
};

const pendingStates = new Set(['Created', 'Pending']);
const decidedStates = new Set(['Approved', 'Rejected']);

function isExpired(item: RecommendationSummaryResponse, nowUtc: number): boolean {
  if (!item.expiresAtUtc) return false;
  const expiresAt = Date.parse(item.expiresAtUtc);
  // Invalid timing metadata is never considered safely actionable.
  return !Number.isFinite(expiresAt) || expiresAt <= nowUtc;
}

export function recommendationIsAwaiting(
  item: RecommendationSummaryResponse,
  nowUtc: number,
): boolean {
  return pendingStates.has(item.status ?? '') && !isExpired(item, nowUtc);
}

export function recommendationIsExpired(
  item: RecommendationSummaryResponse,
  nowUtc: number,
): boolean {
  return pendingStates.has(item.status ?? '') && isExpired(item, nowUtc);
}

export function inboxCounts(
  items: readonly RecommendationSummaryResponse[],
  nowUtc: number,
) {
  return {
    awaiting: items.filter(item => recommendationIsAwaiting(item, nowUtc)).length,
    expired: items.filter(item => recommendationIsExpired(item, nowUtc)).length,
    decided: items.filter(item => decidedStates.has(item.status ?? '')).length,
  };
}

export function filterRecommendationInbox(
  authorizedItems: readonly RecommendationSummaryResponse[],
  criteria: RecommendationInboxCriteria,
  nowUtc: number,
): RecommendationSummaryResponse[] {
  const query = criteria.search.trim().toLocaleLowerCase();
  return authorizedItems.filter(item => {
    if (criteria.filter === 'awaiting' && !recommendationIsAwaiting(item, nowUtc)) return false;
    if (criteria.filter === 'expired' && !recommendationIsExpired(item, nowUtc)) return false;
    if (criteria.filter === 'decided' && !decidedStates.has(item.status ?? '')) return false;

    if (!query) return true;
    return [item.assetId, item.moduleId, item.code, item.command,
      item.quantity, item.reasonCode, item.status]
      .some(value => (value ?? '').toLocaleLowerCase().includes(query));
  }).sort((left, right) => {
    const leftTime = Date.parse(left.createdAtUtc ?? '') || 0;
    const rightTime = Date.parse(right.createdAtUtc ?? '') || 0;
    const diff = criteria.sort === 'oldest' ? leftTime - rightTime : rightTime - leftTime;
    return diff || (left.id ?? '').localeCompare(right.id ?? '');
  });
}

export async function fetchRecommendationInbox(
  signal: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<RecommendationSummaryResponse[]> {
  const response = await fetcher('/api/v1/recommendations?limit=50', {
    method: 'GET',
    credentials: 'same-origin',
    cache: 'no-store',
    signal,
  });
  if (response.status === 401 || response.status === 403) {
    throw new Error('RECOMMENDATION_INBOX_ACCESS_DENIED');
  }
  if (!response.ok) throw new Error('RECOMMENDATION_INBOX_UNAVAILABLE');

  let payload: unknown;
  try {
    payload = await response.json();
  } catch {
    throw new Error('RECOMMENDATION_INBOX_INVALID_RESPONSE');
  }
  if (!Array.isArray(payload) || payload.length > 50 ||
      payload.some(item => item === null || typeof item !== 'object' ||
        typeof item.id !== 'string' ||
        typeof item.assetId !== 'string' ||
        typeof item.status !== 'string')) {
    throw new Error('RECOMMENDATION_INBOX_INVALID_RESPONSE');
  }
  return payload as RecommendationSummaryResponse[];
}
