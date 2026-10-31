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
  assert.match(css, /@media \(width > 56\.25em\) \{\s*\.split-panes \{\s*height:/,
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
  // Both panes are also often a `.panel` (overflow: hidden, loaded later); the scroll rule must outrank it, so it is
  // written as children of .split-panes. A bare `.split-browse` rule silently turns scrolling off in the Runs list.
  assert.match(layout, /\.split-panes > \.split-browse, \.split-panes > \.split-selected \{[^}]*overflow-y: auto/);
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

test('no unclassed, old-style <button>: use ActionButton (or a shared class) so every button looks and behaves the same', () => {
  const offenders = [];
  const walk = folder => {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const path = join(folder, entry.name);
      if (entry.isDirectory()) { walk(path); continue; }
      if (!entry.name.endsWith('.tsx') || entry.name === 'ActionButton.tsx') continue;
      const code = readFileSync(path, 'utf8');
      // A button with no className at all gets only the bare default look.
      for (const match of code.matchAll(/<button(?![^>]*className)[^>]*>/g)) offenders.push(`${relative(root, path)}: ${match[0].slice(0, 70)}`);
    }
  };
  walk(root);
  assert.deepEqual(offenders, [], 'use <ActionButton label=... /> for an action button');
});

test('controls share one height: border-box sizing and a control line height keep inputs, selects and buttons at --size-control', () => {
  const styles = source(join(root, 'styles.css'));
  assert.match(styles, /:where\(input, select, textarea, button\) \{ box-sizing: border-box; \}/);
  assert.match(styles, /:is\(input, select, textarea\)[^{]*\{ line-height: var\(--line-height-control\); \}/);
});

test('the window never scrolls on tablets and wider: the page header stays put and the page body is the one scroll region', () => {
  const styles = source(join(root, 'styles.css'));
  const app = source(join(root, 'App.tsx'));
  assert.match(styles, /@media \(width >= 48em\)\s*\{[^@]*\.app-shell\s*\{[^}]*overflow:\s*hidden[^}]*\}[^@]*\.page-body\s*\{[^}]*overflow-y:\s*auto/);
  assert.match(app, /className="page-body"/);
});

test('refresh and reload actions are always the shared icon-only button', () => {
  const offenders = [];
  const walk = folder => {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const path = join(folder, entry.name);
      if (entry.isDirectory()) { walk(path); continue; }
      if (!entry.name.endsWith('.tsx')) continue;
      const code = readFileSync(path, 'utf8');
      for (const match of code.matchAll(/<ActionButton\b[^>]*label=\{[^}]*\b(?:refresh|reload)[A-Za-z]*'[^}]*\}[^>]*>/g)) {
        if (!/iconOnly/.test(match[0])) offenders.push(`${relative(root, path)}: ${match[0].slice(0, 80)}`);
      }
    }
  };
  walk(root);
  assert.deepEqual(offenders, [], 'Refresh/Reload is a RefreshCw icon-only ActionButton everywhere');
});

test('every list searches and filters through the shared FilterToolbar; the old bars and pane search boxes are gone', () => {
  const offenders = [];
  const walk = folder => {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const path = join(folder, entry.name);
      if (entry.isDirectory()) { walk(path); continue; }
      if (!/\.(tsx|css)$/.test(entry.name)) continue;
      const code = readFileSync(path, 'utf8');
      if (/FilterBar|filter-bar(?!-)|className="split-(search|filters)"|data-view-fields/.test(code.replace(/activity-filter-bar/g, ''))) offenders.push(relative(root, path));
    }
  };
  walk(root);
  assert.deepEqual(offenders, [], 'use <FilterToolbar> from components/FilterToolbar');
});

test('action buttons are the shared ActionButton, not <button className="primary-button|secondary-button">', () => {
  const offenders = [];
  const walk = folder => {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const path = join(folder, entry.name);
      if (entry.isDirectory()) { walk(path); continue; }
      if (!entry.name.endsWith('.tsx') || entry.name === 'ActionButton.tsx') continue;
      const code = readFileSync(path, 'utf8');
      // Tabs keep their button look; everything else that acts is an ActionButton.
      for (const match of code.matchAll(/<button\b(?![^>]*role="tab")[^>]*className="(?:primary|secondary)-button/g)) offenders.push(`${relative(root, path)}: ${match[0].slice(0, 70)}`);
    }
  };
  walk(root);
  assert.deepEqual(offenders, [], 'use <ActionButton label=... variant=... />');
});


test('breakpoints are in em (they follow the reader\'s text size) and use the range syntax, never px', () => {
  const offenders = [];
  const walk = folder => {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const path = join(folder, entry.name);
      if (entry.isDirectory()) { walk(path); continue; }
      if (!entry.name.endsWith('.css')) continue;
      readFileSync(path, 'utf8').split('\n').forEach((line, index) => {
        if (/@(media|container)[^{]*\b(min|max)-width\s*:/.test(line) || /@(media|container)[^{]*\d+px/.test(line)) offenders.push(`${relative(root, path)}:${index + 1}`);
      });
    }
  };
  walk(root);
  assert.deepEqual(offenders, [], 'write (width <= 40em) / (width >= 48em); see docs/design/ui-conventions.md');
});

test('every localization key is used somewhere (dynamic families are listed), so dead copy does not pile up', () => {
  const loc = readFileSync(join(root, 'localization.ts'), 'utf8');
  const keys = [...loc.matchAll(/^\s*'([\w.]+)':/gm)].map(match => match[1]);
  let used = '';
  const walk = folder => {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const path = join(folder, entry.name);
      if (entry.isDirectory()) { walk(path); continue; }
      if (/\.(ts|tsx|mjs)$/.test(entry.name) && entry.name !== 'localization.ts' && entry.name !== 'consistency.test.mjs') used += readFileSync(path, 'utf8');
    }
  };
  walk(root);
  walk(join(root, '..', 'tests'));
  const dynamic = ['assets.createField.', 'moduleApproval.', 'setup.', 'nav.'];
  const dead = keys.filter(key => !used.includes(`'${key}'`) && !used.includes(`"${key}"`) && !used.includes(`\`${key}\``) && !dynamic.some(prefix => key.startsWith(prefix)));
  assert.deepEqual(dead, [], 'delete the unused key, or add its dynamic prefix to this test');
});

test('a scrolling list is never also a .panel with a rule that loses to `.panel { overflow: hidden }`', () => {
  // View stylesheets load before styles.css, so a one-class scroll rule loses to .panel on an element that has both
  // classes and scrolling silently stops (the Runs list hid its older rows). Such a rule must be more specific.
  const panelClasses = new Set();
  for (const path of code.filter(p => p.endsWith('.tsx'))) {
    for (const [, value] of source(path).matchAll(/className="([^"]*)"/g)) {
      const names = value.split(/\s+/);
      if (names.includes('panel')) names.filter(n => n !== 'panel').forEach(n => panelClasses.add(n));
    }
  }
  const risky = [];
  for (const path of files.filter(p => p.endsWith('.css'))) {
    const css = source(path).replace(/\/\*[\s\S]*?\*\//g, '');
    for (const [, selectors, body] of css.matchAll(/([^{}@]+)\{([^{}]*)\}/g)) {
      if (!/overflow(-y|-block)?:\s*(auto|scroll)/.test(body)) continue;
      for (const selector of selectors.split(',').map(s => s.trim())) {
        const single = selector.match(/^\.([\w-]+)$/);
        if (single && panelClasses.has(single[1])) risky.push(`${rel(path)}: ${selector}`);
      }
    }
  }
  assert.deepEqual(risky, []);
});

test('label and value pairs share one type scale: a <dt> is never caption or tag sized', () => {
  // Labels set at 12px were hard to read beside 16px values (Logic "Live revision" row). The base rule in styles.css
  // sets dt/dd once; a component styles colour or layout, not a smaller label.
  const small = [];
  for (const path of files.filter(p => p.endsWith('.css'))) {
    const css = source(path).replace(/\/\*[\s\S]*?\*\//g, '');
    for (const [, selectors, body] of css.matchAll(/([^{}@]+)\{([^{}]*)\}/g)) {
      if (!/(^|[\s,>])dt\b/.test(selectors) || /:where\(/.test(selectors)) continue;
      if (/font-size:\s*var\(--font-size-(caption|tag)\)/.test(body)) small.push(`${rel(path)}: ${selectors.trim()}`);
    }
  }
  assert.deepEqual(small, []);
  assert.match(source(join(root, 'styles.css')), /:where\(dt\) \{ font-size: var\(--font-size-label\)/);
});

test('a tree row keeps its count and actions on the row: only the name flexes, and the row itself never wraps', () => {
  // With flex-wrap on the row and an auto-sized name, a long name pushed the child count onto a new line.
  const css = source(join(root, 'views/assetTree.css'));
  const row = css.match(/\.asset-tree-row \{([^}]*)\}/)?.[1] ?? '';
  assert.doesNotMatch(row, /flex-wrap:\s*wrap/);
  assert.match(css, /\.asset-tree-name \{ flex: 1 1 0; min-width: 0;/);
  assert.match(css, /\.asset-tree-row > small[^{]*\{ flex: none; \}/);
});
