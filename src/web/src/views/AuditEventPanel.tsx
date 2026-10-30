import { t } from '../localization';
import { formatTimestamp } from '../format';
import { CopyButton } from './CopyButton';
import type { AuditEvent } from './auditActivity';
import {
  describeActor, describeAuditEvent, readableDetails, recordedJson, targetKindLabel, targetSubject,
} from './auditPresentation';
import { AuditSentence } from './AuditSentence';
import { Status } from '../components/Status';

function Fact({ label, value, copyable = false }: { label: string; value: string | null | undefined; copyable?: boolean }) {
  if (!value) return null;
  return (
    <div className="activity-fact">
      <dt>{label}</dt>
      <dd>{value}{copyable && <CopyButton value={value} label={label} />}</dd>
    </div>
  );
}

/** Everything recorded about one event, in words first and the exact record last. */
export function AuditEventPanel({ event }: { event: AuditEvent }) {
  const summary = describeAuditEvent(event);
  const actor = describeActor(event);
  const details = readableDetails(event.detailsJson);
  const kind = event.targetType ? targetKindLabel(event.targetType) : null;
  const subject = targetSubject(event);
  const target = [kind, event.targetName?.trim() || null].filter(Boolean).join(' · ');
  return (
    <article className="activity-detail" id="activity-detail" aria-labelledby="activity-detail-title">
      <header>
        <p className="eyebrow">{t('activity.detailHeading')}</p>
        <h3 id="activity-detail-title"><AuditSentence summary={summary} /></h3>
        <div className="activity-detail-meta">
          {summary.outcome && <Status value={summary.outcome.state} label={summary.outcome.label} />}
          {summary.note && <span className="activity-note">{summary.note}</span>}
        </div>
      </header>
      <dl className="activity-facts">
        <Fact label={t('activity.field.when')} value={formatTimestamp(event.occurredAtUtc)} />
        <Fact label={t('activity.field.who')}
          value={actor.identity ? `${actor.name} (${actor.identity})` : actor.name} />
        <Fact label={t('activity.field.target')} value={target || subject} />
        <Fact label={t('activity.field.targetId')} value={event.targetId} copyable />
        <Fact label={t('activity.field.asset')} value={event.assetId} copyable />
        <Fact label={t('activity.field.code')} value={event.action} copyable />
        <Fact label={t('activity.field.correlation')} value={event.correlationId} copyable />
        <Fact label={t('activity.field.eventId')} value={event.id} copyable />
      </dl>
      <section aria-label={t('activity.field.recorded')}>
        <h4>{t('activity.field.recorded')}</h4>
        {details.length === 0
          ? <p className="activity-note">{t('activity.field.nothingRecorded')}</p>
          : (
            <dl className="activity-facts">
              {details.map(item => (
                <div className="activity-fact" key={item.label}>
                  <dt>{item.label}</dt><dd>{item.value}</dd>
                </div>
              ))}
            </dl>
          )}
      </section>
      <details className="activity-raw">
        <summary>{t('activity.field.rawRecord')}</summary>
        <pre>{recordedJson(event.detailsJson)}</pre>
        <CopyButton value={recordedJson(event.detailsJson)} label={t('activity.field.rawRecord')} />
      </details>
    </article>
  );
}
