import type { ExecutionResponse, ExecutionStatus } from '../api/generated/v1/types.gen';

/** Statuses of a run that has not finished. `satisfies` has the compiler check each name against the server's ExecutionStatus. */
export const IN_FLIGHT_EXECUTION_STATUSES: readonly string[] =
  ['Requested', 'Queued', 'Starting', 'Running', 'Waiting'] satisfies ExecutionStatus[];

export type ExecutionDetailErrorKind =
  | 'invalidRequest'
  | 'accessDenied'
  | 'notFound'
  | 'unavailable'
  | 'invalidResponse';

export class ExecutionDetailError extends Error {
  constructor(public readonly kind: ExecutionDetailErrorKind) {
    super(kind);
    this.name = 'ExecutionDetailError';
  }
}

const executionIdPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Fetch detail from the authoritative, asset-scoped API. Do not reuse cached
 * data across operator sessions or fall back to a previously visible result.
 */
export async function fetchExecutionDetail(
  executionId: string,
  signal: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<ExecutionResponse> {
  if (!executionIdPattern.test(executionId)) {
    throw new ExecutionDetailError('invalidRequest');
  }

  const response = await fetcher(
    `/api/v1/executions/${encodeURIComponent(executionId)}`,
    {
      method: 'GET',
      credentials: 'same-origin',
      cache: 'no-store',
      signal,
    },
  );
  if (response.status === 401 || response.status === 403) {
    throw new ExecutionDetailError('accessDenied');
  }
  if (response.status === 404) {
    throw new ExecutionDetailError('notFound');
  }
  if (!response.ok) {
    throw new ExecutionDetailError('unavailable');
  }

  let payload: unknown;
  try {
    payload = await response.json();
  } catch {
    throw new ExecutionDetailError('invalidResponse');
  }

  if (payload === null || typeof payload !== 'object' || Array.isArray(payload)) {
    throw new ExecutionDetailError('invalidResponse');
  }

  const detail = payload as Partial<ExecutionResponse>;
  if (
    typeof detail.executionId !== 'string' ||
    detail.executionId.toLowerCase() !== executionId.toLowerCase() ||
    typeof detail.status !== 'string'
  ) {
    throw new ExecutionDetailError('invalidResponse');
  }

  return detail as ExecutionResponse;
}

export function formatDetailTimestamp(value?: string | null): string {
  if (!value) return '—';
  const timestamp = new Date(value);
  if (Number.isNaN(timestamp.getTime())) return '—';
  return timestamp.toLocaleString(undefined, { timeZoneName: 'short' });
}

export const MAX_EXECUTION_OUTPUT_PREVIEW = 4096;

export function previewExecutionOutput(value?: string | null): {
  text: string;
  truncated: boolean;
} {
  if (!value) return { text: '', truncated: false };
  return {
    text: value.slice(0, MAX_EXECUTION_OUTPUT_PREVIEW),
    truncated: value.length > MAX_EXECUTION_OUTPUT_PREVIEW,
  };
}


type InputObservation = { requirementId: string; assetId: string; quantity: string;
  value: number | null; unit: string | null; timestampUtc: string | null; quality: string; source: string;
  outcomeCode?: string; retrievedAtUtc?: string | null; timestampKind?: string | null };
/** Bounded, defensive view of worker telemetry; never render arbitrary output as HTML. */
export function executionInputObservations(output?: string | null): InputObservation[] {
  if (!output || output.length > 1048576) return [];
  try {
    const value = JSON.parse(output) as { inputObservations?: unknown; inputDiagnostics?: unknown };
    if (Array.isArray(value.inputDiagnostics)) {
      const boundedText = (text: unknown, maximum: number): text is string =>
        typeof text === 'string' && text.length > 0 && text.length <= maximum && ![...text].some(character => character.charCodeAt(0) < 32);
      const timestamp = (text: unknown): text is string | null => text === null ||
        boundedText(text, 64) && Number.isFinite(Date.parse(text));
      return value.inputDiagnostics.slice(0, 64).filter(o => o && typeof o === 'object' &&
        boundedText(o.requirementId, 128) && boundedText(o.assetId, 64) && boundedText(o.quantity, 256) &&
        (o.value === null || typeof o.value === 'number' && Number.isFinite(o.value)) &&
        (o.unit === null || boundedText(o.unit, 32)) && timestamp(o.timestampUtc) && timestamp(o.retrievedAtUtc) &&
        (o.connectionId === null || typeof o.connectionId === 'string' && executionIdPattern.test(o.connectionId)) &&
        ['Good', 'Uncertain', 'Bad', 'Unknown'].includes(o.quality) &&
        (o.timestampKind === null || o.timestampKind === 'UnverifiedScan') &&
        boundedText(o.outcomeCode, 128) && /^[A-Z][A-Z0-9_]*$/.test(o.outcomeCode))
        .map(o => ({ requirementId: o.requirementId, assetId: o.assetId, quantity: o.quantity,
          value: o.value, unit: o.unit, timestampUtc: o.timestampUtc, quality: o.quality,
          source: o.connectionId ?? '—', outcomeCode: o.outcomeCode,
          retrievedAtUtc: o.retrievedAtUtc, timestampKind: o.timestampKind }));
    }
    if (!Array.isArray(value.inputObservations)) return [];
    return value.inputObservations.slice(0, 100).filter((o: InputObservation) =>
      !!o && typeof o === 'object' && Number.isFinite(o.value) &&
      ['requirementId', 'assetId', 'quantity', 'unit', 'timestampUtc', 'quality', 'source'].every(key =>
        typeof o[key as keyof InputObservation] === 'string' &&
        String(o[key as keyof InputObservation]).length <= 512));
  } catch { return []; }
}


/** Only the execution detail response carries pinned provenance; cached catalog data cannot replace it. */
export function executionProvenance(detail: ExecutionResponse) {
  const value = detail.provenance;
  if (!value || !/^[a-f0-9]{64}$/.test(value.packageSha256 ?? '') ||
      typeof value.connectionsTruncated !== 'boolean' || !Array.isArray(value.connections) || value.connections.length > 64 ||
      !value.connections.every(connection => executionIdPattern.test(connection.connectionId ?? '') &&
        Number.isSafeInteger(connection.revision) && (connection.revision ?? 0) > 0 &&
        Number.isSafeInteger(connection.assetCount) && (connection.assetCount ?? 0) > 0)) return null;
  return value;
}
