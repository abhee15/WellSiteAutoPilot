import type {
  AssetResponse, AssetTypeResponse, ConfiguredLogicResponse,
  ConfiguredLogicRevisionResponse, ConfiguredLogicRevisionStatus, CreateConfiguredLogicRequest,
  LogicModuleResponse,
} from '../api/generated/v1/types.gen';
import { isSecurityId } from '../security/identifiers';

// `satisfies` has the compiler check each name against the server's contract.
const REVISION_STATUSES: readonly string[] =
  ['Draft', 'Validated', 'Active', 'Superseded'] satisfies ConfiguredLogicRevisionStatus[];

export type AssetRequirement = {
  role: string; minimumCount: number; maximumCount: number;
  requiredAssetTypeKey?: string | null;
};
export type DataRequirement = { id: string; assetRole: string; quantity: string };
export type SetupManifest = {
  description?: string; parameterSchema?: unknown; requiredAdapterKey?: string | null;
  moduleId: string; version: string; executionProfile: string;
  assetRequirements: AssetRequirement[]; dataRequirements: DataRequirement[];
};
export function setupManifest(module: LogicModuleResponse): SetupManifest | null {
  if (module.isEnabled !== true || module.runtime === 'Python' || !module.manifestJson) return null;
  try {
    const value = JSON.parse(module.manifestJson) as SetupManifest;
    if (value.moduleId !== module.moduleId || value.version !== module.version ||
      (value.requiredAdapterKey != null && typeof value.requiredAdapterKey !== 'string') ||
      !Array.isArray(value.assetRequirements) || !Array.isArray(value.dataRequirements) ||
      !value.assetRequirements.every(r => typeof r.role === 'string' &&
        Number.isInteger(r.minimumCount) && r.minimumCount >= 0 &&
        Number.isInteger(r.maximumCount) && r.maximumCount >= r.minimumCount) ||
      !value.dataRequirements.every(r => typeof r.id === 'string' &&
        typeof r.assetRole === 'string' && typeof r.quantity === 'string')) return null;
    return value;
  } catch { return null; }
}
export function eligibleAssets(requirement: AssetRequirement,
  assets: AssetResponse[], types: AssetTypeResponse[]): AssetResponse[] {
  const typeKey = requirement.requiredAssetTypeKey?.toLowerCase();
  return assets.filter(a => a.isActive && isSecurityId(a.id) && (!typeKey ||
    types.some(t => t.id === a.assetTypeId && t.isActive && t.key?.toLowerCase() === typeKey)));
}
/**
 * Starting bindings when setup begins from an asset ("Put logic on this asset"): the asset goes to
 * the first role it is eligible for. The person can still change or remove it; nothing is sent
 * until they save, and the server checks every binding.
 */
