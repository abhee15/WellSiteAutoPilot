import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { test } from 'node:test';

const root = resolve(import.meta.dirname, '..', 'src');
const tokens = readFileSync(resolve(root, 'tokens.css'), 'utf8');
const css = readFileSync(resolve(root, 'styles.css'), 'utf8');

// Migration parity checksum from the reviewed pre-token stylesheet.
// Resolve EVERY token reference, including the semantic -> palette chain,
// then verify there was no computed CSS declaration or layout change.
const originalStyleSignature = '849da537';
function signature(input) {
  let hash = 2166136261;
  for (let index = 0; index < input.length; index += 1) {
    hash ^= input.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }
  return (hash >>> 0).toString(16).padStart(8, '0');
}
function definitions(text) {
  const vars = new Map();
  for (const match of text.matchAll(/^\s*(--[\w-]+):\s*(.+);$/gm)) {
    assert.equal(vars.has(match[1]), false, 'Duplicate token: ' + match[1]);
    vars.set(match[1], match[2]);
  }
  return vars;
}
const vars = definitions(tokens);
function expand(input) {
  let result = input;
  for (let attempt = 0; attempt < 6 && result.includes('var('); attempt += 1) {
    result = result.replace(/var\((--[\w-]+)\)/g, (match, key) => {
      assert.ok(vars.has(key), 'Unknown design token: ' + key);
      return vars.get(key);
    });
  }
  assert.doesNotMatch(result, /var\(/, 'Unresolved/cyclic design token');
  return result;
}
function frontendCss(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap(item => {
    const filename = resolve(dir, item.name);
    return item.isDirectory() ? frontendCss(filename)
      : item.isFile() && filename.endsWith('.css') && item.name !== 'tokens.css'
        ? [filename] : [];
  });
}

test('token values reproduce reviewed existing visual CSS without declaration changes', () => {
  assert.equal(signature(expand(css)), originalStyleSignature);
});

test('semantic references only resolve through declared palette or foundational tokens', () => {
  assert.ok(vars.size > 40, 'Missing design token catalog');
  for (const [name, value] of vars) {
    if (name.startsWith('--color-')) {
      assert.match(value, /^var\(--palette-[\w-]+\)$/);
    }
    assert.ok(expand('var(' + name + ')').length > 0);
  }
});

test('frontend CSS never introduces a raw color outside tokens.css', () => {
  const hardcodedColor = /#[0-9a-f]{3,8}\b|\brgba?\s*\(|\bhsla?\s*\(/i;
  const files = frontendCss(root);
  assert.ok(files.some(p => p.endsWith('styles.css')));
  for (const file of files) {
    const source = readFileSync(file, 'utf8');
    assert.doesNotMatch(source, hardcodedColor,
      'Hard-coded color in ' + file + '; use a semantic token');
  }
  // The guard itself is regression-tested with a deliberate invalid color.
  assert.match('.bad { color: #ff0000; }', hardcodedColor);
  assert.match('div { background: rgba(0, 0, 0, .5); }', hardcodedColor);
  assert.doesNotMatch('.good { color: var(--color-text-primary); }', hardcodedColor);
});

test('critical UI text and keyboard focus retain explicit semantic tokens', () => {
  assert.match(css, /color:\s*var\(--color-text-primary\)/);
  assert.match(css, /outline:\s*2px solid var\(--color-focus-ring\)/);
  assert.match(css, /border-color:\s*var\(--color-border-status-success\)/);
  assert.match(css, /border-color:\s*var\(--color-border-status-error\)/);
  assert.match(css, /border-color:\s*var\(--color-border-status-warning\)/);
});
