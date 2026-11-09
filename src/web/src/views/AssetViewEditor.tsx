import { SearchInput } from '../components/SearchInput';
import { ActionButton } from '../components/ActionButton';
import { CheckboxField, ConfirmPanel, FormActions, SelectField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import { MAX_VIEW_NAME, ViewsError, createView, deleteView, loadViewMembers, loadViewSelectionDetails, updateView } from './assetViewsApi';
import type { AssetView } from './assetViewsApi';
import type { OwnerGroup } from './assetHierarchy';
import { AssetBrowser } from './AssetBrowser';
import { fetchSearch, SEARCH_PAGE_SIZE } from './assetBrowseApi';
import type { BrowseNode } from './assetBrowseApi';
import { wellsUnder } from './wellSelection';
import './assetViews.css';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { useUnsavedChangesGuard } from '../navigationGuard';

const errors: Record<string, MessageKey> = { accessDenied: 'assetViews.errDenied', notFound: 'assetViews.errNotFound',
  nameTaken: 'assetViews.errNameTaken', referenced: 'assetViews.errReferenced', stale: 'assetViews.errStale', invalid: 'assetViews.errInvalid' };
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
  const [unavailableIds, setUnavailableIds] = useState<ReadonlySet<string>>(new Set());
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(!!view?.canEdit);
  const [selectionReady, setSelectionReady] = useState(!view);
  const [error, setError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const deleteButton = useRef<HTMLButtonElement>(null);
  const cancelDelete = () => { setConfirmDelete(false); requestAnimationFrame(() => deleteButton.current?.focus()); };
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
  // What was loaded or last saved. The member list arrives asynchronously, so the baseline is filled in when it does.
  const baseline = useRef({ name: view?.name ?? '', members: '' });
  const membersKey = (ids: Iterable<string>) => [...ids].sort().join(',');
  const markSaved = useUnsavedChangesGuard(!readOnly && !loading && selectionReady &&
    (name.trim() !== baseline.current.name.trim() || membersKey(selected) !== baseline.current.members));
  const wellTypeIds = useMemo(() => new Set(assetTypes.filter(type => type.key === 'well').map(type => type.id)), [assetTypes]);
  const remember = useCallback((nodes: BrowseNode[]) => setKnown(current => new Map([...current, ...nodes.map(node => [node.id, node] as const)])), []);

  useEffect(() => {
    const request = new AbortController(); controller.current = request;
    if (view?.canEdit) {
      Promise.all([loadViewSelectionDetails(view, request.signal), loadViewMembers(view.id, 0, request.signal)]).then(([ids, members]) => {
        if (request.signal.aborted) return;
        baseline.current.members = membersKey(ids.assetIds);
        setSelected(new Set(ids.assetIds));
        setUnavailableIds(new Set(ids.unavailableMembers.map(member => member.id)));
        remember([...members.map(member => ({ ...member, childCount: 0 })), ...ids.unavailableMembers.map(member => ({
          ...member, assetTypeId: '', parentAssetId: null, ownerConnectionId: view.connectionId, isActive: false, childCount: 0 }))]);
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
      const saved = view ? await updateView(view, name.trim(), [...selected]) : await createView(name.trim(), connectionId, [...selected]);
      markSaved();
      onSaved(saved);
    }); }}>
    <h3>{t(view ? 'assetViews.editTitle' : 'assetViews.createTitle')}</h3>
    <p className="connection-help">{t('assetViews.intro')}</p>
    <fieldset disabled={busy || loading || readOnly}>
      <TextField label={t('assetViews.name')} value={name} maxLength={MAX_VIEW_NAME} onChange={event => setName(event.target.value)} />
      <SelectField label={t('assetViews.connection')} value={connectionId} disabled={!!view}
        onChange={event => { setConnectionId(event.target.value); setSelected(new Set()); setKnown(new Map()); setQuery(''); }}>
        <option value="">{t('assetViews.chooseConnection')}</option>
        {owners.filter(owner => owner.connectionId !== null).map(owner =>
          <option key={owner.connectionId} value={owner.connectionId ?? ''}>{owner.name}</option>)}
      </SelectField>
      {!view && <p className="connection-help">{t('assetViews.connectionHelp')}</p>}
      {connectionId && <div className="well-selection-layout">
        <section aria-label={t('assetViews.availableWells')}>
          <label className="asset-view-find"><span>{t('assetViews.findWells')}</span><SearchInput value={query} maxLength={120} placeholder={t('assetViews.findWellsPlaceholder')} onChange={event => setQuery(event.target.value)} /></label>
          <ActionButton label={t('assetViews.selectAllWells')} onClick={() => void selectBranch(null)} />
          {query.trim() ? <div>
            {hits.filter(node => node.isActive && wellTypeIds.has(node.assetTypeId)).map(node =>
              <CheckboxField key={node.id} label={node.name} checked={selected.has(node.id)}
                aria-label={t('assetViews.selectWell', { name: node.name })} onChange={() => toggle(node)} />)}
            {searchLoading && <LoadingIndicator size="small" label={t('views.loading')} />}
            {searchError && <Notice tone="error">{t('assetBrowse.failed')}</Notice>}
            {hasMore && <ActionButton label={t('assetBrowse.more')} disabled={searchLoading} onClick={() => void moreSearch()} />}
          </div> : <AssetBrowser key={connectionId} selectedId={null} ancestorIds={[]} refreshKey={connectionId} onSelect={() => undefined}
            typeNames={new Map(assetTypes.map(type => [type.id, type.name]))}
            selection={{ connectionId, wellTypeIds, selectedIds: selected, onToggle: toggle, onNodes: remember,
              onSelectBranch: id => void selectBranch(id), busy }} />}
        </section>
        <section aria-label={t('assetViews.selectedWells')}>
          <h4>{t('assetViews.selectedCount', { count: selected.size })}</h4>
          <ActionButton label={t('assetViews.clearSelection')} onClick={() => setSelected(new Set())} />
          <ul className="well-selection-list">{selectedIds.slice(0, reviewLimit).map(id => <li key={id}>
            <span>{known.get(id)?.name ?? id}{unavailableIds.has(id) ? ' · ' + t('assetViews.unavailableWell') : ''}</span><ActionButton variant="quiet" label={t('assetViews.remove')}
              aria-label={t('assetViews.removeWell', { name: known.get(id)?.name ?? id })}
              onClick={() => setSelected(current => new Set([...current].filter(item => item !== id)))} />
          </li>)}</ul>
          {selected.size > reviewLimit && <ActionButton label={t('assetBrowse.more')} onClick={() => setReviewLimit(limit => limit + 100)} />}
        </section>
      </div>}
    </fieldset>
    {loading && <LoadingIndicator size="small" label={t('views.loading')} />}
    {busy && <Notice>{t('assetViews.working')}</Notice>}
    {error && <Notice tone="error">{error}</Notice>}
    <FormActions leading={view?.canDelete && !confirmDelete
      ? <ActionButton ref={deleteButton} label={t('assetViews.delete')} disabled={busy} onClick={() => setConfirmDelete(true)} /> : undefined}>
      <ActionButton label={t('assetViews.cancel')} disabled={busy} onClick={onCancel} />
      {!readOnly && <ActionButton label={t('assetViews.save')} variant="primary" type="submit" disabled={busy || loading || !selectionReady || !name.trim() || !connectionId || selected.size === 0 || selected.size > 50_000 || [...unavailableIds].some(id => selected.has(id))} />}
    </FormActions>
    {view?.canDelete && confirmDelete && <ConfirmPanel title={t('assetViews.deleteTitle')} level={3} focusOnOpen>
      <p>{t('assetViews.deleteConfirm', { name: view.name })}</p>
      <FormActions>
        <ActionButton label={t('assetViews.deleteNo')} disabled={busy} onClick={cancelDelete} />
        <ActionButton label={t('assetViews.deleteYes')} disabled={busy} onClick={() => void run(async () => { await deleteView(view); markSaved(); onDeleted(); })} />
      </FormActions>
    </ConfirmPanel>}
  </form>;
}
