import { ActionButton } from '../components/ActionButton';
import { useCallback, useEffect, useRef, useState } from 'react';
import { CloseButton } from '../components/CloseButton';
import type {
  AssetResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { formatTimestamp } from '../format';
import { hashFor } from '../routing';
import { statusLabel } from '../words';
import { Status } from '../components/Status';
import {
  assetAncestors, assetChildren,
} from './assetPage';
import { fetchAssetSource } from './assetBrowseApi';
import type { AssetSource } from './assetBrowseApi';
import './assetPage.css';
import { LoadingIndicator } from '../components/LoadingIndicator';

import { AssetLogicCard, AssetRunPreview } from './AssetLogicCard';
import { fetchAssetLogic, fetchAssetRecommendations, LOGIC_PAGE_SIZE } from './assetActivity';
import { useLiveResource } from './useLiveResource';

/** One asset explained in words: where it sits, what is on it, what it has done. */
export function AssetDetail({
  asset, assets, typeName, canReadLogic = true, canReadRuns, canReadRecommendations, canConfigureLogic, onSelect, onClose,
  owner = null, canOpenConnection = false,
}: {
  asset: AssetResponse;
  assets: readonly AssetResponse[];
  typeName: string;
  canConfigureLogic?: boolean;
  canReadLogic?: boolean;
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
  const [offset, setOffset] = useState(0);
  const loadLogic = useCallback((signal: AbortSignal) => fetchAssetLogic(assetId, offset, signal), [assetId, offset]);
  const logic = useLiveResource(`${assetId}:${offset}`, loadLogic, ['configured-logic', 'assets', 'executions']);
  const operational = logic.data?.isGroupingNode === false;
  const loadRecommendations = useCallback((signal: AbortSignal) => fetchAssetRecommendations(assetId, signal), [assetId]);
  const recommendations = useLiveResource(assetId, loadRecommendations, ['recommendations'], canReadRecommendations && operational);
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
  const pageRef = useRef<HTMLDivElement>(null);
  // Selecting a row must visibly open the page; it sits below the grid and may be off screen.
  useEffect(() => {
    const el = pageRef.current;
    if (el && typeof el.scrollIntoView === 'function') el.scrollIntoView({ block: 'nearest' });
  }, [assetId]);

  return (
    <div className="asset-page" ref={pageRef}>
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
          <p className="asset-page-meta">
            {where.length === 0 ? t('assetPage.topLevel')
              : t('assetPage.locatedUnder', { path: where.join(' › ') })}
          </p>
        </div>
        <CloseButton label={t('assets.clearSelection')} onClick={onClose} />
      </header>

      <details className="asset-technical">
        <summary>{t('assetDetail.technical')}</summary>
        <p className="asset-page-meta">{t('assetDetail.id')}: <code>{assetId}</code></p>
        {source && <p className="asset-page-meta">
          {t('assetPage.source', { kind: source.resourceKind })} <code>{source.externalId}</code>
          {source.importedAtUtc && <> · {t('assetPage.importedAt', { time: formatTimestamp(source.importedAtUtc) })}</>}
        </p>}
      </details>

      {(children.length > 0 || logic.data?.isGroupingNode) && <section aria-label={t('assetPage.contains')}>
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
      </section>}

      {logic.loading && <LoadingIndicator label={t('assetPage.loading')} />}
      {logic.failed && <p role="status" className="asset-page-empty">{t('assetDetail.logicUnavailable')} <button type="button" className="row-link" onClick={logic.refresh}>{t('assetDetail.retry')}</button></p>}
      {operational && canReadLogic && <section aria-label={t('assetPage.logicHere')}>
        <h4>{t('assetDetail.logicAndRuns')}</h4>
        <p className="asset-page-meta">{t('assetDetail.scheduleHint')}</p>
        {logic.data?.items?.length === 0 && <p className="asset-page-empty">{t('assetPage.noLogic')}</p>}
        <div className="asset-logic-list">
          {logic.data?.items?.map(item => <AssetLogicCard key={item.configuration?.id} item={item} assetId={assetId} canReadRuns={canReadRuns} />)}
        </div>
        <div className="asset-detail-actions">
          {offset > 0 && <ActionButton onClick={() => setOffset(Math.max(0, offset - LOGIC_PAGE_SIZE))} label={t('assetDetail.previousLogic')} />}
          {logic.data?.hasMore && <ActionButton onClick={() => setOffset(offset + LOGIC_PAGE_SIZE)} label={t('assetDetail.moreLogic')} />}
          {canConfigureLogic && asset.isActive && <a className="secondary-button" href={hashFor('logic', 'new:' + assetId)}>{t('assetPage.putLogic')}</a>}
        </div>
      </section>}

      {operational && canReadRuns && (!canReadLogic || (offset === 0 && logic.data?.items?.length === 0)) && <AssetRunPreview assetId={assetId} />}

      {canReadRecommendations && operational && <section aria-label={t('assetPage.recommendations')}>
        <h4>{t('assetPage.recommendations')}</h4>
        {recommendations.loading && <LoadingIndicator label={t('assetPage.loading')} />}
        {recommendations.failed && <p role="status" className="asset-page-empty">{t('assetDetail.recommendationsUnavailable')} <button type="button" className="row-link" onClick={recommendations.refresh}>{t('assetDetail.retry')}</button></p>}
        {recommendations.data && (recommendations.data.length === 0
          ? <p className="asset-page-empty">{t('assetPage.noRecommendations')}</p>
          : <ul className="asset-page-list">
            {recommendations.data.map(item => (
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
