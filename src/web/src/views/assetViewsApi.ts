/** Explicit well selections, visible through the owning Connection. */
export const MAX_VIEW_NAME = 48;
export const VIEW_PAGE_SIZE = 100;
export type AssetView = {
  id: string; connectionId: string; name: string; revision: number; memberCount: number;
  hasUnavailableMembers: boolean; createdAtUtc: string; updatedAtUtc: string; canEdit: boolean; canDelete: boolean;
};
export type ViewAsset = { id: string; assetTypeId: string; name: string; parentAssetId: string | null;
  ownerConnectionId: string | null; isActive: boolean };
export type ViewsErrorKind = 'accessDenied' | 'notFound' | 'nameTaken' | 'stale' | 'invalid' | 'invalidResponse' | 'unavailable';
export class ViewsError extends Error {
  constructor(public readonly kind: ViewsErrorKind) { super(kind); this.name = 'ViewsError'; }
}
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
function one(value: unknown): AssetView {
  if (!object(value) || typeof value.id !== 'string' || typeof value.connectionId !== 'string' ||
    typeof value.name !== 'string' || !Number.isSafeInteger(value.revision) || !Number.isSafeInteger(value.memberCount) ||
    typeof value.canEdit !== 'boolean' || typeof value.canDelete !== 'boolean' || typeof value.hasUnavailableMembers !== 'boolean' ||
    typeof value.createdAtUtc !== 'string' || typeof value.updatedAtUtc !== 'string') throw new ViewsError('invalidResponse');
  return value as unknown as AssetView;
}
async function send(path: string, init: RequestInit, fetcher: typeof fetch = fetch): Promise<unknown> {
  let response: Response;
  try { response = await fetcher('/api/v1/asset-views' + path, { credentials: 'same-origin', cache: 'no-store', ...init }); }
  catch (error) { if (init.signal?.aborted) throw error; throw new ViewsError('unavailable'); }
  if (!response.ok) {
    let code = '';
    try { const body: unknown = await response.json(); if (object(body) && typeof body.code === 'string') code = body.code; } catch { /* status is sufficient */ }
    throw new ViewsError(response.status === 401 || response.status === 403 ? 'accessDenied'
      : response.status === 404 ? 'notFound' : response.status === 409 ? (code === 'ASSET_VIEW_NAME_CONFLICT' ? 'nameTaken' : 'stale')
        : response.status === 400 ? 'invalid' : 'unavailable');
  }
  if (response.status === 204) return null;
  try { return await response.json(); } catch { throw new ViewsError('invalidResponse'); }
}
const json = (method: string, body: unknown): RequestInit => ({ method,
  headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
export async function loadViews(signal: AbortSignal): Promise<AssetView[]> {
  const body = await send('', { signal });
  if (!Array.isArray(body)) throw new ViewsError('invalidResponse');
  return body.map(one);
}
export async function createView(name: string, connectionId: string, assetIds: string[]): Promise<AssetView> {
  return one(await send('', json('POST', { name, connectionId, assetIds })));
}
export async function updateView(view: AssetView, name: string, assetIds: string[]): Promise<AssetView> {
  return one(await send('/' + encodeURIComponent(view.id), json('PUT', { expectedRevision: view.revision, name, assetIds })));
}
export async function deleteView(view: AssetView): Promise<void> {
  await send('/' + encodeURIComponent(view.id) + '?expectedRevision=' + view.revision, { method: 'DELETE' });
}
export async function loadViewSelection(view: AssetView, signal: AbortSignal): Promise<string[]> {
  const body = await send('/' + encodeURIComponent(view.id) + '/selection', { signal });
  if (!object(body) || !Array.isArray(body.assetIds) || !body.assetIds.every(id => typeof id === 'string')) throw new ViewsError('invalidResponse');
  if (body.revision !== view.revision) throw new ViewsError('stale');
  return body.assetIds as string[];
}
export async function loadViewMembers(viewId: string, offset: number, signal: AbortSignal, query = ''): Promise<ViewAsset[]> {
  const body = await send('/' + encodeURIComponent(viewId) + '/members?offset=' + offset + '&limit=' + VIEW_PAGE_SIZE + '&q=' + encodeURIComponent(query), { signal });
  if (!Array.isArray(body) || !body.every(item => object(item) && typeof item.id === 'string' && typeof item.name === 'string'))
    throw new ViewsError('invalidResponse');
  return body as ViewAsset[];
}
