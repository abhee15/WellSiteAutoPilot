import { isWindowsSid } from '../security/windowsSid';
import { isSecurityId as validId } from '../security/identifiers';
import type { AccessGrantResponse } from '../api/generated/v1/types.gen';
import type { GrantDraft } from './scopedAccessGrants';

export type PersistedGrant = AccessGrantResponse & {
  grantId: string;
  subjectKind: 'User' | 'WindowsGroup';
  userId: string | null;
  groupSid: string | null;
  roleId: string;
  scopeKind: GrantDraft['scopeKind'];
  assetIds: string[];
  isActive: boolean;
  revision: number;
};
export class GrantApiError extends Error {
  constructor(public readonly kind:
    'denied' | 'notFound' | 'conflict' | 'invalid' | 'unavailable' | 'invalidResponse') {
    super(kind);
  }
}
function parse(response: unknown): response is PersistedGrant {
  if (!response || typeof response !== 'object' || Array.isArray(response)) return false;
  const p = response as Partial<PersistedGrant>;
  return typeof p.grantId === 'string' && validId(p.grantId) &&
    (p.subjectKind === 'User' ? typeof p.userId === 'string' && validId(p.userId) && p.groupSid === null
      : p.subjectKind === 'WindowsGroup' && p.userId === null && isWindowsSid(p.groupSid) && p.groupSid === p.groupSid.toUpperCase()) && typeof p.roleId === 'string' &&
    validId(p.roleId) && (p.scopeKind === 'ExactAssets' ||
      p.scopeKind === 'DeploymentWide' || p.scopeKind === 'AllAssets') &&
    Array.isArray(p.assetIds) && p.assetIds.every(x =>
      typeof x === 'string' && validId(x)) &&
    new Set(p.assetIds.map(x => x.toLowerCase())).size === p.assetIds.length &&
    (p.scopeKind === 'ExactAssets' ? p.assetIds.length >= 1 && p.assetIds.length <= 500
      : p.assetIds.length === 0) &&
    typeof p.isActive === 'boolean' &&
    Number.isSafeInteger(p.revision) && (p.revision as number) >= 1;
}
function failed(status: number): GrantApiError {
  if (status === 401 || status === 403) return new GrantApiError('denied');
  if (status === 404) return new GrantApiError('notFound');
  if (status === 409) return new GrantApiError('conflict');
  if (status === 400) return new GrantApiError('invalid');
  return new GrantApiError('unavailable');
}
async function payload(response: Response): Promise<unknown> {
  try { return await response.json(); }
  catch { throw new GrantApiError('invalidResponse'); }
}
export function grantSubjectKey(grant: Pick<PersistedGrant, 'userId' | 'groupSid'>): string {
  return grant.groupSid ?? grant.userId ?? '';
}
function subjectPath(key: string): string {
  if (isWindowsSid(key)) return '/api/v1/security/windows-groups/' + encodeURIComponent(key.toUpperCase()) + '/grants';
  if (validId(key)) return '/api/v1/security/users/' + encodeURIComponent(key) + '/grants';
  throw new GrantApiError('invalid');
}
/** Read audited new assignments; these do not yet replace legacy access. */
export async function loadGrants(userId: string, signal: AbortSignal,
  fetcher: typeof fetch = fetch): Promise<PersistedGrant[]> {
  const path = subjectPath(userId);
  let response: Response;
  try {
    response = await fetcher(path, { credentials: 'same-origin', cache: 'no-store', signal });
  } catch (error) {
    if (signal.aborted) throw error;
    throw new GrantApiError('unavailable');
  }
  if (!response.ok) throw failed(response.status);
  const body = await payload(response);
  if (!Array.isArray(body) || body.length > 500 || !body.every(parse) ||
    body.some(row => grantSubjectKey(row).toLowerCase() !== userId.toLowerCase()) ||
    new Set(body.map(row => row.grantId.toLowerCase())).size !== body.length) {
    throw new GrantApiError('invalidResponse');
  }
  return body;
}
/**
 * A single, confirmed POST, never automatically retried after uncertain
 * outcomes. The API is authoritative; success requires matching returned data.
 */
