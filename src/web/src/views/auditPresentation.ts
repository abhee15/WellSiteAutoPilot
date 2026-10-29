import { t } from '../localization';
import type { MessageKey } from '../localization';
import { shortId } from '../format';
import type { AuditEvent } from './auditActivity';
import { auditDetails } from './auditActivity';

/** One run of a sentence; the name of the thing that changed is emphasised. */
export type SentencePart = { text: string; strong?: boolean };
export type AuditTone = 'good' | 'attention' | 'neutral';
export type AuditSummary = {
  /** The whole event as plain text: used for screen readers and tests. */
  text: string;
  parts: SentencePart[];
  /** A quiet second line, such as "Was Unreachable". */
  note: string | null;
  /** The new state, when the event is a state change, shown as a coloured pill. */
  outcome: { label: string; tone: AuditTone } | null;
};
export type AuditActor = { name: string; identity: string | null; isSystem: boolean };

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** "CONFIGURED_LOGIC_CREATED", "connection.status_changed" and "logic-module.approved" share one key shape. */
export function normalizeAction(action: string): string {
  return action.toLowerCase().replace(/[^a-z0-9]+/g, '.').replace(/^\.+|\.+$/g, '');
}

/** Target types are recorded in a few spellings ("connection", "Connection", "logic-module"). */
export function normalizeKind(targetType: string): string {
  return targetType.toLowerCase().replace(/[^a-z0-9]+/g, '');
}

const templates: Record<string, MessageKey> = {
  'connection.imported': 'activity.ev.connectionImported',
  'connection.updated': 'activity.ev.connectionUpdated',
  'connection.tested': 'activity.ev.connectionTested',
  'configured.logic.created': 'activity.ev.logicCreated',
  'configured.logic.revision.created': 'activity.ev.logicRevisionCreated',
  'configured.logic.revision.validated': 'activity.ev.logicRevisionValidated',
  'configured.logic.revision.activated': 'activity.ev.logicRevisionActivated',
  'logic.module.registered': 'activity.ev.moduleRegistered',
  'logic.module.approved': 'activity.ev.moduleApproved',
  'logic.module.retired': 'activity.ev.moduleRetired',
  'logic.module.package.uploaded': 'activity.ev.packageUploaded',
  'logic.module.package.publication.queued': 'activity.ev.packageQueued',
  'logic.module.package.published': 'activity.ev.packagePublished',
  'logic.module.package.publication.skipped': 'activity.ev.packageSkipped',
  'asset.created': 'activity.ev.assetCreated',
  'asset.type.created': 'activity.ev.assetTypeCreated',
  'recommendation.approved': 'activity.ev.recommendationApproved',
  'recommendation.rejected': 'activity.ev.recommendationRejected',
  'execution.shadow.requested': 'activity.ev.runRequested',
  'execution.result.conflict': 'activity.ev.resultConflict',
  'security.grant.created': 'activity.ev.grantCreated',
  'security.grant.deactivated': 'activity.ev.grantDeactivated',
  'security.role.created': 'activity.ev.roleCreated',
  'security.role.updated': 'activity.ev.roleUpdated',
  'security.role.assigned': 'activity.ev.roleAssigned',
  'security.user.provisioned': 'activity.ev.userProvisioned',
  'security.user.access.replaced': 'activity.ev.userAccessReplaced',
  'security.bootstrap.admin.assigned': 'activity.ev.bootstrapAccess',
  'security.bootstrap.grants.assigned': 'activity.ev.bootstrapAccess',
};

const kindLabels: Record<string, MessageKey> = {
  connection: 'activity.kind.connection',
  configuredlogic: 'activity.kind.configuredlogic',
  logicmodule: 'activity.kind.logicmodule',
  asset: 'activity.kind.asset',
  assetview: 'activity.kind.assetview',
  assettype: 'activity.kind.assettype',
  securityuser: 'activity.kind.securityuser',
  securityrole: 'activity.kind.securityrole',
  securityaccessgrant: 'activity.kind.securityaccessgrant',
  recommendation: 'activity.kind.recommendation',
  execution: 'activity.kind.execution',
};

const outcomeLabels: Record<string, MessageKey> = {
  Healthy: 'connections.outcomeHealthy',
  NeedsAttention: 'connections.outcomeNeedsAttention',
  Unreachable: 'connections.outcomeUnreachable',
};

/** "AdapterKey" -> "Adapter key"; "connection-monitor" -> "Connection monitor". */
export function humanize(text: string): string {
  const words = text.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[._\-\s]+/g, ' ').trim().toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/** The kind of record an event touched, in words ("Connection"); the recorded type when it is not a known one. */
