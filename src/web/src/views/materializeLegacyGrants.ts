export type GrantMigrationReceipt = {
  activeUsersInspected: number;
  activeAssetsInspected: number;
  grantsCreated: number;
  usersAlreadyMatching: number;
  recordedGrantsMatchLegacy: true;
  liveAuthorizationUnchanged: true;
};
export class GrantMigrationError extends Error {
  constructor(public readonly kind:
    'invalid' | 'denied' | 'conflict' | 'unavailable' | 'invalidResponse') {
    super(kind);
  }
}
const count = (n: unknown, max: number): n is number =>
  typeof n === 'number' && Number.isSafeInteger(n) && n >= 0 && n <= max;

/** Exactly one explicit administration request. Uncertain outcomes are NEVER
 * retried automatically: re-read the complete deployment snapshot instead. */
export async function materializeLegacyGrants(
  confirmed: boolean, signal: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<GrantMigrationReceipt> {
  if (!confirmed) throw new GrantMigrationError('invalid');
  let response: Response;
  try {
    response = await fetcher('/api/v1/security/materialize-legacy-grants', {
      method: 'POST', credentials: 'same-origin', cache: 'no-store',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ confirmed: true }), signal,
    });
  } catch (error) {
    if (signal.aborted) throw error;
    throw new GrantMigrationError('unavailable');
  }
  if (!response.ok) {
    throw new GrantMigrationError(
      response.status === 401 || response.status === 403 ? 'denied'
        : response.status === 409 ? 'conflict' : 'unavailable');
  }
  let data: unknown;
  try { data = await response.json(); }
  catch { throw new GrantMigrationError('invalidResponse'); }
  if (!data || typeof data !== 'object' || Array.isArray(data)) {
    throw new GrantMigrationError('invalidResponse');
  }
  const result = data as Partial<GrantMigrationReceipt>;
  if (response.status !== 200 ||
    !count(result.activeUsersInspected, 100) ||
    !count(result.activeAssetsInspected, 2000) ||
    !count(result.grantsCreated, 600) ||
    !count(result.usersAlreadyMatching, 100) ||
    (result.usersAlreadyMatching as number) >
      (result.activeUsersInspected as number) ||
    result.recordedGrantsMatchLegacy !== true ||
    result.liveAuthorizationUnchanged !== true) {
    throw new GrantMigrationError('invalidResponse');
  }
  return result as GrantMigrationReceipt;
}
