export type AdapterFieldKind = 'Text' | 'Url' | 'WholeNumber' | 'YesNo';

export type AdapterField = {
  key: string; label: string; helpText: string; kind: AdapterFieldKind; required: boolean; maxLength: number;
};

export type Adapter = {
  key: string; displayName: string; description: string; settingsVersion: number;
  fields: AdapterField[]; capabilities: string[];
  canDiscover: boolean; groupingFields: { key: string; label: string }[];
};

export type DiscoveredObject = { externalId: string; displayName: string; kind: string; metadata: Record<string, string> };

/** `complete` false means partial or failed: the list must never be read as "nothing is there". */
export type DiscoveryFacet = { key: string; label: string; values: { value: string; count: number }[]; hasMore: boolean; knownCount?: number | null };

/**
 * `items` is only the first part of what was found (at most 1,000): `totalCount` says how many there are, and
 * `snapshotId` names the kept list that searching, reviewing and importing work from.
 */
export type ImportHierarchy = { status: 'New' | 'Saved' | 'Legacy'; levels: string[] | null };
export type Discovery = {
  complete: boolean; items: DiscoveredObject[]; warnings: string[]; actingAs: string | null;
  totalCount: number; snapshotId: string; facets: DiscoveryFacet[]; expiresAtUtc: string; sourceAssetCount?: number | null; hierarchy?: ImportHierarchy | null;
};

/** Either all matching objects in a kept snapshot, or explicit source identities without filters. */
export type DiscoveryObjectKey = { kind: string; externalId: string };
export type ImportSelection = { snapshotId: string; query: string; facets: Record<string, string[]>; objects?: DiscoveryObjectKey[]; adoptExistingHierarchy?: boolean };
export const discoveryKey = (item: DiscoveryObjectKey) => JSON.stringify([item.kind, item.externalId]);

export type SearchResult = { matchCount: number; items: DiscoveredObject[] };

export type TestOutcome = 'Healthy' | 'NeedsAttention' | 'Unreachable';

export type ConnectionTest = {
  outcome: TestOutcome; message: string; actingAs: string | null; testedAtUtc: string; testedRevision: number;
};

export type Connection = {
  id: string; name: string; adapterKey: string; settingsVersion: number; settings: Record<string, string>;
  isEnabled: boolean; revision: number; createdAtUtc: string; updatedAtUtc: string;
  lastTest: ConnectionTest | null; lastTestIsCurrent: boolean;
};

export type ConnectionErrorKind = 'accessDenied' | 'notFound' | 'nameTaken' | 'stale' | 'disabled' |
  'invalidSettings' | 'invalid' | 'unsupported' | 'tooLarge' | 'importTooLarge' | 'snapshotExpired' | 'hasImports' | 'needsAllAssets' | 'planChanged' | 'importNotAllowed' |
  'incomplete' | 'invalidLevels' | 'gatewayUnavailable' | 'invalidResponse' | 'unavailable';

export class ConnectionsError extends Error {
  constructor(public readonly kind: ConnectionErrorKind, public readonly detail: string | null = null) {
    super(kind);
    this.name = 'ConnectionsError';
  }
}

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const kinds: readonly string[] = ['Text', 'Url', 'WholeNumber', 'YesNo'];
const outcomes: readonly string[] = ['Healthy', 'NeedsAttention', 'Unreachable'];

function isObject(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}

function isAdapter(value: unknown): value is Adapter {
  if (!isObject(value)) return false;
  return typeof value.key === 'string' && value.key.length > 0 && typeof value.displayName === 'string' &&
    typeof value.description === 'string' && Number.isSafeInteger(value.settingsVersion) &&
    Array.isArray(value.capabilities) && value.capabilities.every(x => typeof x === 'string') &&
    typeof value.canDiscover === 'boolean' && Array.isArray(value.groupingFields) &&
    value.groupingFields.every(f => isObject(f) && typeof f.key === 'string' && typeof f.label === 'string') &&
    Array.isArray(value.fields) && value.fields.every(field => isObject(field) &&
      typeof field.key === 'string' && field.key.length > 0 && typeof field.label === 'string' &&
      typeof field.helpText === 'string' && typeof field.kind === 'string' && kinds.includes(field.kind) &&
      typeof field.required === 'boolean' && Number.isSafeInteger(field.maxLength));
}

