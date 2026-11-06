import { ContextLink } from '../components/ContextLink';
import { useEffect, useRef, useState } from 'react';
import type {
  AssetResponse,
  ConfiguredLogicResponse,
  ExecutionResponse,
  RecommendationSummaryResponse,
  SystemHealthResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { useChangeRefresh, usePollingRefresh } from '../live/LiveUpdates';
import { PanelHeader, SummaryCard } from '../components/common';
import { hashFor } from '../routing';
import { assetTotalLabel, attentionItems, cappedCount } from './overview';
import { SNAPSHOT_ASSET_LIMIT, fetchOwners } from './assetBrowseApi';
import type { OwnerGroup } from './assetHierarchy';
import type { AttentionItem } from './overview';
import { CONTROL_ACTION_LIST_LIMIT, fetchControlActions } from './controlActions';
import type { ControlAction } from './controlActions';
import { fetchRecommendationInbox, recommendationIsAwaiting, recommendationProposal } from './recommendationInbox';
import './overview.css';
import './operatorItems.css';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { Status, StatusIndicator } from '../components/Status';
import { shortId } from '../format';
import { formatDetailTimestamp } from './executionDetail';
import { relativeTime } from './auditPresentation';
import { assetIdentityLabel, describeRunContext, executionAssetLabel } from './runContext';
import { useAssetIdentityDirectory } from './useAssetIdentityDirectory';

function attentionText(item: AttentionItem): string {
  switch (item.kind) {
    case 'decide': return item.count === 1 ? t('overview.decideOne') : t('overview.decide', { count: item.count });
    case 'inDoubt': {
      const shown = cappedCount(item.count, CONTROL_ACTION_LIST_LIMIT);
      return item.count === 1 ? t('overview.inDoubtOne') : t('overview.inDoubt', { count: shown });
    }
    case 'failedRuns': return item.count === 1 ? t('overview.failedRunsOne') : t('overview.failedRuns', { count: item.count });
    case 'idleLogic': return item.count === 1 ? t('overview.idleLogicOne') : t('overview.idleLogic', { count: item.count });
    case 'noAssets': return t('overview.noAssets');
    default: return t('overview.noLogic');
  }
}

/** The home page: what needs a person first, then how things stand. */
export function OverviewPage({
  permissions, systemHealth, executions, assets, configuredLogic, isLoaded, userId,
}: {
  permissions: readonly string[];
  userId?: string | null;
  systemHealth: SystemHealthResponse | null;
  executions: ExecutionResponse[];
  assets: AssetResponse[];
  configuredLogic: ConfiguredLogicResponse[];
  isLoaded: boolean;
}) {
  const canReadRecommendations = permissions.includes('recommendations.read');
  const [recommendations, setRecommendations] = useState<RecommendationSummaryResponse[]>([]);
  const [recommendationsLoaded, setRecommendationsLoaded] = useState(!canReadRecommendations);
  const [recommendationsUnavailable, setRecommendationsUnavailable] = useState(false);
  const reload = useRef<() => void>(() => undefined);
  const canReadAssets = permissions.includes('assets.read');
  // Status is available to connection readers and security managers, not automatically to execution readers.
  const canReadServices = permissions.includes('connections.read') || permissions.includes('security.manage');
  // The workspace list stops at SNAPSHOT_ASSET_LIMIT, so the real total comes from the owners' own counts.
  const [owners, setOwners] = useState<OwnerGroup[] | null>(null);
  const reloadOwners = useRef<() => void>(() => undefined);
  useEffect(() => {
    if (!canReadAssets) return;
    let controller = new AbortController();
    const load = () => {
      controller.abort();
      controller = new AbortController();
      const current = controller;
      fetchOwners(current.signal).then(
        items => { if (!current.signal.aborted) setOwners(items); },
        () => undefined);
    };
    reloadOwners.current = load;
    load();
    return () => { reloadOwners.current = () => undefined; controller.abort(); };
  }, [canReadAssets]);
  useChangeRefresh(['assets'], () => reloadOwners.current());

  useEffect(() => {
    if (!canReadRecommendations) return;
    let controller = new AbortController();
    const load = () => {
      controller.abort();
      controller = new AbortController();
      const current = controller;
      fetchRecommendationInbox(current.signal).then(items => {
        if (current.signal.aborted) return;
        setRecommendations(items);
        setRecommendationsUnavailable(false);
        setRecommendationsLoaded(true);
      }).catch(() => {
        if (!current.signal.aborted) {
          setRecommendations([]);
          setRecommendationsUnavailable(true);
          setRecommendationsLoaded(true);
        }
      });
    };
    reload.current = load;
    load();
    return () => { reload.current = () => undefined; controller.abort(); };
  }, [canReadRecommendations]);
  usePollingRefresh(() => reload.current(), canReadRecommendations);
  useChangeRefresh(['recommendations'], () => reload.current());

  const [controlActions, setControlActions] = useState<ControlAction[] | null>(null);
  const [controlUnavailable, setControlUnavailable] = useState(false);
  const reloadControl = useRef<() => void>(() => undefined);
  useEffect(() => {
    if (!canReadRecommendations) return;
    let controller = new AbortController();
    const load = () => {
      controller.abort();
      controller = new AbortController();
      const current = controller;
      fetchControlActions(current.signal).then(
        rows => {
          if (current.signal.aborted) return;
          setControlActions(rows);
          setControlUnavailable(false);
        },
        () => {
          if (current.signal.aborted) return;
          setControlActions([]);
          setControlUnavailable(true);
        });
    };
    reloadControl.current = load;
    load();
    return () => { reloadControl.current = () => undefined; controller.abort(); };
  }, [canReadRecommendations]);
  usePollingRefresh(() => reloadControl.current(), canReadRecommendations);
  useChangeRefresh(['control'], () => reloadControl.current());

  const items = attentionItems({
    permissions, executions, recommendations, assets, configuredLogic, openControlActions: controlActions?.length, nowUtc: Date.now(),
  });
  const ready = isLoaded && recommendationsLoaded;
  const attentionReady = ready && (!canReadRecommendations || controlActions !== null);
  const latestRuns = [...executions].filter(run => run.executionId)
    .sort((a, b) => Date.parse(b.requestedAtUtc ?? '') - Date.parse(a.requestedAtUtc ?? '')).slice(0, 5);
  const now = Date.now();
  const pending = recommendations.filter(item => recommendationIsAwaiting(item, now))
    .sort((a, b) => Date.parse(a.expiresAtUtc ?? '') - Date.parse(b.expiresAtUtc ?? ''));
  const failed = permissions.includes('executions.read') ? executions.filter(run => {
    const requested = Date.parse(run.requestedAtUtc ?? '');
    return run.executionId && ['Failed', 'TimedOut', 'Suspended'].includes(run.status ?? '') &&
      requested <= now && now - requested <= 86_400_000;
  }).sort((a, b) => Date.parse(b.requestedAtUtc ?? '') - Date.parse(a.requestedAtUtc ?? '')) : [];
  const action = controlActions?.[0];
  const pendingShown = pending.slice(0, action ? 2 : 3);
  const failedShown = failed.slice(0, 3 - pendingShown.length - (action ? 1 : 0));
  const setup = items.filter(item => ['noAssets', 'noLogic'].includes(item.kind));
  const runAssetIds = [...new Set([...latestRuns, ...failedShown].flatMap(run =>
    describeRunContext(run, configuredLogic, assets).assetIds.slice(0, 2))
    .concat(pendingShown.flatMap(item => item.assetId ? [item.assetId] : []), action?.assetId ? [action.assetId] : []))];
  const directory = useAssetIdentityDirectory(runAssetIds, assets, canReadAssets, userId);
  const unhealthy = systemHealth?.unhealthyCount;
  const attentionUnavailable = recommendationsUnavailable || controlUnavailable;

  return (
    <div className="overview-workspace">
      <div className="overview-top">
      <section className="panel overview-attention" aria-label={t('overview.attention')}>
        <PanelHeader title={t('overview.attention')} />
        <div className="overview-attention-body">
        {!attentionReady ? <LoadingIndicator label={t('overview.loading')} /> : <>
          {attentionUnavailable && <p role="status">{t('overview.attentionUnavailable')}</p>}
          {!action && !pendingShown.length && !failedShown.length && !attentionUnavailable && setup.length === 0 && <p>{t('overview.allClear')}</p>}
          <ul className="operator-items">
            {action && <li>
              <div className="operator-item-context">
                <strong>{assetIdentityLabel(action.assetId, directory.assets, directory.owners)}</strong>
                <StatusIndicator tone="warning" label={t('overview.actionUnconfirmed')} />
                <span className="operator-item-proposal">{recommendationProposal({ ...action, suggestedValue: action.value })}</span>
                <small>{t('overview.runReference', { id: shortId(action.executionId) })} · {formatDetailTimestamp(action.createdAtUtc)}</small>
              </div>
              <ContextLink href={hashFor('recommendations', `actions/${action.id}`)} label={t('overview.inspectActions')} />
            </li>}
            {pendingShown.map(item => <li key={item.id}>
              <div className="operator-item-context">
                <strong>{assetIdentityLabel(item.assetId, directory.assets, directory.owners)}</strong>
                <StatusIndicator tone="warning" label={t('operations.awaitingDecision')} />
                <span className="operator-item-proposal">{recommendationProposal(item)}</span>
                <small>{item.moduleId} · {t('overview.runReference', { id: shortId(item.executionId) })} · {t('inbox.expires')} {formatDetailTimestamp(item.expiresAtUtc)}</small>
              </div>
              {item.canDecide && <ContextLink href={`#recommendations/${item.id}`} label={t('operations.reviewChange')} />}
            </li>)}
            {failedShown.map(run => {
              const context = describeRunContext(run, configuredLogic, directory.assets, directory.owners);
              return <li key={run.executionId}>
                <div className="operator-item-context">
                  <strong>{executionAssetLabel(run, context)}</strong>
                  <Status value={run.status} />
                  <small>{context.logicName ?? run.moduleId} · {t('overview.runReference', { id: shortId(run.executionId) })} · {formatDetailTimestamp(run.requestedAtUtc)}</small>
                </div>
                <ContextLink href={`#operations/${run.executionId}`} label={t('overview.inspectRun')} />
              </li>;
            })}
          </ul>
          {setup.length > 0 && <ul className="overview-setup">
            {setup.map(item => <li key={item.kind}><ContextLink href={item.href} label={attentionText(item)} /></li>)}
          </ul>}
          <div className="overview-attention-links">
            {pending.length > pendingShown.length && <a href="#recommendations">{t('overview.allRecommendations')}</a>}
            {failed.length > failedShown.length && <a href="#operations">{t('overview.allRuns')}</a>}
          </div>
        </>}
        </div>
      </section>

      <section className="summary-grid overview-metrics" aria-label={t('overview.glance')}>
        {canReadAssets &&
          <SummaryCard label={t('overview.assets')} value={assetTotalLabel(owners, assets.length, SNAPSHOT_ASSET_LIMIT)} detail="" href={hashFor('assets')} />}
        {permissions.includes('configured-logic.read') &&
          <SummaryCard label={t('overview.logic')} value={cappedCount(configuredLogic.length, 100)} detail="" href={hashFor('logic')} />}
        {permissions.includes('executions.read') &&
          <SummaryCard label={t('overview.recentRuns')} value={String(executions.length)} detail={t('overview.recentRunsDetail')} href={hashFor('operations')} />}
        {canReadServices &&
          <SummaryCard label={t('overview.services')} value={unhealthy == null ? t('overview.unknown') : String(unhealthy)} tone={unhealthy != null && unhealthy > 0 ? 'warning' : undefined} detail="" href={hashFor('status')} />}
      </section>
      </div>
      {permissions.includes('executions.read') && ready && latestRuns.length > 0 && (
        <section className="panel overview-latest" aria-label={t('overview.latestRuns')}>
          <PanelHeader title={t('overview.latestRuns')}
            actions={<ContextLink href="#operations" label={t('overview.allRuns')} />} />
          <ul className="overview-runs">
            {latestRuns.map(run => {
              const context = describeRunContext(run, configuredLogic, directory.assets, directory.owners);
              return <li key={run.executionId}>
                <Status value={run.status} />
                <a href={`#operations/${run.executionId}`}>
                  <strong>{executionAssetLabel(run, context)}</strong>
                </a>
                <span className="overview-run-module">{context.logicName ?? run.moduleId}</span>
                {run.requestedAtUtc && <time dateTime={run.requestedAtUtc} title={formatDetailTimestamp(run.requestedAtUtc)} aria-label={formatDetailTimestamp(run.requestedAtUtc)}>{relativeTime(run.requestedAtUtc, Date.now())}</time>}
              </li>;
            })}
          </ul>
        </section>
      )}
    </div>
  );
}
