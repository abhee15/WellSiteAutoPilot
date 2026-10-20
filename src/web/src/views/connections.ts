export type AdapterFieldKind = 'Text' | 'Url' | 'WholeNumber' | 'YesNo';

export type AdapterField = {
  key: string; label: string; helpText: string; kind: AdapterFieldKind; required: boolean; maxLength: number;
};

export type Adapter = {
  key: string; displayName: string; description: string; settingsVersion: number;
  fields: AdapterField[]; capabilities: string[];
};

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
  'invalidSettings' | 'invalid' | 'gatewayUnavailable' | 'invalidResponse' | 'unavailable';

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
    Array.isArray(value.fields) && value.fields.every(field => isObject(field) &&
      typeof field.key === 'string' && field.key.length > 0 && typeof field.label === 'string' &&
      typeof field.helpText === 'string' && typeof field.kind === 'string' && kinds.includes(field.kind) &&
      typeof field.required === 'boolean' && Number.isSafeInteger(field.maxLength));
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
  if (status === 401 || status === 403) return new ConnectionsError('accessDenied');
  if (status === 404) return new ConnectionsError('notFound');
  if (status === 409) {
    return new ConnectionsError(code === 'CONNECTION_NAME_CONFLICT' ? 'nameTaken'
      : code === 'CONNECTION_DISABLED' ? 'disabled' : 'stale');
  }
  if (status === 400) {
    return code === 'CONNECTION_SETTINGS_INVALID'
      ? new ConnectionsError('invalidSettings', detail) : new ConnectionsError('invalid');
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
