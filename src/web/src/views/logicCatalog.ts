import type { LogicModuleResponse, LogicRuntimeKind } from '../api/generated/v1/types.gen';

export type LogicCatalogCriteria = {
  search: string;
  eligibility: 'all' | 'enabled' | 'disabled';
  runtime: string;
  sort: 'name' | 'newest' | 'oldest';
};

export const DEFAULT_LOGIC_CATALOG_CRITERIA: LogicCatalogCriteria = {
  search: '', eligibility: 'all', runtime: 'all', sort: 'name',
};

// Filter only the module catalog returned by the API. Permission checks,
// trust verification, and execution eligibility remain server-authoritative.
export function filterLogicCatalog(
  authorizedModules: readonly LogicModuleResponse[],
  criteria: LogicCatalogCriteria,
): LogicModuleResponse[] {
  const query = criteria.search.trim().toLocaleLowerCase();
  return authorizedModules.filter(module => {
    if (criteria.eligibility === 'enabled' && !module.isEnabled) return false;
    if (criteria.eligibility === 'disabled' && module.isEnabled) return false;
    if (criteria.runtime !== 'all' && module.runtime !== criteria.runtime) return false;
    if (!query) return true;
    return [module.displayName, module.moduleId, module.version,
      module.publisher, module.runtime, module.executionProfile]
      .some(value => (value ?? '').toLocaleLowerCase().includes(query));
  }).sort((left, right) => {
    if (criteria.sort === 'name') {
      const byName = (left.displayName ?? '').localeCompare(right.displayName ?? '');
      return byName || (left.id ?? '').localeCompare(right.id ?? '');
    }
    const first = Date.parse(left.installedAtUtc ?? '') || 0;
    const second = Date.parse(right.installedAtUtc ?? '') || 0;
    const order = criteria.sort === 'oldest' ? first - second : second - first;
    return order || (left.id ?? '').localeCompare(right.id ?? '');
  });
}

export function availableLogicRuntimes(
  authorizedModules: readonly LogicModuleResponse[],
): LogicRuntimeKind[] {
  return [...new Set(authorizedModules
    .map(module => module.runtime)
    .filter((runtime): runtime is LogicRuntimeKind => typeof runtime === 'string' && !!runtime))]
    .sort((left, right) => left.localeCompare(right));
}
