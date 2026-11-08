import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { URL, fileURLToPath } from 'node:url';
import { check, resolveConfig } from 'prettier';

// Prettier cannot reproduce this codebase's existing layout (it always breaks a JSX element that has an expression child
// onto several lines, and the code keeps them inline), so formatting the whole tree, or every file a change touches, would
// rewrite almost every line. Instead: every file that is NOT in the baseline must already be Prettier-formatted, which
// covers all new files, and the baseline can only shrink. Convert a file on purpose with `npm run format -- <file>`, then
// delete it from tests/prettier-baseline.json. Editing a baseline file never requires reformatting it.
const root = fileURLToPath(new URL('../src/', import.meta.url));
const baselineFile = new URL('./prettier-baseline.json', import.meta.url);

function walk(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap(entry => entry.isDirectory()
    ? entry.name === 'generated' ? [] : walk(join(dir, entry.name))
    : /\.(tsx?|css)$/.test(entry.name) ? [join(dir, entry.name)] : []);
}
const name = file => relative(root, file).replaceAll('\\', '/');

export async function unformatted(files) {
  const result = [];
  for (const file of files) {
    const options = await resolveConfig(file);
    if (!await check(readFileSync(file, 'utf8'), { ...options, filepath: file })) result.push(name(file));
  }
  return result.sort();
}

export function violations(failing, baseline, present) {
  const found = [];
  for (const file of failing) if (!baseline.includes(file)) found.push(`${file}: not formatted (run: npm run format -- src/${file})`);
  for (const file of baseline) {
    if (!present.includes(file)) found.push(`${file}: no longer exists; remove it from tests/prettier-baseline.json`);
    else if (!failing.includes(file)) found.push(`${file}: is formatted now; remove it from tests/prettier-baseline.json`);
  }
  return found;
}

test('new files are Prettier-formatted and the list of unformatted legacy files can only shrink', async () => {
  const files = walk(root);
  const baseline = JSON.parse(readFileSync(baselineFile, 'utf8'));
  assert.deepEqual(violations(await unformatted(files), baseline, files.map(name)), []);
});

test('the guard flags a new unformatted file, a stale entry and a vanished entry, and allows listed legacy files', () => {
  assert.equal(violations(['a.ts'], [], ['a.ts']).length, 1);
  assert.equal(violations([], ['a.ts'], ['a.ts']).length, 1);
  assert.equal(violations([], ['a.ts'], []).length, 1);
  assert.deepEqual(violations(['a.ts'], ['a.ts'], ['a.ts']), []);
});
