import { useEffect, useState } from 'react';
import type { AssetResponse } from '../api/generated/v1/types.gen';
import { useChangeRefresh } from '../live/LiveUpdates';
import { fetchAsset, fetchOwners } from './assetBrowseApi';
import type { OwnerGroup } from './assetHierarchy';

/** Limit independent, authorized asset reads when the workspace's first 100 assets omit configured targets. */
const MAX_PARALLEL_READS = 4;

type Directory = { key: string; assets: AssetResponse[]; owners: OwnerGroup[] };

/**
 * Resolve just the visible target names from the asset-scoped API. The global workspace snapshot
 * is capped, so a missing name is not proof that the asset has no name. A failed/denied lookup
 * never produces a name, and results from a previous identity or change generation are not reused.
 */
export function useConfiguredAssetDirectory(
  targetIds: readonly string[], known: readonly AssetResponse[], canReadAssets: boolean, userId: string | null | undefined,
) {
  const [generation, setGeneration] = useState(0);
  useChangeRefresh(['assets', 'connections'], () => setGeneration(value => value + 1));
  const missing = targetIds.filter(id => !known.some(asset => asset.id === id && asset.name));
  const key = JSON.stringify([userId, canReadAssets, missing, generation]);
  const [directory, setDirectory] = useState<Directory>({ key: '', assets: [], owners: [] });

  useEffect(() => {
    if (!canReadAssets || targetIds.length === 0) return;
    const controller = new AbortController();
    const resolved: AssetResponse[] = [];
    let nextIndex = 0;
    const worker = async () => {
      while (nextIndex < missing.length && !controller.signal.aborted) {
        const id = missing[nextIndex++];
        try {
          const asset = await fetchAsset(id, controller.signal);
          if (asset?.id === id) resolved.push(asset);
        } catch { /* Missing/unauthorized/unavailable assets retain their shortened ID. */ }
      }
    };
    const owners = fetchOwners(controller.signal).catch((): OwnerGroup[] => []);
    const reads = Promise.all(Array.from({ length: Math.min(MAX_PARALLEL_READS, missing.length) }, worker));
    void Promise.all([owners, reads]).then(([visibleOwners]) => {
      if (!controller.signal.aborted) setDirectory({ key, assets: resolved, owners: visibleOwners });
    });
    return () => controller.abort();
    // The input identity key includes requested IDs and live-change generation.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);

  const current = directory.key === key && canReadAssets ? directory : null;
  return { assets: canReadAssets ? [...known, ...(current?.assets ?? [])] : [], owners: current?.owners ?? [] };
}
