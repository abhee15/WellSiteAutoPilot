import { useSplitPaneFocus } from '../components/useSplitPaneFocus';
import { FilterToolbar, FilterField } from '../components/FilterToolbar';
import { SearchInput } from '../components/SearchInput';
import './activity.css';
import { useCallback, useEffect, useRef, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { t } from '../localization';
import { formatTimestamp } from '../format';
import { AuditActivityError, appendAuditEvents, loadAuditPage } from './auditActivity';
import type { AuditErrorKind, AuditEvent, AuditFilters } from './auditActivity';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { describeActor, describeAuditEvent, relativeTime } from './auditPresentation';
import { AuditEventPanel } from './AuditEventPanel';
import { AuditSentence } from './AuditSentence';
import { Status } from '../components/Status';
import { ActionButton } from '../components/ActionButton';

const noFilters: AuditFilters = { actionPrefix: '', targetType: '' };

function errorLabel(kind: AuditErrorKind): string {
  switch (kind) {
    case 'accessDenied': return t('activity.denied');
    case 'invalid': return t('activity.invalid');
    case 'invalidResponse': return t('activity.invalidResponse');
    case 'unavailable': return t('activity.unavailable');
  }
}

/** How long typing must pause before the list follows it. */
const FILTER_DEBOUNCE_MS = 350;

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
  const [selectedId, setSelectedId] = useState<string | null>(null);
  // "5 minutes ago" stays honest while the page is left open.
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 60_000);
    return () => window.clearInterval(timer);
  }, []);

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
  // Type-ahead: the list follows what is typed once typing pauses, so there is nothing to apply.
  const applyRef = useRef(apply);
  applyRef.current = apply;
  useEffect(() => {
    if (draft.actionPrefix === applied.actionPrefix && draft.targetType === applied.targetType) return;
    const timer = window.setTimeout(() => applyRef.current(draft), FILTER_DEBOUNCE_MS);
    return () => window.clearTimeout(timer);
  }, [draft, applied]);
  const busy = loading || loadingMore;
  const selected = events.find(event => event.id === selectedId) ?? null;
  const splitFocus = useSplitPaneFocus(selected?.id ?? null);
  const filtersTyped = draft.actionPrefix.trim() !== '' || draft.targetType.trim() !== '';

  return (
    <section className="panel activity-panel" data-has-selection={selected !== null}>
      {/* The tab already says "Activity"; the heading stays for screen readers only. */}
      <h2 className="visually-hidden">{t('activity.heading')}</h2>
      <div className="activity-toolbar">
        <FilterToolbar className="activity-filters" label={t('activity.filters')} onSubmit={() => apply(draft)}
          search={{ value: draft.actionPrefix, label: t('activity.actionFilter'), maxLength: 128,
            placeholder: `${t('activity.actionFilter')} ${t('activity.actionPlaceholder')}`,
            onChange: actionPrefix => setDraft({ ...draft, actionPrefix }) }}
          chips={draft.targetType.trim() !== '' ? [{ id: 'target', label: `${t('activity.targetFilter')}: ${draft.targetType.trim()}`,
            onRemove: () => setDraft({ ...draft, targetType: '' }) }] : []}
          isFiltered={filtersTyped} onClear={() => apply(noFilters)}
          trailing={<ActionButton icon={RefreshCw} iconOnly label={t('activity.refresh')} disabled={busy} onClick={() => void loadFirst(applied)} />}>
          <FilterField label={t('activity.targetFilter')}>
            <SearchInput maxLength={128} value={draft.targetType} placeholder={t('activity.targetPlaceholder')}
              onChange={event => setDraft({ ...draft, targetType: event.target.value })} />
          </FilterField>
        </FilterToolbar>
      </div>
      {loading && <LoadingIndicator label={t('activity.loading')} />}
      {error && <p role="alert" className="execution-detail-error access-intro">{error}</p>}
      {!loading && !error && events.length === 0 && <p className="access-note access-intro">{t('activity.empty')}</p>}
      {events.length > 0 && (
        <div {...splitFocus} className="split-layout runs-layout activity-layout" data-has-selection={selected !== null}>
          <div className="split-panes">
            <div className="split-browse">
              <div className="table-wrap">
                <table className="activity-table activity-feed" aria-label={t('activity.heading')}>
                  <thead><tr>
                    <th scope="col">{t('activity.when')}</th>
                    <th scope="col" className="run-col-secondary">{t('activity.who')}</th>
                    <th scope="col">{t('activity.event')}</th>
                  </tr></thead>
                  <tbody>
                    {events.map(event => {
                      const summary = describeAuditEvent(event);
                      const actor = describeActor(event);
                      const isSelected = selected?.id === event.id;
                      return (
                        <tr key={event.id} data-selected={isSelected}
                          onClick={click => {
                            if ((click.target as HTMLElement).closest('button, a')) return;
                            setSelectedId(event.id);
                          }}>
                          <td className="activity-time">
                            <strong>{relativeTime(event.occurredAtUtc, now)}</strong>
                            <small>{formatTimestamp(event.occurredAtUtc)}</small>
                          </td>
                          <td className="activity-cell-wrap run-col-secondary">
                            <strong>{actor.name}</strong>
                            {actor.isSystem
                              ? <small>{t('activity.automatic')}</small>
                              : actor.identity && <small>{actor.identity}</small>}
                          </td>
                          <td className="activity-cell-wrap activity-event-cell">
                            <button type="button" className="asset-select activity-open"
                              aria-current={isSelected ? 'true' : undefined}
                              aria-controls={isSelected ? 'activity-detail' : undefined}
                              aria-label={t('activity.open', { summary: summary.text })}
                              onClick={() => setSelectedId(current => current === event.id ? null : event.id)}>
                              <span className="activity-sentence"><AuditSentence summary={summary} /></span>
                            </button>
                            {(summary.outcome || summary.note) && (
                              <span className="activity-meta">
                                {summary.outcome && <Status value={summary.outcome.state} label={summary.outcome.label} />}
                                {summary.note && <small>{summary.note}</small>}
                              </span>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
              {nextCursor && !error && (
                <div className="activity-more">
                  <button type="button" className="secondary-button" disabled={busy} onClick={() => void loadOlder()}>
                    {loadingMore ? t('activity.loading') : t('activity.loadOlder')}
                  </button>
                </div>
              )}
            </div>
            <section className="split-selected" tabIndex={-1} aria-label={t('activity.detailHeading')}>
              <button type="button" className="secondary-button split-back" onClick={() => setSelectedId(null)}>
                {t('activity.backToList')}
              </button>
              {selected && <AuditEventPanel event={selected} />}
            </section>
          </div>
        </div>
      )}
    </section>
  );
}
