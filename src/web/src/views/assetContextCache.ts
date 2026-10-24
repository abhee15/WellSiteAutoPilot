import type { AssetResponse } from '../api/generated/v1/types.gen';
import { fetchAsset, fetchChildren } from './assetBrowseApi';
import type { BrowseNode } from './assetBrowseApi';

/** Matches the page's slow safety-net refresh, so nothing kept is older than one of those. */
export const CONTEXT_CACHE_TTL_MS = 60_000;

type Entry<T> = { at: number; value: T };

/**
 * Remembers what the Assets page recently read about the selected asset, where it sits and what is
 * directly below it, so choosing another asset in the same area does not read the shared ancestors
 * again. An imported hierarchy rarely changes, but it can: `clear` drops everything (the Server's
 * "assets changed" signal and any change the page notices call it), and an entry expires after
 * `ttlMs` regardless. Only found assets and successful reads are kept, never a miss or a failure,
 * and a read that began before a `clear` is not kept.
 */
export function createAssetContextCache(now: () => number = Date.now, ttlMs: number = CONTEXT_CACHE_TTL_MS) {
  let generation = 0;
  const assets = new Map<string, Entry<AssetResponse>>();
  const children = new Map<string, Entry<BrowseNode[]>>();
  const usable = <T>(entry: Entry<T> | undefined): entry is Entry<T> => !!entry && now() - entry.at < ttlMs;
  return {
    clear(): void {
      generation += 1;
      assets.clear();
      children.clear();
    },
    async asset(id: string, signal: AbortSignal): Promise<AssetResponse | null> {
      const kept = assets.get(id);
      if (usable(kept)) return kept.value;
      const turn = generation;
      const value = await fetchAsset(id, signal);
      if (value && turn === generation) assets.set(id, { at: now(), value });
      return value;
    },
    /** The first page of an asset's children, which is all the asset page uses. */
    async children(id: string, signal: AbortSignal): Promise<BrowseNode[]> {
      const kept = children.get(id);
      if (usable(kept)) return kept.value;
      const turn = generation;
      const value = await fetchChildren(id, 0, signal);
      if (turn === generation) children.set(id, { at: now(), value });
      return value;
    },
  };
}

export type AssetContextCache = ReturnType<typeof createAssetContextCache>;
