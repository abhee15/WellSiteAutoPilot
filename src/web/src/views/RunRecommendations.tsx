import { ContextLink } from '../components/ContextLink';
import { useEffect, useState } from 'react';
import './operatorItems.css';
import type { AssetResponse, RecommendationSummaryResponse } from '../api/generated/v1/types.gen';
import { StatusIndicator, Status } from '../components/Status';
import { useChangeRefresh, usePollingRefresh } from '../live/LiveUpdates';
import { shortId } from '../format';
import { t } from '../localization';
import { formatDetailTimestamp } from './executionDetail';
import { fetchRecommendationInbox, recommendationIsAwaiting, recommendationIsExpired, recommendationProposal } from './recommendationInbox';
import { assetIdentityLabel } from './runContext';
import { useAssetIdentityDirectory } from './useAssetIdentityDirectory';

export function useRunRecommendations(enabled: boolean, executionId?: string) {
  const [result, setResult] = useState<{
    items: RecommendationSummaryResponse[]; status: 'loading' | 'loaded' | 'unavailable';
  }>({ items: [], status: 'loading' });
  const [revision, setRevision] = useState(0);
  useChangeRefresh(['recommendations'], () => { if (enabled) setRevision(value => value + 1); });
  usePollingRefresh(() => setRevision(value => value + 1), enabled);
  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();
    void fetchRecommendationInbox(controller.signal, fetch, executionId).then(
      items => { if (!controller.signal.aborted) setResult({ items, status: 'loaded' }); },
      () => { if (!controller.signal.aborted) setResult({ items: [], status: 'unavailable' }); },
    );
    return () => controller.abort();
  }, [enabled, executionId, revision]);
  return result;
}

export function RunRecommendations({ executionId, assets, canReadAssets, userId }: {
  executionId: string; assets: AssetResponse[]; canReadAssets: boolean; userId?: string | null;
}) {
  const { items, status } = useRunRecommendations(true, executionId);
  const ids = items.flatMap(item => item.assetId ? [item.assetId] : []);
  const directory = useAssetIdentityDirectory(ids, assets, canReadAssets, userId);
  if (status === 'loading') return null;
  if (status === 'unavailable') return <p role="status">{t('operations.recommendationsUnavailable')}</p>;
  if (!items.length) return null;
  return <section className="execution-recommendations" aria-label={t('operations.recommendations')}>
    <h4>{t('operations.recommendations')}</h4>
    <small title={executionId}>{t('overview.runReference', { id: shortId(executionId) })}</small>
    <ul className="operator-items">
      {items.map(item => {
        const awaiting = recommendationIsAwaiting(item, Date.now());
        return <li key={item.id}>
          <div className="operator-item-context">
            <strong>{assetIdentityLabel(item.assetId, directory.assets, directory.owners)}</strong>
            {awaiting ? <StatusIndicator tone="warning" label={t('operations.awaitingDecision')} />
              : <Status value={recommendationIsExpired(item, Date.now()) ? 'Expired' : item.status} />}
            <span className="operator-item-proposal">{recommendationProposal(item)}</span>
            {awaiting && <small>{t('inbox.expires')} {formatDetailTimestamp(item.expiresAtUtc)}</small>}
          </div>
          {awaiting && item.canDecide && <ContextLink href={`#recommendations/${item.id}`} label={t('operations.reviewChange')} />}
        </li>;
      })}
    </ul>
    {items.length === 100 && <p>{t('operations.recommendationsLimited')}</p>}
  </section>;
}