export async function createGrant(draft: GrantDraft, confirmed: boolean,
  signal: AbortSignal, fetcher: typeof fetch = fetch): Promise<PersistedGrant> {
  if (!confirmed || !(draft.subjectKind === 'User' ? validId(draft.userId) && !draft.groupSid
      : draft.subjectKind === 'WindowsGroup' && draft.userId === '' && isWindowsSid(draft.groupSid)) || !validId(draft.roleId) ||
    !Number.isSafeInteger(draft.expectedRoleRevision) || draft.expectedRoleRevision < 1 ||
    !(['ExactAssets', 'DeploymentWide', 'AllAssets'] as const).includes(draft.scopeKind)) {
    throw new GrantApiError('invalid');
  }
  let response: Response;
  try {
    response = await fetcher(subjectPath(draft.groupSid ?? draft.userId), {
      method: 'POST', credentials: 'same-origin', cache: 'no-store',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ roleId: draft.roleId,
        scopeKind: draft.scopeKind, assetIds: draft.assetIds,
        expectedRoleRevision: draft.expectedRoleRevision }),
      signal,
    });
  } catch (error) {
    if (signal.aborted) throw error;
    throw new GrantApiError('unavailable');
  }
  if (!response.ok) throw failed(response.status);
  const saved = await payload(response);
  if (response.status !== 201 || !parse(saved) ||
    grantSubjectKey(saved).toLowerCase() !== (draft.groupSid ?? draft.userId).toLowerCase() ||
    saved.subjectKind !== draft.subjectKind ||
    saved.roleId.toLowerCase() !== draft.roleId.toLowerCase() ||
    saved.scopeKind !== draft.scopeKind || !saved.isActive ||
    saved.assetIds.map(x => x.toLowerCase()).sort().join('|') !==
      draft.assetIds.map(x => x.toLowerCase()).sort().join('|')) {
    throw new GrantApiError('invalidResponse');
  }
  return saved;
}

/**
 * Retire an existing audit-only grant with optimistic concurrency. Never
 * retry a write after an ambiguous network outcome. A confirmed response
 * must identify the same grant and its next inactive revision.
 */
export async function deactivateGrant(grant: PersistedGrant, confirmed: boolean,
  signal: AbortSignal, fetcher: typeof fetch = fetch): Promise<PersistedGrant> {
  if (!confirmed || !parse(grant) || !grant.isActive ||
    grant.revision >= Number.MAX_SAFE_INTEGER) {
    throw new GrantApiError('invalid');
  }
  let response: Response;
  try {
    response = await fetcher(subjectPath(grantSubjectKey(grant)) + '/' +
      encodeURIComponent(grant.grantId) + '/deactivate', {
      method: 'PUT', credentials: 'same-origin', cache: 'no-store',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ expectedRevision: grant.revision }),
      signal,
    });
  } catch (error) {
    if (signal.aborted) throw error;
    throw new GrantApiError('unavailable');
  }
  if (!response.ok) throw failed(response.status);
  const saved = await payload(response);
  if (response.status !== 200 || !parse(saved) || saved.isActive ||
    saved.grantId.toLowerCase() !== grant.grantId.toLowerCase() ||
    grantSubjectKey(saved).toLowerCase() !== grantSubjectKey(grant).toLowerCase() ||
    saved.subjectKind !== grant.subjectKind ||
    saved.roleId.toLowerCase() !== grant.roleId.toLowerCase() ||
    saved.scopeKind !== grant.scopeKind ||
    saved.revision !== grant.revision + 1 ||
    saved.assetIds.map(x => x.toLowerCase()).sort().join('|') !==
      grant.assetIds.map(x => x.toLowerCase()).sort().join('|')) {
    throw new GrantApiError('invalidResponse');
  }
  return saved;
}

/** A successful receipt must agree with the authoritative read after writing. */
export function verifyGrantReadback(expected: PersistedGrant, records: readonly PersistedGrant[]): void {
  const actual = records.find(row => row.grantId.toLowerCase() === expected.grantId.toLowerCase());
  if (!actual || actual.subjectKind !== expected.subjectKind || actual.groupSid !== expected.groupSid ||
    actual.userId?.toLowerCase() !== expected.userId?.toLowerCase() ||
    actual.roleId.toLowerCase() !== expected.roleId.toLowerCase() ||
    actual.scopeKind !== expected.scopeKind || actual.isActive !== expected.isActive ||
    actual.revision !== expected.revision ||
    actual.assetIds.map(id => id.toLowerCase()).sort().join('|') !==
      expected.assetIds.map(id => id.toLowerCase()).sort().join('|')) {
    throw new GrantApiError('invalidResponse');
  }
}
