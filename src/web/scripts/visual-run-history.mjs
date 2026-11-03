import { createServer as createHttpServer } from 'node:http';
import { execFile } from 'node:child_process';
import { existsSync, mkdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { createServer as createViteServer } from 'vite';
import react from '@vitejs/plugin-react';

const exec = promisify(execFile);
const root = resolve(fileURLToPath(new URL('../', import.meta.url)));
const output = join(root, 'artifacts', 'visual-run-history');
mkdirSync(output, { recursive: true });
const candidates = [
  process.env.WSA_VISUAL_BROWSER,
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  '/usr/bin/chromium', '/usr/bin/google-chrome',
].filter(Boolean);
const chrome = candidates.find(file => existsSync(file));
if (!chrome) throw new Error('A local Chrome, Chromium or Edge browser is required for real-layout visual checks.');

const vite = await createViteServer({
  root, configFile: false, plugins: [react()], server: { middlewareMode: true },
  appType: 'mpa',
});
const server = createHttpServer((request, response) => vite.middlewares(request, response));
await new Promise((resolve, reject) => server.listen(0, '127.0.0.1', err => err ? reject(err) : resolve()));
const address = server.address();
const base = `http://127.0.0.1:${address.port}/tests/visual/run-history.html`;
const cases = [
  [1440, 900, 'selected'], [1024, 800, 'selected'], [900, 800, 'selected'],
  [768, 800, 'selected'], [390, 760, 'selected'], [320, 760, 'selected'],
  [390, 760, 'list'], [320, 760, 'list'],
];
let problems = [];

try {
  for (const [width, height, mode] of cases) {
    const picture = join(output, `${width}-${mode}.png`);
    const url = `${base}?mode=${mode}${width >= 900 ? '&nav=expanded' : ''}`;
    const flags = [
      '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
      '--disable-extensions', '--disable-dev-shm-usage', '--no-sandbox',
      '--force-device-scale-factor=1', '--virtual-time-budget=5000',
      `--window-size=${width},${height}`,
      `--screenshot=${picture}`, '--dump-dom', url,
    ];
    let stdout;
    try {
      ({ stdout } = await exec(chrome, flags, { timeout: 45000, maxBuffer: 6 * 1024 * 1024, windowsHide: true }));
    } catch (error) {
      problems.push(`${width}/${mode}: Chrome failed: ${error.message}`);
      continue;
    }
    const match = stdout.match(/VISUAL_REPORT:([^<]*?):END/);
    if (!match) {
      problems.push(`${width}/${mode}: no layout report from rendered component`);
      continue;
    }
    let result;
    try { result = JSON.parse(match[1]); } catch {
      problems.push(`${width}/${mode}: invalid layout report`);
      continue;
    }
    const issue = (condition, message) => { if (condition) problems.push(`${width}/${mode}: ${message}`); };
    issue(Math.abs(result.viewport - width) > 8,
      `browser viewport ${result.viewport}px differs from requested ${width}px`);
    issue(result.documentWidth > result.viewport + 2,
      `horizontal document overflow (${result.documentWidth} > ${result.viewport})`);
    issue(result.container < 170, `content region too narrow (${result.container}px)`);
    if (mode === 'selected') {
      issue(!result.inspectorVisible || !result.detailVisible, 'selected run inspector is not visible');
      issue(!result.selectedRun, 'selected run identity is missing');
      if (result.container > 900 && result.viewport > 900) {
        issue(!result.browseVisible, 'history must be visible beside selected details on wide screens');
        issue(!result.selectedRowVisible, 'selected history row must remain visible on wide screens');
        issue(result.selectedRowColor === result.otherRowColor, 'selected row has no visible highlight');
        issue(result.browseRect?.right > result.inspectorRect?.left + 1, 'history and inspector overlap');
        issue(result.detailOverflow !== 'auto', 'inspector body must own its desktop scrolling');
      } else {
        issue(result.browseVisible, 'history browse pane must yield to details at narrow content widths');
        issue(!result.backVisible, 'Back to run history must be reachable at narrow widths');
      }
    } else {
      issue(!result.browseVisible, 'history browse pane must be visible after Back');
      issue(result.inspectorVisible, 'inspector must yield to history after Back');
    }
    console.log(`${width} × ${height} ${mode}:`, JSON.stringify(result));
  }
} finally {
  await new Promise(resolve => server.close(resolve));
  await vite.close();
}

if (problems.length) {
  console.error('Responsive run inspector visual checks failed:\n' + problems.map(item => '- ' + item).join('\n'));
  process.exitCode = 1;
} else {
  console.log('Responsive run inspector browser geometry passed. Screenshots:', output);
}