function isDiscoveredObjects(value: unknown): value is DiscoveredObject[] {
  return Array.isArray(value) && value.every(item => isObject(item) &&
    typeof item.externalId === 'string' && typeof item.displayName === 'string' && typeof item.kind === 'string' &&
    isObject(item.metadata) && Object.values(item.metadata).every(x => typeof x === 'string'));
}

function isFacet(value: unknown): value is DiscoveryFacet {
  return isObject(value) && typeof value.key === 'string' && typeof value.label === 'string' &&
    typeof value.hasMore === 'boolean' && (value.knownCount == null || Number.isSafeInteger(value.knownCount) && (value.knownCount as number) >= 0) && Array.isArray(value.values) &&
    value.values.every(entry => isObject(entry) && typeof entry.value === 'string' &&
      Number.isSafeInteger(entry.count) && (entry.count as number) >= 0);
}

function isDiscovery(value: unknown): value is Discovery {
  return isObject(value) && typeof value.complete === 'boolean' &&
    (value.actingAs === null || typeof value.actingAs === 'string') &&
    Array.isArray(value.warnings) && value.warnings.every(x => typeof x === 'string') &&
    isDiscoveredObjects(value.items) && Number.isSafeInteger(value.totalCount) &&
    (value.totalCount as number) >= value.items.length && typeof value.snapshotId === 'string' && uuid.test(value.snapshotId) &&
    (value.sourceAssetCount == null || Number.isSafeInteger(value.sourceAssetCount) && (value.sourceAssetCount as number) >= 0) &&
    (value.hierarchy == null || isObject(value.hierarchy) && ['New', 'Saved', 'Legacy'].includes(String(value.hierarchy.status)) &&
      (value.hierarchy.status === 'Saved' ? Array.isArray(value.hierarchy.levels) && value.hierarchy.levels.every(level => typeof level === 'string') : value.hierarchy.levels === null)) &&
    typeof value.expiresAtUtc === 'string' && Array.isArray(value.facets) && value.facets.every(facet => isFacet(facet) && (facet.knownCount == null || facet.knownCount <= (value.totalCount as number)));
}

function isConnection(value: unknown): value is Connection {
  if (!isObject(value)) return false;
  const test = value.lastTest;
  return typeof value.id === 'string' && uuid.test(value.id) && typeof value.name === 'string' &&
    typeof value.adapterKey === 'string' && Number.isSafeInteger(value.settingsVersion) &&
    isObject(value.settings) && Object.values(value.settings).every(x => typeof x === 'string') &&
    typeof value.isEnabled === 'boolean' && Number.isSafeInteger(value.revision) && (value.revision as number) >= 1 &&
    typeof value.createdAtUtc === 'string' && typeof value.updatedAtUtc === 'string' &&
    typeof value.lastTestIsCurrent === 'boolean' &&
    (test === null || isObject(test) && typeof test.outcome === 'string' && outcomes.includes(test.outcome) &&
      typeof test.message === 'string' && (test.actingAs === null || typeof test.actingAs === 'string') &&
      typeof test.testedAtUtc === 'string' && Number.isSafeInteger(test.testedRevision));
}

