import type { MessageKey } from '../localization';

export type AuditEvent = {
  id: string;
  occurredAtUtc: string;
  actorUserId: string | null;
  actorIdentity: string;
  action: string;
  targetType: string;
  targetId: string | null;
  assetId: string | null;
  correlationId: string | null;
  detailsJson: string;
  /** The current name of the affected record; absent when it has none, was deleted, or the server predates names. */
  targetName?: string | null;
  /** The display name of the person who acted; absent for system events. */
  actorName?: string | null;
};

export type AuditPage = { items: AuditEvent[]; nextCursor: string | null };
export type AuditFilters = { actionPrefix: string; targetType: string };
export type AuditErrorKind = 'accessDenied' | 'invalid' | 'invalidResponse' | 'unavailable';

export const AUDIT_PAGE_SIZE = 50;

export class AuditActivityError extends Error {
  constructor(public readonly kind: AuditErrorKind) {
    super(kind);
    this.name = 'AuditActivityError';
  }
}

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const cursorPattern = /^[A-Za-z0-9_-]{1,128}$/;

function isObject(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}

const optionalText = (value: unknown, max: number) =>
  value === null || (typeof value === 'string' && value.length <= max);

function isAuditEvent(value: unknown): value is AuditEvent {
  if (!isObject(value)) return false;
  return typeof value.id === 'string' && uuid.test(value.id) &&
    typeof value.occurredAtUtc === 'string' && !Number.isNaN(Date.parse(value.occurredAtUtc)) &&
    (value.actorUserId === null || (typeof value.actorUserId === 'string' && uuid.test(value.actorUserId))) &&
    typeof value.actorIdentity === 'string' && value.actorIdentity.length <= 256 &&
    typeof value.action === 'string' && value.action.length > 0 && value.action.length <= 128 &&
    typeof value.targetType === 'string' && value.targetType.length <= 128 &&
    optionalText(value.targetId, 256) &&
    (value.assetId === null || (typeof value.assetId === 'string' && uuid.test(value.assetId))) &&
    optionalText(value.correlationId, 128) &&
    typeof value.detailsJson === 'string' &&
    (value.targetName === undefined || optionalText(value.targetName, 512)) &&
    (value.actorName === undefined || optionalText(value.actorName, 512));
}

function classify(status: number): AuditActivityError {
  if (status === 401 || status === 403) return new AuditActivityError('accessDenied');
  if (status === 400) return new AuditActivityError('invalid');
  return new AuditActivityError('unavailable');
}

/** The query string for one page; empty filters are left out and text is trimmed. */
export function auditQuery(filters: AuditFilters, cursor: string | null): string {
  const query = new URLSearchParams({ limit: String(AUDIT_PAGE_SIZE) });
  const actionPrefix = filters.actionPrefix.trim();
  const targetType = filters.targetType.trim();
  if (actionPrefix) query.set('actionPrefix', actionPrefix);
  if (targetType) query.set('targetType', targetType);
  if (cursor) query.set('cursor', cursor);
  return query.toString();
}

/** One page, newest first. The cursor is opaque and only ever sent back as received. */
export async function loadAuditPage(
  filters: AuditFilters, cursor: string | null, signal: AbortSignal, fetcher: typeof fetch = fetch,
): Promise<AuditPage> {
  let response: Response;
  try {
    response = await fetcher('/api/v1/security/audit-events?' + auditQuery(filters, cursor),
      { credentials: 'same-origin', cache: 'no-store', signal });
  } catch (error) {
    if (signal.aborted) throw error;
    throw new AuditActivityError('unavailable');
  }
  if (!response.ok) throw classify(response.status);
  let body: unknown;
  try { body = await response.json(); } catch { throw new AuditActivityError('invalidResponse'); }
  if (!isObject(body) || !Array.isArray(body.items) || body.items.length > AUDIT_PAGE_SIZE ||
    !body.items.every(isAuditEvent) ||
    !(body.nextCursor === null || (typeof body.nextCursor === 'string' && cursorPattern.test(body.nextCursor))) ||
    new Set(body.items.map(item => item.id.toLowerCase())).size !== body.items.length) {
    throw new AuditActivityError('invalidResponse');
  }
  return { items: body.items, nextCursor: body.nextCursor };
}

/** Adds an older page, skipping any event already shown (a retried request cannot duplicate rows). */
export function appendAuditEvents(current: readonly AuditEvent[], older: readonly AuditEvent[]): AuditEvent[] {
  const seen = new Set(current.map(item => item.id.toLowerCase()));
  return [...current, ...older.filter(item => !seen.has(item.id.toLowerCase()))];
}

/** Known actions get a plain-language label; anything else shows the recorded action code. */
const actionLabels: Record<string, MessageKey> = {
  'asset.created': 'activity.action.assetCreated',
  'asset-type.created': 'activity.action.assetTypeCreated',
  'logic-module.registered': 'activity.action.moduleRegistered',
  'logic-module.approved': 'activity.action.moduleApproved',
  'logic-module.retired': 'activity.action.moduleRetired',
  'recommendation.approved': 'activity.action.recommendationApproved',
  'recommendation.rejected': 'activity.action.recommendationRejected',
  'execution.result.conflict': 'activity.action.resultConflict',
  'security.grant.created': 'activity.action.grantCreated',
  'security.grant.deactivated': 'activity.action.grantDeactivated',
  'security.role.created': 'activity.action.roleCreated',
  'security.role.updated': 'activity.action.roleUpdated',
  'security.user.provisioned': 'activity.action.userProvisioned',
};

export function auditActionLabelKey(action: string): MessageKey | null {
  return actionLabels[action] ?? null;
}

export type AuditDetail = { name: string; value: string };

const MAX_DETAILS = 12;
const MAX_VALUE = 200;

/**
 * The top-level scalar values of an event's recorded details, for display as plain text.
 * Nested data and malformed JSON are not rendered; the raw text is never injected as markup.
 */
export function auditDetails(detailsJson: string): AuditDetail[] {
  let parsed: unknown;
  try { parsed = JSON.parse(detailsJson); } catch { return []; }
  if (!isObject(parsed)) return [];
  const result: AuditDetail[] = [];
  for (const [name, value] of Object.entries(parsed)) {
    if (result.length >= MAX_DETAILS) break;
    if (value === null || ['string', 'number', 'boolean'].includes(typeof value)) {
      const text = value === null ? '—' : String(value);
      result.push({ name: name.slice(0, 64), value: text.length > MAX_VALUE ? text.slice(0, MAX_VALUE) + '…' : text });
    }
  }
  return result;
}
