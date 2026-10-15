import type { AssetChoice } from './scopedAccessGrants';

const PAGE_SIZE = 200;
const MAX_DIRECTORY_ASSETS = 20_000;
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export class GrantAssetDirectoryError extends Error {
  constructor(public readonly kind: 'unavailable' | 'invalid' | 'tooLarge') {
    super(kind);
  }
}

/**
 * Read every server-authorized administrator directory page. Incomplete,
 * duplicated or malformed data must never be shown as a complete set of
 * resources that can be granted. Writes are validated again by the server.
 */
export async function loadGrantAssetDirectory(signal: AbortSignal,
  fetcher: typeof fetch = fetch): Promise<AssetChoice[]> {
  const assets: AssetChoice[] = [];
  const ids = new Set<string>();
  for (let offset = 0; offset <= MAX_DIRECTORY_ASSETS; offset += PAGE_SIZE) {
    let response: Response;
    try {
      response = await fetcher('/api/v1/security/assets/directory?offset=' +
        offset + '&limit=' + PAGE_SIZE, {
        credentials: 'same-origin', cache: 'no-store', signal,
      });
    } catch (error) {
      if (signal.aborted) throw error;
      throw new GrantAssetDirectoryError('unavailable');
    }
    if (!response.ok) throw new GrantAssetDirectoryError('unavailable');
    let body: unknown;
    try { body = await response.json(); }
    catch { throw new GrantAssetDirectoryError('invalid'); }
    if (!Array.isArray(body) || body.length > PAGE_SIZE) {
      throw new GrantAssetDirectoryError('invalid');
    }
    for (const item of body) {
      if (!item || typeof item !== 'object' || Array.isArray(item)) {
        throw new GrantAssetDirectoryError('invalid');
      }
      const { id, name, isActive } = item as Record<string, unknown>;
      if (typeof id !== 'string' || !guid.test(id) ||
        typeof name !== 'string' || !name.trim() ||
        typeof isActive !== 'boolean' || ids.has(id.toLowerCase())) {
        throw new GrantAssetDirectoryError('invalid');
      }
      ids.add(id.toLowerCase());
      assets.push({ id, name, isActive });
    }
    if (assets.length > MAX_DIRECTORY_ASSETS) {
      throw new GrantAssetDirectoryError('tooLarge');
    }
    if (body.length < PAGE_SIZE) return assets;
  }
  throw new GrantAssetDirectoryError('tooLarge');
}
