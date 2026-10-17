// Generates the TypeScript API client from the running Server's OpenAPI document.
// The client is not committed, so a fresh checkout cannot run the frontend until this has run.
// Used by the Aspire AppHost once the Server is up; it can also be run by hand.
//
//   WSA_API_BASE         Server address (default http://127.0.0.1:5080)
//   WSA_REPO_ROOT        Repository root (default: two folders above this script's folder)
//   WSA_GENERATE_COMMAND Command that generates the client (default: npm run generate-api)
//   WSA_WAIT_SECONDS     How long to wait for the Server (default 120)
import { spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));

export async function waitForOpenApi(base, waitSeconds, fetchImpl = fetch) {
  const url = new URL('/swagger/v1/swagger.json', base);
  const deadline = Date.now() + waitSeconds * 1000;
  let lastProblem = 'no response';
  while (Date.now() < deadline) {
    try {
      const response = await fetchImpl(url);
      if (response.ok) {
        const document = await response.json();
        if (document && typeof document === 'object' && document.paths && Object.keys(document.paths).length > 0) {
          return document;
        }
        lastProblem = 'the document had no paths';
      } else {
        lastProblem = `HTTP ${response.status}`;
      }
    } catch (error) {
      lastProblem = error instanceof Error ? error.message : String(error);
    }
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw new Error(`The Server did not publish OpenAPI at ${url} in time (${lastProblem}).`);
}

export function runCommand(command, cwd) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, { cwd, shell: true, stdio: 'inherit' });
    child.on('error', reject);
    child.on('exit', code => (code === 0 ? resolve() : reject(new Error(`"${command}" exited with code ${code}.`))));
  });
}

export async function main(environment = process.env) {
  const base = environment.WSA_API_BASE || 'http://127.0.0.1:5080';
  const root = environment.WSA_REPO_ROOT || path.resolve(here, '..', '..');
  const command = environment.WSA_GENERATE_COMMAND || 'npm run generate-api';
  const waitSeconds = Number(environment.WSA_WAIT_SECONDS || 120);

  const document = await waitForOpenApi(base, waitSeconds);
  const target = path.join(root, 'artifacts', 'openapi', 'v1', 'server.json');
  await mkdir(path.dirname(target), { recursive: true });
  await writeFile(target, JSON.stringify(document, null, 2));
  await runCommand(command, path.join(root, 'src', 'web'));
  console.log('API client generated.');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(error => {
    console.error(error instanceof Error ? error.message : error);
    process.exit(1);
  });
}
