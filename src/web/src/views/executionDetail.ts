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

export const isGuid = (value: string): boolean => executionIdPattern.test(value);

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

const SECOND = 1000;
/** A length of time in the largest sensible units, for example "45 s", "3 min 5 s" or "1 h 2 min". */
export function formatDuration(milliseconds: number, roundToMinutes = false): string {
  if (!Number.isFinite(milliseconds) || milliseconds < 0) return '—';
  let seconds = Math.round(milliseconds / SECOND);
  // An age of "41 min 54 s" is false precision; from one minute up it reads "42 min".
  if (roundToMinutes && seconds >= 60) seconds = Math.round(seconds / 60) * 60;
  if (seconds < 60) return `${seconds} s`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return seconds % 60 === 0 ? `${minutes} min` : `${minutes} min ${seconds % 60} s`;
  const hours = Math.floor(minutes / 60);
  return minutes % 60 === 0 ? `${hours} h` : `${hours} h ${minutes % 60} min`;
}

/** How long a finished run took, from the times the server recorded; null until it has both. */
export function runDuration(detail: Pick<ExecutionResponse, 'startedAtUtc' | 'completedAtUtc'>): string | null {
  const started = Date.parse(detail.startedAtUtc ?? '');
  const completed = Date.parse(detail.completedAtUtc ?? '');
  return Number.isFinite(started) && Number.isFinite(completed) && completed >= started
    ? formatDuration(completed - started) : null;
}

/** How old a measurement was when it was read, for example "35 min old when read"; null when the times do not allow it. */
export function ageWhenRead(timestampUtc?: string | null, retrievedAtUtc?: string | null): string | null {
  const measured = Date.parse(timestampUtc ?? '');
  const read = Date.parse(retrievedAtUtc ?? '');
  return Number.isFinite(measured) && Number.isFinite(read) && read >= measured
    ? `${formatDuration(read - measured, true)} old when read` : null;
}

/** "pump-fillage" and "PumpFillage" read as "Pump fillage". */
export function humanizeName(value: string): string {
  const words = value.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_\-.]+/g, ' ').trim().toLowerCase();
  return words ? words[0].toUpperCase() + words.slice(1) : value;
}

export type OutputEntry = { label: string; value: string };
const MAX_OUTPUT_ENTRIES = 20;
const MAX_OUTPUT_VALUE = 200;
/**
 * A run's result as label and value rows, for people. Input readings are left out because the Inputs section
 * shows them. Returns no rows when the output is not a JSON object; the raw text stays available beside it.
 * Values are plain text and never markup.
 */
export function describeExecutionOutput(output?: string | null): OutputEntry[] {
  if (!output || output.length > 1048576) return [];
  try {
    const parsed: unknown = JSON.parse(output);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return [];
    return Object.entries(parsed as Record<string, unknown>)
      .filter(([key]) => key !== 'inputDiagnostics' && key !== 'inputObservations')
      .slice(0, MAX_OUTPUT_ENTRIES)
      .map(([key, value]) => {
        const text = typeof value === 'string' ? value
          : typeof value === 'number' || typeof value === 'boolean' ? String(value)
            : value === null ? '—' : JSON.stringify(value);
        return { label: humanizeName(key), value: text.length > MAX_OUTPUT_VALUE ? text.slice(0, MAX_OUTPUT_VALUE) + '…' : text };
      });
  } catch { return []; }
}

/** The output as indented JSON when it parses, so the raw view is readable; otherwise unchanged. */
export function prettyExecutionOutput(output?: string | null): string {
  if (!output || output.length > 1048576) return output ?? '';
  try { return JSON.stringify(JSON.parse(output), null, 2); } catch { return output; }
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
        (o.timestampKind === null || o.timestampKind === 'UnverifiedScan' || o.timestampKind === 'ControllerScan') &&
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
