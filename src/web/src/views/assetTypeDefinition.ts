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

/** Where JSON text stops being valid, for a message a person can act on; null when it parses. */
export function jsonSyntaxProblem(json: string): { line: number; column: number } | null {
  try { JSON.parse(json); return null; } catch (reason) {
    const message = reason instanceof Error ? reason.message : '';
    const position = /position (\d+)/.exec(message);
    if (position) {
      const offset = Math.min(Number(position[1]), json.length);
      const before = json.slice(0, offset);
      return { line: before.split('\n').length, column: offset - before.lastIndexOf('\n') };
    }
    // The error runs to the end of the text (for example, a missing closing brace).
    const lines = json.split('\n');
    return { line: lines.length, column: lines[lines.length - 1].length + 1 };
  }
}