async function failure(response: Response): Promise<ConnectionsError> {
  let code: string | null = null;
  let detail: string | null = null;
  try {
    const body: unknown = await response.json();
    if (isObject(body)) {
      code = typeof body.code === 'string' ? body.code : null;
      detail = typeof body.detail === 'string' ? body.detail : null;
    }
  } catch { /* the status alone decides */ }
  const status = response.status;
  if (status === 403 && code === 'CONNECTION_IMPORT_NEEDS_ALL_ASSETS') return new ConnectionsError('needsAllAssets');
  if (status === 401 || status === 403) return new ConnectionsError('accessDenied');
  if (status === 404) return new ConnectionsError('notFound');
  if (status === 409) {
    return new ConnectionsError(code === 'CONNECTION_NAME_CONFLICT' ? 'nameTaken'
      : code === 'CONNECTION_DISABLED' ? 'disabled'
        : code === 'CONNECTION_DISCOVERY_UNSUPPORTED' ? 'unsupported'
          : code === 'CONNECTION_SNAPSHOT_EXPIRED' ? 'snapshotExpired'
          : code === 'CONNECTION_HAS_IMPORTS' ? 'hasImports'
            : code === 'CONNECTION_IMPORT_PLAN_CHANGED' ? 'planChanged'
              : code === 'CONNECTION_IMPORT_NOT_ALLOWED' ? 'importNotAllowed'
                : code === 'CONNECTION_DISCOVERY_INCOMPLETE' ? 'incomplete' : 'stale', detail);
  }
  if (status === 400) {
    return code === 'CONNECTION_SETTINGS_INVALID' ? new ConnectionsError('invalidSettings', detail)
      : code === 'CONNECTION_DISCOVERY_TOO_LARGE' ? new ConnectionsError('tooLarge', detail)
        : code === 'CONNECTION_IMPORT_TOO_LARGE' ? new ConnectionsError('importTooLarge', detail)
        : code === 'CONNECTION_IMPORT_LEVELS_INVALID' ? new ConnectionsError('invalidLevels') : new ConnectionsError('invalid');
  }
  if (status === 503 || status === 504) return new ConnectionsError('gatewayUnavailable');
  return new ConnectionsError('unavailable');
}

async function send(
  path: string, init: RequestInit, fetcher: typeof fetch,
): Promise<unknown> {
  let response: Response;
  try {
    response = await fetcher('/api/v1' + path, { credentials: 'same-origin', cache: 'no-store', ...init });
  } catch (error) {
    if (init.signal?.aborted) throw error;
    throw new ConnectionsError('unavailable');
  }
  if (!response.ok) throw await failure(response);
  if (response.status === 204) return null;
  try { return await response.json(); } catch { throw new ConnectionsError('invalidResponse'); }
}

const json = (method: string, body: unknown): RequestInit => ({
  method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
});

export async function loadConnections(signal: AbortSignal, fetcher: typeof fetch = fetch): Promise<Connection[]> {
  const body = await send('/connections', { signal }, fetcher);
  if (!Array.isArray(body) || body.length > 1000 || !body.every(isConnection)) throw new ConnectionsError('invalidResponse');
  return body;
}

export async function loadAdapters(signal: AbortSignal, fetcher: typeof fetch = fetch): Promise<Adapter[]> {
  const body = await send('/connections/adapters', { signal }, fetcher);
  if (!Array.isArray(body) || !body.every(isAdapter)) throw new ConnectionsError('invalidResponse');
  return body;
}

function asConnection(body: unknown): Connection {
  if (!isConnection(body)) throw new ConnectionsError('invalidResponse');
  return body;
}

export type ConnectionDraft = { name: string; settings: Record<string, string> };

/** Only settings with a value are sent, so clearing an optional field removes it. */
export function cleanSettings(adapter: Adapter, settings: Record<string, string>): Record<string, string> {
  const cleaned: Record<string, string> = {};
  for (const field of adapter.fields) {
    const value = (settings[field.key] ?? '').trim();
    if (value) cleaned[field.key] = value;
  }
  return cleaned;
}

export async function createConnection(
  adapter: Adapter, draft: ConnectionDraft, fetcher: typeof fetch = fetch,
): Promise<Connection> {
  return asConnection(await send('/connections', json('POST', {
    name: draft.name.trim(), adapterKey: adapter.key, settings: cleanSettings(adapter, draft.settings),
  }), fetcher));
}

export async function updateConnection(
  connection: Connection, adapter: Adapter, draft: ConnectionDraft, fetcher: typeof fetch = fetch,
): Promise<Connection> {
  return asConnection(await send(`/connections/${connection.id}`, json('PUT', {
    expectedRevision: connection.revision, name: draft.name.trim(), settings: cleanSettings(adapter, draft.settings),
  }), fetcher));
}

