import { useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import { VIEW_PAGE_SIZE, loadViewNodes } from './assetViewsApi';
import type { ViewAsset, ViewGroup } from './assetViewsApi';
import './assetTree.css';

const SEPARATOR = '\u0001';
type Level = { groups: ViewGroup[]; assets: ViewAsset[]; hasMore: boolean; loading: boolean; failed: boolean };
const EMPTY: Level = { groups: [], assets: [], hasMore: false, loading: true, failed: false };

/**
 * The Assets of one View, opened one level at a time from the server, so a very large View never has to be
 * loaded at once. The View only arranges Assets; selecting one opens the same asset page as the default hierarchy.
 */
export function AssetViewBrowser({
  viewId, selectedId, onSelect, translateGroup,
}: {
  viewId: string;
  selectedId: string | null;
  onSelect: (id: string) => void;
  translateGroup?: (group: ViewGroup) => string;
}) {
  const [levels, setLevels] = useState<Record<string, Level>>({});
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set());
  const controller = useRef(new AbortController());

  const load = (path: string[], append: boolean) => {
    const key = path.join(SEPARATOR);
    const previous = levels[key];
    const offset = append && previous ? previous.groups.length + previous.assets.length : 0;
    setLevels(current => ({ ...current, [key]: { ...(append && previous ? previous : EMPTY), loading: true, failed: false } }));
    loadViewNodes(viewId, path, offset, controller.current.signal).then(nodes => {
      setLevels(current => {
        const base = append ? current[key] ?? EMPTY : EMPTY;
        const groups = append ? [...base.groups, ...nodes.groups] : nodes.groups;
        const assets = append ? [...base.assets, ...nodes.assets] : nodes.assets;
        return { ...current, [key]: { groups, assets, hasMore: nodes.groups.length + nodes.assets.length === VIEW_PAGE_SIZE,
          loading: false, failed: false } };
      });
    }).catch(() => {
      if (controller.current.signal.aborted) return;
      setLevels(current => ({ ...current, [key]: { ...(current[key] ?? EMPTY), loading: false, failed: true } }));
    });
  };

  useEffect(() => {
    controller.current = new AbortController();
    load([], false);
    return () => controller.current.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [viewId]);

  const toggle = (path: string[]) => {
    const key = path.join(SEPARATOR);
    const isOpen = open.has(key);
    setOpen(current => {
      const next = new Set(current);
      if (isOpen) next.delete(key); else next.add(key);
      return next;
    });
    if (!isOpen && !levels[key]) load(path, false);
  };

  const renderLevel = (path: string[]) => {
    const key = path.join(SEPARATOR);
    const level = levels[key];
    if (!level) return null;
    return (
      <>
        {level.groups.map(group => {
          const childPath = [...path, group.key];
          const childKey = childPath.join(SEPARATOR);
          const expanded = open.has(childKey);
          const label = translateGroup ? translateGroup(group) : group.label;
          return (
            <li key={group.key} role="treeitem" aria-expanded={expanded}>
              <div className="asset-tree-row">
                <button type="button" className="asset-tree-toggle"
                  aria-label={t(expanded ? 'assetTree.collapse' : 'assetTree.expand', { name: label })}
                  onClick={() => toggle(childPath)}>{expanded ? '−' : '+'}</button>
                <button type="button" className="asset-select asset-tree-name" onClick={() => toggle(childPath)}
                  aria-label={t('assetTree.open', { name: label })}>{label}</button>
                <small>{t('assetOwner.count', { count: String(group.count) })}</small>
              </div>
              {expanded && <ul role="group">{renderLevel(childPath)}</ul>}
            </li>
          );
        })}
        {level.assets.map(asset => (
          <li key={asset.id} role="treeitem" aria-selected={asset.id === selectedId}>
            <div className="asset-tree-row">
              <span className="asset-tree-toggle" aria-hidden="true" />
              <button type="button" className="asset-select asset-tree-name" data-selected={asset.id === selectedId}
                aria-label={t('assetTree.open', { name: asset.name })} onClick={() => onSelect(asset.id)}>{asset.name}</button>
            </div>
          </li>
        ))}
        {level.loading && <li role="none" className="asset-tree-status">{t('views.loading')}</li>}
        {level.failed && <li role="none" className="asset-tree-status">
          {t('assetBrowse.failed')}{' '}
          <button type="button" className="asset-select" onClick={() => load(path, level.groups.length + level.assets.length > 0)}>
            {t('assetBrowse.retry')}</button>
        </li>}
        {level.hasMore && !level.loading && <li role="none" className="asset-tree-status">
          <button type="button" className="asset-select" onClick={() => load(path, true)}>{t('assetBrowse.more')}</button>
        </li>}
        {!level.loading && !level.failed && level.groups.length === 0 && level.assets.length === 0 &&
          <li role="none" className="asset-tree-status">{t('assetViews.empty')}</li>}
      </>
    );
  };

  return (
    <nav className="asset-tree" aria-label={t('assetViews.treeLabel')}>
      <ul role="tree">{renderLevel([])}</ul>
    </nav>
  );
}
