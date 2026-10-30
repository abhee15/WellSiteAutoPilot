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

test('split layouts share space by ratio and give each pane the window height', () => {
  const css = source(join(root, 'views/splitLayout.css'));
  const base = css.match(/^\.split-panes \{[^}]*\}/m)?.[0] ?? '';
  assert.match(base, /\d+fr\)/, 'the list/details columns of .split-panes must be fr ratios, so wide windows are filled');
  assert.doesNotMatch(base, /layout-panel-max|\d+px/, 'do not cap the list column at a fixed width: use ratios');
  assert.match(css, /@media \(min-width: 901px\) \{\s*\.split-panes \{\s*height:/,
    'each pane gets the window height and scrolls on its own, so the open details stay in view');
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

test('master-detail panes scroll on their own and the page does not (one shared rule, measured by a hook)', () => {
  const layout = source(join(root, 'views/splitLayout.css'));
  assert.match(layout, /\.split-panes \{\s*height: max\(var\(--layout-pane-height-min\), calc\(100dvh - var\(--pane-top\)/);
  assert.match(layout, /\.split-browse, \.split-selected \{[^}]*overflow-y: auto/);
  // No page re-introduces a sticky details pane or its own height cap: the shared rule is the only one.
  for (const file of ['views/activity.css', 'views/executionSummary.css', 'styles.css']) {
    assert.doesNotMatch(source(join(root, file)), /\.split-selected[^{]*\{[^}]*(position: sticky|max-height)/, file);
  }
  assert.match(source(join(root, 'App.tsx')), /useSplitPaneViewport\(mainRef\)/);
});

test('type scale: weights and line heights come from tokens, and only the three loaded Roboto weights exist', () => {
  const dir = join(root);
  const offenders = [];
  const walk = folder => {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const path = join(folder, entry.name);
      if (entry.isDirectory()) walk(path);
      else if (entry.name.endsWith('.css') && !entry.name.endsWith('tokens.css')) {
        const css = readFileSync(path, 'utf8');
        for (const match of css.matchAll(/font-weight:\s*([^;}]+)/g)) {
          if (!/^(var\(--font-weight-(regular|medium|bold)\)|inherit)$/.test(match[1].trim())) offenders.push(`${entry.name}: ${match[0]}`);
        }
      }
    }
  };
  walk(dir);
  assert.deepEqual(offenders, [], 'use --font-weight-regular/medium/bold; a raw 600 would render as the 700 Roboto file');
  const tokens = source(join(root, 'tokens.css'));
  for (const weight of ['400', '500', '700']) assert.match(tokens, new RegExp(`--font-weight-\\w+: ${weight};`));
  assert.doesNotMatch(tokens, /--font-weight-\w+: 600/);
});

test('closing a details pane is always the shared ✕ CloseButton, never a text button of its own', () => {
  // The one allowed exception: the form footer in ConfiguredLogicWorkspace, where Close sits beside the setup form.
  const allowed = new Set(['ConfiguredLogicWorkspace.tsx']);
  const offenders = [];
  for (const file of readdirSync(join(root, 'views'))) {
    if (!file.endsWith('.tsx') || allowed.has(file)) continue;
    const code = readFileSync(join(root, 'views', file), 'utf8');
    for (const match of code.matchAll(/<(\w+)[^>]*onClick=\{onClose\}/g)) {
      if (match[1] !== 'CloseButton') offenders.push(`${file}: <${match[1]} onClick={onClose}>`);
    }
  }
  assert.deepEqual(offenders, [], 'use <CloseButton onClick={onClose} /> so every pane closes the same way');
});
