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

test('a detail card is white and the boxes inside it carry the tint, as on the Activity details', () => {
  const styles = source(join(root, 'styles.css'));
  const card = styles.match(/^\.execution-detail \{[^}]*\}/m)?.[0] ?? '';
  assert.match(card, /background: var\(--color-surface-panel\)/, 'the run/decision/logic detail card is a panel surface');
  const summary = source(join(root, 'views/executionSummary.css'));
  assert.match(summary.match(/^\.run-input \{[^}]*\}/m)?.[0] ?? '', /background: var\(--color-surface-detail\)/,
    'a box inside a detail card uses the tinted detail surface, not the card colour');
});

test('every search box says what to type, so none is left empty', () => {
  const bare = [];
  for (const path of code) {
    const text = source(path);
    for (const match of text.matchAll(/<SearchInput\b[\s\S]*?\/>/g)) {
      if (!/placeholder=/.test(match[0])) bare.push(`${rel(path)}:${text.slice(0, match.index).split('\n').length}`);
    }
  }
  assert.deepEqual(bare, [], 'Give the SearchInput a placeholder (a short example or what it searches: "Role name or key…")');
});

test('buttons are not restyled by where they sit: use ActionButton, not a container-specific button rule', () => {
  // Known, deliberate exceptions: the Runs saved-view controls (their own form row) and the tab strip.
  const allowed = new Set(['.data-view-controls button', '.administration-tabs button', '.role-actions button']);
  const styles = source(join(root, 'styles.css'));
  const found = [...styles.matchAll(/^\s*(\.[\w-]+ button)(?::[\w-]+(?:\([^)]*\))*)*\s*[{,]/gm)].map(match => match[1]);
  const offenders = [...new Set(found)].filter(selector => !allowed.has(selector));
  assert.deepEqual(offenders, [],
    'A rule like ".some-container button { ... }" gives one screen its own button look; use <ActionButton> (or the shared button classes)');
});

test('table text starts on the same gutter as the panel headings and filters above it', () => {
  const styles = source(join(root, 'styles.css'));
  assert.match(styles, /\.activity-table td:first-child \{ padding-inline-start: var\(--space-18\); \}/);
  assert.match(styles, /\.activity-table td:last-child \{ padding-inline-end: var\(--space-18\); \}/);
});
