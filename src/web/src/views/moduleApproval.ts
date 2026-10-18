import type { LogicModuleResponse } from '../api/generated/v1/types.gen';
import { trustLabel } from '../words';

/** Where a module version stands. A retired version was approved once and is switched off for good. */
export type ModuleState = 'notApproved' | 'approved' | 'retired';
export type ModuleApprovalAction = 'approve' | 'retire';

export function moduleState(module: Pick<LogicModuleResponse, 'trustStatus' | 'isEnabled'>): ModuleState {
  if (module.isEnabled) return 'approved';
  if (module.trustStatus === 'Untrusted') return 'notApproved';
  // Only an explicit "not enabled" means retired; a missing value is not read as a retirement.
  return module.isEnabled === false ? 'retired' : 'approved';
}

/** The word for a module's standing: its approval, or "Retired" once it has been switched off. */
export function moduleStateLabel(module: Pick<LogicModuleResponse, 'trustStatus' | 'isEnabled'>): string {
  return moduleState(module) === 'retired' ? 'Retired' : trustLabel(module.trustStatus);
}

/** The value that picks the pill colour: a retired version is neutral, not green like an approved one. */
export function moduleStateToken(module: Pick<LogicModuleResponse, 'trustStatus' | 'isEnabled'>): string | null | undefined {
  return moduleState(module) === 'retired' ? 'Retired' : module.trustStatus;
}

/** The one action a person can take on a module in this state, if any. */
export function availableApproval(state: ModuleState): ModuleApprovalAction | null {
  return state === 'notApproved' ? 'approve' : state === 'approved' ? 'retire' : null;
}

export const MAXIMUM_REASON_LENGTH = 500;

/** Whether the reason can be sent: something besides spaces, within the server's limit, no control characters. */
export function reasonProblem(reason: string): 'empty' | 'long' | 'characters' | null {
  const trimmed = reason.trim();
  if (trimmed.length === 0) return 'empty';
  if (trimmed.length > MAXIMUM_REASON_LENGTH) return 'long';
  // eslint-disable-next-line no-control-regex
  return /[\u0000-\u001f\u007f-\u009f]/.test(trimmed) ? 'characters' : null;
}

export function approvalPath(module: Pick<LogicModuleResponse, 'moduleId' | 'version'>, action: ModuleApprovalAction): string {
  return `/api/v1/logic-modules/${encodeURIComponent(module.moduleId ?? '')}/${encodeURIComponent(module.version ?? '')}/${action}`;
}

/** Whether the server's answer shows the change actually happened to this module version. */
export function approvalTookEffect(
  before: Pick<LogicModuleResponse, 'id'>,
  receipt: unknown,
  action: ModuleApprovalAction,
): boolean {
  if (typeof receipt !== 'object' || receipt === null) return false;
  const value = receipt as Partial<LogicModuleResponse>;
  return value.id === before.id && value.isEnabled === (action === 'approve');
}
