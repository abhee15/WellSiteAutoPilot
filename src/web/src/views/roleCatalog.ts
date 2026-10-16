import { readSecurityRefusal } from '../security/securityRefusal';
export type RoleRecord = {
  roleId: string;
  key: string;
  name: string;
  description: string;
  isSystem: boolean;
  isActive: boolean;
  revision: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  permissions: string[];
};

export type PermissionDefinition = {
  key: string;
  category: string;
  scopeKind: 'ResourceScoped' | 'DeploymentWide';
  isPrivileged: boolean;
};

export type RoleMutation = {
  name: string;
  description: string;
  permissions: string[];
  isActive: boolean;
};

export type RoleErrorKind = 'accessDenied' | 'notFound' | 'conflict' | 'selfAccess' | 'groupProofRequired' |
  'invalid' | 'invalidResponse' | 'unavailable';

export class RoleCatalogError extends Error {
  constructor(public readonly kind: RoleErrorKind) {
    super(kind);
    this.name = 'RoleCatalogError';
  }
}

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const permissionPattern = /^[a-z][a-z0-9.-]{0,127}$/;

function isObject(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}

function isRole(value: unknown): value is RoleRecord {
  if (!isObject(value)) return false;
  return typeof value.roleId === 'string' && uuid.test(value.roleId) &&
    typeof value.key === 'string' && value.key.length > 0 &&
    typeof value.name === 'string' && value.name.trim().length > 0 &&
    value.name.length <= 80 && typeof value.description === 'string' &&
    value.description.length <= 500 &&
    typeof value.isSystem === 'boolean' && typeof value.isActive === 'boolean' &&
    Number.isSafeInteger(value.revision) && (value.revision as number) >= 1 &&
    typeof value.createdAtUtc === 'string' && typeof value.updatedAtUtc === 'string' &&
    Array.isArray(value.permissions) &&
    value.permissions.every(x => typeof x === 'string' && permissionPattern.test(x)) &&
    new Set(value.permissions).size === value.permissions.length;
}

function isPermission(value: unknown): value is PermissionDefinition {
  if (!isObject(value)) return false;
  return typeof value.key === 'string' && permissionPattern.test(value.key) &&
    typeof value.category === 'string' && value.category.length > 0 &&
    (value.scopeKind === 'ResourceScoped' || value.scopeKind === 'DeploymentWide') &&
    typeof value.isPrivileged === 'boolean';
}

function classify(status: number): RoleCatalogError {
  if (status === 401 || status === 403) return new RoleCatalogError('accessDenied');
  if (status === 404) return new RoleCatalogError('notFound');
  if (status === 409) return new RoleCatalogError('conflict');
  if (status === 400) return new RoleCatalogError('invalid');
  return new RoleCatalogError('unavailable');
}

async function safeJson(response: Response): Promise<unknown> {
  try { return await response.json(); }
  catch { throw new RoleCatalogError('invalidResponse'); }
}

export async function loadRoleCatalog(
  signal: AbortSignal, fetcher: typeof fetch = fetch,
): Promise<{ roles: RoleRecord[]; permissions: PermissionDefinition[] }> {
  let results: Response[];
  try {
    results = await Promise.all([
      fetcher('/api/v1/security/roles?limit=100',
        { credentials: 'same-origin', cache: 'no-store', signal }),
      fetcher('/api/v1/security/role-permissions',
        { credentials: 'same-origin', cache: 'no-store', signal }),
    ]);
  } catch (error) {
    if (signal.aborted) throw error;
    throw new RoleCatalogError('unavailable');
  }

  for (const response of results) if (!response.ok) throw classify(response.status);
  const [rawRoles, rawPermissions] = await Promise.all(results.map(safeJson));
  if (!Array.isArray(rawRoles) || rawRoles.length > 100 || !rawRoles.every(isRole) ||
    !Array.isArray(rawPermissions) || !rawPermissions.every(isPermission) ||
    rawPermissions.length > 200) throw new RoleCatalogError('invalidResponse');

  const roleIds = rawRoles.map(role => role.roleId.toLowerCase());
  const permissionKeys = rawPermissions.map(item => item.key);
  const known = new Set(permissionKeys);
  if (new Set(roleIds).size !== roleIds.length ||
    new Set(permissionKeys).size !== permissionKeys.length ||
    rawRoles.some(role => role.permissions.some(permission => !known.has(permission)))) {
    throw new RoleCatalogError('invalidResponse');
  }
  return { roles: rawRoles, permissions: rawPermissions };
}

export function roleChanged(role: RoleRecord, next: RoleMutation): boolean {
  return role.name !== next.name.trim() || role.description !== next.description.trim() ||
    role.isActive !== next.isActive ||
    [...role.permissions].sort().join('|') !== [...next.permissions].sort().join('|');
}

function hasAsciiControls(value: string): boolean {
  return [...value].some(character => character.charCodeAt(0) < 0x20);
}

export function validateRoleMutation(
  mutation: RoleMutation, allowedPermissions: readonly string[],
): RoleMutation {
  const name = mutation.name.trim();
  const description = mutation.description.trim();
  const permissions = [...new Set(mutation.permissions)].sort();
  const allowed = new Set(allowedPermissions);
  if (name.length < 2 || name.length > 80 || hasAsciiControls(name) ||
    /^(admin|engineer|operator)$/i.test(name) ||
    description.length > 500 || hasAsciiControls(description) ||
    permissions.length !== mutation.permissions.length ||
    permissions.some(p => !allowed.has(p))) throw new RoleCatalogError('invalid');
  return { name, description, permissions, isActive: mutation.isActive };
}

/** A role write is never retried automatically after an uncertain outcome. */
export async function saveRole(
  selected: RoleRecord | null,
  mutation: RoleMutation,
  allowedPermissions: readonly string[],
  confirmed: boolean,
  signal: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<RoleRecord> {
  if (!confirmed || selected?.isSystem) throw new RoleCatalogError('invalid');
  const next = validateRoleMutation(mutation, allowedPermissions);
  if (selected && (!selected.isActive && next.isActive && next.permissions.length === 0 ||
    !roleChanged(selected, next))) throw new RoleCatalogError('invalid');

  const url = selected
    ? '/api/v1/security/roles/' + encodeURIComponent(selected.roleId)
    : '/api/v1/security/roles';
  const method = selected ? 'PUT' : 'POST';
  const body = selected
    ? { ...next, expectedRevision: selected.revision }
    : { name: next.name, description: next.description, permissions: next.permissions };
  let response: Response;
  try {
    response = await fetcher(url, {
      method, credentials: 'same-origin', cache: 'no-store',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body), signal,
    });
  } catch (error) {
    if (signal.aborted) throw error;
    throw new RoleCatalogError('unavailable');
  }
  if (!response.ok) {
    const refusal = await readSecurityRefusal(response);
    throw refusal && refusal !== 'lastDirectAdmin'
      ? new RoleCatalogError(refusal) : classify(response.status);
  }

  const result = await safeJson(response);
  if (!isRole(result) ||
    (selected && (result.roleId.toLowerCase() !== selected.roleId.toLowerCase() ||
      result.revision !== selected.revision + 1)) ||
    result.name !== next.name ||
    result.description !== next.description ||
    result.isActive !== (selected ? next.isActive : true) ||
    [...result.permissions].sort().join('|') !== next.permissions.join('|')) {
    throw new RoleCatalogError('invalidResponse');
  }
  return result;
}
