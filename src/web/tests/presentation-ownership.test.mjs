import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { URL, fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../src/', import.meta.url));
function walk(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap(entry => entry.isDirectory()
    ? entry.name === 'generated' ? [] : walk(join(dir, entry.name)) : [join(dir, entry.name)]);
}
const owners = [
  [/\.panel-header[^,{]*\bh[23]\b/, 'styles.css'],
  [/\.(?:ui-field(?:-[\w-]+)?|ui-choice(?:-[\w-]+)?|ui-radio-group|ui-form-actions|ui-confirm-panel)\b/, 'components/forms.css'],
  [/\.ui-notice(?:-[\w-]+)?\b/, 'components/notice.css'],
  [/\.summary-card(?:-[\w-]+)?\b/, 'components/summaryCard.css'],
  [/\.ui-action-button\b/, 'components/searchControls.css'],
  [/\.(?:loading-indicator|loading-skeleton|loading-spinner|panel-loading-state)\b/, 'components/loading.css'],
  [/\.tab-strip(?:-item|-count)?\b/, 'components/tabStrip.css'],
  [/\.action-link\b/, 'components/contextLink.css'],
];
const visual = /(?:^|;)\s*(?:font(?:-size|-weight)?|min-height|padding(?:-inline|-block)?|border(?:-color|-width|-radius)?|background(?:-color)?|color|animation)\s*:/;

function violations(css, path) {
  const found = [];
  for (const [, selector, declarations] of css.replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    if (!visual.test(declarations)) continue;
    // Existing inline copy controls deliberately retain their compact size; this exception cannot cover other actions.
    if (path === 'styles.css' && selector.trim() === '.ui-action-button.copy-button[data-icon-only]') continue;
    for (const [pattern, owner] of owners) {
      if (pattern.test(selector) && path !== owner) found.push(`${path}: ${selector.trim()} belongs to ${owner}`);
    }
  }
  return found;
}

test('shared action, tab, link and loading visuals have one CSS owner; pages may position them', () => {
  const found = walk(root).filter(path => path.endsWith('.css')).flatMap(path =>
    violations(readFileSync(path, 'utf8'), relative(root, path).replaceAll('\\', '/')));
  assert.deepEqual(found, [], 'Move shared visual changes to the owning component instead of overriding a page.');
});

test('the ownership guard catches the toolbar sizing and loader indentation regressions, while allowing layout', () => {
  assert.equal(violations('.asset-view-bar .ui-action-button { min-height: 28px; }', 'views/assetViews.css').length, 1);
  assert.equal(violations('.loading-indicator { padding: 2rem; }', 'views/page.css').length, 1);
  assert.equal(violations('.tab-strip-item { border-color: red; }', 'views/page.css').length, 1);
  assert.equal(violations('.summary-card { padding: 2rem; }', 'views/page.css').length, 1);
  assert.equal(violations('.action-link { font-size: 12px; }', 'views/page.css').length, 1);
  assert.equal(violations('.ui-field-control { padding: 2rem; }', 'views/page.css').length, 1);
  assert.equal(violations('.ui-notice { background: red; }', 'views/page.css').length, 1);
  assert.equal(violations('.overview .panel-header h2 { font-size: 30px; }', 'views/page.css').length, 1);
  assert.equal(violations('.ui-action-button { margin-inline-start: auto; }', 'views/page.css').length, 0);
});

test('contextual destinations and loading markup are created by their shared components', () => {
  const found = [];
  for (const path of walk(root).filter(path => path.endsWith('.tsx'))) {
    const name = relative(root, path).replaceAll('\\', '/');
    const source = readFileSync(path, 'utf8');
    if (name !== 'components/ContextLink.tsx' && /<a\b[^>]*className="action-link\b/.test(source)) found.push(name + ': use ContextLink');
    if (!['components/LoadingIndicator.tsx', 'components/ActionButton.tsx'].includes(name) && /className="loading-(?:spinner|indicator|skeleton)\b/.test(source)) found.push(name + ': use LoadingIndicator/PanelLoading');
  }
  assert.deepEqual(found, []);
});


test('record and standalone destinations use ContextLink; native prose and section navigation stay explicit', () => {
  const found = [];
  for (const path of walk(join(root, 'views')).filter(path => path.endsWith('.tsx'))) {
    const name = relative(root, path).replaceAll('\\', '/');
    for (const [tag] of readFileSync(path, 'utf8').matchAll(/<a\b[^>]*>/g)) {
      if (name === 'views/RecommendationsPage.tsx' && tag.includes('className="tab-strip-item"')) continue;
      // This link is embedded in the owning-connection sentence; its persistent underline is intentional.
      if (name === 'views/AssetDetail.tsx' && tag.includes("href={hashFor('connections', owner.connectionId)}")) continue;
      found.push(name + ': use ContextLink for a record/destination, or document a genuine prose/navigation exception');
    }
  }
  assert.deepEqual(found, []);
});
