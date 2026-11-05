import type { RecommendationSummaryResponse } from '../api/generated/v1/types.gen';

export type RecommendationRun = {
  key: string;
  executionId: string | null;
  items: RecommendationSummaryResponse[];
  latestAtUtc: string | null;
};

/** The inbox is already authorized and filtered. Never fabricate a run for legacy proposals. */
export function groupRecommendationRuns(items: readonly RecommendationSummaryResponse[]): RecommendationRun[] {
  const groups = new Map<string, RecommendationRun>();
  for (const item of items) {
    const executionId = item.executionId || null;
    const key = executionId ?? 'unlinked';
    let group = groups.get(key);
    if (!group) {
      group = { key, executionId, items: [], latestAtUtc: null };
      groups.set(key, group);
    }
    group.items.push(item);
    if (item.createdAtUtc && (!group.latestAtUtc || Date.parse(item.createdAtUtc) > Date.parse(group.latestAtUtc))) {
      group.latestAtUtc = item.createdAtUtc;
    }
  }
  return [...groups.values()];
}
