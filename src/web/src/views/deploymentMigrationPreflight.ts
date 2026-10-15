export type DeploymentUserParity = {
  userId: string;
  expectedLegacyGrantCount: number;
  recordedGrantCount: number;
  expansionCount: number;
  reductionCount: number;
  recordedGrantsMatchLegacy: boolean;
};
export type DeploymentParity = {
  activeUserCount: number;
  activeAssetCount: number;
  matchingUserCount: number;
  expansionCount: number;
  reductionCount: number;
  users: DeploymentUserParity[];
  allActiveUsersMatchRecordedGrants: boolean;
  isDiagnosticOnly: true;
};
export class DeploymentParityError extends Error {
  constructor(public readonly kind: 'denied' | 'unavailable' | 'invalidResponse') {
    super(kind);
  }
}
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function boundedInt(v: unknown, max: number): v is number {
  return typeof v === 'number' && Number.isSafeInteger(v) && v >= 0 && v <= max;
}
function validUser(v: unknown): v is DeploymentUserParity {
  if (!v || typeof v !== 'object' || Array.isArray(v)) return false;
  const r = v as Partial<DeploymentUserParity>;
  return typeof r.userId === 'string' && guid.test(r.userId) &&
    boundedInt(r.expectedLegacyGrantCount, 6) &&
    boundedInt(r.recordedGrantCount, 500) &&
    boundedInt(r.expansionCount, 1000000) &&
    boundedInt(r.reductionCount, 1000000) &&
    typeof r.recordedGrantsMatchLegacy === 'boolean' &&
    r.recordedGrantsMatchLegacy ===
      (r.expansionCount === 0 && r.reductionCount === 0);
}

/** One complete server-side database snapshot, not client-side union of users. */
export async function loadDeploymentParity(
  signal: AbortSignal, fetcher: typeof fetch = fetch,
): Promise<DeploymentParity> {
  let response: Response;
  try {
    response = await fetcher('/api/v1/security/migration-preflight',
      { credentials: 'same-origin', cache: 'no-store', signal });
  } catch (error) {
    if (signal.aborted) throw error;
    throw new DeploymentParityError('unavailable');
  }
  if (!response.ok) throw new DeploymentParityError(
    response.status === 401 || response.status === 403 ? 'denied' : 'unavailable');
  let body: unknown;
  try { body = await response.json(); }
  catch { throw new DeploymentParityError('invalidResponse'); }
  if (!body || typeof body !== 'object' || Array.isArray(body)) {
    throw new DeploymentParityError('invalidResponse');
  }
  const r = body as Partial<DeploymentParity>;
  if (r.isDiagnosticOnly !== true ||
    !boundedInt(r.activeUserCount, 100) ||
    !boundedInt(r.activeAssetCount, 2000) ||
    !boundedInt(r.matchingUserCount, 100) ||
    !boundedInt(r.expansionCount, 100000000) ||
    !boundedInt(r.reductionCount, 100000000) ||
    !Array.isArray(r.users) || r.users.length !== r.activeUserCount ||
    !r.users.every(validUser) ||
    new Set(r.users.map(x => x.userId.toLowerCase())).size !== r.users.length ||
    r.matchingUserCount !== r.users.filter(x => x.recordedGrantsMatchLegacy).length ||
    r.expansionCount !== r.users.reduce((a, x) => a + x.expansionCount, 0) ||
    r.reductionCount !== r.users.reduce((a, x) => a + x.reductionCount, 0) ||
    typeof r.allActiveUsersMatchRecordedGrants !== 'boolean' ||
    r.allActiveUsersMatchRecordedGrants !==
      (r.matchingUserCount === r.activeUserCount)) {
    throw new DeploymentParityError('invalidResponse');
  }
  return r as DeploymentParity;
}
