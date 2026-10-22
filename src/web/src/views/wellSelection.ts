import { BROWSE_PAGE_SIZE, fetchChildren } from './assetBrowseApi';
import type { BrowseNode } from './assetBrowseApi';

/** Resolve an explicit selection across every page and unopened level; apply it only after the entire operation succeeds. */
export async function wellsUnder(connectionId: string, rootId: string | null, wellTypes: ReadonlySet<string>, signal: AbortSignal): Promise<BrowseNode[]> {
  const queue: (string | null)[] = [rootId];
  const visited = new Set<string | null>();
  const wells = new Map<string, BrowseNode>();
  for (let index = 0; index < queue.length; index++) {
    const parent = queue[index];
    if (visited.has(parent)) continue;
    visited.add(parent);
    for (let offset = 0; ; offset += BROWSE_PAGE_SIZE) {
      const page = await fetchChildren(parent, offset, signal, { connectionId });
      if (signal.aborted) throw signal.reason;
      for (const node of page) {
        if (node.ownerConnectionId !== connectionId) throw new Error('Foreign selection');
        if (node.isActive && wellTypes.has(node.assetTypeId)) wells.set(node.id, node);
        if (node.childCount > 0) queue.push(node.id);
      }
      if (wells.size > 50_000 || visited.size + queue.length > 200_000) throw new Error('Selection limit');
      if (page.length < BROWSE_PAGE_SIZE) break;
    }
  }
  return [...wells.values()];
}
