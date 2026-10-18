import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useHashDetail } from '../routing';
import type { AssetResponse, RecommendationSummaryResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { formatDetailTimestamp } from './executionDetail';
import { RecommendationInboxControls } from './RecommendationInboxControls';
import { RecommendationDecisionReviewPanel } from './RecommendationDecisionReviewPanel';
import {
  DEFAULT_RECOMMENDATION_INBOX_CRITERIA,
  fetchRecommendationInbox,
  filterRecommendationInbox,
  inboxCounts,
  recommendationIsAwaiting,
  recommendationIsExpired,
} from './recommendationInbox';
import type { RecommendationInboxCriteria } from './recommendationInbox';

const INBOX_REFRESH_INTERVAL_MS = 10_000;
type InboxError = 'accessDenied' | 'invalidResponse' | 'unavailable';

function formatSuggestedValue(value?: number | null, unit?: string | null): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) return '—';
  return new Intl.NumberFormat().format(value) + (unit ? ' ' + unit : '');
}

export function RecommendationInboxTable({
  items,
  nowUtc,
  onReview,
  assetNames,
}: {
  items: readonly RecommendationSummaryResponse[];
  nowUtc: number;
  onReview?: (recommendationId: string) => void;
  /** Asset names by id, when the assets are loaded; the id alone is shown otherwise. */
  assetNames?: ReadonlyMap<string, string>;
}) {
  return (
    <div className="table-wrap">
      <table className="activity-table">
        <thead>
          <tr>
            <th scope="col">{t('inbox.status')}</th>
            <th scope="col">{t('inbox.asset')}</th>
            <th scope="col">{t('inbox.module')}</th>
            <th scope="col">{t('inbox.proposal')}</th>
            <th scope="col">{t('inbox.reason')}</th>
            <th scope="col">{t('inbox.timing')}</th>
            <th scope="col">{t('inbox.review')}</th>
            {onReview && <th scope="col">{t('decision.action')}</th>}
          </tr>
        </thead>
        <tbody>
          {items.map(item => (
            <tr key={item.id}>
              <td>
                <strong>{item.status ?? '—'}</strong>
                {recommendationIsExpired(item, nowUtc) && (
                  <small>{t('inbox.expiredNote')}</small>
                )}
              </td>
              <td>{assetNames?.get(item.assetId ?? '')
                ? <><strong>{assetNames.get(item.assetId ?? '')}</strong><small>{item.assetId}</small></>
                : item.assetId ?? '—'}</td>
              <td>
                <strong>{item.moduleId ?? '—'}</strong>
                <small>{item.moduleVersion ?? '—'}</small>
              </td>
              <td>
                <strong>{item.command ?? item.code ?? '—'}</strong>
                <small>{formatSuggestedValue(item.suggestedValue, item.unit)}</small>
                <small>{item.quantity ?? '—'}</small>
              </td>
              <td>{item.reasonCode ?? '—'}</td>
              <td>
                <strong>{formatDetailTimestamp(item.createdAtUtc)}</strong>
                <small>{t('inbox.expires')} {formatDetailTimestamp(item.expiresAtUtc)}</small>
              </td>
              <td>
                <strong>{item.decisionBy ?? '—'}</strong>
                <small>{formatDetailTimestamp(item.decisionAtUtc)}</small>
              </td>
              {onReview && (
                <td>
                  {item.id && item.canDecide === true && recommendationIsAwaiting(item, nowUtc) ? (
                    <button type="button" className="execution-inspect-button"
                      onClick={() => onReview(item.id!)}>
                      {t('decision.open')}
                    </button>
                  ) : '—'}
                </td>
              )}
            </tr>
          ))}
          {items.length === 0 && (
            <tr>
              <td className="empty-state" colSpan={onReview ? 8 : 7}>
                {t('inbox.noMatches')}
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}

export function RecommendationsPage({
  userId, permissions, assets = [],
}: {
  userId: string | null | undefined;
  permissions?: readonly string[] | null;
  assets?: readonly AssetResponse[];
}) {
  const assetNames = useMemo(
    () => new Map(assets.flatMap(asset => (asset.id && asset.name ? [[asset.id, asset.name] as const] : []))),
    [assets],
  );
  const [items, setItems] = useState<RecommendationSummaryResponse[]>([]);
  const [criteria, setCriteria] = useState<RecommendationInboxCriteria>(
    DEFAULT_RECOMMENDATION_INBOX_CRITERIA,
  );
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<InboxError | null>(null);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  const [selectedReviewId, setSelectedReviewId] = useHashDetail('recommendations');
  const [reviewNotice, setReviewNotice] = useState<string | null>(null);
  const canDecide = Boolean(userId && permissions?.includes('recommendations.decide'));
  const activeRefresh = useRef<AbortController | null>(null);

  const refresh = useCallback(async () => {
    activeRefresh.current?.abort();
    const controller = new AbortController();
    activeRefresh.current = controller;
    setIsLoading(true);

    try {
      const data = await fetchRecommendationInbox(controller.signal);
      if (controller.signal.aborted) return;
      setItems(data);
      setLastUpdated(new Date());
      setError(null);
    } catch (reason) {
      if (controller.signal.aborted) return;
      // Never retain a previously authorized inbox after errors or revocation.
      setItems([]);
      setLastUpdated(null);
      setError(reason instanceof Error &&
        reason.message === 'RECOMMENDATION_INBOX_ACCESS_DENIED' ? 'accessDenied'
        : reason instanceof Error &&
          reason.message === 'RECOMMENDATION_INBOX_INVALID_RESPONSE'
          ? 'invalidResponse' : 'unavailable');
    } finally {
      if (activeRefresh.current === controller) {
        activeRefresh.current = null;
        setIsLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    if (!userId) {
      setItems([]);
      setLastUpdated(null);
      setError('accessDenied');
      setIsLoading(false);
      return;
    }

    void refresh();
    const interval = window.setInterval(() => void refresh(), INBOX_REFRESH_INTERVAL_MS);
    return () => {
      window.clearInterval(interval);
      activeRefresh.current?.abort();
      activeRefresh.current = null;
    };
  }, [userId, refresh]);

  const nowUtc = Date.now();
  const selectedRecommendation = items.find(item => item.id === selectedReviewId && item.canDecide === true);
  useEffect(() => {
    if (lastUpdated && selectedReviewId && (!canDecide || !selectedRecommendation ||
      !recommendationIsAwaiting(selectedRecommendation, Date.now()))) {
      setSelectedReviewId(null);
      setReviewNotice(t(canDecide ? 'decision.outdated' : 'decision.accessDenied'));
    }
  }, [lastUpdated, selectedReviewId, selectedRecommendation, canDecide, setSelectedReviewId]);
  const counts = inboxCounts(items, nowUtc);
  const visible = useMemo(
    () => filterRecommendationInbox(items, criteria, nowUtc),
    [items, criteria, nowUtc],
  );
  const errorMessages: Record<InboxError, string> = {
    accessDenied: t('inbox.accessDenied'),
    invalidResponse: t('inbox.invalidResponse'),
    unavailable: t('inbox.unavailable'),
  };

  return (
    <>
      <section className="summary-grid" aria-label={t('inbox.summary')}>
        <article className="summary-card">
          <span>{t('inbox.awaiting')}</span>
          <strong>{counts.awaiting}</strong>
          <small>{t('inbox.authorizedWindow')}</small>
        </article>
        <article className="summary-card">
          <span>{t('inbox.expired')}</span>
          <strong>{counts.expired}</strong>
          <small>{t('inbox.expiryDescription')}</small>
        </article>
        <article className="summary-card">
          <span>{t('inbox.decided')}</span>
          <strong>{counts.decided}</strong>
          <small>{t('inbox.authorizedWindow')}</small>
        </article>
      </section>
      <section className="panel">
        <div className="panel-header">
          <div>
            <p className="eyebrow">{t('inbox.eyebrow')}</p>
            <h2>{t('inbox.heading')}</h2>
          </div>
          <div className="execution-detail-actions">
            {lastUpdated && (
              <span className="last-updated">{t('inbox.updated')} {formatDetailTimestamp(
                lastUpdated.toISOString(),
              )}</span>
            )}
            <button className="secondary-button" type="button"
              disabled={isLoading || !userId} onClick={() => void refresh()}>
              {t('inbox.reload')}
            </button>
          </div>
        </div>
        <p className="recommendation-readonly-note">
          {canDecide ? t('decision.governanceNotice') : t('inbox.readOnly')}
        </p>
        <RecommendationInboxControls
          criteria={criteria}
          onChange={setCriteria}
          matchingCount={visible.length}
          loadedCount={items.length}
        />
        {reviewNotice && <p role="status" className="recommendation-readonly-note">
          {reviewNotice}
        </p>}
        {isLoading && !lastUpdated && <p role="status" className="status-loading">{t('inbox.loading')}</p>}
        {error && <p role="alert" className="execution-detail-error inbox-error">
          {errorMessages[error]}
        </p>}
        {(!isLoading || lastUpdated) && !error && (
          <RecommendationInboxTable items={visible} nowUtc={nowUtc} assetNames={assetNames}
            onReview={canDecide ? recommendationId => {
              setSelectedReviewId(recommendationId);
              setReviewNotice(null);
            } : undefined} />
        )}
        {canDecide && selectedRecommendation &&
          recommendationIsAwaiting(selectedRecommendation, nowUtc) && (
            <RecommendationDecisionReviewPanel
              key={JSON.stringify(selectedRecommendation)}
              item={selectedRecommendation}
              assetName={assetNames.get(selectedRecommendation.assetId ?? '')}
              onCancel={() => setSelectedReviewId(null)}
              onSaved={() => {
                setSelectedReviewId(null);
                setReviewNotice(t('decision.saved'));
                void refresh();
              }}
              onConflict={() => {
                setSelectedReviewId(null);
                setReviewNotice(t('decision.conflict'));
                void refresh();
              }}
            />
          )}
      </section>
    </>
  );
}
