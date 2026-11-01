import type { AutonomyPolicyResponse } from '../api/generated/v1/types.gen';
import { ADVISORY_COMMAND } from './controlExecution';

export type AutonomyPolicyErrorKind = 'invalid' | 'accessDenied' | 'notFound' | 'unavailable' | 'invalidResponse';

export class AutonomyPolicyError extends Error {
  constructor(public readonly kind: AutonomyPolicyErrorKind) {
    super(kind);
    this.name = 'AutonomyPolicyError';
  }
}

export type AutonomyLimit = { command: string; unit: string | null; minimumValue: number | null; maximumValue: number | null };

export type AutonomyPolicy = {
  enabled: boolean;
  limits: AutonomyLimit[];
  maxActionsPerWindow: number;
  windowSeconds: number;
  allowApprovedRecommendations: boolean;
  updatedBy: string;
  updatedAtUtc: string;
};

/** What the person is editing: numbers stay text until they are validated, so half-typed input is never coerced. */
export type AutonomyLimitDraft = { command: string; unit: string; minimumValue: string; maximumValue: string };
export type AutonomyPolicyDraft = {
  enabled: boolean;
  limits: AutonomyLimitDraft[];
  maxActionsPerWindow: string;
  windowSeconds: string;
  allowApprovedRecommendations: boolean;
};

/** These mirror the server's rules in AutonomousControlService.SetPolicyAsync; the server stays the authority. */
export const POLICY_MAX_LIMITS = 64;
export const POLICY_COMMAND_MAX = 256;
export const POLICY_UNIT_MAX = 64;
export const POLICY_ACTIONS_MAX = 1000;
export const POLICY_WINDOW_MAX_SECONDS = 86400;

/** A policy that permits nothing and is off: the starting point for a configuration that has no policy yet. */
export const EMPTY_POLICY_DRAFT: AutonomyPolicyDraft = {
  enabled: false, limits: [], maxActionsPerWindow: '1', windowSeconds: '3600', allowApprovedRecommendations: false,
};

export function draftFromPolicy(policy: AutonomyPolicy | null): AutonomyPolicyDraft {
  if (!policy) return { ...EMPTY_POLICY_DRAFT, limits: [] };
  return {
    enabled: policy.enabled,
    limits: policy.limits.map(limit => ({
      command: limit.command, unit: limit.unit ?? '',
      minimumValue: limit.minimumValue === null ? '' : String(limit.minimumValue),
      maximumValue: limit.maximumValue === null ? '' : String(limit.maximumValue),
    })),
    maxActionsPerWindow: String(policy.maxActionsPerWindow),
    windowSeconds: String(policy.windowSeconds),
    allowApprovedRecommendations: policy.allowApprovedRecommendations,
  };
}

export type PolicyIssue =
  | { code: 'noLimits' }
  | { code: 'tooManyLimits' }
  | { code: 'commandRequired'; index: number }
  | { code: 'commandTooLong'; index: number }
  | { code: 'commandReserved'; index: number }
  | { code: 'commandDuplicate'; index: number }
  | { code: 'unitTooLong'; index: number }
  | { code: 'boundsRequired'; index: number }
  | { code: 'boundNotNumber'; index: number }
  | { code: 'boundsReversed'; index: number }
  | { code: 'rateInvalid' }
  | { code: 'windowInvalid' };

const NUMBER = /^-?\d+(\.\d+)?$/;
function parseNumber(text: string): number | null | 'bad' {
  const trimmed = text.trim();
  if (trimmed === '') return null;
  if (!NUMBER.test(trimmed)) return 'bad';
  const value = Number(trimmed);
  return Number.isFinite(value) ? value : 'bad';
}
function parseWhole(text: string, min: number, max: number): number | null {
  const trimmed = text.trim();
  if (!/^\d+$/.test(trimmed)) return null;
  const value = Number(trimmed);
  return value >= min && value <= max ? value : null;
}

/** Every problem with the draft, or the request to send. An enabled policy must permit a command with both bounds. */
export function validatePolicyDraft(draft: AutonomyPolicyDraft):
  { issues: PolicyIssue[]; request: null } | { issues: []; request: SetPolicyRequest } {
  const issues: PolicyIssue[] = [];
  const limits: AutonomyLimit[] = [];
  if (draft.limits.length > POLICY_MAX_LIMITS) issues.push({ code: 'tooManyLimits' });
  if (draft.enabled && draft.limits.length === 0) issues.push({ code: 'noLimits' });
  const seen = new Set<string>();
  draft.limits.forEach((item, index) => {
    const command = item.command.trim();
    if (command === '') issues.push({ code: 'commandRequired', index });
    else if (command.length > POLICY_COMMAND_MAX) issues.push({ code: 'commandTooLong', index });
    else if (command.toUpperCase() === ADVISORY_COMMAND) issues.push({ code: 'commandReserved', index });
    else if (seen.has(command.toLowerCase())) issues.push({ code: 'commandDuplicate', index });
    seen.add(command.toLowerCase());
    const unit = item.unit.trim();
    if (unit.length > POLICY_UNIT_MAX) issues.push({ code: 'unitTooLong', index });
    const min = parseNumber(item.minimumValue);
    const max = parseNumber(item.maximumValue);
    if (min === 'bad' || max === 'bad') issues.push({ code: 'boundNotNumber', index });
    else {
      if (draft.enabled && (min === null || max === null)) issues.push({ code: 'boundsRequired', index });
      if (min !== null && max !== null && min > max) issues.push({ code: 'boundsReversed', index });
      limits.push({ command, unit: unit === '' ? null : unit, minimumValue: min, maximumValue: max });
    }
  });
  const rate = parseWhole(draft.maxActionsPerWindow, 1, POLICY_ACTIONS_MAX);
  const window = parseWhole(draft.windowSeconds, 1, POLICY_WINDOW_MAX_SECONDS);
  if (rate === null) issues.push({ code: 'rateInvalid' });
  if (window === null) issues.push({ code: 'windowInvalid' });
  if (issues.length > 0 || rate === null || window === null) return { issues, request: null };
  return {
    issues: [],
    request: { enabled: draft.enabled, limits, maxActionsPerWindow: rate, windowSeconds: window,
      allowApprovedRecommendations: draft.allowApprovedRecommendations },
  };
}

