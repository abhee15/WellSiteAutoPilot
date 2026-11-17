import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { format, resolveConfig } from 'prettier';

// Temporary diagnostic: capture the repository's exact Prettier output; remove after updating the file.
test('diagnostic: show formatted authoring contract component', async () => {
  const file = fileURLToPath(new URL('../src/views/GuidedModuleContract.tsx', import.meta.url));
  const formatted = await format(readFileSync(file, 'utf8'), { ...(await resolveConfig(file)), filepath: file });
  console.log('BEGIN_GUIDED_CONTRACT_FORMATTED');
  console.log(formatted);
  console.log('END_GUIDED_CONTRACT_FORMATTED');
});
