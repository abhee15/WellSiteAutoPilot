import type {
  ConfiguredLogicResponse,
  ConfiguredLogicRevisionResponse,
} from '../api/generated/v1/types.gen';

export type ConfiguredLogicCriteria = {
  search: string;
  lifecycle: 'all' | 'active' | 'inactive';
  revisionStatus: 'all' | 'draft' | 'validated' | 'active' | 'superseded';
  sort: 'name' | 'newest' | 'oldest';
};

export const DEFAULT_CONFIGURED_LOGIC_CRITERIA: ConfiguredLogicCriteria = {
  search: '',
  lifecycle: 'all',
  revisionStatus: 'all',
  sort: 'name',
};

export function orderedRevisions(
  item: ConfiguredLogicResponse,
): ConfiguredLogicRevisionResponse[] {
  return [...(item.revisions ?? [])].sort((left, right) => {
    const byNumber = (right.revisionNumber ?? 0) - (left.revisionNumber ?? 0);
    return byNumber || (left.id ?? '').localeCompare(right.id ?? '');
  });
}

export function latestRevision(
  item: ConfiguredLogicResponse,
): ConfiguredLogicRevisionResponse | null {
  return orderedRevisions(item)[0] ?? null;
}

/**
 * Only filter the server-authorized collection. The client must never expand
 * RBAC or asset scope, infer permissions, or present this as a full-site search.
 */
export function filterConfiguredLogic(
  authorizedItems: readonly ConfiguredLogicResponse[],
  criteria: ConfiguredLogicCriteria,
): ConfiguredLogicResponse[] {
  const query = criteria.search.trim().toLocaleLowerCase();
  return authorizedItems.filter(item => {
    if (criteria.lifecycle === 'active' && !item.activeRevisionId) return false;
    if (criteria.lifecycle === 'inactive' && item.activeRevisionId) return false;

    const latest = latestRevision(item);
    if (criteria.revisionStatus !== 'all' &&
      latest?.status?.toLocaleLowerCase() !== criteria.revisionStatus) return false;

    if (!query) return true;
    const textFields = [
      item.name, item.id,
      ...((item.revisions ?? []).flatMap(revision =>
        [revision.moduleId, revision.moduleVersion])),
    ];
    return textFields.some(value => (value ?? '').toLocaleLowerCase().includes(query));
  }).sort((left, right) => {
    if (criteria.sort === 'name') {
      const names = (left.name ?? '').localeCompare(right.name ?? '');
      return names || (left.id ?? '').localeCompare(right.id ?? '');
    }
    const leftAt = Date.parse(left.createdAtUtc ?? '') || 0;
    const rightAt = Date.parse(right.createdAtUtc ?? '') || 0;
    const time = criteria.sort === 'newest' ? rightAt - leftAt : leftAt - rightAt;
    return time || (left.id ?? '').localeCompare(right.id ?? '');
  });
}

/** The status the list shows: the live revision's when there is one, otherwise the newest revision's. */
export function listedStatus(item: ConfiguredLogicResponse): string {
  const live = (item.revisions ?? []).find(revision => revision.id === item.activeRevisionId);
  return live?.status ?? latestRevision(item)?.status ?? 'Draft';
}
