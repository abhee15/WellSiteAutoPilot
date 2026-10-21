/** Personal, server-evaluated Views: a saved query and arrangement over the Assets a person may already see. */
export type ViewLevel = 'connection' | 'assetType' | 'status';
export const VIEW_LEVELS: readonly ViewLevel[] = ['connection', 'assetType', 'status'];
export const STANDALONE = 'standalone';
export const MAX_VIEW_NAME = 48;
export const MAX_VIEW_LEVELS = 3;

export type ViewActivity = 'all' | 'active' | 'inactive';

export type ViewDefinition = {
  version: 1;
  /** Connection ids, or `standalone`; empty means every owner the person can see. */
  connections: string[];
  assetTypeIds: string[];
  activity: ViewActivity;
  search: string | null;
  grouping: ViewLevel[];
};

export type ViewVisibility = 'personal' | 'shared';

export type AssetView = {
  id: string; name: string; definition: ViewDefinition; revision: number; createdAtUtc: string; updatedAtUtc: string;
  visibility: ViewVisibility;
  /** Who owns it; shown next to a shared View. */
  ownerName: string;
  isOwner: boolean;
  /** Only the owner edits, and a shared View only while they may still share. Everyone else saves a copy. */
  canEdit: boolean;
  /** Connections the View names that this person cannot see (never named, only counted). */
  hiddenConnectionCount: number;
};

export type ViewGroup = { key: string; label: string; count: number };
export type ViewAsset = {
  id: string; assetTypeId: string; name: string; parentAssetId: string | null; ownerConnectionId: string | null;
  isActive: boolean;
};
/** `level` is set when `groups` are returned and null when the last level was reached and `assets` are returned. */
export type ViewNodes = { level: ViewLevel | null; groups: ViewGroup[]; assets: ViewAsset[] };

export type ViewsErrorKind = 'accessDenied' | 'notFound' | 'nameTaken' | 'sharedNameTaken' | 'shareForbidden' | 'stale' | 'limit' | 'invalid' | 'invalidResponse' | 'unavailable';

export class ViewsError extends Error {
  constructor(public readonly kind: ViewsErrorKind) {
    super(kind);
    this.name = 'ViewsError';
  }
}

export const emptyDefinition = (): ViewDefinition =>
  ({ version: 1, connections: [], assetTypeIds: [], activity: 'all', search: null, grouping: [] });

const isObject = (value: unknown): value is Record<string, unknown> =>
  !!value && typeof value === 'object' && !Array.isArray(value);
const strings = (value: unknown): value is string[] => Array.isArray(value) && value.every(item => typeof item === 'string');

function toDefinition(value: unknown): ViewDefinition | null {
  if (!isObject(value) || value.version !== 1 || !strings(value.connections) || !strings(value.assetTypeIds)) return null;
  if (value.activity !== 'all' && value.activity !== 'active' && value.activity !== 'inactive') return null;
  if (!(value.search === null || typeof value.search === 'string')) return null;
  if (!strings(value.grouping) || !value.grouping.every(level => (VIEW_LEVELS as readonly string[]).includes(level))) return null;
  return { version: 1, connections: value.connections, assetTypeIds: value.assetTypeIds, activity: value.activity,
    search: value.search, grouping: value.grouping as ViewLevel[] };
}

function toView(value: unknown): AssetView | null {
  if (!isObject(value) || typeof value.id !== 'string' || typeof value.name !== 'string' ||
    !Number.isSafeInteger(value.revision) || typeof value.createdAtUtc !== 'string' || typeof value.updatedAtUtc !== 'string') return null;
  const definition = toDefinition(value.definition);
  const shared = value.visibility === 'shared';
  const isOwner = value.isOwner !== false;
  return definition ? { id: value.id, name: value.name, definition, revision: value.revision as number,
    createdAtUtc: value.createdAtUtc, updatedAtUtc: value.updatedAtUtc, visibility: shared ? 'shared' : 'personal',
    ownerName: typeof value.ownerName === 'string' ? value.ownerName : '', isOwner,
    canEdit: typeof value.canEdit === 'boolean' ? value.canEdit : isOwner,
    hiddenConnectionCount: Number.isSafeInteger(value.hiddenConnectionCount) ? value.hiddenConnectionCount as number : 0 } : null;
}

function toAsset(value: unknown): ViewAsset | null {
  if (!isObject(value) || typeof value.id !== 'string' || typeof value.name !== 'string') return null;
  return {
    id: value.id, name: value.name, assetTypeId: typeof value.assetTypeId === 'string' ? value.assetTypeId : '',
    parentAssetId: typeof value.parentAssetId === 'string' ? value.parentAssetId : null,
    ownerConnectionId: typeof value.ownerConnectionId === 'string' ? value.ownerConnectionId : null,
    isActive: value.isActive !== false,
  };
}

