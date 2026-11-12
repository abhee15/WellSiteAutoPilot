import type { AssetResponse, AssetCreationCapabilitiesResponse, AssetTypeResponse, ConfiguredLogicResponse,
  ExecutionResponse, LogicModuleResponse, SystemHealthResponse } from '../api/generated/v1/types.gen';
import { hasPermission, SessionAccessError } from '../security/sessionAccess';
import type { VerifiedSession } from '../security/sessionAccess';

export type WorkspaceSnapshot = {
  system: SystemHealthResponse | null; executions: ExecutionResponse[]; assetTypes: AssetTypeResponse[];
  assets: AssetResponse[]; capabilities: AssetCreationCapabilitiesResponse | null;
  modules: LogicModuleResponse[]; configurations: ConfiguredLogicResponse[];
  deniedPermissions: string[]; incomplete: boolean;
  /** Datasets that could not be read this time (not denied); callers keep their last good copy. */
  unavailable: SnapshotKey[];
};
export type SnapshotKey = 'system' | 'executions' | 'assetTypes' | 'assets' | 'capabilities' | 'modules' | 'configurations';
/** The datasets one change area covers, so a signal refreshes only what it names. */
export const SNAPSHOT_KEYS_BY_AREA = {
  executions: ['executions'],
  assets: ['assetTypes', 'assets', 'capabilities'],
  'configured-logic': ['configurations'],
  modules: ['modules'],
} as const satisfies Record<string, readonly SnapshotKey[]>;
/**
 * Read only permitted areas; a forbidden area cannot discard unrelated authorized work.
 * With `only`, datasets outside it are not requested and come back empty: the caller keeps its copy of them.
 */
export async function loadWorkspaceSnapshot(user: VerifiedSession, signal: AbortSignal,
  fetcher: typeof fetch = fetch, only: ReadonlySet<SnapshotKey> | null = null): Promise<WorkspaceSnapshot> {
  const denied = new Set<string>();
  let incomplete = false;
  const unavailable = new Set<SnapshotKey>();
  let sessionExpired = false;
  async function read<T>(key: SnapshotKey, permission: string | null, path: string, fallback: T,
    valid: (value: unknown) => boolean): Promise<T> {
    if (only && !only.has(key)) return fallback;
    if (!user.isActive || permission && !hasPermission(user, permission)) return fallback;
    try {
      const response = await fetcher(path, { signal, credentials: 'same-origin', cache: 'no-store' });
      if (response.status === 401) { sessionExpired = true; return fallback; }
      if (response.status === 403) {
        if (permission) denied.add(permission); else sessionExpired = true;
        return fallback;
      }
      if (!response.ok) { incomplete = true; unavailable.add(key); return fallback; }
      const value: unknown = await response.json();
      if (!valid(value)) { incomplete = true; unavailable.add(key); return fallback; }
      return value as T;
    } catch (cause) {
      if (signal.aborted) throw cause;
      incomplete = true; unavailable.add(key);
      return fallback;
    }
  }
  const object = (value: unknown) => !!value && typeof value === 'object' && !Array.isArray(value);
  const records = (value: unknown) => Array.isArray(value) && value.every(object);
  async function readCatalog<T extends { id?: string | null }>(
    key: 'modules' | 'configurations', permission: string, path: string,
  ): Promise<T[]> {
    const pageSize = 100;
    const items = new Map<string | T, T>();
    for (let offset = 0; offset <= 100_000; offset += pageSize) {
      const page = await read<T[]>(key, permission, path + '?limit=' + pageSize +
        (offset ? '&offset=' + offset : ''), [], value => records(value) && (value as unknown[]).length <= pageSize);
      if (denied.has(permission) || sessionExpired || unavailable.has(key)) return [];
      for (const item of page) items.set(item.id ?? item, item);
      if (page.length < pageSize) return [...items.values()];
    }
    incomplete = true;
    unavailable.add(key);
    return [];
  }
  const [system, executions, assetTypes, assets, capabilities, modules, configurations] = await Promise.all([
    read<SystemHealthResponse | null>('system', null, '/api/v1/system/health', null, object),
    read<ExecutionResponse[]>('executions', 'executions.read', '/api/v1/executions?limit=50', [], records),
    read<AssetTypeResponse[]>('assetTypes', 'assets.read', '/api/v1/asset-types', [], records),
    read<AssetResponse[]>('assets', 'assets.read', '/api/v1/assets?limit=100', [], records),
    read<AssetCreationCapabilitiesResponse | null>('capabilities', 'assets.read', '/api/v1/assets/capabilities', null,
      value => object(value) && typeof (value as AssetCreationCapabilitiesResponse).canCreateRoot === 'boolean' &&
        typeof (value as AssetCreationCapabilitiesResponse).canCreateUnderAuthorizedParents === 'boolean'),
    readCatalog<LogicModuleResponse>('modules', 'logic.read', '/api/v1/logic-modules'),
    readCatalog<ConfiguredLogicResponse>('configurations', 'configured-logic.read', '/api/v1/configured-logic'),
  ]);
  if (sessionExpired) throw new SessionAccessError('Session expired');
  // One API family can have multiple reads. A refusal clears that entire
  // family even if another in-flight response succeeded with older access.
  return { system, executions: denied.has('executions.read') ? [] : executions,
    assetTypes: denied.has('assets.read') ? [] : assetTypes, assets: denied.has('assets.read') ? [] : assets,
    capabilities: denied.has('assets.read') ? null : capabilities, modules: denied.has('logic.read') ? [] : modules,
    configurations: denied.has('configured-logic.read') ? [] : configurations,
    deniedPermissions: [...denied], incomplete, unavailable: [...unavailable] };
}
