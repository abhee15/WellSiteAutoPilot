import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { stdout } from 'node:process';
import { URL, fileURLToPath } from 'node:url';
import { format, resolveConfig } from 'prettier';

// Temporary diagnostic: obtain the repository's exact formatter output; remove before merging.
test('diagnostic: format authoring components with repository settings', async () => {
  for (const filename of ['GuidedAuthoringPage.tsx', 'GuidedModuleContract.tsx']) {
    const file = fileURLToPath(new URL('../src/views/' + filename, import.meta.url));
    const formatted = await format(readFileSync(file, 'utf8'), { ...(await resolveConfig(file)), filepath: file });
    stdout.write('BEGIN_FORMATTED_' + filename + '\n' + formatted + 'END_FORMATTED_' + filename + '\n');
  }
});
