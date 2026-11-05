import type { RecommendationStatus, RecommendationSummaryResponse } from '../api/generated/v1/types.gen';

/** Which review states to show. None chosen means every recommendation. */
export type RecommendationInboxFilter = 'awaiting' | 'expired' | 'decided';
export const RECOMMENDATION_INBOX_FILTERS: readonly RecommendationInboxFilter[] = ['awaiting', 'expired', 'decided'];
export type RecommendationInboxSort = 'newest' | 'oldest';
export type RecommendationInboxCriteria = {
  search: string;
  filters: readonly RecommendationInboxFilter[];
  sort: RecommendationInboxSort;
};

export const DEFAULT_RECOMMENDATION_INBOX_CRITERIA: RecommendationInboxCriteria = {
  search: '', filters: ['awaiting'], sort: 'newest',
};

// `satisfies` makes the compiler check each name against the server's RecommendationStatus.
const pendingStates = new Set<string>(['Created', 'Pending'] satisfies RecommendationStatus[]);
// Executed stays with the decided ones: it is what an approved recommendation becomes once its command has been sent.
const decidedStates = new Set<string>(['Approved', 'Rejected', 'Executed'] satisfies RecommendationStatus[]);

function isExpired(item: RecommendationSummaryResponse, nowUtc: number): boolean {
  if (!item.expiresAtUtc) return false;
  const expiresAt = Date.parse(item.expiresAtUtc);
  // Invalid timing metadata is never considered safely actionable.
  return !Number.isFinite(expiresAt) || expiresAt <= nowUtc;
}

/** True once the recommendation's expiry has passed, or its expiry cannot be read. */
export const recommendationExpiryPassed = isExpired;

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
    if (criteria.filters.length > 0 && !criteria.filters.some(kind =>
      kind === 'awaiting' ? recommendationIsAwaiting(item, nowUtc)
        : kind === 'expired' ? recommendationIsExpired(item, nowUtc)
          : decidedStates.has(item.status ?? ''))) return false;

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
  executionId?: string,
): Promise<RecommendationSummaryResponse[]> {
  const limit = executionId ? 100 : 50;
  const query = new URLSearchParams({ limit: String(limit) });
  if (executionId) query.set('executionId', executionId);
  const response = await fetcher('/api/v1/recommendations?' + query, {
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
  if (!Array.isArray(payload) || payload.length > limit ||
      payload.some(item => item === null || typeof item !== 'object' ||
        typeof item.id !== 'string' ||
        typeof item.assetId !== 'string' ||
        typeof item.status !== 'string' || (executionId && item.executionId !== executionId))) {
    throw new Error('RECOMMENDATION_INBOX_INVALID_RESPONSE');
  }
  return payload as RecommendationSummaryResponse[];
}

export async function fetchRecommendationById(id: string, signal: AbortSignal): Promise<RecommendationSummaryResponse> {
  const response = await fetch('/api/v1/recommendations/' + encodeURIComponent(id), {
    credentials: 'same-origin', cache: 'no-store', signal,
  });
  if (!response.ok) throw new Error('RECOMMENDATION_UNAVAILABLE');
  const item: RecommendationSummaryResponse | null = await response.json();
  if (item?.id !== id || typeof item.assetId !== 'string' || typeof item.status !== 'string') {
    throw new Error('RECOMMENDATION_INVALID_RESPONSE');
  }
  return item;
}

export function recommendationProposal(item: Pick<RecommendationSummaryResponse, 'command' | 'code' | 'quantity' | 'suggestedValue' | 'unit'>): string {
  const value = item.suggestedValue;
  const command = item.command ?? item.code ?? '';
  const target = typeof value === 'number' && Number.isFinite(value) ? [String(value), item.unit].filter(Boolean).join(' ') : null;
  return [command, [item.quantity, target].filter(Boolean).join(' · ')].filter(Boolean).join(': ');
}
