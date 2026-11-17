import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { format, resolveConfig } from 'prettier';

// Temporary diagnostic: capture the repository's exact Prettier output; remove once the guard is green.
test('diagnostic: format authoring components using repository settings', async () => {
  for (const filename of ['GuidedAuthoringPage.tsx', 'GuidedModuleContract.tsx']) {
    const file = fileURLToPath(new URL('../src/views/' + filename, import.meta.url));
    const formatted = await format(readFileSync(file, 'utf8'), { ...(await resolveConfig(file)), filepath: file });
    console.log('BEGIN_FORMATTED_' + filename);
    console.log(formatted);
    console.log('END_FORMATTED_' + filename);
  }
});
