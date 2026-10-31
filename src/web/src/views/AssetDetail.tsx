import { useEffect, useRef, useState } from 'react';
import { CloseButton } from '../components/CloseButton';
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
import { Status } from '../components/Status';
import {
  assetAncestors, assetChildren, fetchAssetRecommendations, fetchAssetRuns, logicOnAsset,
} from './assetPage';
import { fetchAssetSource } from './assetBrowseApi';
import type { AssetSource } from './assetBrowseApi';
import './assetPage.css';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { SchedulesPanel } from './SchedulesPanel';

type Activity = {
  runs: ExecutionResponse[];
  recommendations: RecommendationSummaryResponse[];
  state: 'loading' | 'ready' | 'unavailable';
};

/** One asset explained in words: where it sits, what is on it, what it has done. */
export function AssetDetail({
  asset, assets, typeName, configuredLogic, canReadRuns, canReadRecommendations, canConfigureLogic, canReadSchedules = false, onSelect, onClose,
  owner = null, canOpenConnection = false,
}: {
  asset: AssetResponse;
  assets: readonly AssetResponse[];
  typeName: string;
  configuredLogic: readonly ConfiguredLogicResponse[];
  canConfigureLogic?: boolean;
  canReadSchedules?: boolean;
  canReadRuns: boolean;
  canReadRecommendations: boolean;
  onSelect: (assetId: string) => void;
  onClose: () => void;
  /** The Connection that owns this asset, or Standalone (connectionId null); null when no Connection owns anything. */
  owner?: { connectionId: string | null; name: string } | null;
  canOpenConnection?: boolean;
}) {
  const assetId = asset.id ?? '';
  const ancestors = assetAncestors(assets, assetId);
  const children = assetChildren(assets, assetId);
  // The path starts at the owning Connection, so equally named Assets in two Connections stay distinguishable.
  const where = [...(owner && owner.connectionId !== null ? [owner.name] : []), ...ancestors.map(a => a.name ?? '—')];
  const logic = logicOnAsset(configuredLogic, assetId);
  const [source, setSource] = useState<AssetSource | null>(null);
  const importedFromConnection = owner !== null && owner.connectionId !== null;
  useEffect(() => {
    setSource(null);
    if (!importedFromConnection) return;
    const controller = new AbortController();
    fetchAssetSource(assetId, controller.signal).then(found => { if (!controller.signal.aborted) setSource(found); })
      .catch(() => { /* provenance is extra detail; the page works without it */ });
    return () => controller.abort();
  }, [assetId, importedFromConnection]);
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

  const pageRef = useRef<HTMLDivElement>(null);
  // Selecting a row must visibly open the page; it sits below the grid and may be off screen.
  useEffect(() => {
    const el = pageRef.current;
    if (el && typeof el.scrollIntoView === 'function') el.scrollIntoView({ block: 'nearest' });
  }, [assetId]);

  return (
    <div className="asset-page" aria-live="polite" ref={pageRef}>
      <header className="asset-page-header">
        <div>
          <p className="eyebrow">{t('assets.selected')}</p>
          <h3>{asset.name ?? t('assets.unnamed')}</h3>
          <p className="asset-page-meta">
            {typeName} · <Status value={asset.isActive ? 'Active' : 'Inactive'} />
          </p>
          {owner && <p className="asset-page-meta">
            {owner.connectionId === null ? t('assetOwner.standalone') : <>
              {t('assetOwner.ownedBy')}{' '}
              {canOpenConnection ? <a href={hashFor('connections', owner.connectionId)}>{owner.name}</a> : owner.name}</>}
          </p>}
          {source && <p className="asset-page-meta">
            {t('assetPage.source', { kind: source.resourceKind })} <code>{source.externalId}</code>
            {source.importedAtUtc && <> · {t('assetPage.importedAt', { time: formatTimestamp(source.importedAtUtc) })}</>}
          </p>}
          <p className="asset-page-meta"><code>{assetId}</code></p>
          <p className="asset-page-meta">
            {where.length === 0 ? t('assetPage.topLevel')
              : t('assetPage.locatedUnder', { path: where.join(' › ') })}
          </p>
        </div>
        <CloseButton label={t('assets.clearSelection')} onClick={onClose} />
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
              </li>
            ))}
          </ul>
        )}
        {canConfigureLogic && asset.isActive && <a className="secondary-button" href={hashFor('logic', 'new:' + assetId)}>
          {t('assetPage.putLogic')}</a>}
      </section>

      {canReadSchedules && logic.length > 0 && <SchedulesPanel assetId={assetId} hideWhenEmpty canManage={canConfigureLogic === true} />}

      {canReadRuns && <section aria-label={t('assetPage.runs')}>
        <h4>{t('assetPage.runs')}</h4>
        {activity.state === 'loading' && <LoadingIndicator label={t('assetPage.loading')} />}
        {activity.state === 'unavailable' && <p role="alert" className="asset-page-empty">{t('assetPage.activityUnavailable')}</p>}
        {activity.state === 'ready' && (activity.runs.length === 0
          ? <p className="asset-page-empty">{t('assetPage.noRuns')}</p>
          : <ul className="asset-page-list">
            {activity.runs.map(run => (
              <li key={run.executionId}>
                <a href={hashFor('operations', run.executionId)}>{formatTimestamp(run.requestedAtUtc)}</a>
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