export async function setConnectionEnabled(
  connection: Connection, enabled: boolean, fetcher: typeof fetch = fetch,
): Promise<Connection> {
  return asConnection(await send(`/connections/${connection.id}/enabled`,
    json('PUT', { expectedRevision: connection.revision, enabled }), fetcher));
}

export async function testConnection(connection: Connection, fetcher: typeof fetch = fetch): Promise<Connection> {
  return asConnection(await send(`/connections/${connection.id}/test`, { method: 'POST' }, fetcher));
}

/** Looks at the outside system now, as the Gateway's account. Nothing is saved. */
export async function discoverConnection(connection: Connection, fetcher: typeof fetch = fetch): Promise<Discovery> {
  const body = await send(`/connections/${connection.id}/discover`, { method: 'POST' }, fetcher);
  if (!isDiscovery(body)) throw new ConnectionsError('invalidResponse');
  return body;
}

export type ImportGroup = {
  name: string; isFallback: boolean; objectCount: number; sampleNames: string[]; children: ImportGroup[]; key?: string | null; sourceIdentity?: string | null;
};

/** What an import would create, for review. `fingerprint` names this exact plan and is sent back to apply it. */
export type ImportPreview = {
  fingerprint: string; levels: string[]; objectCount: number; groupCount: number; fallbackObjectCount: number;
  rootObjectCount: number; groups: ImportGroup[]; warnings: string[]; conflicts: string[]; typesToCreate: string[];
  canImport: boolean; actingAs: string | null; alreadyImported: number; adoptingHierarchy?: boolean; existingWells?: number; groupsToAdopt?: number;
};

export type ImportResult = {
  wellsCreated: number; groupsCreated: number; groupsReused: number; alreadyImported: number; typesCreated: string[]; hierarchyAdopted?: boolean;
};

const isCount = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0;
const isStrings = (value: unknown): value is string[] => Array.isArray(value) && value.every(x => typeof x === 'string');

function isImportGroup(value: unknown, depth = 0): value is ImportGroup {
  return depth <= 5 && isObject(value) && typeof value.name === 'string' && typeof value.isFallback === 'boolean' &&
    (value.sourceIdentity == null || typeof value.sourceIdentity === 'string') &&
    (value.key == null || typeof value.key === 'string' && /^[A-F0-9]{64}$/.test(value.key)) &&
    isCount(value.objectCount) && isStrings(value.sampleNames) && Array.isArray(value.children) &&
    value.children.every(child => isImportGroup(child, depth + 1));
}

function isImportPreview(value: unknown): value is ImportPreview {
  return isObject(value) && typeof value.fingerprint === 'string' && value.fingerprint.length > 0 &&
    isStrings(value.levels) && isCount(value.objectCount) && isCount(value.groupCount) &&
    isCount(value.fallbackObjectCount) && isCount(value.rootObjectCount) && isCount(value.alreadyImported) &&
    Array.isArray(value.groups) && value.groups.every(group => isImportGroup(group)) &&
    (value.adoptingHierarchy === undefined || typeof value.adoptingHierarchy === 'boolean') &&
    (value.existingWells === undefined || isCount(value.existingWells)) && (value.groupsToAdopt === undefined || isCount(value.groupsToAdopt)) &&
    isStrings(value.warnings) && isStrings(value.conflicts) && isStrings(value.typesToCreate) &&
    typeof value.canImport === 'boolean' && (value.actingAs === null || typeof value.actingAs === 'string');
}

function isImportResult(value: unknown): value is ImportResult {
  return isObject(value) && (value.hierarchyAdopted === undefined || typeof value.hierarchyAdopted === 'boolean') && isCount(value.wellsCreated) && isCount(value.groupsCreated) &&
    isCount(value.groupsReused) && isCount(value.alreadyImported) && isStrings(value.typesCreated);
}

const selectionBody = (selection: ImportSelection | null) => selection === null ? {} : {
  snapshotId: selection.snapshotId, query: selection.query || null,
  facets: Object.keys(selection.facets).length > 0 ? selection.facets : null,
  ...(selection.objects !== undefined ? { objects: selection.objects } : {}),
  ...(selection.adoptExistingHierarchy ? { adoptExistingHierarchy: true } : {}),
};

