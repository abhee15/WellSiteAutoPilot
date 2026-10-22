import type { ExecutionResponse } from '../api/generated/v1/types.gen';

/** Which kinds of run to show. None chosen means every run. */
export type OperationsStatusFilter = 'active' | 'attention' | 'completed';
export type OperationsSort = 'newest' | 'oldest';
export type OperationsViewCriteria = {
  search: string;
  statuses: readonly OperationsStatusFilter[];
  sort: OperationsSort;
};

export type SavedOperationsView = {
  id: string;
  name: string;
  criteria: OperationsViewCriteria;
};

export const MAX_SAVED_OPERATIONS_VIEWS = 20;
export const DEFAULT_OPERATIONS_VIEW: OperationsViewCriteria = {
  search: '',
  statuses: [],
  sort: 'newest',
};

const activeStates = new Set(['requested', 'queued', 'starting', 'running', 'waiting']);
const attentionStates = new Set(['failed', 'suspended', 'timedout']);
export const OPERATIONS_STATUS_FILTERS: readonly OperationsStatusFilter[] = ['active', 'attention', 'completed'];
const allowedStatus = new Set<string>(OPERATIONS_STATUS_FILTERS);
const allowedSort = new Set<OperationsSort>(['newest', 'oldest']);

/**
 * `names` adds text a person can see for a row but the row itself does not carry, such as the
 * customer's name for the logic or the asset, so searching for what is on screen finds it.
 */
export function filterOperations(
  recentExecutions: readonly ExecutionResponse[],
  criteria: OperationsViewCriteria,
  names?: (row: ExecutionResponse) => readonly (string | null | undefined)[],
): ExecutionResponse[] {
  const query = criteria.search.trim().toLowerCase();
  return recentExecutions.filter(row => {
    const status = row.status?.toLowerCase() ?? '';
    if (criteria.statuses.length > 0 && !criteria.statuses.some(kind =>
      kind === 'active' ? activeStates.has(status) : kind === 'attention' ? attentionStates.has(status) : status === 'completed')) return false;
    if (!query) return true;
    return [
      row.assetExternalId, row.quantity, row.moduleId, row.moduleVersion,
      row.mode, row.status, row.resultCode, row.failureCode, ...(names?.(row) ?? []),
    ].some(value => (value ?? '').toLowerCase().includes(query));
  }).sort((left, right) => {
    const leftTime = Date.parse(left.requestedAtUtc ?? '') || 0;
    const rightTime = Date.parse(right.requestedAtUtc ?? '') || 0;
    const delta = criteria.sort === 'oldest' ? leftTime - rightTime : rightTime - leftTime;
    return delta || (left.executionId ?? '').localeCompare(right.executionId ?? '');
  });
}

export function savedViewsKey(userId: string): string {
  return 'wsa:operations:views:v1:' + encodeURIComponent(userId);
}

/** Reads the chosen statuses; views saved before multi-select kept one value in `status` ('all' meant none). */
function savedStatuses(item: Record<string, unknown>): OperationsStatusFilter[] | null {
  if (Array.isArray(item.statuses)) {
    return item.statuses.every(value => allowedStatus.has(value as string))
      ? OPERATIONS_STATUS_FILTERS.filter(kind => (item.statuses as unknown[]).includes(kind)) : null;
  }
  if (item.status === 'all') return [];
  return allowedStatus.has(item.status as string) ? [item.status as OperationsStatusFilter] : null;
}

function readCriteria(value: unknown): OperationsViewCriteria | null {
  if (typeof value !== 'object' || value === null) return null;
  const item = value as Record<string, unknown>;
  const statuses = savedStatuses(item);
  if (typeof item.search !== 'string' || item.search.length > 120 || !statuses ||
      !allowedSort.has(item.sort as OperationsSort)) return null;
  return { search: item.search, statuses, sort: item.sort as OperationsSort };
}

export function parseSavedOperationsViews(raw: string | null): SavedOperationsView[] {
  if (!raw || raw.length > 50000) return [];
  try {
    const envelope: unknown = JSON.parse(raw);
    if (!envelope || typeof envelope !== 'object') return [];
    const parsed = envelope as { version?: unknown; views?: unknown };
    if (parsed.version !== 1 || !Array.isArray(parsed.views)) return [];
    const views: SavedOperationsView[] = [];
    const ids = new Set<string>();
    const names = new Set<string>();
    for (const candidate of parsed.views) {
      if (!candidate || typeof candidate !== 'object') continue;
      const view = candidate as Partial<SavedOperationsView>;
      if (typeof view.id !== 'string' || !view.id.trim() || view.id.length > 80 ||
          typeof view.name !== 'string' || view.name.trim() !== view.name ||
          view.name.length === 0 || view.name.length > 48 ||
          !readCriteria(view.criteria)) continue;

      const uniqueName = view.name.toLowerCase();
      if (ids.has(view.id) || names.has(uniqueName)) continue;
      ids.add(view.id);
      names.add(uniqueName);
      views.push({
        id: view.id,
        name: view.name,
        criteria: readCriteria(view.criteria)!,
      });
      if (views.length === MAX_SAVED_OPERATIONS_VIEWS) break;
    }
    return views;
  } catch {
    return [];
  }
}

export function loadSavedOperationsViews(
  userId: string | null | undefined,
  storage: Pick<Storage, 'getItem'> | null,
): SavedOperationsView[] {
  if (!userId || !storage) return [];
  try {
    return parseSavedOperationsViews(storage.getItem(savedViewsKey(userId)));
  } catch {
    return [];
  }
}

export function persistSavedOperationsViews(
  userId: string | null | undefined,
  views: readonly SavedOperationsView[],
  storage: Pick<Storage, 'setItem'> | null,
): boolean {
  if (!userId || !storage || views.length > MAX_SAVED_OPERATIONS_VIEWS) return false;
  try {
    storage.setItem(savedViewsKey(userId), JSON.stringify({ version: 1, views }));
    return true;
  } catch {
    return false;
  }
}
