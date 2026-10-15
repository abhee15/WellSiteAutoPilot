import type { LegacyMigrationDifferenceResponse, LegacyMigrationPreflightResponse } from '../api/generated/v1/types.gen';
import { isSecurityId } from '../security/identifiers';
export type MigrationDifference = LegacyMigrationDifferenceResponse & {
  assetId: string | null;
  permission: string;
  legacyAllowed: boolean;
  recordedGrantAllowed: boolean;
};
export type LegacyMigrationPreflight = Omit<LegacyMigrationPreflightResponse, 'differenceSamples'> & {
  userId: string;
  activeAssetCount: number;
  expectedLegacyGrantCount: number;
  recordedGrantCount: number;
  expansionCount: number;
  reductionCount: number;
  differenceSamples: MigrationDifference[];
  recordedGrantsMatchLegacy: boolean;
  isDiagnosticOnly: true;
};
export class LegacyMigrationPreflightError extends Error {
  constructor(public readonly kind: 'invalid' | 'denied' | 'notFound' |
    'unavailable' | 'invalidResponse') {
    super(kind);
  }
}
const key = /^[a-z][a-z0-9.-]{0,127}$/;
function isDifference(x: unknown): x is MigrationDifference {
  if (!x || typeof x !== 'object' || Array.isArray(x)) return false;
  const p = x as Partial<MigrationDifference>;
  return (p.assetId === null ||
      typeof p.assetId === 'string' && isSecurityId(p.assetId)) &&
    typeof p.permission === 'string' && key.test(p.permission) &&
    typeof p.legacyAllowed === 'boolean' &&
    typeof p.recordedGrantAllowed === 'boolean' &&
    p.legacyAllowed !== p.recordedGrantAllowed;
}

export async function loadLegacyMigrationPreflight(
  userId: string, signal: AbortSignal, fetcher: typeof fetch = fetch,
): Promise<LegacyMigrationPreflight> {
  if (!isSecurityId(userId)) throw new LegacyMigrationPreflightError('invalid');
  let response: Response;
  try {
    response = await fetcher('/api/v1/security/users/' +
      encodeURIComponent(userId) + '/migration-preflight', {
      credentials: 'same-origin', cache: 'no-store', signal,
    });
  } catch (error) {
    if (signal.aborted) throw error;
    throw new LegacyMigrationPreflightError('unavailable');
  }
  if (!response.ok) {
    throw new LegacyMigrationPreflightError(
      response.status === 401 || response.status === 403 ? 'denied'
        : response.status === 404 ? 'notFound' : 'unavailable');
  }
  let body: unknown;
  try { body = await response.json(); }
  catch { throw new LegacyMigrationPreflightError('invalidResponse'); }
  if (!body || typeof body !== 'object' || Array.isArray(body)) {
    throw new LegacyMigrationPreflightError('invalidResponse');
  }
  const data = body as Partial<LegacyMigrationPreflight>;
  if (data.isDiagnosticOnly !== true ||
    typeof data.userId !== 'string' ||
    data.userId.toLowerCase() !== userId.toLowerCase() ||
    !Number.isSafeInteger(data.activeAssetCount) ||
    (data.activeAssetCount as number) < 0 ||
    (data.activeAssetCount as number) > 2000 ||
    !Number.isSafeInteger(data.expectedLegacyGrantCount) ||
    (data.expectedLegacyGrantCount as number) < 0 ||
    (data.expectedLegacyGrantCount as number) > 6 ||
    !Number.isSafeInteger(data.recordedGrantCount) ||
    (data.recordedGrantCount as number) < 0 ||
    (data.recordedGrantCount as number) > 500 ||
    !Number.isSafeInteger(data.expansionCount) ||
    (data.expansionCount as number) < 0 ||
    !Number.isSafeInteger(data.reductionCount) ||
    (data.reductionCount as number) < 0 ||
    typeof data.recordedGrantsMatchLegacy !== 'boolean' ||
    data.recordedGrantsMatchLegacy !==
      (data.expansionCount === 0 && data.reductionCount === 0) ||
    !Array.isArray(data.differenceSamples) ||
    data.differenceSamples.length > 20 ||
    !data.differenceSamples.every(isDifference) ||
    (data.expansionCount as number) + (data.reductionCount as number) <
      data.differenceSamples.length ||
    new Set(data.differenceSamples.map(x =>
      (x.assetId?.toLowerCase() ?? 'global') + '|' + x.permission)).size !==
      data.differenceSamples.length) {
    throw new LegacyMigrationPreflightError('invalidResponse');
  }
  return data as LegacyMigrationPreflight;
}
