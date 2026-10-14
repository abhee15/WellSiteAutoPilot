import type { SecurityUserResponse } from '../api/generated/v1/types.gen';

export type VerifiedSecurityUser = SecurityUserResponse & {
  userId: string;
  identityName: string;
  isActive: boolean;
  roles: string[];
  assetScopeIds: string[];
};

export const ACCESS_ROLES = ['Admin', 'Engineer', 'Operator'] as const;
export type AccessRole = typeof ACCESS_ROLES[number];
export type AccessErrorKind =
  'accessDenied' | 'notFound' | 'conflict' | 'invalid' | 'invalidResponse' | 'unavailable';

export class AccessError extends Error {
  constructor(public readonly kind: AccessErrorKind) {
    super(kind);
    this.name = 'AccessError';
  }
}

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const allowed = new Set<string>(ACCESS_ROLES);

function isUser(value: unknown): value is VerifiedSecurityUser {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const user = value as Partial<SecurityUserResponse>;
  return typeof user.userId === 'string' && uuid.test(user.userId) &&
    typeof user.identityName === 'string' && !!user.identityName.trim() &&
    typeof user.isActive === 'boolean' &&
    Array.isArray(user.roles) && user.roles.every(role =>
      typeof role === 'string' && allowed.has(role)) &&
    Array.isArray(user.assetScopeIds) && user.assetScopeIds.every(id =>
      typeof id === 'string' && uuid.test(id));
}

function classifyResponse(status: number): AccessError {
  if (status === 401 || status === 403) return new AccessError('accessDenied');
  if (status === 404) return new AccessError('notFound');
  if (status === 409) return new AccessError('conflict');
  if (status === 400) return new AccessError('invalid');
  return new AccessError('unavailable');
}

/** The endpoint is restricted server-side to security.manage (Admin). */
export async function loadSecurityUsers(
  signal: AbortSignal, fetcher: typeof fetch = fetch,
): Promise<VerifiedSecurityUser[]> {
  let response: Response;
  try {
    response = await fetcher('/api/v1/security/users?limit=100', {
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

export function parseScopeIds(raw: string): string[] {
  if (raw.length > 20000) throw new AccessError('invalid');
  const items = raw.split(/[\s,;]+/).filter(Boolean);
  if (items.length > 500 || items.some(id => !uuid.test(id))) {
    throw new AccessError('invalid');
  }
  return [...new Set(items.map(id => id.toLowerCase()))].sort();
}

export function accessChanged(
  user: SecurityUserResponse, roles: readonly AccessRole[], scopes: readonly string[],
): boolean {
  const previousRoles = [...(user.roles ?? [])].sort().join('|');
  const nextRoles = [...roles].sort().join('|');
  const previousScopes = [...(user.assetScopeIds ?? [])].map(x => x.toLowerCase()).sort().join('|');
  const nextScopes = [...scopes].map(x => x.toLowerCase()).sort().join('|');
  return previousRoles !== nextRoles || previousScopes !== nextScopes;
}

/**
 * Submit an explicitly confirmed replacement. There is deliberately no
 * retry: an uncertain write must be resolved by reloading server state.
 */
export async function replaceUserAccess(
  user: SecurityUserResponse,
  currentUserId: string,
  roles: readonly AccessRole[],
  scopeText: string,
  confirmed: boolean,
  signal: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<VerifiedSecurityUser> {
  if (!confirmed || !isUser(user) || !uuid.test(currentUserId) ||
    user.userId.toLowerCase() === currentUserId.toLowerCase() ||
    roles.some(role => !allowed.has(role)) ||
    new Set(roles).size !== roles.length) throw new AccessError('invalid');

  const assetScopeIds = parseScopeIds(scopeText);
  if (!accessChanged(user, roles, assetScopeIds)) throw new AccessError('invalid');
  let response: Response;
  try {
    response = await fetcher(
      '/api/v1/security/users/' + encodeURIComponent(user.userId) + '/access',
      {
        method: 'PUT', credentials: 'same-origin', cache: 'no-store',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ roles, assetScopeIds }), signal,
      },
    );
  } catch (error) {
    if (signal.aborted) throw error;
    throw new AccessError('unavailable');
  }
  if (!response.ok) throw classifyResponse(response.status);
  let body: unknown;
  try { body = await response.json(); }
  catch { throw new AccessError('invalidResponse'); }
  if (!isUser(body) || body.userId.toLowerCase() !== user.userId.toLowerCase() ||
    accessChanged(body, roles, assetScopeIds)) {
    throw new AccessError('invalidResponse');
  }
  return body;
}
