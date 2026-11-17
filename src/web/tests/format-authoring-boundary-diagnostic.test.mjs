import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { stdout } from 'node:process';
import { URL, fileURLToPath } from 'node:url';
import { format, resolveConfig } from 'prettier';

// Temporary diagnostic; delete after applying repository-exact Prettier output.
test('diagnostic: format authoring execution boundary files', async () => {
  for (const filename of ['GuidedModuleContract.tsx', 'guidedAuthoring.css']) {
    const file = fileURLToPath(new URL('../src/views/' + filename, import.meta.url));
    const formatted = await format(readFileSync(file, 'utf8'), { ...(await resolveConfig(file)), filepath: file });
    stdout.write('BEGIN_FORMATTED_' + filename + '\n' + formatted + 'END_FORMATTED_' + filename + '\n');
  }
});
