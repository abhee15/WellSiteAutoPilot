// Runs the frontend checks that do not need the generated API client (lint, node tests, interaction tests),
// the way CI does. The client is built from the server's OpenAPI file, which needs a .NET build; where it is absent
// a stand-in for the one generated module the views import is created for the run and always removed afterwards.
// The type check is the one thing this cannot do: `npm run typecheck` runs in CI right after the client is generated.
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, rmSync, rmdirSync, writeFileSync } from 'node:fs';

const generated = new URL('../src/api/generated/v1/', import.meta.url);
const sdk = new URL('sdk.gen.ts', generated);
const standIn = !existsSync(sdk);
if (standIn) {
  mkdirSync(generated, { recursive: true });
  writeFileSync(sdk, 'export const uploadLogicModulePackage = async () => ({});\nexport const getLogicModulePackageState = async () => ({});\n' + ['getAuthoringCapabilities', 'listAuthoringDrafts', 'createAuthoringDraft', 'getAuthoringDraft', 'updateAuthoringDraft', 'validateAuthoringDraft', 'buildAuthoringDraft', 'cancelAuthoringDraftBuild', 'downloadAuthoringDraftPackage', 'addAuthoringDraftToCatalog'].map(name => `export const ${name} = async () => ({});\n`).join(''));
}
const steps = [
  ['lint', ['run', 'lint']],
  ['node tests', ['run', 'test:ui']],
  ['interaction tests', ['run', 'test:interaction']],
];
let failed = null;
try {
  for (const [name, args] of steps) {
    console.log(`\n== ${name}`);
    const result = spawnSync('npm', args, { stdio: 'inherit', shell: process.platform === 'win32' });
    if (result.status !== 0) { failed = name; break; }
  }
} finally {
  if (standIn) {
    rmSync(sdk, { force: true });
    try { rmdirSync(generated); } catch { /* the folder holds other generated files */ }
  }
}
if (failed) { console.error(`\n${failed} failed`); process.exit(1); }
console.log('\nAll local checks passed. Type errors are caught by `npm run typecheck` in CI.');
