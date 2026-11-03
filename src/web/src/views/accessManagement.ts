import type { SecurityUserResponse } from '../api/generated/v1/types.gen';

export type VerifiedSecurityUser = SecurityUserResponse & {
  userId: string;
  identityName: string;
  isActive: boolean;
  activeDirectGrantCount: number;
};

export type AccessErrorKind =
  'accessDenied' | 'notFound' | 'conflict' | 'invalid' | 'invalidResponse' | 'unavailable';

export class AccessError extends Error {
  constructor(public readonly kind: AccessErrorKind) {
    super(kind);
    this.name = 'AccessError';
  }
}

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isUser(value: unknown): value is VerifiedSecurityUser {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const user = value as Partial<SecurityUserResponse>;
  return typeof user.userId === 'string' && uuid.test(user.userId) &&
    typeof user.identityName === 'string' && !!user.identityName.trim() &&
    typeof user.isActive === 'boolean' &&
    typeof user.activeDirectGrantCount === 'number' &&
    Number.isInteger(user.activeDirectGrantCount) && user.activeDirectGrantCount >= 0;
}

function classifyResponse(status: number): AccessError {
  if (status === 401 || status === 403) return new AccessError('accessDenied');
  if (status === 404) return new AccessError('notFound');
  if (status === 409) return new AccessError('conflict');
  if (status === 400) return new AccessError('invalid');
  return new AccessError('unavailable');
}

export const awaitingAccessQuery = '&awaitingAccess=true';

/**
 * The endpoint is restricted server-side to security.manage (Admin). With `awaitingAccessOnly`
 * the server returns active users with no direct role, newest sign-in first.
 */
export async function loadSecurityUsers(
  signal: AbortSignal, fetcher: typeof fetch = fetch,
  options: { awaitingAccessOnly?: boolean } = {},
): Promise<VerifiedSecurityUser[]> {
  let response: Response;
  try {
    response = await fetcher(`/api/v1/security/users?limit=100${options.awaitingAccessOnly ? awaitingAccessQuery : ''}`, {
      method: 'GET', credentials: 'same-origin', cache: 'no-store', signal,
    });
  } catch (error) {
    if (signal.aborted) throw error;
    throw new AccessError('unavailable');
  }
  if (!response.ok) throw classifyResponse(response.status);
  let body: unknown;
  try { body = await response.json(); }
  catch { throw new AccessError('invalidResponse'); }
  if (!Array.isArray(body) || body.length > 100 || !body.every(isUser)) {
    throw new AccessError('invalidResponse');
  }
  const ids = body.map(item => item.userId.toLowerCase());
  if (new Set(ids).size !== ids.length) throw new AccessError('invalidResponse');
  return body;
}

export type AccountStatusFilter = 'all' | 'active' | 'inactive';
export type DirectAssignmentFilter = 'all' | 'with' | 'without';
export type UserDirectoryFilters = {
  search: string;
  accountStatus: AccountStatusFilter;
  directAssignments: DirectAssignmentFilter;
};
export type SecurityUserPage = { items: VerifiedSecurityUser[]; nextCursor: string | null };
export const USER_DIRECTORY_PAGE_SIZE = 50;
export const DEFAULT_USER_DIRECTORY_FILTERS: UserDirectoryFilters = {
  search: '', accountStatus: 'all', directAssignments: 'all',
};
const directoryCursorPattern = /^[A-Za-z0-9_-]{1,128}$/;

/** Every filter is sent to the server before the page limit; never search a partial client snapshot. */
export function userDirectoryQuery(filters: UserDirectoryFilters, cursor: string | null): string {
  const search = filters.search.trim();
  if (search.length > 100 || Array.from(search).some(char => char.charCodeAt(0) < 32) ||
    !['all', 'active', 'inactive'].includes(filters.accountStatus) ||
    !['all', 'with', 'without'].includes(filters.directAssignments) ||
    (cursor !== null && !directoryCursorPattern.test(cursor))) {
    throw new AccessError('invalid');
  }
  const params = new URLSearchParams({ limit: String(USER_DIRECTORY_PAGE_SIZE) });
  if (search) params.set('search', search);
  if (filters.accountStatus !== 'all') params.set('accountStatus', filters.accountStatus);
  if (filters.directAssignments !== 'all') params.set('directAssignments', filters.directAssignments);
  if (cursor) params.set('cursor', cursor);
  return params.toString();
}

/** Protected, bounded page. The cursor comes only from the server and is never inferred from a row. */
export async function loadSecurityUserPage(
  filters: UserDirectoryFilters, cursor: string | null, signal: AbortSignal, fetcher: typeof fetch = fetch,
): Promise<SecurityUserPage> {
  const query = userDirectoryQuery(filters, cursor);
  let response: Response;
  try {
    response = await fetcher('/api/v1/security/users/page?' + query,
      { method: 'GET', credentials: 'same-origin', cache: 'no-store', signal });
  } catch (error) {
    if (signal.aborted) throw error;
    throw new AccessError('unavailable');
  }
  if (!response.ok) throw classifyResponse(response.status);
  let body: unknown;
  try { body = await response.json(); }
  catch { throw new AccessError('invalidResponse'); }
  if (!body || typeof body !== 'object' || Array.isArray(body)) throw new AccessError('invalidResponse');
  const page = body as Partial<SecurityUserPage>;
  if (!Array.isArray(page.items) || page.items.length > USER_DIRECTORY_PAGE_SIZE ||
      !page.items.every(isUser) ||
      !(page.nextCursor === null ||
        (typeof page.nextCursor === 'string' && directoryCursorPattern.test(page.nextCursor))) ||
      (page.nextCursor !== null && page.items.length !== USER_DIRECTORY_PAGE_SIZE) ||
      new Set(page.items.map(item => item.userId.toLowerCase())).size !== page.items.length ||
      (cursor !== null && cursor === page.nextCursor)) {
    throw new AccessError('invalidResponse');
  }
  return { items: page.items, nextCursor: page.nextCursor };
}
