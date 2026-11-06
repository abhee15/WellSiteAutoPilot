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

export type ModuleVersionGroup = { key: string; versions: LogicModuleResponse[] };

/** Group only the supplied authorized/matching versions; never merge modules by display name. */
export function groupModuleVersions(modules: readonly LogicModuleResponse[]): ModuleVersionGroup[] {
  const groups = new Map<string, LogicModuleResponse[]>();
  for (const module of modules) {
    const key = module.moduleId || module.id;
    if (!key) continue;
    const versions = groups.get(key) ?? [];
    versions.push(module); groups.set(key, versions);
  }
  return [...groups].map(([key, versions]) => ({ key, versions: versions.sort((a, b) =>
    compareModuleVersions(b.version ?? '', a.version ?? '') || (a.id ?? '').localeCompare(b.id ?? '')) }));
}

/** SemVer precedence, including numeric prerelease identifiers; build metadata does not order releases. */
export function compareModuleVersions(left: string, right: string): number {
  const parts = (version: string) => {
    const [core, ...release] = version.split('+')[0].split('-');
    return { core: core.split('.'), pre: release.join('-').split('.').filter(Boolean) };
  };
  const a = parts(left), b = parts(right);
  const numeric = (x: string, y: string) => x.length - y.length || (x < y ? -1 : x > y ? 1 : 0);
  for (let index = 0; index < Math.max(a.core.length, b.core.length); index++) {
    const order = numeric(a.core[index] ?? '0', b.core[index] ?? '0');
    if (order) return order;
  }
  if (!a.pre.length || !b.pre.length) return Number(!a.pre.length) - Number(!b.pre.length);
  for (let index = 0; index < Math.max(a.pre.length, b.pre.length); index++) {
    const x = a.pre[index], y = b.pre[index];
    if (x === undefined || y === undefined) return Number(x !== undefined) - Number(y !== undefined);
    const nx = /^\d+$/.test(x), ny = /^\d+$/.test(y);
    const order = nx && ny ? numeric(x, y) : nx !== ny ? (nx ? -1 : 1) : (x < y ? -1 : x > y ? 1 : 0);
    if (order) return order;
  }
  return 0;
}
