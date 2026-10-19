import { useEffect, useState } from 'react';
import type { AssetResponse } from '../api/generated/v1/types.gen';
import { fetchAncestors, fetchAsset, fetchChildren, nodeAsAsset } from './assetBrowseApi';

type Context = {
  asset: AssetResponse | null;
  ancestors: AssetResponse[];
  children: AssetResponse[];
  status: 'idle' | 'loading' | 'ready' | 'missing' | 'failed';
};
const IDLE: Context = { asset: null, ancestors: [], children: [], status: 'idle' };

/**
 * Resolves one selected asset, where it sits and what is directly below it from the server, for
 * deployments whose flat asset list is incomplete. The previous asset stays in place while a
 * refresh is in flight, so a refresh never looks like the asset vanished.
 */
export function useAssetContext(
  selectedId: string | null, known: readonly AssetResponse[], enabled: boolean, refreshKey: string,
) {
  const [state, setState] = useState<Context>(IDLE);
  useEffect(() => {
    if (!enabled || !selectedId) { setState(IDLE); return; }
    const controller = new AbortController();
    setState(current => ({ ...current, status: 'loading' }));
    void (async () => {
      try {
        const asset = known.find(item => item.id === selectedId) ?? await fetchAsset(selectedId, controller.signal);
        if (controller.signal.aborted) return;
        if (!asset) { setState({ ...IDLE, status: 'missing' }); return; }
        const [ancestors, below] = await Promise.all([
          fetchAncestors(asset, controller.signal), fetchChildren(selectedId, 0, controller.signal)]);
        if (controller.signal.aborted) return;
        setState({ asset, ancestors, children: below.map(nodeAsAsset), status: 'ready' });
      } catch {
        if (!controller.signal.aborted) setState(current => ({ ...current, status: 'failed' }));
      }
    })();
    return () => controller.abort();
    // `known` is read once per selection; refreshKey re-reads it when the data really changed.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedId, enabled, refreshKey]);
  const current = state.asset?.id === selectedId ? state : { ...IDLE, status: state.status };
  return { ...current, asset: current.asset };
}
