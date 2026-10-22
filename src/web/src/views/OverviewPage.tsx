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
import { SummaryCard } from '../components/common';
import { assetTotalLabel, attentionItems, cappedCount } from './overview';
import { SNAPSHOT_ASSET_LIMIT, fetchOwners } from './assetBrowseApi';
import type { OwnerGroup } from './assetHierarchy';
import type { AttentionItem } from './overview';
import { fetchRecommendationInbox } from './recommendationInbox';
import './overview.css';
import { LoadingIndicator } from '../components/LoadingIndicator';

function attentionText(item: AttentionItem): string {
  switch (item.kind) {
    case 'decide': return item.count === 1 ? t('overview.decideOne') : t('overview.decide', { count: item.count });
    case 'failedRuns': return item.count === 1 ? t('overview.failedRunsOne') : t('overview.failedRuns', { count: item.count });
    case 'idleLogic': return item.count === 1 ? t('overview.idleLogicOne') : t('overview.idleLogic', { count: item.count });
    case 'noAssets': return t('overview.noAssets');
    default: return t('overview.noLogic');
  }
}

/** The home page: what needs a person first, then how things stand. */
export function OverviewPage({
  permissions, systemHealth, executions, assets, configuredLogic, isLoaded,
}: {
  permissions: readonly string[];
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

  const items = attentionItems({
    permissions, executions, recommendations, assets, configuredLogic, nowUtc: Date.now(),
  });
  const ready = isLoaded && recommendationsLoaded;
  const unhealthy = systemHealth?.unhealthyCount ?? 0;

  return (
    <>
      <section className="panel overview-attention" aria-label={t('overview.attention')}>
        <h2>{t('overview.attention')}</h2>
        {!ready ? <LoadingIndicator label={t('overview.loading')} />
          : items.length === 0 ? <p>{t('overview.allClear')}</p>
            : <ul className="overview-list">
              {items.map(item => <li key={item.kind}>
                <span>{attentionText(item)}</span>
                <a className="action-link" href={item.href}>{t('overview.open')}</a>
              </li>)}
            </ul>}
      </section>

      <section className="summary-grid" aria-label={t('overview.glance')}>
        {permissions.includes('assets.read') &&
          <SummaryCard label={t('overview.assets')} value={assetTotalLabel(owners, assets.length, SNAPSHOT_ASSET_LIMIT)} detail="" />}
        {permissions.includes('configured-logic.read') &&
          <SummaryCard label={t('overview.logic')} value={cappedCount(configuredLogic.length, 100)} detail="" />}
        {permissions.includes('executions.read') &&
          <SummaryCard label={t('overview.recentRuns')} value={String(executions.length)} detail={t('overview.recentRunsDetail')} />}
        {permissions.includes('executions.read') &&
          <SummaryCard label={t('overview.services')} value={String(unhealthy)} detail="" />}
      </section>
      <p className="overview-note">{t('overview.shadowNote')}</p>
    </>
  );
}
