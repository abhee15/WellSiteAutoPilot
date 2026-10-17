import { useMemo, useState } from 'react';
import { useForgetVanishedSelection, useHashDetail } from '../routing';
import type {
  AssetCreationCapabilitiesResponse,
  AssetResponse,
  AssetTypeResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { EmptyRow, PanelHeader, StatusPill, SummaryCard } from '../components/common';
import { formatTimestamp } from '../format';
import { AssetCreationPanel } from './AssetCreationPanel';
import { AssetInventoryControls } from './AssetInventoryControls';
import { DEFAULT_ASSET_INVENTORY_CRITERIA, filterAssetInventory } from './assetInventory';
import type { AssetInventoryCriteria } from './assetInventory';

export function AssetsPage({
  assetTypes,
  assets,
  userId,
  capabilities,
  onRefresh,
  isLoaded = true,
}: {
  assetTypes: AssetTypeResponse[];
  assets: AssetResponse[];
  userId: string | null | undefined;
  capabilities: AssetCreationCapabilitiesResponse | null;
  onRefresh: () => Promise<void>;
  isLoaded?: boolean;
}) {
  const [selectedId, setSelectedId] = useHashDetail('assets');
  const selectedAuthorized = assets.find(asset => asset.id === selectedId);
  useForgetVanishedSelection(selectedId, Boolean(selectedAuthorized), () => setSelectedId(null));
  const typeNames = useMemo(
    () => new Map(assetTypes.map(type => [type.id, type.displayName])),
    [assetTypes],
  );
  const [inventoryCriteria, setInventoryCriteria] = useState<AssetInventoryCriteria>(
    DEFAULT_ASSET_INVENTORY_CRITERIA,
  );
  const visibleAssets = useMemo(
    () => filterAssetInventory(assets, typeNames, inventoryCriteria),
    [assets, typeNames, inventoryCriteria],
  );
  const rootAssets = assets.filter(asset => !asset.parentAssetId).length;

  return (
    <>
      <section className="summary-grid" aria-label="Asset summary">
        <SummaryCard label="Assets" value={String(assets.length)} detail="Available to configured Logic" />
        <SummaryCard label="Asset Types" value={String(assetTypes.length)} detail="Provider-neutral definitions" />
        <SummaryCard label="Root Assets" value={String(rootAssets)} detail="Top-level hierarchy entries" />
      </section>

      <AssetCreationPanel
        key={userId ?? 'anonymous'}
        userId={userId}
        assetTypes={assetTypes}
        assets={assets}
        capabilities={capabilities}
        onCreated={async created => {
          setSelectedId(created.id ?? null);
          await onRefresh();
        }}
        onRefresh={onRefresh}
      />
      <section className="panel">
        <PanelHeader eyebrow="Inventory" title="Assets" count={visibleAssets.length} />
        <AssetInventoryControls
          userId={userId}
          criteria={inventoryCriteria}
          onChange={setInventoryCriteria}
          matchingCount={visibleAssets.length}
          totalCount={assets.length}
        />
        <div className="table-wrap">
          <table className="activity-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Type</th>
                <th>Hierarchy</th>
                <th>Status</th>
                <th>Created</th>
              </tr>
            </thead>
            <tbody>
              {visibleAssets.map(asset => (
                <tr key={asset.id}>
                  <td><button type="button" className="asset-select"
                    onClick={() => setSelectedId(asset.id ?? null)}
                    aria-label={t('assets.selectNamed', { name: asset.name ?? t('assets.unnamed') })}>
                    {asset.name}
                  </button></td>
                  <td>{typeNames.get(asset.assetTypeId) ?? asset.assetTypeId}</td>
                  <td>{asset.parentAssetId ? 'Child asset' : 'Root asset'}</td>
                  <td><StatusPill value={asset.isActive ? 'Active' : 'Inactive'} /></td>
                  <td>{formatTimestamp(asset.createdAtUtc)}</td>
                </tr>
              ))}
              {visibleAssets.length === 0 && (
                <EmptyRow columns={5}>
                  {!isLoaded ? t('views.loading') : assets.length === 0 ? t('assets.none') : t('assets.noMatches')}
                </EmptyRow>
              )}
            </tbody>
          </table>
        </div>
        {selectedAuthorized && <div className="asset-selected" aria-live="polite">
          <h3>{t('assets.selected')}</h3>
          <strong>{selectedAuthorized.name}</strong>
          <p>{t('assets.selectedType', { name:
            typeNames.get(selectedAuthorized.assetTypeId) ?? selectedAuthorized.assetTypeId ?? t('assets.unknownType') })}</p>
          <code>{selectedAuthorized.id}</code>
          <button type="button" className="secondary-button"
            onClick={() => setSelectedId(null)}>{t('assets.clearSelection')}</button>
        </div>}
      </section>

      <section className="panel">
        <PanelHeader eyebrow="Schema" title="Asset Types" count={assetTypes.length} />
        <div className="table-wrap">
          <table className="activity-table">
            <thead>
              <tr>
                <th>Display name</th>
                <th>Key</th>
                <th>Schema version</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {assetTypes.map(type => (
                <tr key={type.id}>
                  <td><strong>{type.displayName}</strong></td>
                  <td>{type.key}</td>
                  <td>{type.schemaVersion}</td>
                  <td><StatusPill value={type.isActive ? 'Active' : 'Inactive'} /></td>
                </tr>
              ))}
              {assetTypes.length === 0 && <EmptyRow columns={4}>No Asset Types are configured.</EmptyRow>}
            </tbody>
          </table>
        </div>
      </section>
    </>
  );
}
