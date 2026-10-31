import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { URL, fileURLToPath } from 'node:url';

// The web app gates screens and actions on permission strings that the server defines. A typo or a renamed permission
// fails silently (the control just never appears), so every permission the web app checks must exist on the server.
const srcRoot = fileURLToPath(new URL('../src/', import.meta.url));
const serverFile = fileURLToPath(new URL(
  '../../server/WellSiteAutoPilot.Application/Security/WellSitePermissions.cs', import.meta.url));
const roleMapFile = fileURLToPath(new URL(
  '../../server/WellSiteAutoPilot.Application/Security/RolePermissionMap.cs', import.meta.url));

function walk(dir) {
  return readdirSync(dir).flatMap(name => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? (name === 'generated' ? [] : walk(path)) : [path];
  });
}

const catalog = existsSync(serverFile)
  ? [...readFileSync(serverFile, 'utf8').matchAll(/public const string \w+ = "([^"]+)";/g)].map(m => m[1])
  : null;

test('the server permission catalog can be read from this checkout', { skip: catalog === null }, () => {
  assert.ok(catalog.length >= 10, 'expected the permission constants in WellSitePermissions.cs');
});

test('every permission the web app checks exists on the server', { skip: catalog === null }, () => {
  const used = new Map();
  const patterns = [
    /hasPermission\([^,()]+,\s*'([^']+)'\)/g,
    /permissions\??\.includes\('([^']+)'\)/g,
    /\bcan\('([^']+)'\)/g,
  ];
  for (const path of walk(srcRoot).filter(p => /\.(ts|tsx)$/.test(p))) {
    const text = readFileSync(path, 'utf8');
    for (const pattern of patterns) for (const [, permission] of text.matchAll(pattern)) used.set(permission, path);
    // canOpenSection lists its permissions in a table.
    if (path.endsWith('sessionAccess.ts')) {
      for (const [, permission] of text.matchAll(/'([a-z][a-z-]*\.[a-z]+)'/g)) used.set(permission, path);
    }
  }
  assert.ok(used.size >= 10, `only found ${used.size} permission checks; the patterns may be stale`);
  const unknown = [...used.keys()].filter(permission => !catalog.includes(permission));
  assert.deepEqual(unknown, [], 'the web app checks permissions the server does not define');
});

test('the built-in roles are readable so tests can use the real permission sets', { skip: catalog === null || !existsSync(roleMapFile) }, () => {
  const text = readFileSync(roleMapFile, 'utf8');
  for (const role of ['Admin', 'Engineer', 'Operator']) assert.match(text, new RegExp(`ApplicationRole\\.${role}`));
});
