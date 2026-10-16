import type { ProposedAuthorizedAssetResponse, ProposedAssetAccessResponse } from '../api/generated/v1/types.gen';
import { isSecurityId } from '../security/identifiers';

export type ProposedAuthorizedAsset = ProposedAuthorizedAssetResponse & { id: string; name: string };
export type ProposedAssetAccess = Omit<ProposedAssetAccessResponse, 'assets'> & {
  userId: string;
  permission: string;
  offset: number;
  limit: number;
  assets: ProposedAuthorizedAsset[];
  isDiagnosticOnly: true;
};
export class ProposedAssetAccessError extends Error {
  constructor(public readonly kind: 'invalid' | 'unavailable' | 'invalidResponse' | 'denied') {
    super(kind);
  }
}
const permissionKey = /^[a-z][a-z0-9.-]{0,127}$/;

/** The server is authoritative; never derive allowed Assets by combining
 * a user's role permissions with a separate union of Asset IDs on the client. */
export async function loadProposedAssetAccess(
  userId: string, permission: string, offset: number, signal: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<ProposedAssetAccess> {
  const limit = 100;
  if (!isSecurityId(userId) || !permissionKey.test(permission) ||
    !Number.isSafeInteger(offset) || offset < 0 || offset > 1_000_000) {
    throw new ProposedAssetAccessError('invalid');
  }
  let response: Response;
  try {
    response = await fetcher('/api/v1/security/users/' + encodeURIComponent(userId) +
      '/proposed-assets?permission=' + encodeURIComponent(permission) +
      '&offset=' + offset + '&limit=' + limit, {
      credentials: 'same-origin', cache: 'no-store', signal,
    });
  } catch (error) {
    if (signal.aborted) throw error;
    throw new ProposedAssetAccessError('unavailable');
  }
  if (!response.ok) {
    throw new ProposedAssetAccessError(
      response.status === 401 || response.status === 403 ? 'denied' : 'unavailable');
  }
  let body: unknown;
  try { body = await response.json(); }
  catch { throw new ProposedAssetAccessError('invalidResponse'); }
  if (!body || typeof body !== 'object' || Array.isArray(body)) {
    throw new ProposedAssetAccessError('invalidResponse');
  }
  const row = body as Partial<ProposedAssetAccess>;
  if (response.status !== 200 || row.isDiagnosticOnly !== true ||
    typeof row.userId !== 'string' ||
    row.userId.toLowerCase() !== userId.toLowerCase() ||
    row.permission !== permission || row.offset !== offset || row.limit !== limit ||
    !Array.isArray(row.assets) || row.assets.length > limit ||
    !row.assets.every(asset =>
      !!asset && typeof asset === 'object' && !Array.isArray(asset) &&
      typeof asset.id === 'string' && isSecurityId(asset.id) &&
      typeof asset.name === 'string' && !!asset.name.trim()) ||
    new Set(row.assets.map(asset => asset.id.toLowerCase())).size !== row.assets.length) {
    throw new ProposedAssetAccessError('invalidResponse');
  }
  return row as ProposedAssetAccess;
}
