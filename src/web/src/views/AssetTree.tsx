import { useMemo, useState } from 'react';
import type { AssetResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { ancestorIds, buildAssetTree } from './assetHierarchy';
import type { AssetTreeNode } from './assetHierarchy';
import './assetTree.css';

export function AssetTree({
  assets,
  selectedId,
  onSelect,
}: {
  assets: AssetResponse[];
  selectedId: string | null | undefined;
  onSelect: (id: string) => void;
}) {
  const tree = useMemo(() => buildAssetTree(assets), [assets]);
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
            {name}
          </button>
          {hasChildren && <small>{t('assetTree.count', { count: String(node.children.length) })}</small>}
        </div>
        {expanded && <ul role="group">{node.children.map(renderNode)}</ul>}
      </li>
    );
  };
  if (tree.length === 0) return null;
  return (
    <nav className="asset-tree" aria-label={t('assetTree.label')}>
      <ul role="tree">{tree.map(renderNode)}</ul>
    </nav>
  );
}
