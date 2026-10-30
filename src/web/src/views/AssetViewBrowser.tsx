import { useEffect, useState } from 'react';
import { ActionButton } from '../components/ActionButton';
import { t } from '../localization';
import { VIEW_PAGE_SIZE, loadViewMembers } from './assetViewsApi';
import type { ViewAsset } from './assetViewsApi';
import './assetTree.css';
import { LoadingIndicator } from '../components/LoadingIndicator';

/** Browse only persisted members; display filters never determine membership. */
export function AssetViewBrowser({ viewId, selectedId, onSelect, query = '' }: {
  viewId: string; selectedId: string | null; onSelect: (id: string) => void; query?: string;
}) {
  const [assets, setAssets] = useState<ViewAsset[]>([]);
  const [offset, setOffset] = useState(0);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    const request = new AbortController();
    setLoading(true); setFailed(false);
    loadViewMembers(viewId, offset, request.signal, query).then(page => {
      if (request.signal.aborted) return;
      setAssets(current => offset === 0 ? page : [...current, ...page]); setHasMore(page.length === VIEW_PAGE_SIZE);
    }).catch(() => { if (!request.signal.aborted) setFailed(true); })
      .finally(() => { if (!request.signal.aborted) setLoading(false); });
    return () => request.abort();
  }, [viewId, offset, query, retry]);
  // Search is applied to members on the server, including those beyond the loaded page.
  return <nav className="asset-tree" aria-label={t('assetViews.treeLabel')}>
    <ul>{assets.map(asset => <li key={asset.id}>
      <button type="button" className="asset-select" aria-current={asset.id === selectedId ? 'true' : undefined}
        aria-label={t('assetTree.open', { name: asset.name })} onClick={() => onSelect(asset.id)}>{asset.name}</button>
    </li>)}</ul>
    {loading && <LoadingIndicator label={t('views.loading')} />}
    {failed && <p role="alert">{t('assetBrowse.failed')} <ActionButton label={t('assetBrowse.retry')} onClick={() => setRetry(value => value + 1)} /></p>}
    {hasMore && !failed && <ActionButton label={t('assetBrowse.more')} disabled={loading} onClick={() => setOffset(value => value + VIEW_PAGE_SIZE)} />}
    {!loading && !failed && assets.length === 0 && <p>{t('assetViews.empty')}</p>}
  </nav>;
}
