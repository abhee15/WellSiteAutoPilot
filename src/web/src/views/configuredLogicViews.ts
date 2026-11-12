import type {
  ConfiguredLogicResponse,
  ConfiguredLogicRevisionResponse,
  LogicModuleResponse,
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

/** Generic so a verified configuration keeps its required revision ids. */
export function orderedRevisions<R extends ConfiguredLogicRevisionResponse>(
  item: { revisions?: R[] | null },
): R[] {
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
  moduleNames: ReadonlyMap<string, string> = new Map(),
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
        [revision.moduleId, revision.moduleVersion, moduleNames.get(revision.moduleId ?? '')])),
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

/** An active revision is authoritative. A missing active reference must never look like a draft is live. */
export function configurationRevision(item: ConfiguredLogicResponse): ConfiguredLogicRevisionResponse | null {
  return item.activeRevisionId
    ? item.revisions?.find(revision => revision.id === item.activeRevisionId) ?? null
    : latestRevision(item);
}

export function groupConfigurations(
  configurations: readonly ConfiguredLogicResponse[],
  catalog: ReadonlyMap<string, readonly LogicModuleResponse[]>,
) {
  const groups = new Map<string, {
    key: string; moduleId: string | null; name: string; latestApproved?: string | null;
    deployments: { item: ConfiguredLogicResponse; revision: ConfiguredLogicRevisionResponse | null; assetIds: string[] }[];
    assets: Set<string>; active: number;
  }>();
  for (const item of configurations) {
    const revision = configurationRevision(item);
    const moduleId = revision?.moduleId || null;
    const key = moduleId ? 'module:' + moduleId : 'configuration:' + item.id;
    const versions = moduleId ? catalog.get(moduleId) : undefined;
    const group = groups.get(key) ?? {
      key, moduleId, name: versions?.find(version => version.displayName)?.displayName || moduleId || '',
      latestApproved: versions?.find(version => version.isEnabled === true)?.version,
      deployments: [], assets: new Set<string>(), active: 0,
    };
    const assetIds = [...new Set(revision?.assetBindings?.flatMap(binding => binding.assetId ? [binding.assetId] : []) ?? [])];
    group.deployments.push({ item, revision, assetIds });
    for (const id of assetIds) group.assets.add(id);
    if (item.activeRevisionId && revision) group.active++;
    groups.set(key, group);
  }
  return [...groups.values()];
}
