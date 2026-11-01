import type { KillSwitchResponse } from '../api/generated/v1/types.gen';

export type KillSwitchErrorKind = 'invalid' | 'accessDenied' | 'unavailable' | 'invalidResponse';

export class KillSwitchError extends Error {
  constructor(public readonly kind: KillSwitchErrorKind) {
    super(kind);
    this.name = 'KillSwitchError';
  }
}

export type KillSwitchState = {
  engaged: boolean;
  changedBy: string | null;
  changedAtUtc: string | null;
  reason: string | null;
  /** The deployment-wide switch. While it is off the gate refuses every command whatever the kill switch says. */
  autonomousControlEnabled: boolean;
};

export const KILL_SWITCH_REASON_MAX = 1024;
const ENDPOINT = '/api/v1/control/kill-switch';

/** A reason is optional; when given it is trimmed and must be printable text of at most 1024 characters. */
export function normalizeKillSwitchReason(reason: string): string | null {
  const trimmed = reason.trim();
  if (trimmed.length > KILL_SWITCH_REASON_MAX ||
      [...trimmed].some(character => {
        const code = character.charCodeAt(0);
        return code < 32 || (code >= 127 && code <= 159);
      })) {
    throw new KillSwitchError('invalid');
  }
  return trimmed.length > 0 ? trimmed : null;
}

function failFor(status: number): KillSwitchError {
  if (status === 401 || status === 403) return new KillSwitchError('accessDenied');
  if (status === 400) return new KillSwitchError('invalid');
  return new KillSwitchError('unavailable');
}

async function readState(response: Response): Promise<KillSwitchState> {
  let payload: unknown;
  try {
    payload = await response.json();
  } catch {
    throw new KillSwitchError('invalidResponse');
  }
  if (!payload || typeof payload !== 'object' || Array.isArray(payload)) throw new KillSwitchError('invalidResponse');
  const body = payload as Partial<KillSwitchResponse>;
  const text = (value: unknown): string | null | undefined =>
    value === undefined || value === null ? null : typeof value === 'string' ? value : undefined;
  const changedBy = text(body.changedBy);
  const changedAtUtc = text(body.changedAtUtc);
  const reason = text(body.reason);
  if (typeof body.engaged !== 'boolean' || typeof body.autonomousControlEnabled !== 'boolean' ||
      changedBy === undefined || reason === undefined || changedAtUtc === undefined ||
      (changedAtUtc !== null && Number.isNaN(Date.parse(changedAtUtc)))) {
    throw new KillSwitchError('invalidResponse');
  }
  return { engaged: body.engaged, changedBy, changedAtUtc, reason,
    autonomousControlEnabled: body.autonomousControlEnabled };
}

export async function fetchKillSwitch(signal: AbortSignal, fetcher: typeof fetch = fetch): Promise<KillSwitchState> {
  const response = await fetcher(ENDPOINT, { credentials: 'same-origin', cache: 'no-store', signal });
  if (!response.ok) throw failFor(response.status);
  return readState(response);
}

/**
 * Engage or release the kill switch. The answer must report the state that was asked for: a 2xx that says otherwise
 * is not shown as done, because a person who believes commands are stopped must be right.
 */
export async function setKillSwitch(
  engaged: boolean,
  reason: string,
  signal: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<KillSwitchState> {
  const normalized = normalizeKillSwitchReason(reason);
  const response = await fetcher(ENDPOINT, {
    method: 'PUT',
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ engaged, reason: normalized }),
    signal,
  });
  if (!response.ok) throw failFor(response.status);
  const state = await readState(response);
  if (state.engaged !== engaged) throw new KillSwitchError('invalidResponse');
  return state;
}
