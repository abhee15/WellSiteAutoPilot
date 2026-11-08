import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { test } from 'node:test';

// WCAG 2.2 contrast for the colour tokens: 1.4.3 (text, 4.5), 1.4.11 (control boundaries, 3) and the focus
// indicator. jsdom cannot compute colour, so the proof lives here, against the token values themselves.
// Only declared pairs are checked: most text/surface combinations never occur and would only add noise.
const tokens = readFileSync(resolve(import.meta.dirname, '..', 'src', 'tokens.css'), 'utf8');
const { pairs, exempt } = JSON.parse(readFileSync(resolve(import.meta.dirname, 'contrast-pairs.json'), 'utf8'));

const vars = new Map();
for (const match of tokens.matchAll(/^\s*(--[\w-]+):\s*(.+);$/gm)) vars.set(match[1], match[2].trim());

function value(name) {
  let current = vars.get(name);
  assert.ok(current, 'Unknown token in contrast-pairs.json: ' + name);
  for (let hops = 0; hops < 6; hops += 1) {
    const reference = /^var\((--[\w-]+)\)$/.exec(current);
    if (!reference) break;
    current = vars.get(reference[1]);
    assert.ok(current, 'Unresolved token reference: ' + reference[1]);
  }
  return current;
}

function luminance(hex) {
  assert.match(hex, /^#[0-9a-fA-F]{6}$/, 'Contrast checks need a six digit hex colour, got ' + hex);
  const [r, g, b] = [1, 3, 5].map(index => {
    const channel = parseInt(hex.slice(index, index + 2), 16) / 255;
    return channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function contrastRatio(foreground, background) {
  const [light, dark] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
  return (light + 0.05) / (dark + 0.05);
}

test('every declared colour pair meets its WCAG minimum', () => {
  const failures = [];
  for (const { foreground, background, minimum, context } of pairs) {
    const ratio = contrastRatio(value(foreground), value(background));
    if (ratio < minimum) failures.push(`${context}: ${foreground} on ${background} is ${ratio.toFixed(2)}:1, needs ${minimum}:1`);
  }
  assert.deepEqual(failures, []);
});

test('every text, control-boundary and focus colour token is covered by a declared pair or exempted with a reason', () => {
  const covered = new Set(pairs.map(pair => pair.foreground));
  const needed = [...vars.keys()].filter(name => /^--color-(text-|border-control$|focus-)/.test(name));
  const missing = needed.filter(name => !covered.has(name) && !exempt[name]);
  assert.deepEqual(missing, [], 'Add these tokens to tests/contrast-pairs.json (or list them under "exempt" with a reason)');
  for (const [name, reason] of Object.entries(exempt)) {
    assert.ok(vars.has(name), 'Exempt token no longer exists: ' + name);
    assert.ok(typeof reason === 'string' && reason.length > 10, 'Give a real reason for exempting ' + name);
  }
});
