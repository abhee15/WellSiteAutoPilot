import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import test from 'node:test';
import { URL, fileURLToPath } from 'node:url';

// A class name in a component with no rule anywhere means the element renders unstyled. That is how the kill switch,
// control actions and autonomy policy panels shipped without a stylesheet. This is a ratchet: the list below holds the
// classes that already lack a rule and may only shrink. A new class needs a rule, or it fails here.
const BASELINE = [
  // Built from a prefix plus a variable, so the literal text is not a class.
  '-button', 'connection-test-',
  // Hooks or markers with no rule of their own yet.
  'asset-create-panel', 'loading-label', 'logic-detail-overview',
  // The control panels and the schedule panel were built without styles. Each goes when its screen is rebuilt on the
  // shared field and notice components (docs/design/control-ui-audit.md, steps D2 to D5).
  'autonomy-policy', 'autonomy-policy-form', 'autonomy-policy-limits', 'autonomy-policy-row',
  'schedule-control', 'schedule-control-form',
];

const root = fileURLToPath(new URL('../src/', import.meta.url));
function walk(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry =>
    entry.isDirectory() ? walk(join(directory, entry.name)) : [join(directory, entry.name)]);
}
const files = walk(root);
const css = files.filter(path => path.endsWith('.css')).map(path => readFileSync(path, 'utf8')).join('\n');
const defined = new Set([...css.matchAll(/\.([A-Za-z_][\w-]*)/g)].map(match => match[1]));

function usedClasses() {
  const used = new Map();
  for (const path of files.filter(file => file.endsWith('.tsx'))) {
    const source = readFileSync(path, 'utf8');
    for (const match of source.matchAll(/className=(?:"([^"]*)"|\{`([^`]*)`\})/g)) {
      const text = (match[1] ?? match[2] ?? '').replace(/\$\{[^}]*\}/g, ' ');
      for (const name of text.split(/\s+/).filter(Boolean)) {
        if (!used.has(name)) used.set(name, new Set());
        used.get(name).add(relative(root, path));
      }
    }
  }
  return used;
}

test('every class a component uses has a style rule, except the shrinking baseline', () => {
  const missing = [...usedClasses()].filter(([name]) => !defined.has(name)).map(([name, where]) => `${name} (${[...where][0]})`);
  const fresh = missing.filter(entry => !BASELINE.includes(entry.split(' ')[0]));
  assert.deepEqual(fresh, [], 'These classes have no CSS rule. Add the rule (or use a shared component) instead of adding to the baseline.');
});

test('the baseline only holds classes that are still unstyled, so it can only shrink', () => {
  const used = usedClasses();
  const stale = BASELINE.filter(name => defined.has(name) || !used.has(name));
  assert.deepEqual(stale, [], 'Remove these from the baseline: they now have a rule or are no longer used.');
});
