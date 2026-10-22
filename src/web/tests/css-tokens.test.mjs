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
walk(fileURLToPath(import.meta.resolve('../src')));

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

test('sizes, spacing, radii and timings come from design tokens, not raw lengths', () => {
  // Only src/tokens.css may hold raw lengths. Media-query breakpoints are exempt because CSS cannot use
  // custom properties in a media query.
  const offenders = [];
  for (const file of files) {
    if (file.endsWith('tokens.css')) continue;
    const css = readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '').replace(/@media[^{]*/g, '');
    css.split('\n').forEach((line, index) => {
      for (const match of line.matchAll(/(?<![\w.-])-?\d*\.?\d+(px|rem|em|ms|s)\b/g)) {
        offenders.push(`${file.split('/src/')[1]}:${index + 1} ${match[0]}`);
      }
    });
  }
  assert.deepEqual(offenders, [], 'Use a token from src/tokens.css (add one there if none fits)');
});
