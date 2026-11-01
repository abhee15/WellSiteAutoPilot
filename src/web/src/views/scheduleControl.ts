import { isSecurityId } from '../security/identifiers';

export type ScheduleControlErrorKind = 'invalid' | 'accessDenied' | 'stale' | 'unavailable' | 'invalidResponse';

export class ScheduleControlError extends Error {
  constructor(public readonly kind: ScheduleControlErrorKind) {
    super(kind);
    this.name = 'ScheduleControlError';
  }
}

export type ScheduleShape = {
  enabled: boolean;
  cadenceSeconds: number | null;
  startAtUtc: string | null;
  timeZoneId: string | null;
};

export type ScheduleChangeKind = 'Paused' | 'Resumed' | 'FrequencyChanged' | 'Reset';

export type ScheduleChange = {
  id: string;
  version: number;
  kind: ScheduleChangeKind;
  schedule: ScheduleShape;
  changedBy: string;
  changedAtUtc: string;
  reason: string | null;
};

export type ScheduleControl = {
  configuredLogicId: string;
  activeRevisionId: string | null;
  /** The opaque reviewed version, sent back in If-Match so a change made against stale information is refused. */
  version: string;
  declared: ScheduleShape | null;
  effective: ScheduleShape | null;
  differsFromRevision: boolean;
  nextRunAtUtc: string | null;
  minimumCadenceSeconds: number;
  maximumCadenceSeconds: number;
  canManage: boolean;
  changes: ScheduleChange[];
};

export const SCHEDULE_REASON_MAX = 1024;
const CHANGE_KINDS: readonly ScheduleChangeKind[] = ['Paused', 'Resumed', 'FrequencyChanged', 'Reset'];

const urlFor = (configuredLogicId: string, suffix = '') =>
  `/api/v1/configured-logic/${encodeURIComponent(configuredLogicId)}/schedule${suffix}`;

/** A reason is optional; when given it is trimmed and must be printable text of at most 1024 characters. */
export function normalizeScheduleReason(reason: string): string | null {
  const trimmed = reason.trim();
  if (trimmed.length > SCHEDULE_REASON_MAX ||
      [...trimmed].some(character => {
        const code = character.charCodeAt(0);
        return code < 32 || (code >= 127 && code <= 159);
      })) {
    throw new ScheduleControlError('invalid');
  }
  return trimmed.length > 0 ? trimmed : null;
}

/** Whole seconds within the server's bounds; the server remains the authority and re-checks. */
export function parseCadenceSeconds(value: number, minimum: number, maximum: number): number {
  if (!Number.isInteger(value) || value < minimum || value > maximum) throw new ScheduleControlError('invalid');
  return value;
}

function failFor(status: number): ScheduleControlError {
  if (status === 401 || status === 403) return new ScheduleControlError('accessDenied');
  if (status === 409 || status === 412) return new ScheduleControlError('stale');
  if (status === 400) return new ScheduleControlError('invalid');
  return new ScheduleControlError('unavailable');
}

const invalid = () => new ScheduleControlError('invalidResponse');
const isObject = (value: unknown): value is Record<string, unknown> =>
  Boolean(value) && typeof value === 'object' && !Array.isArray(value);
const isInstant = (value: unknown): value is string => typeof value === 'string' && Number.isFinite(Date.parse(value));
const nullable = <T>(value: unknown, test: (input: unknown) => input is T): T | null => {
  if (value === undefined || value === null) return null;
  if (!test(value)) throw invalid();
  return value;
};
const isText = (value: unknown): value is string => typeof value === 'string';

function readShape(value: unknown): ScheduleShape {
  if (!isObject(value) || typeof value.enabled !== 'boolean') throw invalid();
  const cadence = nullable(value.cadenceSeconds, (input): input is number => typeof input === 'number' && Number.isInteger(input) && input > 0);
  return {
    enabled: value.enabled,
    cadenceSeconds: cadence,
    startAtUtc: nullable(value.startAtUtc, isInstant),
    timeZoneId: nullable(value.timeZoneId, isText),
  };
}

function readChange(value: unknown): ScheduleChange {
  if (!isObject(value) || !isSecurityId(value.id) || typeof value.version !== 'number' || !Number.isInteger(value.version) ||
      typeof value.kind !== 'string' || !CHANGE_KINDS.includes(value.kind as ScheduleChangeKind) ||
      typeof value.changedBy !== 'string' || !isInstant(value.changedAtUtc)) throw invalid();
  return {
    id: value.id, version: value.version, kind: value.kind as ScheduleChangeKind, schedule: readShape(value.schedule),
    changedBy: value.changedBy, changedAtUtc: value.changedAtUtc, reason: nullable(value.reason, isText),
  };
}

