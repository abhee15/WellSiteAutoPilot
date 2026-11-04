import { createServer as createHttpServer } from 'node:http';
import { spawn } from 'node:child_process';
import { Buffer } from 'node:buffer';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer as createViteServer } from 'vite';
import react from '@vitejs/plugin-react';

const delay = ms => new Promise(resolve => globalThis.setTimeout(resolve, ms));
const root = resolve(fileURLToPath(new URL('../', import.meta.url)));
const output = join(root, 'artifacts', 'visual-run-history');
mkdirSync(output, { recursive: true });
const chrome = [
  process.env.WSA_VISUAL_BROWSER,
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  '/usr/bin/chromium', '/usr/bin/google-chrome',
].filter(Boolean).find(file => existsSync(file));
if (!chrome) throw new Error('Chrome, Chromium or Edge is required for real browser layout checks.');

const vite = await createViteServer({
  root, configFile: false, plugins: [react()], server: { middlewareMode: true }, appType: 'mpa',
});
const server = createHttpServer((request, response) => vite.middlewares(request, response));
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const base = `http://127.0.0.1:${server.address().port}/tests/visual/run-history.html`;

// Headless Chrome command-line windows have a 500 CSS-pixel minimum and a 16-pixel scrollbar
// difference. Use CDP viewport emulation, rather than screenshots pretending to be 320px.
const profile = join(output, 'headless-profile');
mkdirSync(profile, { recursive: true });
const browser = spawn(chrome, [
  '--headless=new', '--disable-gpu', '--disable-extensions', '--no-first-run',
  '--no-default-browser-check', '--disable-dev-shm-usage', '--no-sandbox',
  '--disable-background-timer-throttling', '--remote-allow-origins=*',
  '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank',
], { stdio: 'ignore', windowsHide: true });
const portFile = join(profile, 'DevToolsActivePort');
let socket;
let channel;

async function connectDebugger() {
  let port;
  for (let tries = 0; tries < 100; tries++) {
    if (existsSync(portFile)) {
      const value = readFileSync(portFile, 'utf8').split(/\r?\n/)[0];
      if (/^\d+$/.test(value)) { port = Number(value); break; }
    }
    if (browser.exitCode !== null) throw new Error(`Browser exited early (code ${browser.exitCode})`);
    await delay(200);
  }
  if (!port) throw new Error('Headless browser did not expose its DevTools debugging port.');
  const targets = await (await globalThis.fetch(`http://127.0.0.1:${port}/json/list`)).json();
  const target = targets.find(item => item.type === 'page' && item.webSocketDebuggerUrl);
  if (!target) throw new Error('Browser did not expose a page target.');
  const webSocket = new globalThis.WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    webSocket.addEventListener('open', resolve, { once: true });
    webSocket.addEventListener('error', reject, { once: true });
  });
  let sequence = 0;
  const callbacks = new Map();
  webSocket.addEventListener('message', event => {
    const message = JSON.parse(String(event.data));
    if (!message.id || !callbacks.has(message.id)) return;
    const pending = callbacks.get(message.id);
    callbacks.delete(message.id);
    globalThis.clearTimeout(pending.timeout);
    if (message.error) pending.reject(new Error(JSON.stringify(message.error)));
    else pending.resolve(message.result ?? {});
  });
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++sequence;
    const timeout = globalThis.setTimeout(() => {
      callbacks.delete(id);
      reject(new Error(`DevTools request timed out: ${method}`));
    }, 15000);
    callbacks.set(id, { resolve, reject, timeout });
    webSocket.send(JSON.stringify({ id, method, params }));
  });
  return { webSocket, send };
}

const cases = [
  [1440, 900, 'selected'], [1024, 800, 'selected'], [900, 800, 'selected'],
  [768, 800, 'selected'], [390, 760, 'selected'], [320, 760, 'selected'],
  [390, 760, 'list'], [320, 760, 'list'],
  [1440, 900, 'selected', 'logic'], [1024, 800, 'selected', 'logic'],
  [900, 800, 'selected', 'logic'], [768, 800, 'selected', 'logic'],
  [390, 760, 'selected', 'logic'], [320, 760, 'selected', 'logic'],
];
const problems = [];

try {
  channel = await connectDebugger();
  socket = channel.webSocket;
  await channel.send('Page.enable');
  await channel.send('Runtime.enable');

  for (const [width, height, mode, layout = 'history'] of cases) {
    const url = `${base}?mode=${mode}${width >= 900 ? '&nav=expanded' : ''}${layout === 'logic' ? '&layout=logic' : ''}`;
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
      if (result?.viewport === width && result.mode === mode && result.logicMode === (layout === 'logic')) break;
      result = null;
    }
    if (!result) { problems.push(`${layout}/${width}/${mode}: no complete layout report from the browser`); continue; }

    const screenshot = await channel.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    writeFileSync(join(output, `${layout === 'logic' ? 'logic-' : ''}${width}-${mode}.png`), Buffer.from(screenshot.data, 'base64'));
    const issue = (condition, message) => { if (condition) problems.push(`${layout}/${width}/${mode}: ${message}`); };
    if (layout === 'logic') {
      issue(result.activityFocused !== 'true', 'Logic Activity did not enter focused workspace');
      issue(result.outerBrowseVisible, 'configured logic browse list remained visible in focused Activity');
      if (result.viewport > 900) {
        issue(result.outerSelectedOverflow !== 'hidden', 'focused outer selected pane must not be a nested scroll region');
      }
    }
    issue(result.documentWidth > result.viewportClient + 2,
      `horizontal document overflow (${result.documentWidth} > usable ${result.viewportClient})`);
    issue(result.bodyWidth > result.viewportClient + 2,
      `body overflows the usable viewport (${result.bodyWidth} > ${result.viewportClient})`);
    issue(result.pageBodyScroll > result.pageBodyClient + 2,
      `page content has horizontal scrolling (${result.pageBodyScroll} > ${result.pageBodyClient})`);
    issue(result.container < 170, `content region too narrow (${result.container}px)`);
    if (mode === 'selected') {
      issue(!result.inspectorVisible || !result.detailVisible, 'selected run inspector is not visible');
      issue(!result.selectedRun, 'selected run identity is missing');
      if (result.container > 900 && result.viewport > 900) {
        issue(!result.browseVisible, 'history must be visible beside details on wide screens');
        issue(!result.selectedRowVisible, 'selected history row must be highlighted on wide screens');
        issue(result.selectedRowColor === result.otherRowColor, 'selected row lacks visible highlighting');
        issue(result.browseRect?.right > result.inspectorRect?.left + 1, 'history and inspector overlap');
        issue(result.detailOverflow !== 'auto', 'inspector body must own its desktop scrolling');
      } else {
        issue(result.browseVisible, 'history pane must yield to details at narrow content widths');
        issue(!result.backVisible, 'Back to run history must be reachable at narrow widths');
      }
    } else {
      issue(!result.browseVisible, 'history pane must be visible after Back');
      issue(result.inspectorVisible, 'inspector must yield to history after Back');
    }
    console.log(`${width} × ${height} ${layout} ${mode}:`, JSON.stringify(result));
  }
} finally {
  socket?.close();
  browser.kill();
  await new Promise(resolve => server.close(resolve));
  await vite.close();
}

if (problems.length) {
  console.error('Responsive run inspector visual checks failed:\n' + problems.map(item => '- ' + item).join('\n'));
  process.exitCode = 1;
} else {
  console.log('Responsive run inspector browser geometry passed. Screenshots:', output);
}
