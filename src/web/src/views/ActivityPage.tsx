import { SearchInput } from '../components/SearchInput';
import './activity.css';
import { useCallback, useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import { formatTimestamp } from '../format';
import { AuditActivityError, appendAuditEvents, auditActionLabelKey, auditDetails, loadAuditPage } from './auditActivity';
import type { AuditErrorKind, AuditEvent, AuditFilters } from './auditActivity';
import { LoadingIndicator } from '../components/LoadingIndicator';

const noFilters: AuditFilters = { actionPrefix: '', targetType: '' };

function errorLabel(kind: AuditErrorKind): string {
  switch (kind) {
    case 'accessDenied': return t('activity.denied');
    case 'invalid': return t('activity.invalid');
    case 'invalidResponse': return t('activity.invalidResponse');
    case 'unavailable': return t('activity.unavailable');
  }
}

/** Who did what, newest first. Administrators only: the server refuses everyone else. */
export function ActivityPage() {
  const [draft, setDraft] = useState<AuditFilters>(noFilters);
  const [applied, setApplied] = useState<AuditFilters>(noFilters);
  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);

  const fail = (caught: unknown) => {
    if (caught instanceof DOMException && caught.name === 'AbortError') return;
    setError(errorLabel(caught instanceof AuditActivityError ? caught.kind : 'unavailable'));
  };

  const loadFirst = useCallback(async (filters: AuditFilters) => {
    request.current?.abort();
    const abort = new AbortController();
    request.current = abort;
    setLoading(true); setError(null); setEvents([]); setNextCursor(null);
    try {
      const page = await loadAuditPage(filters, null, abort.signal);
      if (request.current !== abort) return;
      setEvents(page.items); setNextCursor(page.nextCursor);
    } catch (caught) {
      if (request.current === abort) fail(caught);
    } finally {
      if (request.current === abort) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadFirst(noFilters);
    return () => { request.current?.abort(); };
  }, [loadFirst]);

  const loadOlder = async () => {
    if (!nextCursor) return;
    request.current?.abort();
    const abort = new AbortController();
    request.current = abort;
    setLoadingMore(true); setError(null);
    try {
      const page = await loadAuditPage(applied, nextCursor, abort.signal);
      if (request.current !== abort) return;
      setEvents(current => appendAuditEvents(current, page.items)); setNextCursor(page.nextCursor);
    } catch (caught) {
      if (request.current === abort) fail(caught);
    } finally {
      if (request.current === abort) setLoadingMore(false);
    }
  };

  const apply = (filters: AuditFilters) => { setDraft(filters); setApplied(filters); void loadFirst(filters); };
  const busy = loading || loadingMore;

  return (
    <section className="panel">
      <div className="panel-header">
        <h2>{t('activity.heading')}</h2>
        <button type="button" className="secondary-button" disabled={busy} onClick={() => void loadFirst(applied)}>
          {t('activity.refresh')}
        </button>
      </div>
      <p className="access-note access-intro">{t('activity.intro')}</p>
      <form className="activity-filters" onSubmit={event => { event.preventDefault(); apply(draft); }}>
        <label>{t('activity.actionFilter')}
          <SearchInput maxLength={128} value={draft.actionPrefix}
            onChange={event => setDraft({ ...draft, actionPrefix: event.target.value })} />
        </label>
        <label>{t('activity.targetFilter')}
          <SearchInput maxLength={128} value={draft.targetType}
            onChange={event => setDraft({ ...draft, targetType: event.target.value })} />
        </label>
        <div className="activity-filter-actions">
          <button type="submit" className="secondary-button" disabled={busy}>{t('activity.apply')}</button>
          <button type="button" className="secondary-button" disabled={busy}
            onClick={() => apply(noFilters)}>{t('activity.reset')}</button>
        </div>
      </form>
      {loading && <LoadingIndicator label={t('activity.loading')} />}
      {error && <p role="alert" className="execution-detail-error access-intro">{error}</p>}
      {!loading && !error && events.length === 0 && <p className="access-note access-intro">{t('activity.empty')}</p>}
      {events.length > 0 && (
        <div className="table-wrap">
          <table className="activity-table" aria-label={t('activity.heading')}>
            <thead><tr>
              <th scope="col">{t('activity.when')}</th>
              <th scope="col">{t('activity.who')}</th>
              <th scope="col">{t('activity.what')}</th>
              <th scope="col">{t('activity.target')}</th>
              <th scope="col">{t('activity.details')}</th>
            </tr></thead>
            <tbody>
              {events.map(event => {
                const label = auditActionLabelKey(event.action);
                const details = auditDetails(event.detailsJson);
                return (
                  <tr key={event.id}>
                    <td>{formatTimestamp(event.occurredAtUtc)}</td>
                    <td className="activity-cell-wrap">{event.actorIdentity}</td>
                    <td className="activity-cell-wrap">
                      {label ? t(label) : event.action}
                      {label && <small> ({event.action})</small>}
                    </td>
                    <td className="activity-cell-wrap">
                      {event.targetType}{event.targetId ? ` · ${event.targetId}` : ''}
                    </td>
                    <td className="activity-event-details">
                      {details.length === 0 ? '—' : (
                        <details>
                          <summary>{t('activity.show')}</summary>
                          <dl>{details.map(item => (
                            <div key={item.name} style={{ display: 'contents' }}>
                              <dt>{item.name}</dt><dd>{item.value}</dd>
                            </div>
                          ))}</dl>
                        </details>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {nextCursor && !error && (
        <div className="activity-more">
          <button type="button" className="secondary-button" disabled={busy} onClick={() => void loadOlder()}>
            {loadingMore ? t('activity.loading') : t('activity.loadOlder')}
          </button>
        </div>
      )}
    </section>
  );
}
