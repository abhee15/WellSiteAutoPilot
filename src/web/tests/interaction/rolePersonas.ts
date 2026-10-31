import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

/**
 * The built-in roles as the server defines them, read from the server source so a change to a role's permissions is
 * exercised here without anyone remembering to update a copy. Custom roles are written out where a test needs them.
 */
// Tests run from src/web, so the server source is two folders up.
const dir = resolve(process.cwd(), '../server/WellSiteAutoPilot.Application/Security');
const read = (name: string) => readFileSync(resolve(dir, name), 'utf8');

const constants = new Map<string, string>(
  [...read('WellSitePermissions.cs').matchAll(/public const string (\w+) = "([^"]+)";/g)].map(m => [m[1]!, m[2]!]));
export const allPermissions: string[] = [...constants.values()];

function permissionsOf(role: string): string[] {
  if (role === 'Admin') return allPermissions;
  const map = read('RolePermissionMap.cs');
  const start = map.indexOf(`[ApplicationRole.${role}]`);
  const end = map.indexOf('StringComparer.Ordinal', start);
  return [...map.slice(start, end).matchAll(/WellSitePermissions\.(\w+)/g)].map(m => constants.get(m[1]!)!);
}

export const personas: Record<string, string[]> = {
  Admin: permissionsOf('Admin'),
  Engineer: permissionsOf('Engineer'),
  Operator: permissionsOf('Operator'),
  // Custom roles an administrator might build: read-only slices, and a manager with no read access to what it manages.
  'Asset reader': ['assets.read'],
  'Run viewer': ['executions.read'],
  'Logic catalog reader': ['logic.read'],
  'Connection manager': ['connections.read', 'connections.manage'],
  // Views belong to Connections, so creating one needs to read them too: these two sit on either side of that rule.
  'Connection reader': ['assets.read', 'connections.read'],
  'View author without connections': ['assets.read', 'asset-views.create'],
  'View author': ['assets.read', 'connections.read', 'asset-views.create'],
  'Decide without read': ['recommendations.decide'],
  'Security only': ['security.manage'],
};
