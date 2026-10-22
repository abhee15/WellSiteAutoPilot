import { useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import { formatTimestamp } from '../format';
import { ConnectionsError, discoveryKey, searchDiscovered } from './connections';
import type { Connection, Discovery, DiscoveredObject, ImportSelection } from './connections';

const pageSize = 100;
const importLimit = 5000;
const scopeFor = (found: Discovery, query: string, facets: Record<string, string>): ImportSelection => ({
  snapshotId: found.snapshotId, query: query.trim(), facets: Object.fromEntries(Object.entries(facets).filter(([, value]) => value).map(([key, value]) => [key, [value]])),
});
/** Browses the full kept inventory. An explicit selection survives paging and filtering. */
export function ConnectionInventory({ connection, found, stale, disabled = false, onSelect }: {
  connection: Connection; found: Discovery; stale: boolean; disabled?: boolean;
  onSelect: (selection: ImportSelection | null, count: number) => void;
}) {
  const [query, setQuery] = useState('');
  const [facets, setFacets] = useState<Record<string, string>>({});
  const [applied, setApplied] = useState({ query: '', facets: {} as Record<string, string> });
  const [items, setItems] = useState(found.items.slice(0, pageSize));
  const [count, setCount] = useState(found.totalCount);
  const [offset, setOffset] = useState(0);
  const [mode, setMode] = useState<'matching' | 'individual'>('matching');
  const [selected, setSelected] = useState(new Map<string, DiscoveredObject>());
  const [searching, setSearching] = useState(false);
  const [capacityError, setCapacityError] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const controller = useRef<AbortController | null>(null);
  useEffect(() => () => controller.current?.abort(), []);
  const dirty = query.trim() !== applied.query || JSON.stringify(facets) !== JSON.stringify(applied.facets);
  const blocked = disabled || stale || searching;
  const device = found.facets.find(facet => facet.key === 'deviceType');
  const groups = new Map<string, DiscoveredObject[]>();
  for (const item of items) {
    const group = item.metadata.assetName || t('inventory.noAsset');
    groups.set(group, [...(groups.get(group) ?? []), item]);
  }
  const explicit = (selection: Map<string, DiscoveredObject>): ImportSelection => ({ snapshotId: found.snapshotId, query: '', facets: {},
    objects: [...selection.values()].map(({ kind, externalId }) => ({ kind, externalId })) });

  function publish(nextMode: typeof mode, next: Map<string, DiscoveredObject>, scope: ImportSelection, total: number) {
    onSelect(nextMode === 'individual' ? explicit(next) : scope, nextMode === 'individual' ? next.size : total);
  }
  async function search(nextQuery: string, nextFacets: Record<string, string>, nextOffset = 0) {
    controller.current?.abort(); const request = new AbortController(); controller.current = request;
    setSearching(true); setProblem(null); onSelect(null, 0);
    const scope = scopeFor(found, nextQuery, nextFacets);
    try {
      const result = await searchDiscovered(connection, scope, undefined, { offset: nextOffset, limit: pageSize, signal: request.signal });
      if (request.signal.aborted) return;
      if (result.items.length > pageSize || (nextOffset < result.matchCount && result.items.length === 0)) throw new ConnectionsError('invalidResponse');
      setItems(result.items); setCount(result.matchCount); setOffset(nextOffset);
      setApplied({ query: nextQuery.trim(), facets: nextFacets });
      publish(mode, selected, scope, result.matchCount);
    } catch (error) {
      if (!request.signal.aborted) setProblem(t(error instanceof ConnectionsError && error.kind === 'snapshotExpired' ? 'connections.errSnapshotExpired' : 'inventory.searchFailed'));
    } finally { if (!request.signal.aborted) setSearching(false); }
  }
  function choose(nextMode: typeof mode) {
    setMode(nextMode);
    if (dirty || problem) { onSelect(null, 0); return; }
    publish(nextMode, selected, scopeFor(found, query, facets), count);
  }
  function select(next: Map<string, DiscoveredObject>) {
    if (next.size > importLimit) { setCapacityError(true); return; }
    setCapacityError(false);
    setSelected(next);
    if (dirty || problem) onSelect(null, 0);
    else publish('individual', next, scopeFor(found, query, facets), count);
  }
  return <section className="connection-found" aria-label={t('connections.findHeading')}>
    <p className="connection-step">{t('connections.stageDiscover')}</p>
    <h3>{t('inventory.heading')}</h3>
    {stale && <p role="status" className="connection-help">{t('connections.findStale')}</p>}
    <p className="connection-help">{t('connections.findKeptUntil', { time: formatTimestamp(found.expiresAtUtc) })}</p>
    {found.actingAs && <p className="connection-help">{t('connections.findActingAs', { account: found.actingAs })}</p>}
    {!found.complete && <p role="alert" className="status-error">{t('connections.findIncomplete')}</p>}
    {found.warnings.map(warning => <p key={warning} role="status" className="connection-help">{warning}</p>)}
    {found.complete && <dl className="inventory-summary">
      <div><dt>{t('inventory.totalWells')}</dt><dd>{found.totalCount}</dd></div>
      {found.sourceAssetCount != null && <div><dt>{t('inventory.sourceAssets')}</dt><dd>{found.sourceAssetCount}</dd></div>}
      {device && <div><dt>{t('inventory.deviceCoverage')}</dt><dd>{device.knownCount == null ? t('inventory.deviceUnreported')
        : t('inventory.deviceKnown', { known: device.knownCount, unknown: found.totalCount - device.knownCount })}</dd></div>}
    </dl>}
    {found.complete && found.totalCount === 0 && <p className="empty-state">{t('connections.findEmpty')}</p>}
    {found.complete && found.totalCount > 0 && <>
      <fieldset disabled={blocked} className="inventory-controls">
        <legend>{t('inventory.selectionHeading')}</legend>
        <div className="inventory-modes">
          <label><input type="radio" name={'selection-' + connection.id} checked={mode === 'matching'} onChange={() => choose('matching')} />{t('inventory.allMatching')}</label>
          <label><input type="radio" name={'selection-' + connection.id} checked={mode === 'individual'} onChange={() => choose('individual')} />{t('inventory.individual')}</label>
        </div>
        <p className="connection-help">{t('inventory.selectionHelp')}</p>
        <details className="inventory-filters"><summary>{t('inventory.filters')}</summary>
          <div className="connection-facets">{found.facets.map(facet => <label key={facet.key}>
            <span>{facet.label}</span><select value={facets[facet.key] ?? ''} onChange={event => {
              const next = { ...facets, [facet.key]: event.target.value }; setFacets(next); void search(query, next);
            }}><option value="">{t('connections.findFacetAny')}</option>
              {facet.values.map(value => <option key={value.value} value={value.value}>{t('connections.findFacetOption', value)}</option>)}
            </select>{facet.hasMore && <small className="connection-help">{t('connections.findFacetMore')}</small>}
          </label>)}</div>
        </details>
        <div className="connection-control-row">
          <label><span>{t('connections.findSearch')}</span><input type="search" value={query} maxLength={100}
            onChange={event => { setQuery(event.target.value); onSelect(null, 0); }} onKeyDown={event => {
              if (event.key === 'Enter') { event.preventDefault(); void search(query, facets); }
            }} /></label>
          <button type="button" className="secondary-button" onClick={() => void search(query, facets)}>{t('connections.findSearchButton')}</button>
        </div>
        {dirty && <p role="status" className="connection-help">{t('inventory.applySearch')}</p>}
        {mode === 'individual' && <div className="connection-actions">
          <strong>{t('inventory.selected', { count: selected.size })}</strong>
          <button type="button" onClick={() => select(new Map([...selected, ...items.map(item => [discoveryKey(item), item] as const)]))}>{t('inventory.selectPage')}</button>
          <button type="button" onClick={() => select(new Map())}>{t('inventory.clear')}</button>
        </div>}
        {count === 0 && <p>{t('connections.findNone')}</p>}
        {count > 0 && <p role="status">{t('inventory.page', { from: offset + 1, to: offset + items.length, count })}</p>}
        <div className="inventory-groups">{[...groups].map(([name, wells]) => <details key={name} open>
          <summary>{t('inventory.groupPage', { name, count: wells.length })}</summary>
          <ul className="connection-found-list">{wells.map(item => <li key={discoveryKey(item)}>
            <label className="inventory-well">{mode === 'individual' && <input type="checkbox" checked={selected.has(discoveryKey(item))}
              aria-label={t('inventory.selectWell', { name: item.displayName })} aria-describedby={found.snapshotId + '-' + encodeURIComponent(discoveryKey(item))} onChange={event => {
                const next = new Map(selected); if (event.target.checked) next.set(discoveryKey(item), item); else next.delete(discoveryKey(item)); select(next);
              }} />}<span><strong>{item.displayName}</strong><small id={found.snapshotId + '-' + encodeURIComponent(discoveryKey(item))}>{t('inventory.sourceId', { id: item.externalId })}</small><small>{[item.metadata.wellType, item.metadata.deviceType || t('inventory.unknownDevice'), item.metadata.field, item.metadata.lease].filter(Boolean).join(' · ')}</small></span></label>
          </li>)}</ul>
        </details>)}</div>
        <nav className="connection-actions" aria-label={t('inventory.paging')}>
          <button type="button" disabled={offset === 0} onClick={() => void search(query, facets, Math.max(0, offset - pageSize))}>{t('inventory.previous')}</button>
          <button type="button" disabled={offset + items.length >= count} onClick={() => void search(query, facets, offset + pageSize)}>{t('inventory.next')}</button>
        </nav>
      </fieldset>
      {searching && <p role="status">{t('views.loading')}</p>}
      {capacityError && <p role="alert" className="status-error">{t('inventory.tooMany')}</p>}
      {problem && <p role="alert" className="status-error">{problem}</p>}
    </>}
  </section>;
}
