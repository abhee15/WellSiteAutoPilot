import type { ControlActionResponse } from '../api/generated/v1/types.gen';
import { CONTROL_OUTCOME_MESSAGES } from './controlExecution';

export type ControlActionErrorKind =
  | 'invalid'
  | 'accessDenied'
  | 'notFound'
  | 'conflict'
  | 'unavailable'
  | 'invalidResponse';

export class ControlActionError extends Error {
  constructor(public readonly kind: ControlActionErrorKind) {
    super(kind);
    this.name = 'ControlActionError';
  }
}

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export const RESOLUTION_NOTE_MAX = 1024;
const LIST_LIMIT = 200;

/** Control actions whose result nobody has confirmed: the only ones a person can verify or resolve. */
export function controlActionNeedsDecision(action: Pick<ControlActionResponse, 'status'>): boolean {
  return action.status === 'Dispatched' || action.status === 'Ambiguous';
}

/** The note an operator attests with: required, at most 1024 characters, no control characters besides line breaks. */
export function normalizeResolutionNote(note: string): string {
  const trimmed = note.trim();
  if (trimmed.length === 0 || trimmed.length > RESOLUTION_NOTE_MAX ||
      [...trimmed].some(character => {
        const code = character.charCodeAt(0);
        return (code < 32 && code !== 9 && code !== 10 && code !== 13) || (code >= 127 && code <= 159);
      })) {
    throw new ControlActionError('invalid');
  }
  return trimmed;
}

function failFor(status: number): ControlActionError {
  if (status === 401 || status === 403) return new ControlActionError('accessDenied');
  if (status === 404) return new ControlActionError('notFound');
  if (status === 409) return new ControlActionError('conflict');
  if (status === 400) return new ControlActionError('invalid');
  return new ControlActionError('unavailable');
}

/** An action is trusted only when it names itself, its recommendation and a status this build can describe. */
function validAction(value: unknown): value is ControlActionResponse {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const action = value as Partial<ControlActionResponse>;
  const optionalText = (item: unknown) => item === undefined || item === null || typeof item === 'string';
  const optionalNumber = (item: unknown) => item === undefined || item === null || (typeof item === 'number' && Number.isFinite(item));
  const optionalDate = (item: unknown) =>
    item === undefined || item === null || (typeof item === 'string' && !Number.isNaN(Date.parse(item)));
  return typeof action.id === 'string' && uuidPattern.test(action.id) &&
    typeof action.recommendationId === 'string' && uuidPattern.test(action.recommendationId) &&
    typeof action.command === 'string' && typeof action.gateCode === 'string' &&
    typeof action.status === 'string' && Object.prototype.hasOwnProperty.call(CONTROL_OUTCOME_MESSAGES, action.status) &&
    typeof action.createdAtUtc === 'string' && !Number.isNaN(Date.parse(action.createdAtUtc)) &&
    optionalText(action.quantity) && optionalText(action.unit) && optionalText(action.sinkReference) &&
    optionalText(action.resolutionNote) && optionalNumber(action.value) && optionalNumber(action.readbackValue) &&
    optionalDate(action.completedAtUtc) && optionalDate(action.verifiedAtUtc);
}

async function readAction(response: Response, actionId: string): Promise<ControlActionResponse> {
  let payload: unknown;
  try {
    payload = await response.json();
  } catch {
    throw new ControlActionError('invalidResponse');
  }
  if (!validAction(payload) || payload.id.toLowerCase() !== actionId.toLowerCase()) {
    throw new ControlActionError('invalidResponse');
  }
  return payload;
}

/** The most recent control actions this person may read, newest first as the server orders them. */
export async function fetchControlActions(
  signal: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<ControlActionResponse[]> {
  const response = await fetcher(`/api/v1/control/actions?limit=${LIST_LIMIT}`,
    { credentials: 'same-origin', cache: 'no-store', signal });
  if (!response.ok) throw failFor(response.status);
  let payload: unknown;
  try {
    payload = await response.json();
  } catch {
    throw new ControlActionError('invalidResponse');
  }
  if (!Array.isArray(payload) || !payload.every(validAction)) throw new ControlActionError('invalidResponse');
  return payload;
}

/** Ask the target what it holds now. The answer may be that nothing changed because the target did not answer. */
export async function verifyControlAction(
  actionId: string,
  signal: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<ControlActionResponse> {
  if (!uuidPattern.test(actionId)) throw new ControlActionError('invalid');
  const response = await fetcher(`/api/v1/control/actions/${encodeURIComponent(actionId)}/verify`,
    { method: 'POST', credentials: 'same-origin', cache: 'no-store', signal });
  if (!response.ok) throw failFor(response.status);
  return readAction(response, actionId);
}

/**
 * Record, as an operator, whether the command was applied. The answer must settle the action the way the person
 * said: anything else is not shown as done, because a recorded attestation that contradicts itself must be checked.
 */
export async function resolveControlAction(
  actionId: string,
  applied: boolean,
  note: string,
  signal: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<ControlActionResponse> {
  if (!uuidPattern.test(actionId)) throw new ControlActionError('invalid');
  const normalized = normalizeResolutionNote(note);
  const response = await fetcher(`/api/v1/control/actions/${encodeURIComponent(actionId)}/resolve`, {
    method: 'POST', credentials: 'same-origin', cache: 'no-store',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ applied, note: normalized }), signal,
  });
  if (!response.ok) throw failFor(response.status);
  const action = await readAction(response, actionId);
  if (action.status !== (applied ? 'Verified' : 'Failed')) throw new ControlActionError('invalidResponse');
  return action;
}

/** Whether a verify changed what is known; false means the target gave no answer and the action still needs a decision. */
export function verifyChangedAction(before: ControlActionResponse, after: ControlActionResponse): boolean {
  return before.status !== after.status || before.gateCode !== after.gateCode;
}
