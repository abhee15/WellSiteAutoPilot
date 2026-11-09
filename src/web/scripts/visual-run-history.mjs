import { Buffer } from 'node:buffer';
import { join } from 'node:path';
import { writeFileSync } from 'node:fs';
import { root, delay, startVisualBrowser } from './lib/visual-browser.mjs';

const adapters = process.argv.includes('--adapters');
const output = join(root, 'artifacts', adapters ? 'visual-adapter-operations' : 'visual-run-history');
// Headless Chrome command-line windows have a 500 CSS-pixel minimum and a 16-pixel scrollbar
// difference. The shared browser uses CDP viewport emulation, rather than screenshots pretending to be 320px.
const browserSession = await startVisualBrowser(output);
const { channel } = browserSession;
const base = `${browserSession.origin}/tests/visual/${adapters ? 'adapter-operations' : 'run-history'}.html`;
const cases = [
  [1440, 900, 'selected'], [1024, 800, 'selected'], [900, 800, 'selected'],
  [768, 800, 'selected'], [390, 760, 'selected'], [320, 760, 'selected'],
  [390, 760, 'list'], [320, 760, 'list'],
];
if (adapters) cases.push([1440, 900, 'examples'], [390, 760, 'examples'], [320, 760, 'examples']);
const problems = [];

try {
  for (const [width, height, mode] of cases) {
    const url = `${base}?mode=${mode}${width >= 900 ? '&nav=expanded' : ''}`;
    await channel.send('Emulation.setDeviceMetricsOverride', {
      width, height, deviceScaleFactor: 1, mobile: false,
    });
    await channel.send('Page.navigate', { url });
    let result = null;
    for (let attempts = 0; attempts < 80; attempts++) {
      await delay(250);
      const evaluated = await channel.send('Runtime.evaluate', {
        expression: "document.getElementById('visual-report')?.textContent",
        returnByValue: true,
      });
      const value = evaluated.result?.value;
      if (!value?.startsWith('VISUAL_REPORT:') || !value.endsWith(':END')) continue;
      try { result = JSON.parse(value.slice('VISUAL_REPORT:'.length, -':END'.length)); } catch { /* wait for complete report */ }
      if (result?.viewport === width && result.mode === mode) break;
      result = null;
    }
    if (!result) { problems.push(`${width}/${mode}: no complete layout report from the browser`); continue; }

    const screenshot = await channel.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    writeFileSync(join(output, `${width}-${mode}.png`), Buffer.from(screenshot.data, 'base64'));
    const issue = (condition, message) => { if (condition) problems.push(`${width}/${mode}: ${message}`); };
    issue(result.documentWidth > result.viewportClient + 2,
      `horizontal document overflow (${result.documentWidth} > usable ${result.viewportClient})`);
    issue(result.bodyWidth > result.viewportClient + 2,
      `body overflows the usable viewport (${result.bodyWidth} > ${result.viewportClient})`);
    issue(result.pageBodyScroll > result.pageBodyClient + 2,
      `page content has horizontal scrolling (${result.pageBodyScroll} > ${result.pageBodyClient})`);
    issue(result.container < 170, `content region too narrow (${result.container}px)`);
    if (adapters) {
      issue(result.rowDisplay !== 'grid', 'operation list styling is missing');
      issue(!result.adapterSelectorBackground || result.adapterSelectorBackground === 'rgba(0, 0, 0, 0)', 'adapter select lacks a surface background');
      issue(result.adapterSelectorBackground === result.adapterSelectorText, 'adapter select has identical background/text colors');
    }
    if (adapters && mode === 'selected') {
      issue(result.contractWidth < 479, 'contract columns are compressed below a readable width');
      issue(result.fieldNameCase !== 'none', 'API field identifiers must retain their declared casing');
    }
    if (mode !== 'list') {
      if (adapters && mode === 'examples') issue(!result.exampleVisible, 'API examples are not visible');
      issue(!result.inspectorVisible || !result.detailVisible, 'selected run inspector is not visible');
      issue(!(adapters ? result.selectedOperation : result.selectedRun), 'selected identity is missing');
      if (result.viewport > 900 && (adapters || result.container > 900)) {
        issue(!result.browseVisible, 'history must be visible beside details on wide screens');
        issue(!result.selectedRowVisible, 'selected history row must be highlighted on wide screens');
        issue(result.selectedRowColor === result.otherRowColor, 'selected row lacks visible highlighting');
        issue(result.browseRect?.right > result.inspectorRect?.left + 1, 'history and inspector overlap');
        if (adapters) {
          issue(Math.abs(result.browseRect?.top - result.inspectorRect?.top) > 2, 'adapter panes start at different heights');
          issue(Math.abs(result.browseRect?.bottom - result.inspectorRect?.bottom) > 2, 'adapter panes end at different heights');
        }
        issue(result.detailOverflow !== 'auto', 'inspector body must own its desktop scrolling');
      } else {
        issue(result.browseVisible, 'history pane must yield to details at narrow content widths');
        issue(!result.backVisible, 'Back to run history must be reachable at narrow widths');
      }
    } else {
      issue(!result.browseVisible, 'history pane must be visible after Back');
      issue(result.inspectorVisible, 'inspector must yield to history after Back');
    }
    console.log(`${width} × ${height} ${mode}:`, JSON.stringify(result));
  }
} finally {
  await browserSession.close();
}

if (problems.length) {
  console.error('Responsive run inspector visual checks failed:\n' + problems.map(item => '- ' + item).join('\n'));
  process.exitCode = 1;
} else {
  console.log('Responsive run inspector browser geometry passed. Screenshots:', output);
}
