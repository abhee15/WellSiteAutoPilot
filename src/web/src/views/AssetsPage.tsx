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
import './assetsLayout.css';

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
  const selectedAuthorized = assets.find(asset => asset.id === selectedId);
  useForgetVanishedSelection(selectedId, Boolean(selectedAuthorized), () => setDetail(null));
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
    <div className="assets-layout" data-has-selection={Boolean(selectedAuthorized)}>
      <div className="assets-tabs" role="tablist" aria-label={t('assets.tabs')}>
        <a role="tab" href={hashFor('assets')} aria-selected={!onTypesTab}>
          {t('assets.tabAssets')} <span>{assets.length}</span>
        </a>
        <a role="tab" href={hashFor('assets', TYPES_TAB)} aria-selected={onTypesTab}>
          {t('assets.tabTypes')} <span>{assetTypes.length}</span>
        </a>
        {!onTypesTab && canAdd && <button type="button" className="primary-button assets-add"
          onClick={() => setAdding(open => !open)} aria-expanded={adding}>
          {selectedAuthorized
            ? t('assets.addUnder', { name: selectedAuthorized.name ?? t('assets.unnamed') })
            : t('assets.add')}
        </button>}
      </div>

      {onTypesTab ? (
        <section className="panel" aria-label={t('assets.tabTypes')}>
          <p className="assets-intro">{t('assets.typesIntro')}</p>
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
                      <td>{count > 0
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
            assets={assets}
            capabilities={capabilities}
            suggestedParentId={selectedAuthorized?.id ?? null}
            onCreated={async created => {
              setAdding(false);
              setDetail(created.id ?? null);
              await onRefresh();
            }}
            onRefresh={onRefresh}
          />}
          <div className="assets-panes">
            <section className="panel assets-browse" aria-label={t('assets.browse')}>
              <AssetInventoryControls
                userId={userId}
                criteria={criteria}
                onChange={setCriteria}
                matchingCount={visibleAssets.length}
                totalCount={assets.length}
              />
              {!isLoaded ? <p className="assets-note">{t('views.loading')}</p>
                : assets.length === 0 ? (
                  <>
                    <p className="assets-note">{t('assets.none')}</p>
                    <p className="assets-note">{t('assets.noneHint')}</p>
                  </>
                ) : !filtering ? (
                  <AssetTree assets={assets} selectedId={selectedId} onSelect={select} />
                ) : visibleAssets.length === 0 ? (
                  <p className="assets-note">{t('assets.noMatches')}</p>
                ) : (
                  <>
                    <ul className="assets-results" aria-label={t('assets.results')}>
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
                    {visibleAssets.length > RESULT_LIMIT && <p className="assets-note">
                      {t('assets.resultsCapped', { shown: RESULT_LIMIT, total: visibleAssets.length })}</p>}
                  </>
                )}
            </section>
            <section className="assets-selected" aria-label={t('assets.selected')}>
              <button type="button" className="secondary-button assets-back" onClick={() => select(null)}>
                {t('assets.browse')}
              </button>
              {selectedAuthorized ? <AssetDetail
                key={selectedAuthorized.id}
                asset={selectedAuthorized}
                assets={assets}
                typeName={typeNames.get(selectedAuthorized.assetTypeId) ?? selectedAuthorized.assetTypeId ?? t('assets.unknownType')}
                configuredLogic={configuredLogic}
                canReadRuns={permissions.includes('executions.read')}
                canConfigureLogic={permissions.includes('configured-logic.manage') && permissions.includes('configured-logic.read')}
                canReadRecommendations={permissions.includes('recommendations.read')}
                onSelect={select}
                onClose={() => select(null)}
              /> : <p className="assets-note">{t('assets.pickOne')}</p>}
            </section>
          </div>
        </>
      )}
    </div>
  );
}
