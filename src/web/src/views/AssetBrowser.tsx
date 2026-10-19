import { useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import { BROWSE_PAGE_SIZE, SEARCH_PAGE_SIZE, fetchChildren, fetchSearch } from './assetBrowseApi';
import type { BrowseNode, SearchHit } from './assetBrowseApi';
import './assetTree.css';

const ROOT = '__root__';

type Level = { items: BrowseNode[]; hasMore: boolean; loading: boolean; failed: boolean };

/**
 * The hierarchy for deployments with more assets than one page: each level is read from the server
 * when it is opened, so a very large hierarchy never has to be loaded at once.
 */
export function AssetBrowser({
  selectedId, ancestorIds, refreshKey, onSelect,
}: {
  selectedId: string | null;
  /** Ancestors of the selected asset, top first; they are opened so the selection is visible. */
  ancestorIds: readonly string[];
  /** Changes when the underlying data did; open levels are re-read in place. */
  refreshKey: string;
  onSelect: (id: string) => void;
}) {
  const levels = useRef<Record<string, Level>>({});
  const [, redraw] = useState(0);
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set());
  const generation = useRef(0);
  const controller = useRef<AbortController>(new AbortController());

  const put = (key: string, level: Level) => { levels.current = { ...levels.current, [key]: level }; redraw(n => n + 1); };

  const load = async (key: string, append: boolean) => {
    const turn = generation.current;
    const previous = levels.current[key]?.items ?? [];
    const offset = append ? previous.length : 0;
    put(key, { items: previous, hasMore: false, loading: true, failed: false });
    try {
      const page = await fetchChildren(key === ROOT ? null : key, offset, controller.current.signal);
      if (turn !== generation.current) return;
      put(key, { items: append ? [...previous, ...page] : page, hasMore: page.length === BROWSE_PAGE_SIZE,
        loading: false, failed: false });
    } catch {
      if (turn !== generation.current) return;
      put(key, { items: previous, hasMore: false, loading: false, failed: true });
    }
  };

  useEffect(() => {
    generation.current += 1;
    controller.current.abort();
    controller.current = new AbortController();
    // Re-read the top level and every open level in place; old rows stay until the new ones arrive.
    for (const key of [ROOT, ...expanded]) void load(key, false);
    return () => controller.current.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refreshKey]);

  const revealKey = ancestorIds.join(',');
  useEffect(() => {
    if (ancestorIds.length === 0) return;
    setExpanded(current => new Set([...current, ...ancestorIds]));
    for (const id of ancestorIds) if (!levels.current[id]) void load(id, false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [revealKey]);

  const toggle = (id: string) => {
    const open = expanded.has(id);
    setExpanded(current => {
      const next = new Set(current);
      if (open) next.delete(id); else next.add(id);
      return next;
    });
    if (!open && !levels.current[id]) void load(id, false);
  };

  const renderLevel = (key: string) => {
    const level = levels.current[key];
    if (!level) return null;
    return (
      <>
        {level.items.map(node => {
          const name = node.name || t('assets.unnamed');
          const hasChildren = node.childCount > 0;
          const open = hasChildren && expanded.has(node.id);
          return (
            <li key={node.id} role="treeitem" aria-expanded={hasChildren ? open : undefined}
              aria-selected={node.id === selectedId}>
              <div className="asset-tree-row">
                {hasChildren ? (
                  <button type="button" className="asset-tree-toggle"
                    aria-label={t(open ? 'assetTree.collapse' : 'assetTree.expand', { name })}
                    onClick={() => toggle(node.id)}>{open ? '−' : '+'}</button>
                ) : <span className="asset-tree-toggle" aria-hidden="true" />}
                <button type="button" className="asset-select asset-tree-name" data-selected={node.id === selectedId}
                  aria-label={t('assetTree.open', { name })} onClick={() => onSelect(node.id)}>{name}</button>
                {hasChildren && <small>{t('assetTree.count', { count: String(node.childCount) })}</small>}
              </div>
              {open && <ul role="group">{renderLevel(node.id)}</ul>}
            </li>
          );
        })}
        {level.loading && <li role="none" className="asset-tree-status">{t('views.loading')}</li>}
        {level.failed && <li role="none" className="asset-tree-status">
          {t('assetBrowse.failed')}{' '}
          <button type="button" className="asset-select" onClick={() => void load(key, level.items.length > 0)}>
            {t('assetBrowse.retry')}</button>
        </li>}
        {level.hasMore && !level.loading && <li role="none" className="asset-tree-status">
          <button type="button" className="asset-select" onClick={() => void load(key, true)}>
            {t('assetBrowse.more')}</button>
        </li>}
      </>
    );
  };

  return (
    <nav className="asset-tree" aria-label={t('assetTree.label')}>
      <ul role="tree">{renderLevel(ROOT)}</ul>
    </nav>
  );
}

/** Matches found by the server, each with where it sits. */
export function AssetSearchResults({
  query, selectedId, typeNames, onSelect,
}: {
  query: string;
  selectedId: string | null;
  typeNames: ReadonlyMap<string | undefined, string | null | undefined>;
  onSelect: (id: string) => void;
}) {
  const [state, setState] = useState<{ hits: SearchHit[]; hasMore: boolean; loading: boolean; failed: boolean }>(
    { hits: [], hasMore: false, loading: true, failed: false });

  useEffect(() => {
    const abort = new AbortController();
    setState(current => ({ ...current, loading: true, failed: false }));
    const timer = setTimeout(() => {
      fetchSearch(query, 0, abort.signal).then(hits => {
        if (!abort.signal.aborted) setState({ hits, hasMore: hits.length === SEARCH_PAGE_SIZE, loading: false, failed: false });
      }).catch(() => {
        if (!abort.signal.aborted) setState({ hits: [], hasMore: false, loading: false, failed: true });
      });
    }, 250);
    return () => { clearTimeout(timer); abort.abort(); };
  }, [query]);

  const more = () => {
    const abort = new AbortController();
    setState(current => ({ ...current, loading: true }));
    fetchSearch(query, state.hits.length, abort.signal).then(hits => {
      setState(current => ({ hits: [...current.hits, ...hits], hasMore: hits.length === SEARCH_PAGE_SIZE,
        loading: false, failed: false }));
    }).catch(() => setState(current => ({ ...current, loading: false, failed: true })));
  };

  return (
    <>
      <ul className="split-results" aria-label={t('assets.results')}>
        {state.hits.map(hit => {
          const path = hit.path.map(step => step.name).join(' › ');
          return (
            <li key={hit.id} data-selected={hit.id === selectedId}>
              <button type="button" className="asset-select"
                aria-current={hit.id === selectedId ? 'true' : undefined}
                aria-label={t('assets.selectNamed', { name: hit.name })}
                onClick={() => onSelect(hit.id)}>{hit.name}</button>
              <small>{typeNames.get(hit.assetTypeId) ?? ''}{path ? ' · ' + path : ''}</small>
            </li>
          );
        })}
      </ul>
      {state.loading && <p className="split-note">{t('views.loading')}</p>}
      {state.failed && <p className="split-note" role="alert">{t('assetBrowse.failed')}</p>}
      {!state.loading && !state.failed && state.hits.length === 0 && <p className="split-note">{t('assets.noMatches')}</p>}
      {state.hasMore && !state.loading && <p className="split-note">
        <button type="button" className="asset-select" onClick={more}>{t('assetBrowse.more')}</button></p>}
    </>
  );
}
