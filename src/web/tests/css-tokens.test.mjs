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
  // Only src/tokens.css may hold raw lengths. Media- and container-query breakpoints are exempt because CSS
  // cannot use custom properties in them.
  const offenders = [];
  for (const file of files) {
    if (file.endsWith('tokens.css')) continue;
    const css = readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '').replace(/@(media|container)[^{]*/g, '');
    css.split('\n').forEach((line, index) => {
      for (const match of line.matchAll(/(?<![\w.-])-?\d*\.?\d+(px|rem|em|ms|s)\b/g)) {
        offenders.push(`${file.split('/src/')[1]}:${index + 1} ${match[0]}`);
      }
    });
  }
  assert.deepEqual(offenders, [], 'Use a token from src/tokens.css (add one there if none fits)');
});

test('fading, the base button reset and form captions stay on the shared primitives', () => {
  const read = name => readFileSync(fileURLToPath(import.meta.resolve(`../src/${name}`)), 'utf8');
  const styles = read('styles.css');
  // A literal opacity would drift from the one disabled look (--opacity-disabled in tokens.css).
  const literal = files
    .filter(file => !file.endsWith('tokens.css'))
    .flatMap(file => [...readFileSync(file, 'utf8').matchAll(/opacity:\s*(0?\.\d+)\s*;/g)]
      .map(match => `${file.split('/src/')[1]} opacity ${match[1]}`));
  assert.deepEqual(literal, [], 'Use var(--opacity-disabled), or add a token');
  // `button { font: inherit }` has type specificity and beats the :where() button primitive, leaving bare
  // buttons at the browser's 16px instead of the control size. The reset has to stay zero-specificity.
  assert.match(styles, /:where\(button, a\)\s*\{\s*font:\s*inherit/);
  assert.doesNotMatch(styles, /^button, a\s*\{/m);
  // Feature forms take their field box from the shared primitives, so they must not restyle it again.
  for (const name of ['views/configuredLogicSetup.css', 'views/assetCreation.css']) {
    const css = read(name).replace(/\/\*[\s\S]*?\*\//g, '');
    const restyled = [...css.matchAll(/([^{}]+)\{([^}]*)\}/g)]
      .filter(([, selector, body]) => /(?<![\w-])(input|select|textarea)(?![\w-])/.test(selector.replace(/:is\([^)]*\)/g, m => m))
        && !/:focus-visible/.test(selector) && /(padding|border|background|font)/.test(body))
      .map(([, selector]) => selector.trim());
    assert.deepEqual(restyled, [], `${name} should only lay fields out`);
  }
});
