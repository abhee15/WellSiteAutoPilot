import { memo, useCallback, useMemo, useState } from 'react';
import type { AssetResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { ancestorIds, buildAssetTree, groupRootsByOwner } from './assetHierarchy';
import type { AssetTreeNode, OwnerGroup } from './assetHierarchy';
import { AssetOwnerRow, ownerKey } from './AssetOwnerRow';
import { AssetNodeIcon, assetNodeKind } from './AssetNodeIcon';
import './assetTree.css';
import { Disclosure } from '../components/Chevron';

type TypeNames = ReadonlyMap<string | undefined, string | null | undefined>;
const NONE: ReadonlySet<string> = new Set();

/**
 * What a row is told about the selection. Only a row on the path to the selected asset (or the selected
 * asset itself) receives it; every other row gets the same empty values each time, so choosing another
 * asset redraws just the rows on the old and new paths and not the whole tree.
 */
function pathProps(node: AssetTreeNode, selectedId: string | null, forcedOpen: ReadonlySet<string>) {
  const id = node.asset.id as string;
  return selectedId !== null && (id === selectedId || forcedOpen.has(id))
    ? { selectedId, forcedOpen }
    : { selectedId: null, forcedOpen: NONE };
}

/**
 * The inner function must not share the memoized row's name: a function expression's own name is bound inside
 * its body, so a child written as <AssetTreeRow> would be the raw function and escape the memo.
 */
function AssetTreeRowView({
  node, selectedId, forcedOpen, collapsed, onToggle, onSelect, typeNames,
}: {
  node: AssetTreeNode;
  /** The selected asset, or null when it is not this row or below it. */
  selectedId: string | null;
  /** Ancestors of the selected asset, or empty when the selection is not below this row. */
  forcedOpen: ReadonlySet<string>;
  collapsed: ReadonlySet<string>;
  onToggle: (id: string) => void;
  onSelect: (id: string) => void;
  typeNames?: TypeNames;
}) {
  const id = node.asset.id as string;
  const name = node.asset.name ?? t('assets.unnamed');
  const hasChildren = node.children.length > 0;
  const expanded = hasChildren && (forcedOpen.has(id) || !collapsed.has(id));
  return (
    <li role="treeitem" aria-expanded={hasChildren ? expanded : undefined}
      aria-selected={id === selectedId}>
      <div className="asset-tree-row">
        {hasChildren ? (
          <button type="button" className="asset-tree-toggle"
            aria-label={t(expanded ? 'assetTree.collapse' : 'assetTree.expand', { name })}
            onClick={() => onToggle(id)}><Disclosure open={expanded} /></button>
        ) : <span className="asset-tree-toggle" aria-hidden="true" />}
        <button type="button" className="asset-select asset-tree-name" data-selected={id === selectedId}
          aria-label={t('assetTree.open', { name })} onClick={() => onSelect(id)}>
          <AssetNodeIcon kind={assetNodeKind(hasChildren, typeNames?.get(node.asset.assetTypeId))} />
          {name}
        </button>
        {hasChildren && <small>{t('assetTree.count', { count: String(node.children.length) })}</small>}
      </div>
      {expanded && <ul role="group">{node.children.map(child => (
        <AssetTreeRow key={child.asset.id} node={child} {...pathProps(child, selectedId, forcedOpen)}
          collapsed={collapsed} onToggle={onToggle} onSelect={onSelect} typeNames={typeNames} />
      ))}</ul>}
    </li>
  );
}

const AssetTreeRow = memo(AssetTreeRowView);

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
  typeNames?: TypeNames;
}) {
  const tree = useMemo(() => buildAssetTree(assets), [assets]);
  const grouped = useMemo(() => groupRootsByOwner(tree, owners), [tree, owners]);
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(new Set());
  const forcedOpen = useMemo(() => ancestorIds(assets, selectedId), [assets, selectedId]);
  const toggle = useCallback((id: string) => setCollapsed(current => {
    const next = new Set(current);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  }), []);
  const selected = selectedId ?? null;
  const renderNode = (node: AssetTreeNode) => (
    <AssetTreeRow key={node.asset.id} node={node} {...pathProps(node, selected, forcedOpen)}
      collapsed={collapsed} onToggle={toggle} onSelect={onSelect} typeNames={typeNames} />
  );
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
