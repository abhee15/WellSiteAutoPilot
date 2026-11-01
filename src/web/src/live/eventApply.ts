import type { ExecutionResponse } from '../api/generated/v1/types.gen';
import type { ChangeEvent } from './changeSignal';

const TERMINAL = new Set(['Completed', 'Failed', 'Cancelled', 'TimedOut']);
const ACTIVE_KINDS = new Set(['requested', 'started', 'progress']);

function statusFor(event: ChangeEvent): string | null {
  const labelled = event.payload.status;
  if (labelled) return labelled;
  switch (event.kind) {
    case 'requested': return 'Requested';
    case 'started': case 'progress': return 'Running';
    case 'completed': return 'Completed';
    case 'failed': return 'Failed';
    default: return null;
  }
}

/**
 * Applies one execution event to the Runs list without reading anything. Returns the same array
 * when nothing changes, and 'unknown' when the run is not in the list, so the caller can read just
 * that run. A late start or progress message never moves a finished run back to Running.
 */
export function applyExecutionEvent(list: readonly ExecutionResponse[], event: ChangeEvent): readonly ExecutionResponse[] | 'unknown' {
  if (event.entity !== 'execution') return list;
  const index = list.findIndex(row => row.executionId === event.id);
  if (index < 0) return 'unknown';
  const row = list[index]!;
  const status = statusFor(event);
  if (!status) return list;
  if (TERMINAL.has(row.status) && (ACTIVE_KINDS.has(event.kind) || row.status === status)) return list;
  const code = event.payload.code ?? null;
  const next: ExecutionResponse = {
    ...row,
    status: status as ExecutionResponse['status'],
    ...(event.kind === 'started' && !row.startedAtUtc && event.occurredAt ? { startedAtUtc: event.occurredAt } : {}),
    ...(event.kind === 'completed' ? { completedAtUtc: event.occurredAt || row.completedAtUtc, resultCode: code ?? row.resultCode } : {}),
    ...(event.kind === 'failed' ? { completedAtUtc: event.occurredAt || row.completedAtUtc, failureCode: code ?? row.failureCode } : {}),
  };
  return list.map((item, at) => (at === index ? next : item));
}

/** Puts a freshly read run into the list: replaced in place if present, otherwise newest first. */
export function upsertExecution(list: readonly ExecutionResponse[], run: ExecutionResponse, limit = 50): ExecutionResponse[] {
  const index = list.findIndex(row => row.executionId === run.executionId);
  if (index >= 0) return list.map((item, at) => (at === index ? run : item));
  return [run, ...list].slice(0, limit);
}
