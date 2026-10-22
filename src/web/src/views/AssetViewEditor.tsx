import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import { MAX_VIEW_NAME, ViewsError, createView, deleteView, loadViewMembers, loadViewSelection, updateView } from './assetViewsApi';
import type { AssetView } from './assetViewsApi';
import type { OwnerGroup } from './assetHierarchy';
import { AssetBrowser } from './AssetBrowser';
import { fetchSearch, SEARCH_PAGE_SIZE } from './assetBrowseApi';
import type { BrowseNode } from './assetBrowseApi';
import { wellsUnder } from './wellSelection';
import './assetViews.css';

const errors: Record<string, MessageKey> = { accessDenied: 'assetViews.errDenied', notFound: 'assetViews.errNotFound',
  nameTaken: 'assetViews.errNameTaken', stale: 'assetViews.errStale', invalid: 'assetViews.errInvalid' };
const errorMessage = (error: unknown) => t(error instanceof ViewsError ? errors[error.kind] ?? 'assetViews.errUnavailable' : 'assetViews.errUnavailable');

export function AssetViewEditor({ view, owners, assetTypes, initialConnectionId, onSaved, onDeleted, onCancel }: {
  view: AssetView | null; owners: readonly OwnerGroup[];
  assetTypes: readonly { id: string; key: string; name: string }[]; initialConnectionId?: string | null;
  onSaved: (saved: AssetView) => void; onDeleted: () => void; onCancel: () => void;
}) {
  const [name, setName] = useState(view?.name ?? '');
  const [connectionId, setConnectionId] = useState(view?.connectionId ?? initialConnectionId ?? '');
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [known, setKnown] = useState<ReadonlyMap<string, BrowseNode>>(new Map());
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(!!view?.canEdit);
  const [selectionReady, setSelectionReady] = useState(!view);
  const [error, setError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [query, setQuery] = useState('');
  const [hits, setHits] = useState<BrowseNode[]>([]);
  const [hasMore, setHasMore] = useState(false);
  const [searchOffset, setSearchOffset] = useState(0);
  const [searchLoading, setSearchLoading] = useState(false);
  const [searchError, setSearchError] = useState(false);
  const [reviewLimit, setReviewLimit] = useState(100);
  const controller = useRef(new AbortController());
  const pending = useRef(false);
  const searchController = useRef(new AbortController());
  const readOnly = !!view && !view.canEdit;
  const wellTypeIds = useMemo(() => new Set(assetTypes.filter(type => type.key === 'well').map(type => type.id)), [assetTypes]);
  const remember = useCallback((nodes: BrowseNode[]) => setKnown(current => new Map([...current, ...nodes.map(node => [node.id, node] as const)])), []);

  useEffect(() => {
    const request = new AbortController(); controller.current = request;
    if (view?.canEdit) {
      Promise.all([loadViewSelection(view, request.signal), loadViewMembers(view.id, 0, request.signal)]).then(([ids, members]) => {
        if (request.signal.aborted) return;
        setSelected(new Set(ids)); remember(members.map(member => ({ ...member, childCount: 0 })));
        setSelectionReady(true);
      }).catch(reason => { if (!request.signal.aborted) setError(errorMessage(reason)); })
        .finally(() => { if (!request.signal.aborted) setLoading(false); });
    }
    return () => request.abort();
  }, [view, remember]);

  useEffect(() => {
    const request = new AbortController(); searchController.current = request;
    setHits([]); setSearchOffset(0); setHasMore(false); setSearchError(false);
    if (!query.trim() || !connectionId) { setSearchLoading(false); return () => request.abort(); }
    setSearchLoading(true);
    const timer = setTimeout(() => {
      fetchSearch(query.trim(), 0, request.signal, { connectionId }).then(rows => {
        if (request.signal.aborted) return;
        const nodes = rows.map(row => ({ ...row, childCount: 0 }));
        setHits(nodes); remember(nodes); setHasMore(rows.length === SEARCH_PAGE_SIZE);
      }).catch(() => { if (!request.signal.aborted) setSearchError(true); })
        .finally(() => { if (!request.signal.aborted) setSearchLoading(false); });
    }, 250);
    return () => { clearTimeout(timer); request.abort(); };
  }, [query, connectionId, remember]);

  const toggle = (node: BrowseNode) => {
    if (busy || loading || readOnly || node.ownerConnectionId !== connectionId || !wellTypeIds.has(node.assetTypeId)) return;
    remember([node]);
    setSelected(current => { const next = new Set(current); if (next.has(node.id)) next.delete(node.id); else next.add(node.id); return next; });
  };
  const selectBranch = async (id: string | null) => {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError(null);
    try {
      const nodes = await wellsUnder(connectionId, id, wellTypeIds, controller.current.signal);
      if (controller.current.signal.aborted) return;
      remember(nodes); setSelected(current => new Set([...current, ...nodes.map(node => node.id)]));
    } catch (reason) { if (!controller.current.signal.aborted) setError(errorMessage(reason)); }
    finally { pending.current = false; if (!controller.current.signal.aborted) setBusy(false); }
  };
  const moreSearch = async () => {
    const request = searchController.current;
    setSearchLoading(true); setSearchError(false);
    const offset = searchOffset + SEARCH_PAGE_SIZE;
    try {
      const rows = await fetchSearch(query.trim(), offset, request.signal, { connectionId });
      if (request.signal.aborted) return;
      const nodes = rows.map(row => ({ ...row, childCount: 0 }));
      setHits(current => [...current, ...nodes]); remember(nodes); setSearchOffset(offset); setHasMore(rows.length === SEARCH_PAGE_SIZE);
    } catch { if (!request.signal.aborted) setSearchError(true); }
    finally { if (!request.signal.aborted) setSearchLoading(false); }
  };
  const run = async (action: () => Promise<void>) => {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError(null);
    try { await action(); } catch (reason) { if (!controller.current.signal.aborted) setError(errorMessage(reason)); }
    finally { pending.current = false; if (!controller.current.signal.aborted) setBusy(false); }
  };
  const selectedIds = [...selected].sort((a, b) => Number(known.has(b)) - Number(known.has(a)) ||
    (known.get(a)?.name ?? a).localeCompare(known.get(b)?.name ?? b));

  return <form className="asset-view-editor panel" aria-label={t(view ? 'assetViews.editTitle' : 'assetViews.createTitle')}
    onSubmit={event => { event.preventDefault(); if (!readOnly && selectionReady) void run(async () => {
      onSaved(view ? await updateView(view, name.trim(), [...selected]) : await createView(name.trim(), connectionId, [...selected]));
    }); }}>
    <h3>{t(view ? 'assetViews.editTitle' : 'assetViews.createTitle')}</h3>
    <p className="connection-help">{t('assetViews.intro')}</p>
    <fieldset disabled={busy || loading || readOnly}>
      <label><span>{t('assetViews.name')}</span><input value={name} maxLength={MAX_VIEW_NAME} onChange={event => setName(event.target.value)} /></label>
      <label><span>{t('assetViews.connection')}</span><select value={connectionId} disabled={!!view}
        onChange={event => { setConnectionId(event.target.value); setSelected(new Set()); setKnown(new Map()); setQuery(''); }}>
        <option value="">{t('assetViews.chooseConnection')}</option>
        {owners.filter(owner => owner.connectionId !== null).map(owner =>
          <option key={owner.connectionId} value={owner.connectionId ?? ''}>{owner.name}</option>)}
      </select></label>
      {!view && <p className="connection-help">{t('assetViews.connectionHelp')}</p>}
      {connectionId && <div className="well-selection-layout">
        <section aria-label={t('assetViews.availableWells')}>
          <label><span>{t('assetViews.findWells')}</span><input type="search" value={query} maxLength={120} onChange={event => setQuery(event.target.value)} /></label>
          <button type="button" className="secondary-button" onClick={() => void selectBranch(null)}>{t('assetViews.selectAllWells')}</button>
          {query.trim() ? <div>
            {hits.filter(node => node.isActive && wellTypeIds.has(node.assetTypeId)).map(node =>
              <label key={node.id} className="asset-view-check"><input type="checkbox" checked={selected.has(node.id)}
                aria-label={t('assetViews.selectWell', { name: node.name })} onChange={() => toggle(node)} />{node.name}</label>)}
            {searchLoading && <p role="status">{t('views.loading')}</p>}
            {searchError && <p role="alert">{t('assetBrowse.failed')}</p>}
            {hasMore && <button type="button" disabled={searchLoading} onClick={() => void moreSearch()}>{t('assetBrowse.more')}</button>}
          </div> : <AssetBrowser key={connectionId} selectedId={null} ancestorIds={[]} refreshKey={connectionId} onSelect={() => undefined}
            typeNames={new Map(assetTypes.map(type => [type.id, type.name]))}
            selection={{ connectionId, wellTypeIds, selectedIds: selected, onToggle: toggle, onNodes: remember,
              onSelectBranch: id => void selectBranch(id), busy }} />}
        </section>
        <section aria-label={t('assetViews.selectedWells')}>
          <h4>{t('assetViews.selectedCount', { count: selected.size })}</h4>
          <button type="button" className="secondary-button" onClick={() => setSelected(new Set())}>{t('assetViews.clearSelection')}</button>
          <ul className="well-selection-list">{selectedIds.slice(0, reviewLimit).map(id => <li key={id}>
            <span>{known.get(id)?.name ?? id}</span><button type="button" className="asset-select"
              aria-label={t('assetViews.removeWell', { name: known.get(id)?.name ?? id })}
              onClick={() => setSelected(current => new Set([...current].filter(item => item !== id)))}>{t('assetViews.remove')}</button>
          </li>)}</ul>
          {selected.size > reviewLimit && <button type="button" onClick={() => setReviewLimit(limit => limit + 100)}>{t('assetBrowse.more')}</button>}
        </section>
      </div>}
    </fieldset>
    {loading && <p role="status">{t('views.loading')}</p>}
    {busy && <p role="status">{t('assetViews.working')}</p>}
    {error && <p role="alert" className="status-error">{error}</p>}
    <div className="connection-actions">
      {!readOnly && <button type="submit" className="primary-button" disabled={busy || loading || !selectionReady || !name.trim() || !connectionId || selected.size === 0 || selected.size > 50_000}>
        {t('assetViews.save')}</button>}
      <button type="button" className="secondary-button" disabled={busy} onClick={onCancel}>{t('assetViews.cancel')}</button>
      {view?.canDelete && !confirmDelete && <button type="button" className="secondary-button" disabled={busy}
        onClick={() => setConfirmDelete(true)}>{t('assetViews.delete')}</button>}
    </div>
    {view?.canDelete && confirmDelete && <div role="alertdialog" aria-label={t('assetViews.delete')}>
      <p>{t('assetViews.deleteConfirm', { name: view.name })}</p>
      <button type="button" disabled={busy} onClick={() => void run(async () => { await deleteView(view); onDeleted(); })}>{t('assetViews.deleteYes')}</button>
      <button type="button" disabled={busy} onClick={() => setConfirmDelete(false)}>{t('assetViews.deleteNo')}</button>
    </div>}
  </form>;
}
