import type { AssetResponse, AssetCreationCapabilitiesResponse, AssetTypeResponse, ConfiguredLogicResponse,
  ExecutionResponse, LogicModuleResponse, SystemInfoResponse } from '../api/generated/v1/types.gen';
import { hasPermission, SessionAccessError } from '../security/sessionAccess';
import type { VerifiedSession } from '../security/sessionAccess';

export type WorkspaceSnapshot = {
  system: SystemInfoResponse | null; executions: ExecutionResponse[]; assetTypes: AssetTypeResponse[];
  assets: AssetResponse[]; capabilities: AssetCreationCapabilitiesResponse | null;
  modules: LogicModuleResponse[]; configurations: ConfiguredLogicResponse[];
  deniedPermissions: string[]; incomplete: boolean;
};
/** Read only permitted areas; a forbidden area cannot discard unrelated authorized work. */
export async function loadWorkspaceSnapshot(user: VerifiedSession, signal: AbortSignal,
  fetcher: typeof fetch = fetch): Promise<WorkspaceSnapshot> {
  const denied = new Set<string>();
  let incomplete = false;
  let sessionExpired = false;
  async function read<T>(permission: string | null, path: string, fallback: T,
    valid: (value: unknown) => boolean): Promise<T> {
    if (!user.isActive || permission && !hasPermission(user, permission)) return fallback;
    try {
      const response = await fetcher(path, { signal, credentials: 'same-origin', cache: 'no-store' });
      if (response.status === 401) { sessionExpired = true; return fallback; }
      if (response.status === 403) {
        if (permission) denied.add(permission); else sessionExpired = true;
        return fallback;
      }
      if (!response.ok) { incomplete = true; return fallback; }
      const value: unknown = await response.json();
      if (!valid(value)) { incomplete = true; return fallback; }
      return value as T;
    } catch (cause) {
      if (signal.aborted) throw cause;
      incomplete = true;
      return fallback;
    }
  }
  const object = (value: unknown) => !!value && typeof value === 'object' && !Array.isArray(value);
  const records = (value: unknown) => Array.isArray(value) && value.every(object);
  const [system, executions, assetTypes, assets, capabilities, modules, configurations] = await Promise.all([
    read<SystemInfoResponse | null>(null, '/api/v1/system/info', null, object),
    read<ExecutionResponse[]>('executions.read', '/api/v1/executions?limit=50', [], records),
    read<AssetTypeResponse[]>('assets.read', '/api/v1/asset-types', [], records),
    read<AssetResponse[]>('assets.read', '/api/v1/assets?limit=100', [], records),
    read<AssetCreationCapabilitiesResponse | null>('assets.read', '/api/v1/assets/capabilities', null,
      value => object(value) && typeof (value as AssetCreationCapabilitiesResponse).canCreateRoot === 'boolean' &&
        typeof (value as AssetCreationCapabilitiesResponse).canCreateUnderAuthorizedParents === 'boolean'),
    read<LogicModuleResponse[]>('logic.read', '/api/v1/logic-modules?limit=100', [], records),
    read<ConfiguredLogicResponse[]>('configured-logic.read', '/api/v1/configured-logic?limit=100', [], records),
  ]);
  if (sessionExpired) throw new SessionAccessError('Session expired');
  // One API family can have multiple reads. A refusal clears that entire
  // family even if another in-flight response succeeded with older access.
  return { system, executions: denied.has('executions.read') ? [] : executions,
    assetTypes: denied.has('assets.read') ? [] : assetTypes, assets: denied.has('assets.read') ? [] : assets,
    capabilities: denied.has('assets.read') ? null : capabilities, modules: denied.has('logic.read') ? [] : modules,
    configurations: denied.has('configured-logic.read') ? [] : configurations,
    deniedPermissions: [...denied], incomplete };
}
