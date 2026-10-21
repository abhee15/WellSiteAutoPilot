import { useMemo, useState } from 'react';
import type { AssetResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { ancestorIds, buildAssetTree, groupRootsByOwner } from './assetHierarchy';
import type { AssetTreeNode, OwnerGroup } from './assetHierarchy';
import { AssetOwnerRow, ownerKey } from './AssetOwnerRow';
import { AssetNodeIcon, assetNodeKind } from './AssetNodeIcon';
import './assetTree.css';

export function AssetTree({
  assets,
  selectedId,
  onSelect,
  owners = [],
  canOpenConnection = false,
  typeNames,
}: {
  assets: AssetResponse[];
  selectedId: string | null | undefined;
  onSelect: (id: string) => void;
  /** Connections that own assets; when any do, the top level is grouped under them. */
  owners?: readonly OwnerGroup[];
  canOpenConnection?: boolean;
  typeNames?: ReadonlyMap<string | undefined, string | null | undefined>;
}) {
  const tree = useMemo(() => buildAssetTree(assets), [assets]);
  const grouped = useMemo(() => groupRootsByOwner(tree, owners), [tree, owners]);
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(new Set());
  const forcedOpen = useMemo(() => ancestorIds(assets, selectedId), [assets, selectedId]);
  const toggle = (id: string) => setCollapsed(current => {
    const next = new Set(current);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });
  const renderNode = (node: AssetTreeNode) => {
    const id = node.asset.id as string;
    const name = node.asset.name ?? t('assets.unnamed');
    const hasChildren = node.children.length > 0;
    const expanded = hasChildren && (forcedOpen.has(id) || !collapsed.has(id));
    return (
      <li key={id} role="treeitem" aria-expanded={hasChildren ? expanded : undefined}
        aria-selected={id === selectedId}>
        <div className="asset-tree-row">
          {hasChildren ? (
            <button type="button" className="asset-tree-toggle"
              aria-label={t(expanded ? 'assetTree.collapse' : 'assetTree.expand', { name })}
              onClick={() => toggle(id)}>{expanded ? '−' : '+'}</button>
          ) : <span className="asset-tree-toggle" aria-hidden="true" />}
          <button type="button" className="asset-select asset-tree-name" data-selected={id === selectedId}
            aria-label={t('assetTree.open', { name })} onClick={() => onSelect(id)}>
            <AssetNodeIcon kind={assetNodeKind(hasChildren, typeNames?.get(node.asset.assetTypeId))} />
            {name}
          </button>
          {hasChildren && <small>{t('assetTree.count', { count: String(node.children.length) })}</small>}
        </div>
        {expanded && <ul role="group">{node.children.map(renderNode)}</ul>}
      </li>
    );
  };
  if (tree.length === 0) return null;
  // The Connection that owns the selection stays open so the selection is visible.
  const selectedRoot = grouped?.find(group => group.roots.some(root => selectedIn(root, selectedId)));
  return (
    <nav className="asset-tree" aria-label={t('assetTree.label')}>
      <ul role="tree">{grouped ? grouped.map(group => {
        const key = ownerKey(group.owner.connectionId);
        const expanded = !collapsed.has(key) || selectedRoot === group;
        return (
          <li key={key} role="treeitem" aria-expanded={expanded}>
            <AssetOwnerRow owner={group.owner} expanded={expanded} canOpenConnection={canOpenConnection}
              onToggle={() => toggle(key)} />
            {expanded && <ul role="group">{group.roots.map(renderNode)}</ul>}
          </li>
        );
      }) : tree.map(renderNode)}</ul>
    </nav>
  );
}

function selectedIn(node: AssetTreeNode, id: string | null | undefined): boolean {
  return !!id && (node.asset.id === id || node.children.some(child => selectedIn(child, id)));
}