export function prefillBindings(manifest: SetupManifest, assetId: string | null | undefined,
  assets: AssetResponse[], types: AssetTypeResponse[]): Record<string, string[]> {
  if (!assetId) return {};
  const role = manifest.assetRequirements.find(r => r.maximumCount >= 1 &&
    eligibleAssets(r, assets, types).some(a => a.id === assetId));
  return role ? { [role.role]: [assetId] } : {};
}
export function isJsonObject(value: string): boolean {
  try {
    const object: unknown = JSON.parse(value);
    return value.length <= 65536 && !!object && typeof object === 'object' && !Array.isArray(object);
  } catch { return false; }
}
export type VerifiedConfiguration = ConfiguredLogicResponse & {
  id: string; name: string; revisions: (ConfiguredLogicRevisionResponse & { id: string })[];
};
export function verifiedConfiguration(value: unknown): value is VerifiedConfiguration {
  if (!value || typeof value !== 'object') return false;
  const v = value as ConfiguredLogicResponse;
  return isSecurityId(v.id) && typeof v.name === 'string' &&
    (v.activeRevisionId === null || isSecurityId(v.activeRevisionId)) &&
    Array.isArray(v.revisions) && v.revisions.length > 0 &&
    v.revisions.every(r => isSecurityId(r.id) &&
      Number.isSafeInteger(r.revisionNumber) && (r.revisionNumber ?? 0) > 0 &&
      REVISION_STATUSES.includes(r.status ?? '') &&
      ['Shadow', 'Recommendation', 'Autonomous'].includes(r.mode ?? '') && (!r.view || (isSecurityId(r.view.viewId) && isSecurityId(r.view.connectionId) &&
        Number.isSafeInteger(r.view.revision) && (r.view.revision ?? 0) > 0 && typeof r.view.role === 'string' && r.view.role.length > 0)) &&
      Array.isArray(r.assetBindings) &&
      r.assetBindings.every(b => isSecurityId(b.assetId)) && Array.isArray(r.dataBindings)) &&
    new Set(v.revisions.map(r => r.id)).size === v.revisions.length &&
    (v.activeRevisionId === null
      ? v.revisions.every(r => r.status !== 'Active')
      : v.revisions.filter(r => r.status === 'Active').length === 1 &&
        v.revisions.some(r => r.id === v.activeRevisionId && r.status === 'Active'));
}
export class SetupApiError extends Error {
  constructor(public readonly kind: 'denied' | 'conflict' | 'invalid' | 'unavailable' | 'uncertain',
    public readonly code?: string) { super(kind); }
}
export async function setupResponse(response: Response, expected: number,
  write: boolean): Promise<unknown> {
  if (response.status === expected) {
    try { return await response.json(); }
    catch { throw new SetupApiError(write ? 'uncertain' : 'unavailable'); }
  }
  const kind = response.status === 401 || response.status === 403 || response.status === 404
    ? 'denied' : response.status === 409 || response.status === 412 ? 'conflict'
      : response.status === 400 ? 'invalid' : write ? 'uncertain' : 'unavailable';
  let code: string | undefined;
  try {
    const problem = await response.json() as { code?: unknown };
    if (typeof problem.code === 'string' && /^[A-Z][A-Z0-9_]{0,99}$/.test(problem.code)) code = problem.code;
  } catch { /* Stable local text, never reflect an arbitrary server payload. */ }
  throw new SetupApiError(kind, code);
}
export async function readConfiguration(id: string, signal: AbortSignal): Promise<VerifiedConfiguration> {
  if (!isSecurityId(id)) throw new SetupApiError('unavailable');
  const value = await setupResponse(await fetch('/api/v1/configured-logic/' + id,
    { signal, credentials: 'same-origin', cache: 'no-store' }), 200, false);
  if (!verifiedConfiguration(value) || value.id !== id) throw new SetupApiError('unavailable');
  return value;
}
// Keep raw JSON in transport. Parsing/re-serializing would silently erase
// duplicate properties which the authoritative server must reject.
export type SetupRequest = CreateConfiguredLogicRequest;

/** Verify immutable content as well as the receipt identity before enabling
 * validation. Ordering and JSON whitespace may be normalized by persistence. */
export function matchesSetupRevision(revision: ConfiguredLogicRevisionResponse, request: SetupRequest): boolean {
  const json = (text: string | null | undefined) => {
    try { return JSON.stringify(JSON.parse(text ?? '')); } catch { return null; }
  };
  const bindings = (items: ConfiguredLogicRevisionResponse['assetBindings']) =>
    items?.map(b => JSON.stringify([b.role, b.assetId, json(b.parameterOverridesJson)])).sort();
  const data = (items: ConfiguredLogicRevisionResponse['dataBindings']) =>
    items?.map(b => JSON.stringify([b.requirementId, b.assetId, b.providerId?.trim(),
      b.providerAssetExternalId?.trim(), json(b.providerMappingJson)])).sort();
  return json(revision.moduleManifestJson) === json(request.moduleManifestJson) &&
    json(revision.parametersJson) === json(request.parametersJson) &&
    JSON.stringify(bindings(revision.assetBindings)) === JSON.stringify(bindings(request.assetBindings)) &&
    JSON.stringify(data(revision.dataBindings)) === JSON.stringify(data(request.dataBindings)) &&
    (!request.view ? !revision.view : !!revision.view && revision.view.viewId === request.view.viewId &&
      revision.view.revision === request.view.expectedRevision && revision.view.role === request.view.role) &&
    (!request.schedule ? !revision.schedule : !!revision.schedule &&
      revision.schedule.enabled === request.schedule.enabled &&
      revision.schedule.cadenceSeconds === request.schedule.cadenceSeconds &&
      Date.parse(revision.schedule.startAtUtc ?? '') === Date.parse(request.schedule.startAtUtc ?? '') &&
      revision.schedule.timeZoneId === request.schedule.timeZoneId);
}
