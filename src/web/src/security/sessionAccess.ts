import type { CurrentUserResponse } from '../api/generated/v1/types.gen';
import type { Section } from '../components/NavigationLink';
import { isSecurityId } from './identifiers';

export type VerifiedSession = CurrentUserResponse & {
  userId: string; identityName: string; isActive: boolean;
  permissions: string[];
};
export class SessionAccessError extends Error {
  readonly status: number | undefined;
  constructor(message: string, status?: number) { super(message); this.status = status; }
}
/**
 * True when the server challenged for Windows credentials and the browser did not
 * supply them. Retrying on a timer cannot fix this and can repeat browser prompts.
 */
export function isSignInRequired(reason: unknown): boolean {
  return reason instanceof SessionAccessError && reason.status === 401;
}
/** An authenticated, active user with no effective permissions is waiting for an administrator to grant access. */
export function isAwaitingAccess(user: CurrentUserResponse | null | undefined): boolean {
  return user?.isActive === true && (user.permissions?.length ?? 0) === 0;
}

export function hasPermission(user: CurrentUserResponse | null | undefined, permission: string): boolean {
  return user?.isActive === true && Array.isArray(user.permissions) && user.permissions.includes(permission);
}
export function canOpenSection(user: CurrentUserResponse | null | undefined, section: Section): boolean {
  if (section === 'overview') {
    return ['executions.read', 'recommendations.read', 'assets.read', 'logic.read', 'configured-logic.read']
      .some(permission => hasPermission(user, permission));
  }
  if (section === 'logic') return hasPermission(user, 'logic.read') || hasPermission(user, 'configured-logic.read');
  if (section === 'status') return hasPermission(user, 'connections.read') || hasPermission(user, 'security.manage');
  const required = { operations: 'executions.read', recommendations: 'recommendations.read',
    assets: 'assets.read', connections: 'connections.read', access: 'security.manage' };
  return hasPermission(user, required[section]);
}
export function sessionAccessKey(user: VerifiedSession): string {
  return JSON.stringify([user.userId, [...user.permissions].sort()]);
}
export async function loadSession(signal: AbortSignal, fetcher: typeof fetch = fetch): Promise<VerifiedSession> {
  const response = await fetcher('/api/v1/security/me', { signal, credentials: 'same-origin', cache: 'no-store' });
  if (!response.ok) throw new SessionAccessError('Session unavailable', response.status);
  const body: unknown = await response.json();
  if (!body || typeof body !== 'object' || Array.isArray(body)) throw new SessionAccessError('Invalid session');
  const user = body as Partial<VerifiedSession>;
  const strings = (values: unknown, maximum: number): values is string[] =>
    Array.isArray(values) && values.length <= maximum && values.every(x => typeof x === 'string' && x.length > 0) &&
    new Set(values).size === values.length;
  if (!isSecurityId(user.userId) || typeof user.identityName !== 'string' || !user.identityName.trim() ||
    typeof user.isActive !== 'boolean' || !strings(user.permissions, 200) ||
    user.permissions.some(p => !/^[a-z][a-z0-9.-]{0,127}$/.test(p)))
    throw new SessionAccessError('Invalid session');
  return { userId: user.userId, identityName: user.identityName, isActive: user.isActive,
    permissions: [...user.permissions], displayName: typeof user.displayName === 'string' ? user.displayName : null };
}
