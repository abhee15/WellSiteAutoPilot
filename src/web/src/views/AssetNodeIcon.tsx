/** What a row in the Assets tree is, for choosing its icon. Icons help scanning; the row's text always names it. */
export type AssetNodeKind = 'connection' | 'standalone' | 'group' | 'well' | 'asset';

const PATHS: Record<AssetNodeKind, string> = {
  connection: 'M9 7V3M15 7V3M7 7h10v4a5 5 0 0 1-10 0zM12 16v5',
  standalone: 'M12 3l9 5-9 5-9-5zM3 13l9 5 9-5M3 17l9 5 9-5',
  group: 'M3 6a1 1 0 0 1 1-1h5l2 2h9a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z',
  well: 'M12 3c3 4 6 7 6 11a6 6 0 0 1-12 0c0-4 3-7 6-11z',
  asset: 'M4 8l8-4 8 4v8l-8 4-8-4zM4 8l8 4 8-4M12 12v8',
};

/**
 * The kind of an Asset row. Anything with Assets below it reads as a group; otherwise the Asset type's own name or
 * key decides between a well and a generic Asset, so no vendor-specific type is wired into the tree.
 */
export function assetNodeKind(hasChildren: boolean, typeLabel: string | null | undefined): AssetNodeKind {
  if (hasChildren) return 'group';
  return typeLabel && typeLabel.toLowerCase().includes('well') ? 'well' : 'asset';
}

export function AssetNodeIcon({ kind }: { kind: AssetNodeKind }) {
  return (
    <svg className="asset-node-icon" data-kind={kind} viewBox="0 0 24 24" width="16" height="16" fill="none"
      stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <path d={PATHS[kind]} />
    </svg>
  );
}
