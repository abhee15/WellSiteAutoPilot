import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'vite';

let vite, checkAssetTypeDraft, problemCode;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  ({ checkAssetTypeDraft, problemCode } = await vite.ssrLoadModule('/src/views/assetTypeDefinition.ts'));
});
after(async () => { await vite?.close(); });

const ok = { key: 'well', displayName: 'Well', schemaJson: '{}' };

test('a complete draft passes', () => assert.equal(checkAssetTypeDraft(ok), null));
test('key and name are required and bounded', () => {
  assert.equal(checkAssetTypeDraft({ ...ok, key: '  ' }), 'key');
  assert.equal(checkAssetTypeDraft({ ...ok, key: 'k'.repeat(129) }), 'key');
  assert.equal(checkAssetTypeDraft({ ...ok, displayName: '' }), 'name');
  assert.equal(checkAssetTypeDraft({ ...ok, displayName: 'n'.repeat(257) }), 'name');
});
test('schema must be a JSON object within 32 KiB', () => {
  assert.equal(checkAssetTypeDraft({ ...ok, schemaJson: '{' }), 'schemaSyntax');
  assert.equal(checkAssetTypeDraft({ ...ok, schemaJson: '[]' }), 'schemaShape');
  assert.equal(checkAssetTypeDraft({ ...ok, schemaJson: 'null' }), 'schemaShape');
  assert.equal(checkAssetTypeDraft({ ...ok, schemaJson: '{"a":"' + 'x'.repeat(33 * 1024) + '"}' }), 'schemaSize');
});
test('problemCode accepts only stable upper-case codes', () => {
  assert.equal(problemCode({ code: 'ASSET_TYPE_KEY_CONFLICT' }), 'ASSET_TYPE_KEY_CONFLICT');
  assert.equal(problemCode({ code: 'not a code' }), null);
  assert.equal(problemCode(null), null);
});
