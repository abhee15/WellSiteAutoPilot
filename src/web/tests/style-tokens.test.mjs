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
  // Git on Windows may check out CSS with CRLF. Normalize only line endings;
  // declarations, selector order, whitespace and token values must still match.
  const baseline = css.replace(/\r\n/g, '\n').split('\n/* Asset creation:')[0];
  assert.notEqual(baseline, css.replace(/\r\n/g, '\n'),
    'Missing feature stylesheet boundary');
  assert.equal(signature(expand(baseline)), originalStyleSignature);
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

function luminance(hex) {
  assert.match(hex, /^#[0-9a-f]{6}$/i, 'Contrast colors must be opaque RGB hex');
  const channels = [1, 3, 5].map(index => parseInt(hex.slice(index, index + 2), 16) / 255);
  const [red, green, blue] = channels.map(channel => channel <= 0.04045
    ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4);
  return red * 0.2126 + green * 0.7152 + blue * 0.0722;
}

test('critical text states retain WCAG AA contrast in the current dark palette', () => {
  // Compare actual resolved semantic tokens, not duplicated hard-coded RGB.
  const pairs = [
    ['text-primary', 'surface-canvas'],
    ['text-muted', 'surface-panel'],
    ['text-navigation', 'surface-panel'],
    ['text-control', 'surface-input'],
    ['text-warning', 'surface-status-warning'],
    ['text-warning-strong', 'surface-status-warning'],
    ['text-link', 'surface-panel'],
    ['text-selected', 'surface-input'],
    ['text-subtle', 'surface-panel']
  ];
  for (const [foreground, background] of pairs) {
    const lighter = luminance(expand('var(--color-' + foreground + ')'));
    const darker = luminance(expand('var(--color-' + background + ')'));
    const ratio = (Math.max(lighter, darker) + 0.05) /
      (Math.min(lighter, darker) + 0.05);
    assert.ok(ratio >= 4.5,
      foreground + '/' + background + ' is below WCAG AA text contrast: ' + ratio);
  }
});

test('critical UI text and keyboard focus retain explicit semantic tokens', () => {
  assert.match(css, /color:\s*var\(--color-text-primary\)/);
  assert.match(css, /outline:\s*2px solid var\(--color-focus-ring\)/);
  assert.match(css, /border-color:\s*var\(--color-border-status-success\)/);
  assert.match(css, /border-color:\s*var\(--color-border-status-error\)/);
  assert.match(css, /border-color:\s*var\(--color-border-status-warning\)/);
});
