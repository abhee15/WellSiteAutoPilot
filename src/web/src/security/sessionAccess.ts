import type { CurrentUserResponse } from '../api/generated/v1/types.gen';
import type { Section } from '../components/NavigationLink';
import { isSecurityId } from './identifiers';

export type VerifiedSession = CurrentUserResponse & {
  userId: string; identityName: string; isActive: boolean;
  permissions: string[]; roles: string[]; assetScopeIds: string[];
};
export class SessionAccessError extends Error {}
export function hasPermission(user: CurrentUserResponse | null | undefined, permission: string): boolean {
  return user?.isActive === true && Array.isArray(user.permissions) && user.permissions.includes(permission);
}
export function canOpenSection(user: CurrentUserResponse | null | undefined, section: Section): boolean {
  if (section === 'logic') return hasPermission(user, 'logic.read') || hasPermission(user, 'configured-logic.read');
  const required = { operations: 'executions.read', recommendations: 'recommendations.read',
    assets: 'assets.read', access: 'security.manage' };
  return hasPermission(user, required[section]);
}
export function sessionAccessKey(user: VerifiedSession): string {
  return JSON.stringify([user.userId, [...user.permissions].sort(), [...user.assetScopeIds].sort()]);
}
export async function loadSession(signal: AbortSignal, fetcher: typeof fetch = fetch): Promise<VerifiedSession> {
  const response = await fetcher('/api/v1/security/me', { signal, credentials: 'same-origin', cache: 'no-store' });
  if (!response.ok) throw new SessionAccessError('Session unavailable');
  const body: unknown = await response.json();
  if (!body || typeof body !== 'object' || Array.isArray(body)) throw new SessionAccessError('Invalid session');
  const user = body as Partial<VerifiedSession>;
  const strings = (values: unknown, maximum: number): values is string[] =>
    Array.isArray(values) && values.length <= maximum && values.every(x => typeof x === 'string' && x.length > 0) &&
    new Set(values).size === values.length;
  if (!isSecurityId(user.userId) || typeof user.identityName !== 'string' || !user.identityName.trim() ||
    typeof user.isActive !== 'boolean' || !strings(user.roles, 100) || !strings(user.permissions, 200) ||
    user.permissions.some(p => !/^[a-z][a-z0-9.-]{0,127}$/.test(p)) ||
    !strings(user.assetScopeIds, 500) || user.assetScopeIds.some(id => !isSecurityId(id)))
    throw new SessionAccessError('Invalid session');
  return user as VerifiedSession;
}
