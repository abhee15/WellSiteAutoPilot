import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

// Guards for the shared building blocks. Each one names the component to use instead, so a new screen that
// reaches for its own markup fails here with a pointer to the right place, rather than drifting from the rest.
// fileURLToPath, not URL.pathname: on Windows the pathname is "/D:/...", which is not a usable path.
const root = fileURLToPath(new URL('../src/', import.meta.url));
function walk(dir) {
  return readdirSync(dir).flatMap(name => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? (name === 'generated' ? [] : walk(path)) : [path];
  });
}
const files = walk(root).filter(path => /\.(tsx|ts|css)$/.test(path));
const source = path => readFileSync(path, 'utf8');
const rel = path => relative(root, path).replaceAll('\\', '/');
const code = files.filter(path => /\.tsx?$/.test(path) && !path.endsWith('localization.ts'));

function offenders(pattern, { except = [] } = {}) {
  return files.filter(path => !except.includes(rel(path)) && pattern.test(source(path))).map(rel);
}

test('states are shown with components/Status, never with ad-hoc pills or colour classes', () => {
  assert.deepEqual(offenders(/StatusPill|status-pill|health-state|\bstatus-(good|bad|warn|ok)\b/), []);
});

test('icon-only actions are ActionButton (iconOnly), not a second icon-button component', () => {
  assert.deepEqual(offenders(/\bIconButton\b|\bicon-button\b|execution-inspect-button/), []);
});

test('search boxes are SearchInput, so they look, size and clear the same everywhere', () => {
  assert.deepEqual(offenders(/<input[^>]*type=["']search["']/, { except: ['components/SearchInput.tsx'] }), []);
});

test('panel and page headers carry no eyebrow or subtitle; only detail panes label themselves', () => {
  assert.deepEqual(offenders(/className=["']eyebrow["']/, { except: ['views/AuditEventPanel.tsx', 'views/AssetDetail.tsx'] }), []);
  assert.deepEqual(offenders(/page-description/), []);
});

test('no browser dialogs: alert, confirm and prompt block the page and cannot be styled or tested', () => {
  assert.deepEqual(code.filter(path => /\b(window\.)?(alert|confirm|prompt)\(/.test(source(path))).map(rel), []);
});

test('no raw HTML injection', () => {
  assert.deepEqual(offenders(/dangerouslySetInnerHTML/), []);
});

test('user-facing text goes through t(): no hard-coded sentence in a button or heading', () => {
  const literal = /<(button|h1|h2|h3)\b[^>]*>\s*[A-Z][a-z]+(?: [a-z]+){1,}\s*<\//;
  const found = code.filter(path => literal.test(source(path))).map(rel);
  assert.deepEqual(found, []);
});

test('every stat/summary heading and table header on a shared page is a message key, not a literal (spot check)', () => {
  const overview = source(join(root, 'views/OverviewPage.tsx'));
  assert.ok(!/<SummaryCard[^>]*label="/.test(overview));
});

test('split layouts share space by ratio and keep the details in view', () => {
  const css = source(join(root, 'views/splitLayout.css'));
  const base = css.match(/^\.split-panes \{[^}]*\}/m)?.[0] ?? '';
  assert.match(base, /\d+fr\)/, 'the list/details columns of .split-panes must be fr ratios, so wide windows are filled');
  assert.doesNotMatch(base, /layout-panel-max|\d+px/, 'do not cap the list column at a fixed width: use ratios');
  assert.match(css, /\.runs-layout\[data-has-selection='true'\] \.split-selected \{[^}]*position: sticky/,
    'the open details stay in view while a long list is scrolled');
});
