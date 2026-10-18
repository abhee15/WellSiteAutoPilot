import { useEffect, useState } from 'react';
import type {
  AssetResponse,
  ConfiguredLogicResponse,
  ExecutionResponse,
  RecommendationSummaryResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { formatTimestamp } from '../format';
import { hashFor } from '../routing';
import { statusLabel } from '../words';
import { StatusPill } from '../components/common';
import {
  assetAncestors, assetChildren, fetchAssetRecommendations, fetchAssetRuns, logicOnAsset,
} from './assetPage';
import './assetPage.css';

type Activity = {
  runs: ExecutionResponse[];
  recommendations: RecommendationSummaryResponse[];
  state: 'loading' | 'ready' | 'unavailable';
};

/** One asset explained in words: where it sits, what is on it, what it has done. */
export function AssetDetail({
  asset, assets, typeName, configuredLogic, canReadRuns, canReadRecommendations, onSelect, onClose,
}: {
  asset: AssetResponse;
  assets: readonly AssetResponse[];
  typeName: string;
  configuredLogic: readonly ConfiguredLogicResponse[];
  canReadRuns: boolean;
  canReadRecommendations: boolean;
  onSelect: (assetId: string) => void;
  onClose: () => void;
}) {
  const assetId = asset.id ?? '';
  const ancestors = assetAncestors(assets, assetId);
  const children = assetChildren(assets, assetId);
  const logic = logicOnAsset(configuredLogic, assetId);
  const [activity, setActivity] = useState<Activity>({ runs: [], recommendations: [], state: 'loading' });

  useEffect(() => {
    const controller = new AbortController();
    setActivity({ runs: [], recommendations: [], state: 'loading' });
    Promise.all([
      canReadRuns ? fetchAssetRuns(assetId, controller.signal) : Promise.resolve([]),
      canReadRecommendations ? fetchAssetRecommendations(assetId, controller.signal) : Promise.resolve([]),
    ]).then(([runs, recommendations]) => {
      if (!controller.signal.aborted) setActivity({ runs, recommendations, state: 'ready' });
    }).catch(() => {
      if (!controller.signal.aborted) setActivity({ runs: [], recommendations: [], state: 'unavailable' });
    });
    return () => controller.abort();
  }, [assetId, canReadRuns, canReadRecommendations]);

  return (
    <div className="asset-page" aria-live="polite">
      <header className="asset-page-header">
        <div>
          <p className="eyebrow">{t('assets.selected')}</p>
          <h3>{asset.name ?? t('assets.unnamed')}</h3>
          <p className="asset-page-meta">
            {typeName} · <StatusPill value={asset.isActive ? 'Active' : 'Inactive'} />
          </p>
          <p className="asset-page-meta"><code>{assetId}</code></p>
          <p className="asset-page-meta">
            {ancestors.length === 0 ? t('assetPage.topLevel')
              : t('assetPage.locatedUnder', { path: ancestors.map(a => a.name ?? '—').join(' › ') })}
          </p>
        </div>
        <button type="button" className="secondary-button" onClick={onClose}>
          {t('assets.clearSelection')}
        </button>
      </header>

      <section aria-label={t('assetPage.contains')}>
        <h4>{t('assetPage.contains')}</h4>
        {children.length === 0 ? <p className="asset-page-empty">{t('assetPage.noChildren')}</p> : (
          <ul className="asset-page-list">
            {children.map(child => (
              <li key={child.id}>
                <button type="button" className="asset-select"
                  onClick={() => child.id && onSelect(child.id)}>{child.name}</button>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section aria-label={t('assetPage.logicHere')}>
        <h4>{t('assetPage.logicHere')}</h4>
        {logic.length === 0 ? <p className="asset-page-empty">{t('assetPage.noLogic')}</p> : (
          <ul className="asset-page-list">
            {logic.map(item => (
              <li key={item.id}>
                <a href={hashFor('logic', item.id)}>{item.name}</a>
                <small>{item.moduleId}</small>
              </li>
            ))}
          </ul>
        )}
      </section>

      {canReadRuns && <section aria-label={t('assetPage.runs')}>
        <h4>{t('assetPage.runs')}</h4>
        {activity.state === 'loading' && <p role="status" className="asset-page-empty">{t('assetPage.loading')}</p>}
        {activity.state === 'unavailable' && <p role="alert" className="asset-page-empty">{t('assetPage.activityUnavailable')}</p>}
        {activity.state === 'ready' && (activity.runs.length === 0
          ? <p className="asset-page-empty">{t('assetPage.noRuns')}</p>
          : <ul className="asset-page-list">
            {activity.runs.map(run => (
              <li key={run.id}>
                <a href={hashFor('operations', run.id)}>{formatTimestamp(run.requestedAtUtc)}</a>
                <small>{statusLabel(run.status)}</small>
              </li>
            ))}
          </ul>)}
      </section>}

      {canReadRecommendations && <section aria-label={t('assetPage.recommendations')}>
        <h4>{t('assetPage.recommendations')}</h4>
        {activity.state === 'ready' && (activity.recommendations.length === 0
          ? <p className="asset-page-empty">{t('assetPage.noRecommendations')}</p>
          : <ul className="asset-page-list">
            {activity.recommendations.map(item => (
              <li key={item.id}>
                <a href={hashFor('recommendations', item.id)}>{item.command ?? item.code ?? '—'}</a>
                <small>{statusLabel(item.status)} · {formatTimestamp(item.createdAtUtc)}</small>
              </li>
            ))}
          </ul>)}
      </section>}
    </div>
  );
}