export type SetPolicyRequest = {
  enabled: boolean;
  limits: AutonomyLimit[];
  maxActionsPerWindow: number;
  windowSeconds: number;
  allowApprovedRecommendations: boolean;
};

function endpoint(configuredLogicId: string): string {
  return `/api/v1/control/policies/${encodeURIComponent(configuredLogicId)}`;
}

function failFor(status: number): AutonomyPolicyError {
  if (status === 401 || status === 403) return new AutonomyPolicyError('accessDenied');
  if (status === 404) return new AutonomyPolicyError('notFound');
  if (status === 400) return new AutonomyPolicyError('invalid');
  return new AutonomyPolicyError('unavailable');
}

function optionalNumber(value: unknown): number | null | undefined {
  if (value === undefined || value === null) return null;
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

async function readPolicy(response: Response, configuredLogicId: string): Promise<AutonomyPolicy> {
  let payload: unknown;
  try {
    payload = await response.json();
  } catch {
    throw new AutonomyPolicyError('invalidResponse');
  }
  if (!payload || typeof payload !== 'object' || Array.isArray(payload)) throw new AutonomyPolicyError('invalidResponse');
  const body = payload as Partial<AutonomyPolicyResponse>;
  if (body.configuredLogicId !== configuredLogicId || typeof body.enabled !== 'boolean' ||
      typeof body.allowApprovedRecommendations !== 'boolean' ||
      !Number.isInteger(body.maxActionsPerWindow) || !Number.isInteger(body.windowSeconds) ||
      typeof body.updatedBy !== 'string' || typeof body.updatedAtUtc !== 'string' ||
      Number.isNaN(Date.parse(body.updatedAtUtc)) || !Array.isArray(body.limits)) {
    throw new AutonomyPolicyError('invalidResponse');
  }
  const limits: AutonomyLimit[] = [];
  for (const raw of body.limits as unknown[]) {
    if (!raw || typeof raw !== 'object') throw new AutonomyPolicyError('invalidResponse');
    const item = raw as { command?: unknown; unit?: unknown; minimumValue?: unknown; maximumValue?: unknown };
    const minimumValue = optionalNumber(item.minimumValue);
    const maximumValue = optionalNumber(item.maximumValue);
    if (typeof item.command !== 'string' || (item.unit !== undefined && item.unit !== null && typeof item.unit !== 'string') ||
        minimumValue === undefined || maximumValue === undefined) {
      throw new AutonomyPolicyError('invalidResponse');
    }
    limits.push({ command: item.command, unit: (item.unit as string | null | undefined) ?? null, minimumValue, maximumValue });
  }
  return { enabled: body.enabled, limits, maxActionsPerWindow: body.maxActionsPerWindow as number,
    windowSeconds: body.windowSeconds as number, allowApprovedRecommendations: body.allowApprovedRecommendations,
    updatedBy: body.updatedBy, updatedAtUtc: body.updatedAtUtc };
}

/** The policy of one configured logic, or null when it has none yet (which means no command is permitted). */
export async function fetchAutonomyPolicy(
  configuredLogicId: string, signal: AbortSignal, fetcher: typeof fetch = fetch,
): Promise<AutonomyPolicy | null> {
  const response = await fetcher(endpoint(configuredLogicId), { credentials: 'same-origin', cache: 'no-store', signal });
  if (response.status === 404) return null;
  if (!response.ok) throw failFor(response.status);
  return readPolicy(response, configuredLogicId);
}

/**
 * Replace the policy. The answer must describe what was asked for: a 2xx that reports a different policy is not
 * shown as saved, because a person who believes a command is bounded must be right.
 */
export async function saveAutonomyPolicy(
  configuredLogicId: string, request: SetPolicyRequest, signal: AbortSignal, fetcher: typeof fetch = fetch,
): Promise<AutonomyPolicy> {
  const response = await fetcher(endpoint(configuredLogicId), {
    method: 'PUT', credentials: 'same-origin', cache: 'no-store',
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request), signal,
  });
  if (!response.ok) throw failFor(response.status);
  const saved = await readPolicy(response, configuredLogicId);
  const same = saved.enabled === request.enabled &&
    saved.maxActionsPerWindow === request.maxActionsPerWindow && saved.windowSeconds === request.windowSeconds &&
    saved.allowApprovedRecommendations === request.allowApprovedRecommendations &&
    saved.limits.length === request.limits.length &&
    saved.limits.every((limit, index) => {
      const want = request.limits[index];
      return limit.command === want.command && limit.unit === want.unit &&
        limit.minimumValue === want.minimumValue && limit.maximumValue === want.maximumValue;
    });
  if (!same) throw new AutonomyPolicyError('invalidResponse');
  return saved;
}
