import type {
  ControlActionResponse,
  ExecuteApprovedRecommendationResponse,
  RecommendationSummaryResponse,
} from '../api/generated/v1/types.gen';
import type { MessageKey } from '../localization';
import { recommendationExpiryPassed } from './recommendationInbox';

export type ControlExecutionErrorKind =
  | 'invalid'
  | 'accessDenied'
  | 'notFound'
  | 'conflict'
  | 'unavailable'
  | 'invalidResponse';

export class ControlExecutionError extends Error {
  constructor(public readonly kind: ControlExecutionErrorKind) {
    super(kind);
    this.name = 'ControlExecutionError';
  }
}

/** The command the server records for advice. It has no device action behind it and can never be executed. */
export const ADVISORY_COMMAND = 'ADVISORY_REVIEW';

/** The permission that governs control writes. It is deployment-wide, so the server also checks asset scope. */
export const CONTROL_MANAGE_PERMISSION = 'control.manage';

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Only a recommendation a person approved, that still has a device action and has not been acted on, is offered for
 * execution. The server decides again (kill switch, policy, bounds, rate limit); this only avoids offering the
 * impossible.
 */
export function recommendationIsExecutable(
  item: RecommendationSummaryResponse,
  nowUtc: number,
  canExecute: boolean,
): boolean {
  const decidedBy = item.decisionBy ?? '';
  return canExecute &&
    item.canDecide === true &&
    item.status === 'Approved' &&
    !item.controlActionId &&
    item.command !== ADVISORY_COMMAND &&
    decidedBy.trim().length > 0 &&
    !decidedBy.startsWith('system:') &&
    !recommendationExpiryPassed(item, nowUtc);
}

/** One message per control action status the server can report. */
export const CONTROL_OUTCOME_MESSAGES = {
  Requested: 'control.outcome.Requested',
  Dispatched: 'control.outcome.Dispatched',
  Verified: 'control.outcome.Verified',
  Ambiguous: 'control.outcome.Ambiguous',
  Failed: 'control.outcome.Failed',
  Blocked: 'control.outcome.Blocked',
} as const satisfies Record<string, MessageKey>;

/**
 * Every reason the control gate can give for refusing or settling an action, by its stable code. A test keeps this
 * list equal to the server's GateCodes, so a new refusal cannot reach a person as a bare code.
 */
export const CONTROL_GATE_MESSAGES = {
  GATE_ALLOWED: 'control.gate.GATE_ALLOWED',
  GATE_AUTONOMOUS_DISABLED: 'control.gate.GATE_AUTONOMOUS_DISABLED',
  GATE_KILL_SWITCH: 'control.gate.GATE_KILL_SWITCH',
  GATE_NO_POLICY: 'control.gate.GATE_NO_POLICY',
  GATE_POLICY_DISABLED: 'control.gate.GATE_POLICY_DISABLED',
  GATE_NOT_AUTONOMOUS_REVISION: 'control.gate.GATE_NOT_AUTONOMOUS_REVISION',
  GATE_SOURCE_INVALID: 'control.gate.GATE_SOURCE_INVALID',
  GATE_RECOMMENDATION_EXPIRED: 'control.gate.GATE_RECOMMENDATION_EXPIRED',
  GATE_COMMAND_NOT_PERMITTED: 'control.gate.GATE_COMMAND_NOT_PERMITTED',
  GATE_VALUE_REQUIRED: 'control.gate.GATE_VALUE_REQUIRED',
  GATE_UNIT_MISMATCH: 'control.gate.GATE_UNIT_MISMATCH',
  GATE_VALUE_OUT_OF_BOUNDS: 'control.gate.GATE_VALUE_OUT_OF_BOUNDS',
  GATE_TARGET_NO_CONNECTION: 'control.gate.GATE_TARGET_NO_CONNECTION',
  GATE_TARGET_CONNECTION_DISABLED: 'control.gate.GATE_TARGET_CONNECTION_DISABLED',
  GATE_TARGET_NOT_BOUND: 'control.gate.GATE_TARGET_NOT_BOUND',
  GATE_TARGET_AMBIGUOUS: 'control.gate.GATE_TARGET_AMBIGUOUS',
  GATE_COMMAND_NOT_IN_CATALOG: 'control.gate.GATE_COMMAND_NOT_IN_CATALOG',
  GATE_COMMAND_NOT_ENABLED: 'control.gate.GATE_COMMAND_NOT_ENABLED',
  GATE_CATALOG_OUT_OF_BOUNDS: 'control.gate.GATE_CATALOG_OUT_OF_BOUNDS',
  GATE_RATE_LIMITED: 'control.gate.GATE_RATE_LIMITED',
  REAL_PROVIDER_WRITES_DISABLED: 'control.gate.REAL_PROVIDER_WRITES_DISABLED',
  GATE_SINK_UNAVAILABLE: 'control.gate.GATE_SINK_UNAVAILABLE',
  SINK_REFUSED: 'control.gate.SINK_REFUSED',
  SINK_AMBIGUOUS: 'control.gate.SINK_AMBIGUOUS',
  READBACK_CONFIRMED: 'control.gate.READBACK_CONFIRMED',
  READBACK_MISMATCH: 'control.gate.READBACK_MISMATCH',
  READBACK_NOT_APPLIED: 'control.gate.READBACK_NOT_APPLIED',
  OPERATOR_RESOLVED_APPLIED: 'control.gate.OPERATOR_RESOLVED_APPLIED',
  OPERATOR_RESOLVED_NOT_APPLIED: 'control.gate.OPERATOR_RESOLVED_NOT_APPLIED',
  GATE_APPROVED_EXECUTION_NOT_ENABLED: 'control.gate.GATE_APPROVED_EXECUTION_NOT_ENABLED',
  GATE_NOT_RECOMMENDATION_MODE: 'control.gate.GATE_NOT_RECOMMENDATION_MODE',
  GATE_RECOMMENDATION_STALE: 'control.gate.GATE_RECOMMENDATION_STALE',
  GATE_ADVISORY_NOT_EXECUTABLE: 'control.gate.GATE_ADVISORY_NOT_EXECUTABLE',
} as const satisfies Record<string, MessageKey>;