export function targetKindLabel(targetType: string): string {
  const key = kindLabels[normalizeKind(targetType)];
  return key ? t(key) : humanize(targetType);
}

/** What to call the affected record: its name, or a short id when it has none (for example, it was deleted). */
export function targetSubject(event: AuditEvent): string {
  const name = event.targetName?.trim();
  if (name) return name;
  if (!event.targetId) return '';
  return uuid.test(event.targetId) ? shortId(event.targetId) : event.targetId;
}

function detailValues(detailsJson: string): Record<string, string> {
  return Object.fromEntries(auditDetails(detailsJson).map(item => [item.name, item.value]));
}

function toneFor(state: string): AuditTone {
  if (state === 'Healthy') return 'good';
  if (state === 'Unreachable' || state === 'NeedsAttention') return 'attention';
  return 'neutral';
}

function outcomeLabel(state: string): string {
  const key = outcomeLabels[state];
  return key ? t(key) : state;
}

// A character that cannot occur in a localized message marks where the highlighted name goes.
const MARK = '\u0001';

function sentence(key: MessageKey, subject: string, extra: Record<string, string> = {}): SentencePart[] {
  const message = t(key, { ...extra, target: MARK });
  // Some events have no single record to name ("An access grant was created"): the sentence stands alone.
  if (!message.includes(MARK)) return [{ text: message }];
  const [before = '', after = ''] = message.split(MARK);
  if (!subject) return [{ text: (before + after).replace(/\s{2,}/g, ' ').trim() }];
  return [{ text: before }, { text: subject, strong: true }, { text: after }].filter(part => part.text !== '');
}

/** A plain-language sentence for one event, in the same shape for every kind of event. */
export function describeAuditEvent(event: AuditEvent): AuditSummary {
  const action = normalizeAction(event.action);
  const subject = targetSubject(event);
  let parts: SentencePart[];
  let note: string | null = null;
  let outcome: AuditSummary['outcome'] = null;

  if (action === 'connection.status.changed') {
    const details = detailValues(event.detailsJson);
    const to = details.To;
    if (to) {
      parts = sentence('activity.ev.connectionStatus', subject, { to: outcomeLabel(to) });
      outcome = { label: outcomeLabel(to), tone: toneFor(to) };
      if (details.From) note = t('activity.ev.was', { from: outcomeLabel(details.From) });
    } else {
      parts = sentence('activity.ev.connectionStatusUnknown', subject);
    }
  } else if (templates[action]) {
    parts = sentence(templates[action], subject);
  } else {
    // An action this page does not know yet still reads as words, with the record it touched.
    const words = humanize(event.action);
    parts = subject
      ? [{ text: words + ' · ' }, { text: subject, strong: true }]
      : [{ text: words }];
  }
  return { text: parts.map(part => part.text).join(''), parts, note, outcome };
}

/** The person (or automatic process) behind an event. */
export function describeActor(event: AuditEvent): AuditActor {
  const identity = event.actorIdentity.trim();
  if (/^system:/i.test(identity)) {
    return { name: humanize(identity.slice('system:'.length)) || t('activity.system'), identity, isSystem: true };
  }
  const name = event.actorName?.trim();
  if (name) return { name, identity: identity && identity !== name ? identity : null, isSystem: false };
  return { name: identity || t('activity.unknownPerson'), identity: null, isSystem: false };
}

/** "Just now", "5 minutes ago", "yesterday"; an older event shows its date. The exact time is always shown beside it. */
export function relativeTime(occurredAtUtc: string, now: number): string {
  const then = Date.parse(occurredAtUtc);
  if (Number.isNaN(then)) return occurredAtUtc;
  const seconds = Math.round((then - now) / 1000);
  const age = Math.abs(seconds);
  if (age < 45) return t('activity.justNow');
  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
  if (age < 3600) return formatter.format(Math.round(seconds / 60), 'minute');
  if (age < 86400) return formatter.format(Math.round(seconds / 3600), 'hour');
  if (age < 7 * 86400) return formatter.format(Math.round(seconds / 86400), 'day');
  return new Date(then).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

/** The recorded details as readable label/value pairs ("Adapter key", "foresite"). */
export function readableDetails(detailsJson: string): { label: string; value: string }[] {
  return auditDetails(detailsJson).map(item => ({ label: humanize(item.name), value: item.value }));
}

/** The record exactly as stored, indented for reading and copying. */
export function recordedJson(detailsJson: string): string {
  try { return JSON.stringify(JSON.parse(detailsJson), null, 2); } catch { return detailsJson; }
}