async function failure(response: Response): Promise<ViewsError> {
  let code: string | null = null;
  try {
    const body: unknown = await response.json();
    if (isObject(body) && typeof body.code === 'string') code = body.code;
  } catch { /* the status alone decides */ }
  if (response.status === 403 && code === 'ASSET_VIEW_SHARE_FORBIDDEN') return new ViewsError('shareForbidden');
  if (response.status === 401 || response.status === 403) return new ViewsError('accessDenied');
  if (response.status === 404) return new ViewsError('notFound');
  if (response.status === 409) {
    return new ViewsError(code === 'ASSET_VIEW_NAME_CONFLICT' ? 'nameTaken'
      : code === 'ASSET_VIEW_SHARED_NAME_CONFLICT' ? 'sharedNameTaken'
        : code === 'ASSET_VIEW_LIMIT_REACHED' || code === 'ASSET_VIEW_SHARED_LIMIT_REACHED' ? 'limit' : 'stale');
  }
  if (response.status === 400) return new ViewsError('invalid');
  return new ViewsError('unavailable');
}

async function send(path: string, init: RequestInit, fetcher: typeof fetch): Promise<unknown> {
  let response: Response;
  try {
    response = await fetcher('/api/v1' + path, { credentials: 'same-origin', cache: 'no-store', ...init });
  } catch (error) {
    if (init.signal?.aborted) throw error;
    throw new ViewsError('unavailable');
  }
  if (!response.ok) throw await failure(response);
  if (response.status === 204) return null;
  try { return await response.json(); } catch { throw new ViewsError('invalidResponse'); }
}

const json = (method: string, body: unknown): RequestInit => ({
  method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
});

function one(body: unknown): AssetView {
  const view = toView(body);
  if (!view) throw new ViewsError('invalidResponse');
  return view;
}

export async function loadViews(signal: AbortSignal, fetcher: typeof fetch = fetch): Promise<AssetView[]> {
  const body = await send('/asset-views', { signal }, fetcher);
  if (!Array.isArray(body)) throw new ViewsError('invalidResponse');
  return body.map(one);
}

export async function createView(
  name: string, definition: ViewDefinition, visibility: ViewVisibility = 'personal', fetcher: typeof fetch = fetch,
): Promise<AssetView> {
  return one(await send('/asset-views', json('POST', { name, definition, visibility }), fetcher));
}

export async function updateView(
  view: AssetView, name: string, definition: ViewDefinition, visibility: ViewVisibility, fetcher: typeof fetch = fetch,
): Promise<AssetView> {
  return one(await send('/asset-views/' + encodeURIComponent(view.id),
    json('PUT', { expectedRevision: view.revision, name, definition, visibility }), fetcher));
}

/** A personal copy of a View the person can read (their own or a shared one). */
export async function copyView(view: AssetView, name: string, fetcher: typeof fetch = fetch): Promise<AssetView> {
  return one(await send('/asset-views/' + encodeURIComponent(view.id) + '/copy', json('POST', { name }), fetcher));
}

/** Administrators only: take a shared View back to its owner alone. */
export async function unshareView(view: AssetView, fetcher: typeof fetch = fetch): Promise<AssetView> {
  return one(await send('/asset-views/' + encodeURIComponent(view.id) + '/unshare', { method: 'POST' }, fetcher));
}

export async function deleteView(view: AssetView, fetcher: typeof fetch = fetch): Promise<void> {
  await send('/asset-views/' + encodeURIComponent(view.id) + '?expectedRevision=' + view.revision,
    { method: 'DELETE' }, fetcher);
}

export const VIEW_PAGE_SIZE = 100;

/** The next level under `path` (the group keys already chosen, top first). */
export async function loadViewNodes(
  viewId: string, path: readonly string[], offset: number, signal: AbortSignal, fetcher: typeof fetch = fetch,
): Promise<ViewNodes> {
  const query = [`offset=${offset}`, `limit=${VIEW_PAGE_SIZE}`, ...path.map(key => 'path=' + encodeURIComponent(key))].join('&');
  const body = await send(`/asset-views/${encodeURIComponent(viewId)}/nodes?${query}`, { signal }, fetcher);
  if (!isObject(body) || !Array.isArray(body.groups) || !Array.isArray(body.assets)) throw new ViewsError('invalidResponse');
  const groups = body.groups.flatMap((item: unknown) => isObject(item) && typeof item.key === 'string' &&
    typeof item.label === 'string' && typeof item.count === 'number'
    ? [{ key: item.key, label: item.label, count: item.count }] : []);
  const assets = body.assets.flatMap((item: unknown) => { const asset = toAsset(item); return asset ? [asset] : []; });
  const level = typeof body.level === 'string' && (VIEW_LEVELS as readonly string[]).includes(body.level)
    ? body.level as ViewLevel : null;
  return { level, groups, assets };
}