/** Strict: a response that cannot be fully read is never shown as a running or paused schedule. */
export function readScheduleControl(payload: unknown): ScheduleControl {
  if (!isObject(payload) || !isSecurityId(payload.configuredLogicId) || typeof payload.version !== 'string' || !payload.version ||
      typeof payload.differsFromRevision !== 'boolean' || typeof payload.canManage !== 'boolean' ||
      typeof payload.minimumCadenceSeconds !== 'number' || typeof payload.maximumCadenceSeconds !== 'number' ||
      !(payload.minimumCadenceSeconds > 0) || payload.maximumCadenceSeconds < payload.minimumCadenceSeconds ||
      !Array.isArray(payload.changes)) throw invalid();
  const revisionId = payload.activeRevisionId === undefined || payload.activeRevisionId === null ? null : payload.activeRevisionId;
  if (revisionId !== null && !isSecurityId(revisionId)) throw invalid();
  return {
    configuredLogicId: payload.configuredLogicId,
    activeRevisionId: revisionId,
    version: payload.version,
    declared: payload.declared === undefined || payload.declared === null ? null : readShape(payload.declared),
    effective: payload.effective === undefined || payload.effective === null ? null : readShape(payload.effective),
    differsFromRevision: payload.differsFromRevision,
    nextRunAtUtc: nullable(payload.nextRunAtUtc, isInstant),
    minimumCadenceSeconds: payload.minimumCadenceSeconds,
    maximumCadenceSeconds: payload.maximumCadenceSeconds,
    canManage: payload.canManage,
    changes: payload.changes.map(readChange),
  };
}

async function readBody(response: Response): Promise<ScheduleControl> {
  let payload: unknown;
  try {
    payload = await response.json();
  } catch {
    throw invalid();
  }
  return readScheduleControl(payload);
}

export async function fetchScheduleControl(configuredLogicId: string, signal: AbortSignal,
  fetcher: typeof fetch = fetch): Promise<ScheduleControl> {
  const response = await fetcher(urlFor(configuredLogicId), { credentials: 'same-origin', cache: 'no-store', signal });
  if (!response.ok) throw failFor(response.status);
  return readBody(response);
}

type Change = { kind: 'pause' } | { kind: 'resume' } | { kind: 'frequency'; cadenceSeconds: number };

/**
 * Pause, resume or change the frequency of the live schedule against the version the person reviewed. The answer must
 * show the requested outcome: a 2xx that says otherwise is not presented as done, because a person who believes a
 * schedule is stopped must be right.
 */
export async function changeSchedule(current: ScheduleControl, change: Change, reason: string, signal: AbortSignal,
  fetcher: typeof fetch = fetch): Promise<ScheduleControl> {
  const normalized = normalizeScheduleReason(reason);
  const frequency = change.kind === 'frequency'
    ? parseCadenceSeconds(change.cadenceSeconds, current.minimumCadenceSeconds, current.maximumCadenceSeconds) : null;
  const response = await fetcher(change.kind === 'frequency' ? urlFor(current.configuredLogicId) : urlFor(current.configuredLogicId, `/${change.kind}`), {
    method: change.kind === 'frequency' ? 'PUT' : 'POST',
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { 'Content-Type': 'application/json', 'If-Match': `"${current.version}"` },
    body: JSON.stringify(frequency === null ? { reason: normalized } : { cadenceSeconds: frequency, reason: normalized }),
    signal,
  });
  if (!response.ok) throw failFor(response.status);
  const next = await readBody(response);
  const effective = next.effective;
  const honoured = change.kind === 'pause' ? effective?.enabled === false
    : change.kind === 'resume' ? effective?.enabled === true
      : effective?.cadenceSeconds === frequency;
  if (!honoured || next.configuredLogicId !== current.configuredLogicId) throw invalid();
  return next;
}

/** Running, paused, or no schedule at all, from the schedule that is in effect and not only the one declared. */
export type ScheduleRunState = 'running' | 'paused' | 'none';
export function scheduleRunState(control: Pick<ScheduleControl, 'activeRevisionId' | 'effective'>): ScheduleRunState {
  if (!control.activeRevisionId || !control.effective) return 'none';
  return control.effective.enabled ? 'running' : 'paused';
}

/** Resume is offered only when the declared revision itself has a schedule; a logic declared "off" has nothing to resume. */
export function canResume(control: ScheduleControl): boolean {
  return scheduleRunState(control) === 'paused' && control.effective?.cadenceSeconds != null;
}
