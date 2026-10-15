export type PermissionComparison = {
  permission: string;
  scopeKind: 'ResourceScoped' | 'DeploymentWide';
  legacyAllowed: boolean;
  proposedAllowed: boolean;
  proposedReasonCode: string;
  proposedGrantId: string | null;
};
export type EffectiveAccessInspection = {
  userId: string;
  assetId: string | null;
  assetName: string | null;
  permissions: PermissionComparison[];
  wideningCount: number;
  reductionCount: number;
  isDiagnosticOnly: true;
};
export class EffectiveAccessInspectionError extends Error {
  constructor(public readonly kind:
    'denied' | 'notFound' | 'invalid' | 'unavailable' | 'invalidResponse') {
    super(kind);
  }
}
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const key = /^[a-z][a-z0-9.-]{0,127}$/;
function validEntry(item: unknown): item is PermissionComparison {
  if (!item || typeof item !== 'object' || Array.isArray(item)) return false;
  const row = item as Partial<PermissionComparison>;
  return typeof row.permission === 'string' && key.test(row.permission) &&
    (row.scopeKind === 'ResourceScoped' || row.scopeKind === 'DeploymentWide') &&
    typeof row.legacyAllowed === 'boolean' &&
    typeof row.proposedAllowed === 'boolean' &&
    typeof row.proposedReasonCode === 'string' &&
    row.proposedReasonCode.length > 0 && row.proposedReasonCode.length <= 128 &&
    (row.proposedGrantId === null ||
      typeof row.proposedGrantId === 'string' && uuid.test(row.proposedGrantId)) &&
    (row.proposedAllowed ? row.proposedGrantId !== null : row.proposedGrantId === null);
}

/** Inspect only server-computed, non-authoritative diagnostic comparisons. */
export async function loadEffectiveAccess(
  userId: string, assetId: string | null, signal: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<EffectiveAccessInspection> {
  if (!uuid.test(userId) || assetId !== null && !uuid.test(assetId)) {
    throw new EffectiveAccessInspectionError('invalid');
  }
  const url = '/api/v1/security/users/' + encodeURIComponent(userId) +
    '/effective-access' + (assetId ? '?assetId=' + encodeURIComponent(assetId) : '');
  let response: Response;
  try {
    response = await fetcher(url, {
      credentials: 'same-origin', cache: 'no-store', signal,
    });
  } catch (error) {
    if (signal.aborted) throw error;
    throw new EffectiveAccessInspectionError('unavailable');
  }
  if (!response.ok) {
    throw new EffectiveAccessInspectionError(
      response.status === 401 || response.status === 403 ? 'denied'
        : response.status === 404 ? 'notFound' : 'unavailable');
  }
  let data: unknown;
  try { data = await response.json(); }
  catch { throw new EffectiveAccessInspectionError('invalidResponse'); }
  if (!data || typeof data !== 'object' || Array.isArray(data)) {
    throw new EffectiveAccessInspectionError('invalidResponse');
  }
  const row = data as Partial<EffectiveAccessInspection>;
  if (row.isDiagnosticOnly !== true ||
    typeof row.userId !== 'string' ||
    row.userId.toLowerCase() !== userId.toLowerCase() ||
    (assetId === null ? row.assetId !== null || row.assetName !== null :
      typeof row.assetId !== 'string' ||
      row.assetId.toLowerCase() !== assetId.toLowerCase() ||
      typeof row.assetName !== 'string' || !row.assetName.trim()) ||
    !Array.isArray(row.permissions) || row.permissions.length === 0 ||
    row.permissions.length > 200 || !row.permissions.every(validEntry) ||
    (assetId === null && row.permissions.some(p => p.scopeKind === 'ResourceScoped')) ||
    new Set(row.permissions.map(p => p.permission)).size !== row.permissions.length ||
    !Number.isSafeInteger(row.wideningCount) || !Number.isSafeInteger(row.reductionCount) ||
    row.wideningCount !== row.permissions.filter(p =>
      p.proposedAllowed && !p.legacyAllowed).length ||
    row.reductionCount !== row.permissions.filter(p =>
      p.legacyAllowed && !p.proposedAllowed).length) {
    throw new EffectiveAccessInspectionError('invalidResponse');
  }
  return row as EffectiveAccessInspection;
}