/** Reads a stable page of the full kept inventory, narrowed by words and grouping values. */
export async function searchDiscovered(
  connection: Connection, selection: ImportSelection, fetcher: typeof fetch = fetch,
  page?: { offset: number; limit: number; signal?: AbortSignal },
): Promise<SearchResult> {
  const body = await send(`/connections/${connection.id}/discover/search`, { ...json('POST', { ...selectionBody(selection), ...(page ? { offset: page.offset, limit: page.limit } : {}) }), signal: page?.signal }, fetcher);
  if (!isObject(body) || !Number.isSafeInteger(body.matchCount) || !isDiscoveredObjects(body.items) ||
    (body.matchCount as number) < body.items.length) throw new ConnectionsError('invalidResponse');
  return { matchCount: body.matchCount as number, items: body.items };
}

/**
 * Works out what importing would do with these grouping levels, for the chosen part of what was found (everything
 * when there is no selection). Nothing is saved.
 */
export async function previewImport(
  connection: Connection, levels: string[], selection: ImportSelection | null = null, fetcher: typeof fetch = fetch,
): Promise<ImportPreview> {
  const body = await send(`/connections/${connection.id}/import/preview`, json('POST', { levels, ...selectionBody(selection) }), fetcher);
  if (!isImportPreview(body)) throw new ConnectionsError('invalidResponse');
  return body;
}

/** Applies exactly the plan that was reviewed; the server refuses if the source has changed since. */
export async function importFromConnection(
  connection: Connection, levels: string[], expectedFingerprint: string, selection: ImportSelection | null = null,
  fetcher: typeof fetch = fetch,
): Promise<ImportResult> {
  const body = await send(
    `/connections/${connection.id}/import`, json('POST', { levels, expectedFingerprint, ...selectionBody(selection) }), fetcher);
  if (!isImportResult(body)) throw new ConnectionsError('invalidResponse');
  return body;
}

/** Case-insensitive match on the name and every fact, so "ESP" or a field name finds what people expect. */
export function filterDiscovered(items: DiscoveredObject[], query: string): DiscoveredObject[] {
  const needle = query.trim().toLowerCase();
  if (!needle) return items;
  return items.filter(item => item.displayName.toLowerCase().includes(needle) ||
    Object.values(item.metadata).some(value => value.toLowerCase().includes(needle)));
}

export async function deleteConnection(connection: Connection, fetcher: typeof fetch = fetch): Promise<void> {
  await send(`/connections/${connection.id}`, { method: 'DELETE' }, fetcher);
}

/** True when the form differs from the saved record, so Test (which uses saved settings) must wait for a save. */
export function isDirty(connection: Connection, adapter: Adapter, draft: ConnectionDraft): boolean {
  const saved = connection.settings;
  const next = cleanSettings(adapter, draft.settings);
  const keys = new Set([...Object.keys(saved), ...Object.keys(next)]);
  return draft.name.trim() !== connection.name || [...keys].some(key => (saved[key] ?? '') !== (next[key] ?? ''));
}

export type ConnectionStatus = 'disabled' | 'notTested' | 'outdated' | TestOutcome;

export function connectionStatus(connection: Connection): ConnectionStatus {
  if (!connection.isEnabled) return 'disabled';
  if (!connection.lastTest) return 'notTested';
  return connection.lastTestIsCurrent ? connection.lastTest.outcome : 'outdated';
}

