import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { hashFor, useHashDetail } from '../routing';
import { useChangeRefresh, usePollingRefresh } from '../live/LiveUpdates';
import type { AssetResponse, ConfiguredLogicResponse, RecommendationSummaryResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { formatDetailTimestamp } from './executionDetail';
import { RecommendationInboxControls } from './RecommendationInboxControls';
import { RecommendationDecisionReviewPanel } from './RecommendationDecisionReviewPanel';
import { ControlExecutionPanel } from './ControlExecutionPanel';
import { KillSwitchPanel } from './KillSwitchPanel';
import { ControlActionsPanel } from './ControlActionsPanel';
import { CONTROL_MANAGE_PERMISSION, outcomeMessageKey, recommendationIsExecutable } from './controlExecution';
import {
  DEFAULT_RECOMMENDATION_INBOX_CRITERIA,
  fetchRecommendationInbox,
  fetchRecommendationById,
  filterRecommendationInbox,
  inboxCounts,
  recommendationIsAwaiting,
  recommendationIsExpired,
  RECOMMENDATION_INBOX_FILTERS,
} from './recommendationInbox';
import type { RecommendationInboxCriteria } from './recommendationInbox';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { Status } from '../components/Status';
import { ActionButton } from '../components/ActionButton';
import { EmptyRow, SummaryCard } from '../components/common';
import type { MessageKey } from '../localization';
import { humanize } from './auditPresentation';
import { useSplitPaneFocus } from '../components/useSplitPaneFocus';
import { useAssetIdentityDirectory } from './useAssetIdentityDirectory';
import { assetIdentityLabel } from './runContext';
import { groupRecommendationRuns } from './recommendationRuns';
import { shortId } from '../format';
import './recommendationRuns.css';
import './splitLayout.css';

type InboxError = 'accessDenied' | 'invalidResponse' | 'unavailable';

function formatSuggestedValue(value?: number | null, unit?: string | null): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) return '—';
  return new Intl.NumberFormat().format(value) + (unit ? ' ' + unit : '');
}

export function RecommendationInboxTable({
  items,
  nowUtc,
  onReview,
  onExecute,
  selectedId,
  assetNames,
  onClearFilters,
}: {
  /** Offered in the empty state when filters are what hid the rows. */
  onClearFilters?: () => void;
  items: readonly RecommendationSummaryResponse[];
  nowUtc: number;
  onReview?: (recommendationId: string) => void;
  /** Offered for approved recommendations a person may send through the control gate. */
  onExecute?: (recommendationId: string) => void;
  /** The recommendation open for review beside the table, marked in its row. */
  selectedId?: string | null;
  /** Asset names by id, when the assets are loaded; the id alone is shown otherwise. */
  assetNames?: ReadonlyMap<string, string>;
}) {
  return (
    <div className="table-wrap">
      <table className="activity-table recommendation-proposals">
        <thead>
          <tr>
            <th scope="col">{t('inbox.status')}</th>
            <th scope="col">{t('inbox.asset')}</th>
            <th scope="col" className="run-col-secondary">{t('inbox.module')}</th>
            <th scope="col">{t('inbox.proposal')}</th>
            <th scope="col" className="run-col-secondary">{t('inbox.reason')}</th>
            <th scope="col" className="run-col-secondary">{t('inbox.timing')}</th>
            <th scope="col" className="run-col-secondary">{t('inbox.review')}</th>
            {(onReview || onExecute) && <th scope="col">{t('decision.action')}</th>}
          </tr>
        </thead>
        <tbody>
          {items.map(item => {
            const reviewable = Boolean(onReview && item.id && item.canDecide === true && recommendationIsAwaiting(item, nowUtc));
            const executable = Boolean(onExecute && item.id && recommendationIsExecutable(item, nowUtc, true));
            return (
            <tr key={item.id} data-selected={Boolean(selectedId) && selectedId === item.id}
              data-reviewable={reviewable || executable || undefined}
              onClick={event => {
                // The row is a convenience; the Review button stays the keyboard and screen-reader route.
                if ((!reviewable && !executable) || (event.target as HTMLElement).closest('button, a')) return;
                if (reviewable) onReview?.(item.id!);
                else onExecute?.(item.id!);
              }}>
              <td>
                {item.status ? <Status value={item.status} tone={recommendationIsAwaiting(item, nowUtc) ? 'warning' : undefined} /> : '—'}
                {recommendationIsExpired(item, nowUtc) && (
                  <small>{t('inbox.expiredNote')}</small>
                )}
              </td>
              <td>{assetNames?.get(item.assetId ?? '')
                ? <strong title={item.assetId}>{assetNames.get(item.assetId ?? '')}</strong>
                : item.assetId ?? '—'}</td>
              <td className="run-col-secondary">
                <strong>{item.moduleId ?? '—'}</strong>
                <small>{item.moduleVersion ?? '—'}</small>
              </td>
              <td>
                <strong>{item.command ?? item.code ?? '—'}</strong>
                <small>{formatSuggestedValue(item.suggestedValue, item.unit)}</small>
                <small>{item.quantity ?? '—'}</small>
              </td>
              <td className="run-col-secondary">{item.reasonCode ? humanize(item.reasonCode) : '—'}</td>
              <td className="run-col-secondary">
                <strong>{formatDetailTimestamp(item.createdAtUtc)}</strong>
                <small>{t('inbox.expires')} {formatDetailTimestamp(item.expiresAtUtc)}</small>
              </td>
              <td className="run-col-secondary">
                <strong>{item.decisionBy ?? '—'}</strong>
                <small>{formatDetailTimestamp(item.decisionAtUtc)}</small>
              </td>
              {(onReview || onExecute) && (
                <td>
                  {reviewable ? (
                    <ActionButton label={t('decision.open')} onClick={() => onReview?.(item.id!)} />
                  ) : executable ? (
                    <ActionButton label={t('control.execute.open')} onClick={() => onExecute?.(item.id!)} />
                  ) : '—'}
                </td>
              )}
            </tr>
            );
          })}
          {items.length === 0 && (
            <EmptyRow columns={onReview || onExecute ? 8 : 7}
              action={onClearFilters ? <ActionButton label={t('views.clearFilters')} onClick={onClearFilters} /> : undefined}>
              {t('inbox.noMatches')}
            </EmptyRow>
          )}
        </tbody>
      </table>
    </div>
  );
}

