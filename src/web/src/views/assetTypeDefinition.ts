/** Client-side checks for the Define Asset Type form. The server stays authoritative
 *  (bounded JSON Schema subset, see docs/architecture/asset-attribute-schema-v1.md);
 *  these only catch mistakes that do not need a round trip. */
export const MAX_ASSET_TYPE_KEY = 128;
export const MAX_ASSET_TYPE_NAME = 256;
export const MAX_ASSET_TYPE_SCHEMA_BYTES = 32 * 1024;

export type AssetTypeDraft = { key: string; displayName: string; schemaJson: string };
export type AssetTypeDraftProblem =
  | 'key' | 'name' | 'schemaSyntax' | 'schemaShape' | 'schemaSize';

export function checkAssetTypeDraft(draft: AssetTypeDraft): AssetTypeDraftProblem | null {
  const key = draft.key.trim();
  if (!key || key.length > MAX_ASSET_TYPE_KEY) return 'key';
  const name = draft.displayName.trim();
  if (!name || name.length > MAX_ASSET_TYPE_NAME) return 'name';
  if (new TextEncoder().encode(draft.schemaJson).length > MAX_ASSET_TYPE_SCHEMA_BYTES) return 'schemaSize';
  let parsed: unknown;
  try { parsed = JSON.parse(draft.schemaJson); } catch { return 'schemaSyntax'; }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return 'schemaShape';
  return null;
}

/** Stable server failure code from an application/problem+json body, if present. */
export function problemCode(body: unknown): string | null {
  if (!body || typeof body !== 'object') return null;
  const code = (body as { code?: unknown }).code;
  return typeof code === 'string' && /^[A-Z][A-Z0-9_]{0,99}$/.test(code) ? code : null;
}
