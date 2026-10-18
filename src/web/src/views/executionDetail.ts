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
  value: number; unit: string; timestampUtc: string; quality: string; source: string };
/** Bounded, defensive view of worker telemetry; never render arbitrary output as HTML. */
export function executionInputObservations(output?: string | null): InputObservation[] {
  if (!output || output.length > 1048576) return [];
  try {
    const value = JSON.parse(output) as { inputObservations?: unknown };
    if (!Array.isArray(value.inputObservations)) return [];
    return value.inputObservations.slice(0, 100).filter((o: InputObservation) =>
      !!o && typeof o === 'object' && Number.isFinite(o.value) &&
      ['requirementId', 'assetId', 'quantity', 'unit', 'timestampUtc', 'quality', 'source'].every(key =>
        typeof o[key as keyof InputObservation] === 'string' &&
        String(o[key as keyof InputObservation]).length <= 512));
  } catch { return []; }
}