const cardLabels: Record<'awaiting' | 'expired' | 'decided', { label: MessageKey; detail: MessageKey }> = {
  awaiting: { label: 'inbox.awaiting', detail: 'inbox.authorizedWindow' },
  expired: { label: 'inbox.expired', detail: 'inbox.expiryDescription' },
  decided: { label: 'inbox.decided', detail: 'inbox.authorizedWindow' },
};

export function RecommendationsPage({
  userId, permissions, assets = [], configuredLogic = [],
}: {
  userId: string | null | undefined;
  permissions?: readonly string[] | null;
  assets?: readonly AssetResponse[];
  configuredLogic?: readonly ConfiguredLogicResponse[];
}) {
  const [items, setItems] = useState<RecommendationSummaryResponse[]>([]);
  const [criteria, setCriteria] = useState<RecommendationInboxCriteria>(
    DEFAULT_RECOMMENDATION_INBOX_CRITERIA,
  );
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<InboxError | null>(null);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  const [detail, setSelectedReviewId] = useHashDetail('recommendations');
  const controlSelected = detail === 'actions' || detail?.startsWith('actions/');
  const selectedActionId = detail?.startsWith('actions/') ? detail.slice('actions/'.length) : null;
  const selectedReviewId = controlSelected ? null : detail;
  const [chosenRun, setChosenRun] = useState<string | null>(null);
  const [reviewNotice, setReviewNotice] = useState<string | null>(null);
  const canDecide = Boolean(userId && permissions?.includes('recommendations.decide'));
  const canExecute = Boolean(userId && permissions?.includes(CONTROL_MANAGE_PERMISSION));
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
    return () => {
      activeRefresh.current?.abort();
      activeRefresh.current = null;
    };
  }, [userId, refresh]);
  usePollingRefresh(() => void refresh(), Boolean(userId));
  useChangeRefresh(['recommendations'], () => { if (userId) void refresh(); });

  const nowUtc = Date.now();
  const inboxSelection = items.find(item => item.id === selectedReviewId);
  const [linkedSelection, setLinkedSelection] = useState<{
    id: string; item: RecommendationSummaryResponse | null;
  } | null>(null);
  const fetchLinked = Boolean(userId && selectedReviewId && !inboxSelection);
  useEffect(() => {
    if (!fetchLinked || !selectedReviewId) return;
    const controller = new AbortController();
    const id = selectedReviewId;
    void fetchRecommendationById(id, controller.signal).then(
      item => { if (!controller.signal.aborted) setLinkedSelection({ id, item }); },
      () => { if (!controller.signal.aborted) setLinkedSelection({ id, item: null }); },
    );
    return () => controller.abort();
  }, [fetchLinked, selectedReviewId, lastUpdated]);
  const candidate = inboxSelection ?? (linkedSelection?.id === selectedReviewId ? linkedSelection?.item : null);
  const selectedRecommendation = candidate?.canDecide === true ? candidate : undefined;
  const selectionLoading = fetchLinked && linkedSelection?.id !== selectedReviewId;
  useEffect(() => {
    // A recommendation open beside the list closes once it can no longer be reviewed or executed by this person.
    const stillActionable = Boolean(selectedRecommendation && (
      (canDecide && recommendationIsAwaiting(selectedRecommendation, Date.now())) ||
      recommendationIsExecutable(selectedRecommendation, Date.now(), canExecute)));
    if (lastUpdated && selectedReviewId && !selectionLoading && !stillActionable) {
      setSelectedReviewId(null);
      setReviewNotice(t(canDecide || canExecute ? 'decision.outdated' : 'decision.accessDenied'));
    }
  }, [lastUpdated, selectedReviewId, selectedRecommendation, selectionLoading, canDecide, canExecute, setSelectedReviewId]);
  const reviewing = Boolean(canDecide && selectedRecommendation && recommendationIsAwaiting(selectedRecommendation, nowUtc));
  const executing = Boolean(selectedRecommendation && recommendationIsExecutable(selectedRecommendation, nowUtc, canExecute));
  const reviewOpen = reviewing || executing;
  const splitFocus = useSplitPaneFocus(reviewOpen ? selectedRecommendation?.id ?? null : null);
  const counts = inboxCounts(items, nowUtc);
  const visible = useMemo(
    () => filterRecommendationInbox(items, criteria, nowUtc),
    [items, criteria, nowUtc],
  );
  const runs = useMemo(() => groupRecommendationRuns(visible).slice(0, 3), [visible]);
  // A direct recommendation link takes priority over the preview and its filters.
  const linkedRun = selectedRecommendation?.executionId;
  const selectedRun = linkedRun ?? (runs.some(run => run.key === chosenRun) ? chosenRun : runs[0]?.key) ?? null;
  const selectedGroup = runs.find(run => run.key === selectedRun);
  const runExecutionId = selectedRun && selectedRun !== 'unlinked' ? selectedRun : null;
  const runRequestKey = `${userId}:${runExecutionId}:${lastUpdated?.getTime()}`;
  const [runResult, setRunResult] = useState<{
    key: string; items: RecommendationSummaryResponse[]; failed: boolean;
  } | null>(null);
  useEffect(() => {
    if (!userId || !runExecutionId || error) return;
    const controller = new AbortController();
    void fetchRecommendationInbox(controller.signal, fetch, runExecutionId).then(
      next => { if (!controller.signal.aborted) setRunResult({ key: runRequestKey, items: next, failed: false }); },
      () => { if (!controller.signal.aborted) setRunResult({ key: runRequestKey, items: [], failed: true }); },
    );
    return () => controller.abort();
  }, [userId, runExecutionId, runRequestKey, error]);
  const runLoading = Boolean(runExecutionId && runResult?.key !== runRequestKey);
  const runFailed = Boolean(runExecutionId && runResult?.key === runRequestKey && runResult.failed);
  const runItems = runExecutionId
    ? runResult?.key === runRequestKey ? runResult.items : []
    : selectedGroup?.items ?? [];
  const proposals = linkedRun ? runItems : filterRecommendationInbox(runItems, criteria, nowUtc);
  const targetIds = useMemo(() => [...new Set([...visible, ...proposals, ...(selectedRecommendation ? [selectedRecommendation] : [])]
    .flatMap(item => item.assetId ? [item.assetId] : []))], [visible, proposals, selectedRecommendation]);
  const assetDirectory = useAssetIdentityDirectory(targetIds, assets,
    Boolean(permissions?.includes('assets.read')), userId);
  const assetNames = new Map(assetDirectory.assets.flatMap(asset =>
    asset.id && asset.name
      ? [[asset.id, assetIdentityLabel(asset.id, assetDirectory.assets, assetDirectory.owners)] as const] : []));
  const errorMessages: Record<InboxError, string> = {
    accessDenied: t('inbox.accessDenied'),
    invalidResponse: t('inbox.invalidResponse'),
    unavailable: t('inbox.unavailable'),
  };

  return (
    <>
      <KillSwitchPanel userId={userId} canManage={canExecute} />
      <ControlActionsPanel userId={userId} canManage={canExecute} selectedActionId={selectedActionId} />
      <section className="summary-grid" aria-label={t('inbox.summary')}>
        {RECOMMENDATION_INBOX_FILTERS.map(kind => (
          <SummaryCard key={kind} label={t(cardLabels[kind].label)} value={String(counts[kind])}
            detail={t(cardLabels[kind].detail)}
            selected={criteria.filters.length === 1 && criteria.filters[0] === kind}
            onSelect={() => setCriteria(current => ({ ...current,
              filters: current.filters.length === 1 && current.filters[0] === kind ? [] : [kind] }))} />
        ))}
      </section>
      <div {...splitFocus} className="split-layout runs-layout recs-layout" data-has-selection={reviewOpen}>
      <div className="split-panes">
      <section className="panel split-browse">
        <div className="panel-header">
          <h2>{t('inbox.recentRuns')}</h2>
          <div className="execution-detail-actions">
            {lastUpdated && (
              <span className="last-updated">{t('inbox.updated')} {formatDetailTimestamp(
                lastUpdated.toISOString(),
              )}</span>
            )}
            <ActionButton icon={RefreshCw} iconOnly label={t('inbox.reload')} busy={isLoading} disabled={!userId} onClick={() => void refresh()} />
          </div>
        </div>
        <p className="recommendation-readonly-note">
          {canExecute ? t('control.execute.pageNotice') : canDecide ? t('decision.governanceNotice') : t('inbox.readOnly')}
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
        {isLoading && !lastUpdated && <LoadingIndicator label={t('inbox.loading')} />}
        {error && <p role="alert" className="execution-detail-error inbox-error">
          {errorMessages[error]}
        </p>}
        <div className="recommendation-run-heading">
          <p>{t('inbox.runPreview')}</p>
          {permissions?.includes('executions.read') && <a className="action-link" href={hashFor('operations')}>{t('inbox.viewAllRuns')}</a>}
        </div>
        {(!isLoading || lastUpdated) && !error && <>
          <div className="recommendation-run-list" role="group" aria-label={t('inbox.runSelection')}>
            {runs.map(run => <button type="button" key={run.key}
              className="recommendation-run" aria-pressed={selectedRun === run.key}
              onClick={() => { setChosenRun(run.key); setSelectedReviewId(null); setReviewNotice(null); }}>
              <strong>{[...new Set(run.items.map(item => configuredLogic.find(logic => logic.id === item.configuredLogicId)?.name ?? item.moduleId).filter(Boolean))].join(', ') || t('inbox.runUnavailable')}</strong>
              <span>{run.executionId ? t('overview.runReference', { id: shortId(run.executionId) }) : t('inbox.runUnavailable')}</span>
              <small>{t('inbox.runProposals', { count: run.items.length })}</small>
              {run.latestAtUtc && <time dateTime={run.latestAtUtc}>{formatDetailTimestamp(run.latestAtUtc)}</time>}
            </button>)}
          </div>
          {!runs.length && !linkedRun && <p>{t('inbox.noRuns')}</p>}
          {linkedRun && !selectedGroup && <h3>{t('overview.runReference', { id: shortId(linkedRun) })}</h3>}
          {runLoading && <LoadingIndicator label={t('inbox.loading')} />}
          {runFailed && <p role="alert">{t('operations.recommendationsUnavailable')}</p>}
          {!runLoading && !runFailed && <RecommendationInboxTable items={proposals} nowUtc={nowUtc} assetNames={assetNames}
            onClearFilters={items.length > 0 ? () => setCriteria(DEFAULT_RECOMMENDATION_INBOX_CRITERIA) : undefined}
            selectedId={reviewOpen ? selectedRecommendation?.id : null}
            onReview={canDecide ? recommendationId => {
              setSelectedReviewId(recommendationId);
              setReviewNotice(null);
            } : undefined}
            onExecute={canExecute ? recommendationId => {
              setSelectedReviewId(recommendationId);
              setReviewNotice(null);
            } : undefined} />}
          {runItems.length === 100 && <p>{t('operations.recommendationsLimited')}</p>}
        </>}
      </section>
      <div className="split-selected">
        <ActionButton label={t('inbox.backToList')} className="split-back" onClick={() => setSelectedReviewId(null)} />
        {reviewing && selectedRecommendation && (
            <RecommendationDecisionReviewPanel
              key={JSON.stringify(selectedRecommendation)}
              item={selectedRecommendation}
              assetName={assetNames.get(selectedRecommendation.assetId ?? '')}
              configuredLogic={configuredLogic}
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
        {executing && selectedRecommendation && (
            <ControlExecutionPanel
              key={JSON.stringify(selectedRecommendation)}
              item={selectedRecommendation}
              assetName={assetNames.get(selectedRecommendation.assetId ?? '')}
              configuredLogic={configuredLogic}
              onCancel={() => setSelectedReviewId(null)}
              onExecuted={action => {
                const outcome = outcomeMessageKey(action.status);
                setSelectedReviewId(null);
                setReviewNotice(t('control.execute.done', { outcome: outcome ? t(outcome) : action.status ?? '' }));
                void refresh();
              }}
              onConflict={() => {
                setSelectedReviewId(null);
                setReviewNotice(t('control.execute.conflict'));
                void refresh();
              }}
            />
          )}
      </div>
      </div>
      </div>
    </>
  );
}
