import { createServer as createHttpServer } from 'node:http';
import { spawn } from 'node:child_process';
import { appendFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer as createViteServer } from 'vite';
import react from '@vitejs/plugin-react';

export const delay = ms => new Promise(resolve => globalThis.setTimeout(resolve, ms));
export const root = resolve(fileURLToPath(new URL('../../', import.meta.url)));

/**
 * Starts Vite for tests/visual/*.html and a headless Chrome, Chromium or Edge driven over DevTools. Shared by the
 * layout checks and the accessibility check so both run in the same real browser. Call close() when finished.
 * Returns { origin, channel, close } where origin serves /tests/visual/<page>.html.
 */
export async function startVisualBrowser(output) {
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
  root, configFile: false, plugins: [react()],
  server: { middlewareMode: true, watch: { ignored: ['**/artifacts/**'] } }, appType: 'mpa',
});
const server = createHttpServer((request, response) => vite.middlewares(request, response));
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;

// Headless Chrome command-line windows have a 500 CSS-pixel minimum and a 16-pixel scrollbar
// difference. Use CDP viewport emulation, rather than screenshots pretending to be 320px.
// Isolate each run from prior profiles and the Vite watcher (Windows locks browser cache files).
const profile = mkdtempSync(join(tmpdir(), 'wsa-run-history-'));
const browserLog = join(output, 'browser-startup.log');
writeFileSync(browserLog, JSON.stringify({ browser: chrome, profile }) + '\n');
let diagnostics = '';
let spawnError = null;
const capture = chunk => {
  appendFileSync(browserLog, chunk);
  diagnostics = (diagnostics + String(chunk)).slice(-8000);
};
const browser = spawn(chrome, [
  '--headless=new', '--disable-gpu', '--disable-extensions', '--no-first-run',
  '--no-default-browser-check', '--disable-dev-shm-usage', '--no-sandbox',
  '--disable-background-timer-throttling', '--remote-allow-origins=*', '--enable-logging=stderr',
  '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank',
], { stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });
browser.stdout.on('data', capture);
browser.stderr.on('data', capture);
browser.on('error', error => { spawnError = error; capture(String(error)); });
const portFile = join(profile, 'DevToolsActivePort');
let socket;
let channel;

async function connectDebugger() {
  const failure = message => new Error(`${message} See ${browserLog}.\n${diagnostics.slice(-4000)}`);
  let target;
  const deadline = Date.now() + 45000;
  while (Date.now() < deadline) {
    if (spawnError) throw failure(`Browser could not start: ${spawnError.message}`);
    if (browser.exitCode !== null) throw failure(`Browser exited early (code ${browser.exitCode})`);
    try {
      const value = readFileSync(portFile, 'utf8').split(/\r?\n/)[0];
      if (/^\d+$/.test(value)) {
        const response = await globalThis.fetch(`http://127.0.0.1:${Number(value)}/json/list`, { signal: globalThis.AbortSignal.timeout(1000) });
        if (response.ok) {
          const targets = await response.json();
          target = targets.find(item => item.type === 'page' && item.webSocketDebuggerUrl);
          if (target) break;
        }
      }
    } catch {
      // The profile, port file and HTTP page target become ready at different points during startup.
    }
    await delay(200);
  }
  if (!target) throw failure('Browser did not expose a DevTools page target within 45 seconds.');
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

channel = await connectDebugger();
socket = channel.webSocket;
await channel.send('Page.enable');
await channel.send('Runtime.enable');
const close = async () => {
  socket?.close();
  browser.kill();
  await new Promise(resolve => server.close(resolve));
  await vite.close();
  try { rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 }); }
  catch { console.warn('Temporary browser profile remains locked:', profile); }
};
return { origin, channel, close };
}
