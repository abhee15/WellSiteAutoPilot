import { Box, Droplet, Folder, Layers, Plug } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';

/** What a row in the Assets tree is, for choosing its icon. Icons help scanning; the row's text always names it. */
export type AssetNodeKind = 'connection' | 'standalone' | 'group' | 'well' | 'asset';

const ICONS: Record<AssetNodeKind, LucideIcon> = {
  connection: Plug,
  standalone: Layers,
  group: Folder,
  well: Droplet,
  asset: Box,
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
  const Icon = ICONS[kind];
  return <Icon className="asset-node-icon" data-kind={kind} size={16} strokeWidth={1.75} aria-hidden="true" focusable="false" />;
}
