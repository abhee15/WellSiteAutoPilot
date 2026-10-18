import { useEffect, useState } from 'react';
import type {
  AssetResponse,
  ConfiguredLogicResponse,
  ExecutionResponse,
  RecommendationSummaryResponse,
  SystemInfoResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { SummaryCard } from '../components/common';
import { attentionItems } from './overview';
import type { AttentionItem } from './overview';
import { fetchRecommendationInbox } from './recommendationInbox';
import './overview.css';

const REFRESH_MS = 10_000;

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
  permissions, systemInfo, executions, assets, configuredLogic, isLoaded,
}: {
  permissions: readonly string[];
  systemInfo: SystemInfoResponse | null;
  executions: ExecutionResponse[];
  assets: AssetResponse[];
  configuredLogic: ConfiguredLogicResponse[];
  isLoaded: boolean;
}) {
  const canReadRecommendations = permissions.includes('recommendations.read');
  const [recommendations, setRecommendations] = useState<RecommendationSummaryResponse[]>([]);
  const [recommendationsLoaded, setRecommendationsLoaded] = useState(!canReadRecommendations);

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
    load();
    const timer = window.setInterval(load, REFRESH_MS);
    return () => { window.clearInterval(timer); controller.abort(); };
  }, [canReadRecommendations]);

  const items = attentionItems({
    permissions, executions, recommendations, assets, configuredLogic, nowUtc: Date.now(),
  });
  const ready = isLoaded && recommendationsLoaded;
  const unhealthy = (systemInfo?.components ?? []).filter(component => component.status !== 'Healthy').length;

  return (
    <>
      <section className="panel overview-attention" aria-label={t('overview.attention')}>
        <h2>{t('overview.attention')}</h2>
        {!ready ? <p role="status" className="status-loading">{t('overview.loading')}</p>
          : items.length === 0 ? <p>{t('overview.allClear')}</p>
            : <ul className="overview-list">
              {items.map(item => <li key={item.kind}>
                <span>{attentionText(item)}</span>
                <a href={item.href}>{t('overview.open')}</a>
              </li>)}
            </ul>}
      </section>

      <section className="summary-grid" aria-label={t('overview.glance')}>
        {permissions.includes('assets.read') &&
          <SummaryCard label={t('overview.assets')} value={String(assets.length)} detail="" />}
        {permissions.includes('configured-logic.read') &&
          <SummaryCard label={t('overview.logic')} value={String(configuredLogic.length)} detail="" />}
        {permissions.includes('executions.read') &&
          <SummaryCard label={t('overview.recentRuns')} value={String(executions.length)} detail={t('overview.recentRunsDetail')} />}
        {permissions.includes('executions.read') &&
          <SummaryCard label={t('overview.services')} value={String(unhealthy)} detail="" />}
      </section>
      <p className="overview-note">{t('overview.shadowNote')}</p>
    </>
  );
}
