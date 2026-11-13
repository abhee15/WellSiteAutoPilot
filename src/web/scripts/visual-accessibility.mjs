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
    if (page === 'guided-authoring') {
      const layout = await evaluate(`(() => {
        const visible = selector => Boolean(document.querySelector(selector)?.getClientRects().length);
        return {
          editor: visible('.guided-authoring-editor'),
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
      if (layout.editor === phone || !layout.review || !layout.evidence || layout.approval === phone
        || layout.phoneMessage !== phone || layout.overflow || !layout.navLabels) {
        problems.push(`${key}: unexpected authoring layout ${JSON.stringify(layout)}`);
      }
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
