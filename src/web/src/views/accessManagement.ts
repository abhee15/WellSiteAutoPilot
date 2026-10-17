import type { SecurityUserResponse } from '../api/generated/v1/types.gen';

export type VerifiedSecurityUser = SecurityUserResponse & {
  userId: string;
  identityName: string;
  isActive: boolean;
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
    typeof user.isActive === 'boolean';
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
