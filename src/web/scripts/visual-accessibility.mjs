import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { delay, root, startVisualBrowser } from './lib/visual-browser.mjs';

// Runs axe-core in a real browser against the visual pages. jsdom cannot compute colour or layout, so the checks it
// has to skip (colour contrast, landmark regions, hidden-by-layout content) are enforced here instead.
// Add a page or state to `cases` to put it under the gate. Do not turn a rule off to get green: fix the markup or
// token, or add a narrow, explained entry to `knownViolations`.
const cases = [
  ['run-history', '?nav=expanded', 1440], ['run-history', '?mode=list', 390],
  ['adapter-operations', '?nav=expanded', 1440], ['adapter-operations', '?mode=examples', 390],
  ['operator-review', '', 1440], ['operator-review', '?page=overview', 390],
  ['shared-forms', '', 1440], ['shared-forms', '', 390],
  ['guided-authoring', '', 1440], ['guided-authoring', '', 768], ['guided-authoring', '', 390],
  ['guided-authoring', '?view=ide', 1440], ['guided-authoring', '?view=ide', 768],
  ['guided-authoring', '?view=ide', 820], ['guided-authoring', '?view=ide', 1024], ['guided-authoring', '?view=ide', 1180],
  ['guided-authoring', '?view=surface-cards', 1440], ['guided-authoring', '?view=surface-cards', 768],
];
/**
 * Rules that cannot be judged on these pages. heading-order: each page renders one component without the heading
 * that frames it in the real application (for example a panel's h3 with no h2 above it), so the order is an
 * artifact of the fixture. Heading structure is still checked on the rendered components in the jsdom axe tests.
 */
const fixtureRules = { 'heading-order': { enabled: false } };
/** `${page}${query}@${width}` -> { rule, reason }[] for violations that are understood and tracked, never for new ones. */
const knownViolations = {};

const output = join(root, 'artifacts', 'visual-accessibility');
const session = await startVisualBrowser(output);
const { channel } = session;
const axeSource = readFileSync(join(root, 'node_modules', 'axe-core', 'axe.min.js'), 'utf8');
const problems = [];
const report = [];

async function evaluate(expression) {
  const result = await channel.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? 'Page script failed');
  return result.result?.value;
}

async function settled() {
  // The pages fetch mocked data and render asynchronously; wait until the text stops changing.
  let previous = -1; let stable = 0;
  for (let attempts = 0; attempts < 60 && stable < 3; attempts += 1) {
    await delay(250);
    const length = await evaluate('document.body.innerText.length');
    stable = length === previous && length > 0 ? stable + 1 : 0;
    previous = length;
  }
  if (stable < 3) throw new Error('The page never finished rendering');
}

try {
  for (const [page, query, width] of cases) {
    const key = `${page}${query}@${width}`;
    await channel.send('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false });
    await channel.send('Page.navigate', { url: `${session.origin}/tests/visual/${page}.html${query}` });
    try { await settled(); } catch (error) { problems.push(`${key}: ${error.message}`); continue; }
    if (page === 'guided-authoring' && query === '?view=surface-cards') {
      const plot = await evaluate(`(() => {
        const canvas = document.querySelector('.guided-surface-card canvas');
        if (!canvas || document.documentElement.scrollWidth > innerWidth) return false;
        return canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height)
          .data.some((value, index) => index % 4 === 3 && value > 0);
      })()`);
      if (!plot) problems.push(`${key}: stored surface card chart is empty or overflows the page`);
    } else if (page === 'guided-authoring') {
      const layout = await evaluate(`(() => {
        const visible = selector => Boolean(document.querySelector(selector)?.getClientRects().length);
        const bounds = selector => document.querySelector(selector).getBoundingClientRect();
        const editor = bounds('.guided-authoring-main');
        const inspector = bounds('.guided-authoring-inspector');
        const tabs = document.querySelector('.guided-authoring-view-bar .tab-strip');
        const tools = bounds('.guided-authoring-view-bar > .guided-authoring-actions');
        return {
          sideBySide: editor.right <= inspector.left && editor.top === inspector.top,
          actionsAboveEditor: tools.bottom <= editor.top,
          tabOverflow: tabs.scrollWidth > tabs.clientWidth,
          editor: visible('.guided-authoring-editor'),
          ide: visible('.guided-authoring-workbench[data-view=ide] .guided-authoring-outline'),
          review: visible('.guided-authoring-review'),
          evidence: visible('[title="LOW_FILLAGE"]'),
          approval: visible('.guided-authoring-approval'),
          phoneMessage: visible('.guided-authoring-mobile'),
          overflow: document.documentElement.scrollWidth > innerWidth,
          navLabels: [...document.querySelectorAll('.navigation .nav-label')].every(label => {
            const range = document.createRange();
            range.selectNodeContents(label);
            return range.getClientRects().length === (innerWidth < 768 ? 0 : 1);
          }),
        };
      })()`);
      const phone = width === 390;
      if (layout.editor === phone || layout.ide !== (query === '?view=ide') || !layout.review || !layout.evidence || layout.approval === phone
        || layout.phoneMessage !== phone || layout.overflow || !layout.navLabels
        || (!phone && (layout.sideBySide !== (width > 896) || !layout.actionsAboveEditor || layout.tabOverflow))) {
        problems.push(`${key}: unexpected authoring layout ${JSON.stringify(layout)}`);
      }
    }
    if (page === 'guided-authoring') {
      await evaluate('document.fonts.ready');
      const screenshot = await channel.send('Page.captureScreenshot', { format: 'png' });
      const view = query.split('=')[1] ?? 'workflow';
      writeFileSync(join(output, `guided-authoring-${view}-${width}.png`), Buffer.from(screenshot.data, 'base64'));
    }
    await evaluate(axeSource);
    const violations = await evaluate(`axe.run(document, { resultTypes: ['violations'], rules: ${JSON.stringify(fixtureRules)} }).then(result =>
      result.violations.map(v => ({ id: v.id, impact: v.impact, help: v.help, nodes: v.nodes.map(n => ({ target: n.target.join(' '), summary: n.failureSummary })) })))`);
    const allowed = new Set((knownViolations[key] ?? []).map(item => item.rule));
    for (const violation of violations) {
      report.push({ key, ...violation });
      if (allowed.has(violation.id)) continue;
      problems.push(`${key}: ${violation.id} (${violation.impact}) ${violation.help}\n` +
        violation.nodes.slice(0, 5).map(node => `    ${node.target}\n      ${node.summary.replace(/\n/g, ' ')}`).join('\n') +
        (violation.nodes.length > 5 ? `\n    ... and ${violation.nodes.length - 5} more` : ''));
    }
    console.log(`${key}: ${violations.length} violation group(s)`);
  }
} finally {
  writeFileSync(join(output, 'report.json'), JSON.stringify(report, null, 2));
  await session.close();
}

if (problems.length) {
  console.error('Accessibility checks failed in a real browser:\n' + problems.map(item => '- ' + item).join('\n'));
  process.exitCode = 1;
} else {
  console.log('Real-browser accessibility checks passed for', cases.length, 'page states.');
}
