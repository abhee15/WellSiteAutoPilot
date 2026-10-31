import { useSplitPaneFocus } from '../components/useSplitPaneFocus';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { ActionButton } from '../components/ActionButton';
import { hashFor, useForgetVanishedSelection, useHashDetail } from '../routing';
import type {
  AssetCreationCapabilitiesResponse,
  AssetResponse,
  AssetTypeResponse,
  ConfiguredLogicResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { EmptyRow } from '../components/common';
import { Status } from '../components/Status';
import { AssetDetail } from './AssetDetail';
import { SchedulesPanel } from './SchedulesPanel';
import { AssetCreationPanel } from './AssetCreationPanel';
import { AssetTypeDefinitionPanel } from './AssetTypeDefinitionPanel';
import { AssetTypeDetail } from './AssetTypeDetail';
import { AssetTree } from './AssetTree';
import { descendantsOf } from './assetHierarchy';
import { AssetInventoryControls } from './AssetInventoryControls';
import { DEFAULT_ASSET_INVENTORY_CRITERIA, filterAssetInventory } from './assetInventory';
import type { AssetInventoryCriteria } from './assetInventory';
import { assetAncestors } from './assetPage';
import { SNAPSHOT_ASSET_LIMIT, fetchOwners } from './assetBrowseApi';
import { AssetViewBrowser } from './AssetViewBrowser';
import { AssetViewEditor } from './AssetViewEditor';
import { loadViews } from './assetViewsApi';
import type { AssetView } from './assetViewsApi';
import type { OwnerGroup } from './assetHierarchy';
import { ownerLabel, ownerNameFor } from './AssetOwnerRow';
import { AssetBrowser, AssetSearchResults } from './AssetBrowser';
import { useAssetContext } from './useAssetContext';
import { createAssetContextCache } from './assetContextCache';
import { useChangeRefresh } from '../live/LiveUpdates';
import { isSecurityId } from '../security/identifiers';
import './splitLayout.css';
import { LoadingIndicator } from '../components/LoadingIndicator';

/** The address `#assets/types` opens the Asset types tab; any other detail is an asset id. */
const TYPES_TAB = 'types';
const RESULT_LIMIT = 200;

export function AssetsPage({
  assetTypes,
  assets,
  userId,
  capabilities,
  onRefresh,
  isLoaded = true,
  configuredLogic = [],
  permissions = [],
}: {
  assetTypes: AssetTypeResponse[];
  assets: AssetResponse[];
  userId: string | null | undefined;
  capabilities: AssetCreationCapabilitiesResponse | null;
  onRefresh: () => Promise<void>;
  isLoaded?: boolean;
  configuredLogic?: ConfiguredLogicResponse[];
  permissions?: readonly string[];
}) {
  const [detail, setDetail] = useHashDetail('assets');
  const onTypesTab = detail === TYPES_TAB;
  const viewIntent = detail?.startsWith('new-view:') ? detail.slice('new-view:'.length) : null;
  const selectedId = onTypesTab || viewIntent ? null : detail;
  // A full snapshot means the flat list may be incomplete: browse and search on the server instead.
  const lazy = assets.length >= SNAPSHOT_ASSET_LIMIT;
  const [localRefresh, setLocalRefresh] = useState(0);
  const refreshKey = useMemo(
    () => assets.map(asset => `${asset.id}|${asset.name}|${asset.isActive}`).join(',') + ':' + localRefresh,
    [assets, localRefresh],
  );
  const [views, setViews] = useState<AssetView[]>([]);
  const [activeViewId, setActiveViewId] = useState<string>('');
  const [editingView, setEditingView] = useState<'new' | AssetView | null>(null);
  const viewAccess = permissions.includes('connections.read');
  const [initialViewConnection, setInitialViewConnection] = useState<string | null>(null);
  useEffect(() => {
    if (viewIntent && viewAccess && permissions.includes('asset-views.create') && isSecurityId(viewIntent)) {
      setInitialViewConnection(viewIntent); setEditingView('new'); setDetail(null);
    }
  }, [viewIntent, viewAccess, permissions, setDetail]);
  useEffect(() => {
    const controller = new AbortController();
    if (!viewAccess) { setViews([]); return () => controller.abort(); }
    loadViews(controller.signal).then(list => { if (!controller.signal.aborted) setViews(list); })
      .catch(() => { if (!controller.signal.aborted) setViews([]); });
    return () => controller.abort();
  }, [viewAccess]);
  const activeView = views.find(view => view.id === activeViewId) ?? null;
  const [owners, setOwners] = useState<OwnerGroup[]>([]);
  useEffect(() => {
    const controller = new AbortController();
    fetchOwners(controller.signal).then(list => { if (!controller.signal.aborted) setOwners(list); })
      .catch(() => { if (!controller.signal.aborted) setOwners([]); });
    return () => controller.abort();
  }, [refreshKey]);
  // What was recently read about the selection is kept so nearby selections share it; anything the page
  // learns has changed drops it. Declared before the hook below so, on a change, it clears first and the
  // hook's re-read goes to the Server.
  const contextCache = useMemo(() => createAssetContextCache(), []);
  useEffect(() => { contextCache.clear(); }, [contextCache, refreshKey]);
  useChangeRefresh(['assets'], () => contextCache.clear());
  const context = useAssetContext(selectedId, assets, lazy, refreshKey, contextCache);
  const selectedAuthorized = lazy ? context.asset ?? undefined : assets.find(asset => asset.id === selectedId);
  useForgetVanishedSelection(selectedId, Boolean(selectedAuthorized), () => setDetail(null));
  const splitFocus = useSplitPaneFocus(selectedAuthorized?.id ?? null);
  // What the asset page and the add form need to know about around the selection.
  const nearby = useMemo(() => {
    if (!lazy || !context.asset) return assets;
    const seen = new Set<string | undefined>();
    return [context.asset, ...context.ancestors, ...context.children, ...assets].filter(asset => {
      if (seen.has(asset.id)) return false;
      seen.add(asset.id);
      return true;
    });
  }, [lazy, context.asset, context.ancestors, context.children, assets]);
  const owningConnection = useMemo(() => {
    const id = (selectedAuthorized as { ownerConnectionId?: string | null } | undefined)?.ownerConnectionId ?? null;
    const owner = owners.find(item => item.connectionId === id);
    return selectedAuthorized && owners.some(item => item.connectionId !== null)
      ? { connectionId: id, name: owner ? ownerLabel(owner) : t('assetOwner.unknown') } : null;
  }, [owners, selectedAuthorized]);
  const canOpenConnection = permissions.includes('connections.read');
  const typeNames = useMemo(
    () => new Map(assetTypes.map(type => [type.id, type.displayName])),
    [assetTypes],
  );
  const typeCounts = useMemo(() => {
    const counts = new Map<string, number>();
    for (const asset of assets) {
      const key = asset.assetTypeId ?? '';
      counts.set(key, (counts.get(key) ?? 0) + 1);
    }
    return counts;
  }, [assets]);
  const [criteria, setCriteria] = useState<AssetInventoryCriteria>(DEFAULT_ASSET_INVENTORY_CRITERIA);
  const [adding, setAdding] = useState(false);
  const [focus, setFocus] = useState<{ id: string; name: string } | null>(null);
  const focusId = focus?.id ?? null;
  const canFocus = lazy || assets.some(asset => asset.parentAssetId === selectedAuthorized?.id);
  const [openTypeId, setOpenTypeId] = useState<string | null>(null);
  const visibleAssets = useMemo(
    () => filterAssetInventory(assets, typeNames, criteria),
    [assets, typeNames, criteria],
  );
  const filtering = (Object.keys(DEFAULT_ASSET_INVENTORY_CRITERIA) as (keyof AssetInventoryCriteria)[])
    .some(key => criteria[key] !== DEFAULT_ASSET_INVENTORY_CRITERIA[key]);
  const canAdd = !!userId && !!capabilities
    && (capabilities.canCreateRoot === true || capabilities.canCreateUnderAuthorizedParents === true);
  const canManageTypes = permissions.includes('asset-types.manage');
  const canHideTypes = permissions.includes('asset-types.hide');
  const [showHidden, setShowHidden] = useState(false);
  const listedTypes = useMemo(
    () => showHidden ? assetTypes : assetTypes.filter(type => type.isActive !== false),
    [assetTypes, showHidden],
  );
  const activeTypes = useMemo(() => assetTypes.filter(type => type.isActive !== false), [assetTypes]);
  // Stable between renders, so the tree's rows are not redrawn just because the page was.
  const select = useCallback((id: string | null) => { setDetail(id); setAdding(false); }, [setDetail]);
  const treeAssets = useMemo(() => focusId ? descendantsOf(assets, focusId) : assets, [assets, focusId]);
  const showTypeAssets = (typeName: string) => {
    setCriteria({ ...DEFAULT_ASSET_INVENTORY_CRITERIA, search: typeName });
    setDetail(null);
  };

  return (
    <div {...splitFocus} className="split-layout" data-has-selection={Boolean(selectedAuthorized)}>
      <div className="split-tabs" role="tablist" aria-label={t('assets.tabs')}>
        <a role="tab" href={hashFor('assets')} aria-selected={!onTypesTab}>
          {t('assets.tabAssets')} <span>{lazy ? `${assets.length}+` : assets.length}</span>
        </a>
        <a role="tab" href={hashFor('assets', TYPES_TAB)} aria-selected={onTypesTab}>
          {t('assets.tabTypes')} <span>{listedTypes.length}</span>
        </a>
        {!onTypesTab && canAdd && <ActionButton variant="primary" className="split-add" onClick={() => setAdding(open => !open)} aria-expanded={adding}
          label={adding ? t('assets.closeAdd') : selectedAuthorized
            ? t('assets.addUnder', { name: selectedAuthorized.name ?? t('assets.unnamed') })
            : t('assets.add')} />}
      </div>

      {onTypesTab ? (
        <section className="panel" aria-label={t('assets.tabTypes')}>
          {canManageTypes && <AssetTypeDefinitionPanel onCreated={async created => {
            await onRefresh();
            setOpenTypeId(created.id ?? null);
          }} />}
          {(canManageTypes || canHideTypes) && <label className="split-note">
            <input type="checkbox" checked={showHidden} onChange={event => setShowHidden(event.target.checked)} />
            {' '}{t('assetTypes.showHidden')}</label>}
          <div className="table-wrap">
            <table className="activity-table">
              <thead>
                <tr>
                  <th>Display name</th>
                  <th>Key</th>
                  <th>{t('assets.typeCount')}</th>
                  <th>Schema version</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {listedTypes.map(type => {
                  const count = typeCounts.get(type.id ?? '') ?? 0;
                  const typeName = type.displayName ?? type.key ?? '';
                  return (
                    <tr key={type.id}>
                      <td><button type="button" className="asset-select" aria-expanded={openTypeId === type.id}
                        aria-label={t('assetTypes.openDetails', { name: typeName })}
                        onClick={() => setOpenTypeId(openTypeId === type.id ? null : type.id ?? null)}>
                        {type.displayName}</button></td>
                      <td>{type.key}</td>
                      <td>{lazy ? '—' : count > 0
                        ? <button type="button" className="asset-select"
                          aria-label={t('assets.showOfType', { count, name: typeName })}
                          onClick={() => showTypeAssets(typeName)}>{count}</button>
                        : 0}</td>
                      <td>{type.schemaVersion}</td>
                      <td><Status value={type.isActive ? 'Active' : 'Hidden'} label={type.isActive ? undefined : t('assetTypes.hidden')} /></td>
                    </tr>
                  );
                })}
                {listedTypes.length === 0 && <EmptyRow columns={5}>{t('assets.noTypes')}</EmptyRow>}
              </tbody>
            </table>
          </div>
          {listedTypes.filter(type => type.id === openTypeId).map(type => (
            <AssetTypeDetail key={type.id} type={type} count={typeCounts.get(type.id ?? '') ?? 0}
              countKnown={!lazy} canHide={canHideTypes} canManage={canManageTypes}
              onChanged={async () => { setOpenTypeId(null); await onRefresh(); }}
              onShowAssets={showTypeAssets} onClose={() => setOpenTypeId(null)} />
          ))}
        </section>
      ) : (
        <>
          {adding && canAdd && <AssetCreationPanel
            key={(userId ?? 'anonymous') + ':' + (selectedAuthorized?.id ?? 'none')}
            userId={userId}
            assetTypes={activeTypes}
            assets={nearby}
            capabilities={capabilities}
            suggestedParentId={selectedAuthorized?.id ?? null}
            onCreated={async created => {
              setAdding(false);
              setDetail(created.id ?? null);
              setLocalRefresh(count => count + 1);
              await onRefresh();
            }}
            onRefresh={onRefresh}
            onCancel={() => setAdding(false)}
          />}
          {editingView && <AssetViewEditor
            key={editingView === 'new' ? 'new' : editingView.id}
            view={editingView === 'new' ? null : editingView}
            initialConnectionId={initialViewConnection}
            owners={owners}
            assetTypes={activeTypes.map(type => ({ id: type.id ?? '', key: type.key ?? '', name: type.displayName ?? type.key ?? '' }))}
            onSaved={saved => {
              setViews(current => [...current.filter(view => view.id !== saved.id), saved]
                .sort((a, b) => a.name.localeCompare(b.name)));
              setActiveViewId(saved.id);
              setEditingView(null);
            }}
            onDeleted={() => {
              if (editingView !== 'new') setViews(current => current.filter(view => view.id !== editingView.id));
              setActiveViewId('');
              setEditingView(null);
            }}
            onCancel={() => { setEditingView(null); setInitialViewConnection(null); }} />}
          {viewAccess && <div className="asset-view-bar">
            <label><span>{t('assetViews.selector')}</span>{' '}
              <select value={activeView?.id ?? ''} onChange={event => { setActiveViewId(event.target.value); setFocus(null); setCriteria(DEFAULT_ASSET_INVENTORY_CRITERIA); }}>
                <option value="">{t('assetViews.default')}</option>
                {views.map(view => <option key={view.id} value={view.id}>{view.name} · {owners.find(owner => owner.connectionId === view.connectionId)?.name ?? view.connectionId}</option>)}
              </select>
            </label>
            {permissions.includes('asset-views.create') && <ActionButton label={t('assetViews.create')} onClick={() => { setInitialViewConnection(null); setEditingView('new'); }} />}
            {activeView && (activeView.canEdit || activeView.canDelete) && <ActionButton label={t('assetViews.edit')} onClick={() => setEditingView(activeView)} />}
            {activeView?.hasUnavailableMembers && <span role="note" className="connection-help">{t('assetViews.unavailableMembers')}</span>}
          </div>}
          <div className="split-panes">
            <section className="panel split-browse" aria-label={t('assets.browse')}>
              <AssetInventoryControls
                userId={userId}
                criteria={criteria}
                onChange={setCriteria}
                matchingCount={visibleAssets.length}
                totalCount={assets.length}
                searchOnly={lazy || Boolean(activeView)}
              />
              {activeView ? null : focus ? (
                <p className="split-focus">
                  <span>{t('assets.focusedOn', { name: focus.name })}</span>
                  <ActionButton label={t('assets.showAll')} onClick={() => setFocus(null)} />
                </p>
              ) : selectedAuthorized && canFocus && !criteria.search.trim() && !filtering && (
                <p className="split-focus">
                  <ActionButton onClick={() => setFocus({ id: selectedAuthorized.id ?? '', name: selectedAuthorized.name ?? t('assets.unnamed') })}
                    label={t('assets.focusOn', { name: selectedAuthorized.name ?? t('assets.unnamed') })} />
                </p>
              )}
              {!isLoaded ? <LoadingIndicator variant="skeleton" label={t('views.loading')} />
                : assets.length === 0 ? (
                  <>
                    <p className="split-note">{t('assets.none')}</p>
                    <p className="split-note">{t('assets.noneHint')}</p>
                    {permissions.includes('connections.manage') && permissions.includes('connections.read') &&
                      <p className="split-note"><a href="#connections">{t('assets.importFromConnection')}</a></p>}
                  </>
                ) : activeView ? (
                  <>
                    {permissions.includes('configured-logic.read') && <SchedulesPanel viewId={activeView.id} hideWhenEmpty
                      canManage={permissions.includes('configured-logic.manage')} />}
                    <AssetViewBrowser key={activeView.id + ':' + activeView.revision + ':' + localRefresh + ':' + criteria.search} viewId={activeView.id}
                      selectedId={selectedId} onSelect={select} query={criteria.search} />
                  </>
                ) : lazy ? (
                  criteria.search.trim()
                    ? <AssetSearchResults query={criteria.search.trim()} selectedId={selectedId}
                      typeNames={typeNames} onSelect={select} owners={owners} />
                    : <AssetBrowser key={focusId ?? 'all'} rootId={focusId} selectedId={selectedId} refreshKey={refreshKey} onSelect={select}
                      selectedOwnerId={context.asset ? context.asset.ownerConnectionId ?? null : undefined}
                      canOpenConnection={canOpenConnection} typeNames={typeNames}
                      ancestorIds={context.ancestors.map(item => item.id ?? '')} />
                ) : !filtering ? (
                  <AssetTree assets={treeAssets} selectedId={selectedId} onSelect={select}
                    owners={owners} canOpenConnection={canOpenConnection} typeNames={typeNames} />
                ) : visibleAssets.length === 0 ? (
                  <p className="split-note">{t('assets.noMatches')}</p>
                ) : (
                  <>
                    <ul className="split-results" aria-label={t('assets.results')}>
                      {visibleAssets.slice(0, RESULT_LIMIT).map(asset => {
                        const id = asset.id ?? '';
                        const ownerName = ownerNameFor(owners, (asset as { ownerConnectionId?: string | null }).ownerConnectionId);
                        const path = [...(ownerName ? [ownerName] : []), ...assetAncestors(assets, id).map(a => a.name ?? '—')].join(' › ');
                        return (
                          <li key={id} data-selected={id === selectedId}>
                            <button type="button" className="asset-select"
                              aria-current={id === selectedId ? 'true' : undefined}
                              aria-label={t('assets.selectNamed', { name: asset.name ?? t('assets.unnamed') })}
                              onClick={() => select(id)}>{asset.name}</button>
                            <small>{typeNames.get(asset.assetTypeId) ?? asset.assetTypeId}{path ? ' · ' + path : ''}</small>
                          </li>
                        );
                      })}
                    </ul>
                    {visibleAssets.length > RESULT_LIMIT && <p className="split-note">
                      {t('assets.resultsCapped', { shown: RESULT_LIMIT, total: visibleAssets.length })}</p>}
                  </>
                )}
            </section>
            <section className="split-selected" tabIndex={-1} aria-label={t('assets.selected')}>
              <ActionButton label={t('assets.browse')} className="split-back" onClick={() => select(null)} />
              {selectedAuthorized ? <AssetDetail
                key={selectedAuthorized.id}
                asset={selectedAuthorized}
                assets={nearby}
                typeName={typeNames.get(selectedAuthorized.assetTypeId) ?? selectedAuthorized.assetTypeId ?? t('assets.unknownType')}
                configuredLogic={configuredLogic}
                canReadRuns={permissions.includes('executions.read')}
                canConfigureLogic={permissions.includes('configured-logic.manage') && permissions.includes('configured-logic.read')}
                canReadRecommendations={permissions.includes('recommendations.read')}
                canReadSchedules={permissions.includes('configured-logic.read')}
                owner={owningConnection}
                canOpenConnection={canOpenConnection}
                onSelect={select}
                onClose={() => select(null)}
              /> : <p className="split-note">{t('assets.pickOne')}</p>}
            </section>
          </div>
        </>
      )}
    </div>
  );
}
