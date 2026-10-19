import { useMemo, useState } from 'react';
import { hashFor, useForgetVanishedSelection, useHashDetail } from '../routing';
import type {
  AssetCreationCapabilitiesResponse,
  AssetResponse,
  AssetTypeResponse,
  ConfiguredLogicResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { EmptyRow, StatusPill } from '../components/common';
import { AssetDetail } from './AssetDetail';
import { AssetCreationPanel } from './AssetCreationPanel';
import { AssetTypeDefinitionPanel } from './AssetTypeDefinitionPanel';
import { AssetTree } from './AssetTree';
import { AssetInventoryControls } from './AssetInventoryControls';
import { DEFAULT_ASSET_INVENTORY_CRITERIA, filterAssetInventory } from './assetInventory';
import type { AssetInventoryCriteria } from './assetInventory';
import { assetAncestors } from './assetPage';
import { SNAPSHOT_ASSET_LIMIT } from './assetBrowseApi';
import { AssetBrowser, AssetSearchResults } from './AssetBrowser';
import { useAssetContext } from './useAssetContext';
import './splitLayout.css';

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
  const selectedId = onTypesTab ? null : detail;
  // A full snapshot means the flat list may be incomplete: browse and search on the server instead.
  const lazy = assets.length >= SNAPSHOT_ASSET_LIMIT;
  const [localRefresh, setLocalRefresh] = useState(0);
  const refreshKey = useMemo(
    () => assets.map(asset => `${asset.id}|${asset.name}|${asset.isActive}`).join(',') + ':' + localRefresh,
    [assets, localRefresh],
  );
  const context = useAssetContext(selectedId, assets, lazy, refreshKey);
  const selectedAuthorized = lazy ? context.asset ?? undefined : assets.find(asset => asset.id === selectedId);
  useForgetVanishedSelection(selectedId, Boolean(selectedAuthorized), () => setDetail(null));
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
  const visibleAssets = useMemo(
    () => filterAssetInventory(assets, typeNames, criteria),
    [assets, typeNames, criteria],
  );
  const filtering = (Object.keys(DEFAULT_ASSET_INVENTORY_CRITERIA) as (keyof AssetInventoryCriteria)[])
    .some(key => criteria[key] !== DEFAULT_ASSET_INVENTORY_CRITERIA[key]);
  const canAdd = !!userId && !!capabilities
    && (capabilities.canCreateRoot === true || capabilities.canCreateUnderAuthorizedParents === true);
  const canManageTypes = permissions.includes('assets.manage');
  const select = (id: string | null) => { setDetail(id); setAdding(false); };
  const showTypeAssets = (typeName: string) => {
    setCriteria({ ...DEFAULT_ASSET_INVENTORY_CRITERIA, search: typeName });
    setDetail(null);
  };

  return (
    <div className="split-layout" data-has-selection={Boolean(selectedAuthorized)}>
      <div className="split-tabs" role="tablist" aria-label={t('assets.tabs')}>
        <a role="tab" href={hashFor('assets')} aria-selected={!onTypesTab}>
          {t('assets.tabAssets')} <span>{lazy ? `${assets.length}+` : assets.length}</span>
        </a>
        <a role="tab" href={hashFor('assets', TYPES_TAB)} aria-selected={onTypesTab}>
          {t('assets.tabTypes')} <span>{assetTypes.length}</span>
        </a>
        {!onTypesTab && canAdd && <button type="button" className="primary-button split-add"
          onClick={() => setAdding(open => !open)} aria-expanded={adding}>
          {selectedAuthorized
            ? t('assets.addUnder', { name: selectedAuthorized.name ?? t('assets.unnamed') })
            : t('assets.add')}
        </button>}
      </div>

      {onTypesTab ? (
        <section className="panel" aria-label={t('assets.tabTypes')}>
          <p className="split-intro">{t('assets.typesIntro')}</p>
          {canManageTypes && <AssetTypeDefinitionPanel onCreated={onRefresh} />}
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
                {assetTypes.map(type => {
                  const count = typeCounts.get(type.id ?? '') ?? 0;
                  const typeName = type.displayName ?? type.key ?? '';
                  return (
                    <tr key={type.id}>
                      <td><strong>{type.displayName}</strong></td>
                      <td>{type.key}</td>
                      <td>{lazy ? '—' : count > 0
                        ? <button type="button" className="asset-select"
                          aria-label={t('assets.showOfType', { count, name: typeName })}
                          onClick={() => showTypeAssets(typeName)}>{count}</button>
                        : 0}</td>
                      <td>{type.schemaVersion}</td>
                      <td><StatusPill value={type.isActive ? 'Active' : 'Inactive'} /></td>
                    </tr>
                  );
                })}
                {assetTypes.length === 0 && <EmptyRow columns={5}>{t('assets.noTypes')}</EmptyRow>}
              </tbody>
            </table>
          </div>
        </section>
      ) : (
        <>
          {adding && canAdd && <AssetCreationPanel
            key={(userId ?? 'anonymous') + ':' + (selectedAuthorized?.id ?? 'none')}
            userId={userId}
            assetTypes={assetTypes}
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
          />}
          <div className="split-panes">
            <section className="panel split-browse" aria-label={t('assets.browse')}>
              <div className="split-search">
                <input type="search" maxLength={120} value={criteria.search}
                  aria-label={t('assets.search')} placeholder={t('assets.searchPlaceholder')}
                  onChange={event => setCriteria({ ...criteria, search: event.target.value })} />
              </div>
              {!lazy && <details className="split-filters">
                <summary>{t('assets.moreFilters')}</summary>
                <AssetInventoryControls
                  userId={userId}
                  criteria={criteria}
                  onChange={setCriteria}
                  matchingCount={visibleAssets.length}
                  totalCount={assets.length}
                  hideSearch
                />
              </details>}
              {!isLoaded ? <p className="split-note">{t('views.loading')}</p>
                : assets.length === 0 ? (
                  <>
                    <p className="split-note">{t('assets.none')}</p>
                    <p className="split-note">{t('assets.noneHint')}</p>
                  </>
                ) : lazy ? (
                  criteria.search.trim()
                    ? <AssetSearchResults query={criteria.search.trim()} selectedId={selectedId}
                      typeNames={typeNames} onSelect={select} />
                    : <AssetBrowser selectedId={selectedId} refreshKey={refreshKey} onSelect={select}
                      ancestorIds={context.ancestors.map(item => item.id ?? '')} />
                ) : !filtering ? (
                  <AssetTree assets={assets} selectedId={selectedId} onSelect={select} />
                ) : visibleAssets.length === 0 ? (
                  <p className="split-note">{t('assets.noMatches')}</p>
                ) : (
                  <>
                    <ul className="split-results" aria-label={t('assets.results')}>
                      {visibleAssets.slice(0, RESULT_LIMIT).map(asset => {
                        const id = asset.id ?? '';
                        const path = assetAncestors(assets, id).map(a => a.name ?? '—').join(' › ');
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
            <section className="split-selected" aria-label={t('assets.selected')}>
              <button type="button" className="secondary-button split-back" onClick={() => select(null)}>
                {t('assets.browse')}
              </button>
              {selectedAuthorized ? <AssetDetail
                key={selectedAuthorized.id}
                asset={selectedAuthorized}
                assets={nearby}
                typeName={typeNames.get(selectedAuthorized.assetTypeId) ?? selectedAuthorized.assetTypeId ?? t('assets.unknownType')}
                configuredLogic={configuredLogic}
                canReadRuns={permissions.includes('executions.read')}
                canConfigureLogic={permissions.includes('configured-logic.manage') && permissions.includes('configured-logic.read')}
                canReadRecommendations={permissions.includes('recommendations.read')}
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
