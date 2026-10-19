import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const files = [];
const walk = dir => {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) walk(path);
    else if (path.endsWith('.css')) files.push(path);
  }
};
walk(fileURLToPath(new URL('../src', import.meta.url)));

test('every CSS custom property that is used is defined by the design tokens', () => {
  const defined = new Set();
  const used = [];
  for (const file of files) {
    const css = readFileSync(file, 'utf8');
    for (const match of css.matchAll(/(--[\w-]+)\s*:/g)) defined.add(match[1]);
    for (const match of css.matchAll(/var\((--[\w-]+)/g)) used.push([match[1], file]);
  }
  const missing = used.filter(([name]) => !defined.has(name)).map(([name, file]) => `${name} in ${file}`);
  assert.deepEqual(missing, []);
});
