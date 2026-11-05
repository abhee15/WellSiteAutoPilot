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
import { fetchRecommendationInbox } from './recommendationInbox';
import './overview.css';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { Status, StatusIndicator } from '../components/Status';
import { relativeTime } from './auditPresentation';
import { describeRunContext, executionAssetLabel } from './runContext';
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
        setRecommendationsLoaded(true);
      }).catch(() => {
        // Keep the last good list; the awaiting-count simply stops updating until the next try.
        if (!current.signal.aborted) setRecommendationsLoaded(true);
      });
    };
    reload.current = load;
    load();
    return () => { reload.current = () => undefined; controller.abort(); };
  }, [canReadRecommendations]);
  usePollingRefresh(() => reload.current(), canReadRecommendations);
  useChangeRefresh(['recommendations'], () => reload.current());

  // Commands sent but not confirmed. A failed read simply leaves the last known count; the Recommendations page
  // shows the authoritative list and its own error.
  const [openControlActions, setOpenControlActions] = useState(0);
  const reloadControl = useRef<() => void>(() => undefined);
  useEffect(() => {
    if (!canReadRecommendations) return;
    let controller = new AbortController();
    const load = () => {
      controller.abort();
      controller = new AbortController();
      const current = controller;
      fetchControlActions(current.signal).then(
        rows => { if (!current.signal.aborted) setOpenControlActions(rows.length); },
        () => undefined);
    };
    reloadControl.current = load;
    load();
    return () => { reloadControl.current = () => undefined; controller.abort(); };
  }, [canReadRecommendations]);
  usePollingRefresh(() => reloadControl.current(), canReadRecommendations);
  useChangeRefresh(['control'], () => reloadControl.current());

  const items = attentionItems({
    permissions, executions, recommendations, assets, configuredLogic, openControlActions, nowUtc: Date.now(),
  });
  const ready = isLoaded && recommendationsLoaded;
  const latestRuns = [...executions].filter(run => run.executionId)
    .sort((a, b) => Date.parse(b.requestedAtUtc ?? '') - Date.parse(a.requestedAtUtc ?? '')).slice(0, 5);
  const runAssetIds = [...new Set(latestRuns.flatMap(run =>
    describeRunContext(run, configuredLogic, assets).assetIds.slice(0, 2)))];
  const directory = useAssetIdentityDirectory(runAssetIds, assets, canReadAssets, userId);
  const unhealthy = systemHealth?.unhealthyCount ?? 0;

  return (
    <>
      <section className="panel overview-attention" aria-label={t('overview.attention')}>
        <h2>{t('overview.attention')}</h2>
        {!ready ? <LoadingIndicator label={t('overview.loading')} />
          : items.length === 0 ? <p>{t('overview.allClear')}</p>
            : <ul className="overview-list">
              {items.map(item => <li key={item.kind}>
                {item.kind === 'decide' || item.kind === 'inDoubt' || item.kind === 'failedRuns' ? (
                  <StatusIndicator tone="warning" label={attentionText(item)} />
                ) : <span>{attentionText(item)}</span>}
                <a className="action-link" href={item.href}>{t('overview.open')}</a>
              </li>)}
            </ul>}
      </section>

      <section className="summary-grid" aria-label={t('overview.glance')}>
        {canReadAssets &&
          <SummaryCard label={t('overview.assets')} value={assetTotalLabel(owners, assets.length, SNAPSHOT_ASSET_LIMIT)} detail="" href={hashFor('assets')} />}
        {permissions.includes('configured-logic.read') &&
          <SummaryCard label={t('overview.logic')} value={cappedCount(configuredLogic.length, 100)} detail="" href={hashFor('logic')} />}
        {permissions.includes('executions.read') &&
          <SummaryCard label={t('overview.recentRuns')} value={String(executions.length)} detail={t('overview.recentRunsDetail')} href={hashFor('operations')} />}
        {canReadServices &&
          <SummaryCard label={t('overview.services')} value={String(unhealthy)} detail="" href={hashFor('status')} />}
      </section>
      {permissions.includes('executions.read') && ready && latestRuns.length > 0 && (
        <section className="panel overview-latest" aria-label={t('overview.latestRuns')}>
          <PanelHeader title={t('overview.latestRuns')}
            actions={<a className="action-link" href="#operations">{t('overview.allRuns')}</a>} />
          <ul className="overview-runs">
            {latestRuns.map(run => {
              const context = describeRunContext(run, configuredLogic, directory.assets, directory.owners);
              return <li key={run.executionId}>
                <Status value={run.status} />
                <a href={`#operations/${run.executionId}`}>
                  <strong>{executionAssetLabel(run, context)}</strong>
                </a>
                <span className="overview-run-module">{run.moduleId}</span>
                {run.requestedAtUtc && <time dateTime={run.requestedAtUtc}>{relativeTime(run.requestedAtUtc, Date.now())}</time>}
              </li>;
            })}
          </ul>
        </section>
      )}
    </>
  );
}
