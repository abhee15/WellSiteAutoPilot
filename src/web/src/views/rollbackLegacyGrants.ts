export type GrantRollbackReceipt = {
  migratedGrantsInspected: number;
  grantsDeactivated: number;
  liveAuthorizationUnchanged: true;
};
export class GrantRollbackError extends Error {
  constructor(public readonly kind:
    'invalid' | 'denied' | 'conflict' | 'unavailable' | 'invalidResponse') {
    super(kind);
  }
}
const bounded = (value: unknown, maximum: number): value is number =>
  typeof value === 'number' && Number.isSafeInteger(value) &&
  value >= 0 && value <= maximum;

/** Never retries an uncertain security write or decides which grants to
 * roll back from locally cached UI state: the server uses audit provenance. */
export async function rollbackLegacyGrants(
  confirmed: boolean, signal: AbortSignal, fetcher: typeof fetch = fetch,
): Promise<GrantRollbackReceipt> {
  if (!confirmed) throw new GrantRollbackError('invalid');
  let response: Response;
  try {
    response = await fetcher('/api/v1/security/rollback-legacy-grants', {
      method: 'POST', credentials: 'same-origin', cache: 'no-store',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ confirmed: true }), signal,
    });
  } catch (error) {
    if (signal.aborted) throw error;
    throw new GrantRollbackError('unavailable');
  }
  if (!response.ok) {
    throw new GrantRollbackError(
      response.status === 401 || response.status === 403 ? 'denied'
        : response.status === 409 ? 'conflict' : 'unavailable');
  }
  let body: unknown;
  try { body = await response.json(); }
  catch { throw new GrantRollbackError('invalidResponse'); }
  if (!body || typeof body !== 'object' || Array.isArray(body)) {
    throw new GrantRollbackError('invalidResponse');
  }
  const value = body as Partial<GrantRollbackReceipt>;
  if (response.status !== 200 ||
    !bounded(value.migratedGrantsInspected, 600) ||
    !bounded(value.grantsDeactivated, 600) ||
    (value.grantsDeactivated as number) >
      (value.migratedGrantsInspected as number) ||
    value.liveAuthorizationUnchanged !== true) {
    throw new GrantRollbackError('invalidResponse');
  }
  return value as GrantRollbackReceipt;
}