export function gateMessageKey(code: string | null | undefined): MessageKey | null {
  return code && Object.prototype.hasOwnProperty.call(CONTROL_GATE_MESSAGES, code)
    ? CONTROL_GATE_MESSAGES[code as keyof typeof CONTROL_GATE_MESSAGES]
    : null;
}

export function outcomeMessageKey(status: string | null | undefined): MessageKey | null {
  return status && Object.prototype.hasOwnProperty.call(CONTROL_OUTCOME_MESSAGES, status)
    ? CONTROL_OUTCOME_MESSAGES[status as keyof typeof CONTROL_OUTCOME_MESSAGES]
    : null;
}

export type ControlExecutionResult = {
  /** The action this execution produced (or the one an earlier execution already produced). */
  action: ControlActionResponse | null;
  /** The gate's reason when it refused; the recommendation stays approved. */
  refusalCode: string | null;
};

/**
 * Execute one approved recommendation through the control gate. The request carries no body: the server takes the
 * command from the stored, approved recommendation, never from the browser. Not retried, because an execution is
 * not safe to repeat blindly; the server is idempotent but the person should see what happened.
 */
export async function executeApprovedRecommendation(
  recommendationId: string,
  signal: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<ControlExecutionResult> {
  if (!uuidPattern.test(recommendationId)) throw new ControlExecutionError('invalid');

  const response = await fetcher(
    `/api/v1/control/recommendations/${encodeURIComponent(recommendationId)}/execute`,
    { method: 'POST', credentials: 'same-origin', cache: 'no-store', signal },
  );
  if (response.status === 401 || response.status === 403) throw new ControlExecutionError('accessDenied');
  if (response.status === 404) throw new ControlExecutionError('notFound');
  if (response.status === 409) throw new ControlExecutionError('conflict');
  if (response.status === 400) throw new ControlExecutionError('invalid');
  if (!response.ok) throw new ControlExecutionError('unavailable');

  let payload: unknown;
  try {
    payload = await response.json();
  } catch {
    throw new ControlExecutionError('invalidResponse');
  }
  return validateExecution(payload, recommendationId);
}

function validateExecution(payload: unknown, recommendationId: string): ControlExecutionResult {
  if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
    throw new ControlExecutionError('invalidResponse');
  }
  const body = payload as Partial<ExecuteApprovedRecommendationResponse>;
  const action = body.action ?? null;
  const refusalCode = body.refusalCode ?? null;
  if (typeof body.executed !== 'boolean' || (refusalCode !== null && typeof refusalCode !== 'string')) {
    throw new ControlExecutionError('invalidResponse');
  }
  if (body.executed) {
    // An execution always names its action, and the action must be this recommendation's.
    if (!action || typeof action !== 'object' || typeof action.id !== 'string' ||
        typeof action.recommendationId !== 'string' ||
        action.recommendationId.toLowerCase() !== recommendationId.toLowerCase() ||
        typeof action.status !== 'string' || !action.status) {
      throw new ControlExecutionError('invalidResponse');
    }
    return { action, refusalCode: null };
  }
  // A refusal always says why, and never carries an action.
  if (action !== null || !refusalCode) throw new ControlExecutionError('invalidResponse');
  return { action: null, refusalCode };
}
