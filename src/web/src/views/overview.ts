import type {
  AssetResponse,
  ConfiguredLogicResponse,
  ExecutionResponse,
  ExecutionStatus,
  RecommendationSummaryResponse,
} from '../api/generated/v1/types.gen';
import { recommendationIsAwaiting } from './recommendationInbox';

export type AttentionKind = 'decide' | 'failedRuns' | 'noAssets' | 'noLogic' | 'idleLogic';

export type AttentionItem = {
  kind: AttentionKind;
  count: number;
  /** Section address to open, e.g. "#recommendations". */
  href: string;
};

const PROBLEM_STATUSES = new Set<string>(['Failed', 'TimedOut', 'Suspended'] satisfies ExecutionStatus[]);
const RECENT_MS = 24 * 60 * 60 * 1000;

/**
 * What needs a person, from data already loaded. Only families the person may read are considered, so
 * the list never hints at records they cannot open. Authorization stays on the server.
 */
export function attentionItems(input: {
  permissions: readonly string[];
  executions: readonly ExecutionResponse[];
  recommendations: readonly RecommendationSummaryResponse[];
  assets: readonly AssetResponse[];
  configuredLogic: readonly ConfiguredLogicResponse[];
  nowUtc: number;
}): AttentionItem[] {
  const can = (permission: string) => input.permissions.includes(permission);
  const items: AttentionItem[] = [];

  if (can('recommendations.read')) {
    const waiting = input.recommendations.filter(item => recommendationIsAwaiting(item, input.nowUtc)).length;
    if (waiting > 0) items.push({ kind: 'decide', count: waiting, href: '#recommendations' });
  }
  if (can('executions.read')) {
    const problems = input.executions.filter(run => {
      const time = Date.parse(run.requestedAtUtc ?? '');
      return PROBLEM_STATUSES.has(run.status ?? '') && Number.isFinite(time) && input.nowUtc - time <= RECENT_MS;
    }).length;
    if (problems > 0) items.push({ kind: 'failedRuns', count: problems, href: '#operations' });
  }
  if (can('assets.read') && input.assets.length === 0) {
    items.push({ kind: 'noAssets', count: 0, href: '#assets' });
  } else if (can('configured-logic.read') && input.configuredLogic.length === 0) {
    items.push({ kind: 'noLogic', count: 0, href: '#logic' });
  }
  if (can('configured-logic.read') && can('executions.read') && input.assets.length > 0 &&
    input.configuredLogic.length > 0) {
    const ran = new Set(input.executions.map(run => run.logicInstanceId));
    const idle = input.configuredLogic.filter(item => item.id && !ran.has(item.id)).length;
    if (idle > 0) items.push({ kind: 'idleLogic', count: idle, href: '#logic' });
  }
  return items;
}

/**
 * A count read from a list that was cut off at `limit` is "at least" that many, so say so instead of
 * presenting the cut-off as the total.
 */
export function cappedCount(loaded: number, limit: number): string {
  return loaded >= limit ? `${limit}+` : String(loaded);
}

/** The assets everyone can see: the owners' own totals when the server gave them, else the capped list. */
export function assetTotalLabel(
  owners: readonly { assetCount: number }[] | null, loaded: number, limit: number,
): string {
  if (owners && owners.length > 0) return String(owners.reduce((sum, owner) => sum + owner.assetCount, 0));
  return cappedCount(loaded, limit);
}
