import { SearchInput } from '../components/SearchInput';
import { ActionButton } from '../components/ActionButton';
import { CheckboxField, RadioGroup } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import { formatTimestamp } from '../format';
import { ConnectionsError, discoveryKey, searchDiscovered } from './connections';
import type { Connection, Discovery, DiscoveryFacet, DiscoveredObject, ImportSelection } from './connections';
import { LoadingIndicator } from '../components/LoadingIndicator';

const pageSize = 100;
const importLimit = 5000;
const scopeFor = (found: Discovery, query: string, facets: Record<string, string[]>): ImportSelection => ({
  snapshotId: found.snapshotId, query: query.trim(), facets: Object.fromEntries(Object.entries(facets).filter(([, values]) => values.length > 0)),
});
/** Browses the full kept inventory. An explicit selection survives paging and filtering. */
export function ConnectionInventory({ connection, found, stale, disabled = false, onSelect }: {
  connection: Connection; found: Discovery; stale: boolean; disabled?: boolean;
  onSelect: (selection: ImportSelection | null, count: number) => void;
}) {
  const [query, setQuery] = useState('');
  const [facets, setFacets] = useState<Record<string, string[]>>({});
  const [needsApply, setNeedsApply] = useState(false);
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
  const dirty = needsApply;
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
  async function search(nextQuery: string, nextFacets: Record<string, string[]>, nextOffset = 0) {
    controller.current?.abort(); const request = new AbortController(); controller.current = request;
    setSearching(true); setProblem(null); onSelect(null, 0);
    const scope = scopeFor(found, nextQuery, nextFacets);
    try {
      const result = await searchDiscovered(connection, scope, undefined, { offset: nextOffset, limit: pageSize, signal: request.signal });
      if (request.signal.aborted) return;
      if (result.items.length > pageSize || (nextOffset < result.matchCount && result.items.length === 0)) throw new ConnectionsError('invalidResponse');
      setItems(result.items); setCount(result.matchCount); setOffset(nextOffset);
      setNeedsApply(false);
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
    {stale && <Notice tone="warning">{t('connections.findStale')}</Notice>}
    <p className="connection-help">{t('connections.findKeptUntil', { time: formatTimestamp(found.expiresAtUtc) })}</p>
    {found.actingAs && <p className="connection-help">{t('connections.findActingAs', { account: found.actingAs })}</p>}
    {!found.complete && <Notice tone="error">{t('connections.findIncomplete')}</Notice>}
    {found.warnings.map(warning => <Notice key={warning} tone="warning">{warning}</Notice>)}
    {found.complete && <dl className="inventory-summary">
      <div><dt>{t('inventory.totalWells')}</dt><dd>{found.totalCount}</dd></div>
      {found.sourceAssetCount != null && <div><dt>{t('inventory.sourceAssets')}</dt><dd>{found.sourceAssetCount}</dd></div>}
      {device && <div><dt>{t('inventory.deviceCoverage')}</dt><dd>{device.knownCount == null ? t('inventory.deviceUnreported')
        : t('inventory.deviceKnown', { known: device.knownCount, unknown: found.totalCount - device.knownCount })}</dd></div>}
    </dl>}
    {found.complete && found.totalCount === 0 && <p className="empty-state">{t('connections.findEmpty')}</p>}
    {found.complete && found.totalCount > 0 && <>
      <fieldset disabled={blocked} className="inventory-controls">
        <RadioGroup label={t('inventory.selectionHeading')} helper={t('inventory.selectionHelp')} name={'selection-' + connection.id} value={mode}
          options={[{ value: 'matching', label: t('inventory.allMatching') }, { value: 'individual', label: t('inventory.individual') }]}
          onChange={value => choose(value === 'individual' ? 'individual' : 'matching')} />
        <details className="inventory-filters"><summary>{t('inventory.filters')}</summary>
          <p className="connection-help">{t('inventory.filterHelp')}</p>
          <div className="connection-facets">{found.facets.map(facet => <InventoryFacet key={facet.key} facet={facet}
            selected={facets[facet.key] ?? []} onChange={values => {
              setFacets(current => ({ ...current, [facet.key]: values })); setNeedsApply(true); onSelect(null, 0);
            }} />)}</div>
          {found.facets.length === 0 && <p className="connection-help">{t('inventory.noFacets')}</p>}
          <ActionButton label={t('inventory.clearFilters')} disabled={Object.values(facets).every(values => values.length === 0)} onClick={() => { setFacets({}); setNeedsApply(true); onSelect(null, 0); }} />
        </details>
        <div className="connection-control-row">
          <label><span>{t('connections.findSearch')}</span><SearchInput value={query} maxLength={100} placeholder={t('connections.findSearchPlaceholder')}
            onChange={event => { setQuery(event.target.value); setNeedsApply(true); onSelect(null, 0); }} onKeyDown={event => {
              if (event.key === 'Enter') { event.preventDefault(); void search(query, facets); }
            }} /></label>
          <ActionButton label={t('inventory.applyFilters')} onClick={() => void search(query, facets)} />
        </div>
        {dirty && <Notice>{t('inventory.applySearch')}</Notice>}
        {mode === 'individual' && <div className="connection-actions">
          <strong>{t('inventory.selected', { count: selected.size })}</strong>
          <ActionButton label={t('inventory.selectPage')} disabled={dirty || problem !== null} onClick={() => select(new Map([...selected, ...items.map(item => [discoveryKey(item), item] as const)]))} />
          <ActionButton label={t('inventory.clear')} disabled={dirty || problem !== null} onClick={() => select(new Map())} />
        </div>}
        {!dirty && !problem && !searching && <>
          {count === 0 && <p>{t('connections.findNone')}</p>}
          {count > 0 && <Notice>{t('inventory.page', { from: offset + 1, to: offset + items.length, count })}</Notice>}
          <div className="inventory-groups">{[...groups].map(([name, wells]) => <details key={name} open>
            <summary>{t('inventory.groupPage', { name, count: wells.length })}</summary>
            <ul className="connection-found-list">{wells.map(item => <li key={discoveryKey(item)}>
              <div className="inventory-well">
                {mode === 'individual'
                  ? <CheckboxField label={item.displayName} helper={t('inventory.sourceId', { id: item.externalId })} aria-label={t('inventory.selectWell', { name: item.displayName })}
                    checked={selected.has(discoveryKey(item))} onChange={event => {
                      const next = new Map(selected); if (event.target.checked) next.set(discoveryKey(item), item); else next.delete(discoveryKey(item)); select(next);
                    }} />
                  : <span><strong>{item.displayName}</strong><small>{t('inventory.sourceId', { id: item.externalId })}</small></span>}
                <small>{[item.metadata.wellType, item.metadata.deviceType || t('inventory.unknownDevice'), item.metadata.field, item.metadata.lease].filter(Boolean).join(' · ')}</small>
              </div>
            </li>)}</ul>
          </details>)}</div>
          <nav className="connection-actions" aria-label={t('inventory.paging')}>
            <ActionButton label={t('inventory.previous')} disabled={offset === 0} onClick={() => void search(query, facets, Math.max(0, offset - pageSize))} />
            <ActionButton label={t('inventory.next')} disabled={offset + items.length >= count} onClick={() => void search(query, facets, offset + pageSize)} />
          </nav>
        </>}
      </fieldset>
      {searching && <LoadingIndicator size="small" label={t('views.loading')} />}
      {capacityError && <Notice tone="error">{t('inventory.tooMany')}</Notice>}
      {problem && <Notice tone="error">{problem}</Notice>}
    </>}
  </section>;
}

/** Values refer to the complete discovery, not just the current page or other filters. */
function InventoryFacet({ facet, selected, onChange }: {
  facet: DiscoveryFacet; selected: string[]; onChange: (values: string[]) => void;
}) {
  const [valueQuery, setValueQuery] = useState('');
  const values = facet.values.filter(value => value.value.toLocaleLowerCase().includes(valueQuery.trim().toLocaleLowerCase()))
    .sort((a, b) => a.value.localeCompare(b.value));
  return <details className="inventory-facet">
    <summary>{facet.label}<span>{selected.length === 0 ? t('connections.findFacetAny') : t('inventory.facetSelected', { count: selected.length })}</span></summary>
    <fieldset>
      <legend>{facet.label}</legend>
      <label><span>{t('inventory.searchValues', { field: facet.label })}</span><SearchInput value={valueQuery} maxLength={100} placeholder={t('inventory.searchValuesPlaceholder')}
        onChange={event => setValueQuery(event.target.value)} onKeyDown={event => { if (event.key === 'Enter') event.preventDefault(); }} /></label>
      <div className="inventory-facet-values">{values.map(value => <CheckboxField key={value.value} label={t('connections.findFacetOption', value)}
        checked={selected.includes(value.value)} onChange={event => onChange(event.target.checked
          ? [...selected, value.value].sort() : selected.filter(item => item !== value.value))} />)}</div>
      {values.length === 0 && <p className="connection-help">{t('inventory.noValues')}</p>}
      {facet.hasMore && <p className="connection-help">{t('connections.findFacetMore')}</p>}
    </fieldset>
  </details>;
}