export type SourceCheck = {
  id: string; revision: number; connectionRevision: number; status: string;
  startedAtUtc: string; completedAtUtc: string | null; actingAs: string | null;
  found: number; changed: number; new: number; notReturned: number; comparisonUnavailable: number;
  warnings: string[]; unavailableFields: string[] | null; sourceScopeCount: number | null; sourceScopeFingerprint: string | null;
};
export type SourceReference = { id: string; name: string; revisionId: string | null; revisionNumber: number | null; status: string; kind: string; reference: string };
export type SourceChange = {
  id: string; assetId: string | null; kind: string; externalId: string; name: string; status: string;
  firstSeenAtUtc: string; lastSeenAtUtc: string;
  evidence: { status: string; fingerprint: string; differences: { field: string; accepted: string | null; observed: string | null }[]; unknownFields: string[] };
  views: SourceReference[]; configurations: SourceReference[]; referencesTruncated: boolean;
};
export type SourceChangePage = { latestCheck: SourceCheck | null; lastSuccessfulCheck: SourceCheck | null; total: number; offset: number; items: SourceChange[] };
const nullableString = (value: unknown) => value === null || typeof value === 'string';
function isSourceCheck(value: unknown): value is SourceCheck {
  return isObject(value) && typeof value.id === 'string' && uuid.test(value.id) && isCount(value.revision) && isCount(value.connectionRevision) &&
    ['Checking', 'Complete', 'Partial', 'Failed', 'Stale', 'Abandoned'].includes(String(value.status)) &&
    typeof value.startedAtUtc === 'string' && nullableString(value.completedAtUtc) && nullableString(value.actingAs) &&
    ['found', 'changed', 'new', 'notReturned', 'comparisonUnavailable'].every(key => isCount(value[key])) &&
    isStrings(value.warnings) && (value.unavailableFields === null || isStrings(value.unavailableFields)) &&
    (value.sourceScopeCount === null || isCount(value.sourceScopeCount)) && nullableString(value.sourceScopeFingerprint);
}
function isSourceReference(value: unknown): value is SourceReference {
  return isObject(value) && typeof value.id === 'string' && uuid.test(value.id) &&
    ['name', 'status', 'kind', 'reference'].every(key => typeof value[key] === 'string') &&
    (value.revisionId === null || typeof value.revisionId === 'string' && uuid.test(value.revisionId)) &&
    (value.revisionNumber === null || isCount(value.revisionNumber));
}
function isSourceChange(value: unknown): value is SourceChange {
  if (!isObject(value) || !isObject(value.evidence)) return false;
  const evidence = value.evidence;
  return typeof value.id === 'string' && uuid.test(value.id) &&
    (value.assetId === null || typeof value.assetId === 'string' && uuid.test(value.assetId)) &&
    ['kind', 'externalId', 'name', 'firstSeenAtUtc', 'lastSeenAtUtc'].every(key => typeof value[key] === 'string') &&
    ['Changed', 'New', 'NotReturned'].includes(String(value.status)) && evidence.status === value.status &&
    typeof evidence.fingerprint === 'string' && /^[A-F0-9]{64}$/.test(evidence.fingerprint) &&
    isStrings(evidence.unknownFields) && Array.isArray(evidence.differences) && evidence.differences.every(d =>
      isObject(d) && typeof d.field === 'string' && nullableString(d.accepted) && nullableString(d.observed)) &&
    typeof value.referencesTruncated === 'boolean' && Array.isArray(value.views) && value.views.length <= 100 && value.views.every(isSourceReference) &&
    Array.isArray(value.configurations) && value.configurations.length <= 100 && value.configurations.every(isSourceReference);
}
export async function checkSourceChanges(connectionId: string, fetcher: typeof fetch = fetch): Promise<SourceCheck> {
  const body = await send(`/connections/${connectionId}/source-checks`, { method: 'POST' }, fetcher);
  if (!isSourceCheck(body)) throw new ConnectionsError('invalidResponse');
  return body;
}
export async function loadSourceChanges(connectionId: string, offset: number, query: string, signal?: AbortSignal,
  fetcher: typeof fetch = fetch): Promise<SourceChangePage> {
  const params = new URLSearchParams({ offset: String(offset), limit: '25', query });
  const body = await send(`/connections/${connectionId}/source-changes?${params}`, { signal }, fetcher);
  if (!isObject(body) || !(body.latestCheck === null || isSourceCheck(body.latestCheck)) ||
    !(body.lastSuccessfulCheck === null || isSourceCheck(body.lastSuccessfulCheck)) || !isCount(body.total) ||
    !isCount(body.offset) || !Array.isArray(body.items) || body.items.length > 25 || !body.items.every(isSourceChange))
    throw new ConnectionsError('invalidResponse');
  return body as SourceChangePage;
}
