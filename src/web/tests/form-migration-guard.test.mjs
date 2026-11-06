import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { URL, fileURLToPath } from 'node:url';
import ts from 'typescript';

const root = fileURLToPath(new URL('../src/', import.meta.url));
function walk(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap(entry => entry.isDirectory()
    ? entry.name === 'generated' || entry.name === 'components' ? [] : walk(join(dir, entry.name))
    : entry.name.endsWith('.tsx') ? [join(dir, entry.name)] : []);
}
function inventory(source) {
  const result = { controls: 0, notices: 0 };
  const file = ts.createSourceFile('screen.tsx', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  function visit(node) {
    if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) {
      const tag = node.tagName.getText(file);
      if (['input', 'textarea', 'select'].includes(tag)) result.controls++;
      const role = node.attributes.properties.find(item => ts.isJsxAttribute(item) && item.name.getText(file) === 'role');
      if (/^[a-z]/.test(tag) && role?.initializer && /['"](?:alert|status|note)['"]/.test(role.initializer.getText(file))) result.notices++;
    }
    ts.forEachChild(node, visit);
  }
  visit(file);
  return result;
}
function violations(actual, legacy) {
  const found = [];
  for (const [file, counts] of Object.entries(actual)) {
    for (const kind of ['controls', 'notices']) {
      if (counts[kind] > (legacy[file]?.[kind] ?? 0)) found.push(`${file}: new raw ${kind}; use FormFields/Notice`);
    }
  }
  for (const [file, counts] of Object.entries(legacy)) {
    for (const kind of ['controls', 'notices']) {
      if ((actual[file]?.[kind] ?? 0) < counts[kind]) found.push(`${file}: remove migrated ${kind} from the legacy baseline`);
    }
  }
  return found;
}

test('raw fields and notice roles outside components cannot grow; migrated screens remain at zero', () => {
  const actual = Object.fromEntries(walk(root).map(file => [relative(root, file).replaceAll('\\', '/'), inventory(readFileSync(file, 'utf8'))]));
  const legacy = JSON.parse(readFileSync(new URL('./legacy-form-notice-baseline.json', import.meta.url), 'utf8'));
  assert.deepEqual(violations(actual, legacy), [], 'Use the shared controls and notices, and shrink the baseline on migration.');
});

test('the migration guard catches new fields, dynamic notices and stale exceptions while allowing native options', () => {
  assert.deepEqual(inventory('<><input type="text" /><textarea /><select><option /></select><p role={bad ? "alert" : "status"}>Message</p></>'), { controls: 3, notices: 1 });
  assert.deepEqual(inventory('<><TextField label="Name" /><Notice>Message</Notice><option>Value</option></>'), { controls: 0, notices: 0 });
  assert.equal(violations({ 'views/new.tsx': { controls: 1, notices: 0 } }, {}).length, 1);
  assert.equal(violations({ 'views/old.tsx': { controls: 1, notices: 1 } }, { 'views/old.tsx': { controls: 1, notices: 0 } }).length, 1);
  assert.equal(violations({ 'views/old.tsx': { controls: 0, notices: 0 } }, { 'views/old.tsx': { controls: 1, notices: 0 } }).length, 1);
});
