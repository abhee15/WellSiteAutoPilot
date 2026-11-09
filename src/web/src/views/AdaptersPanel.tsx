import { useEffect, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import type { AdapterOperationsResponse } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { PanelHeader } from '../components/common';
import { PanelLoading } from '../components/LoadingIndicator';
import { SelectField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { SearchInput } from '../components/SearchInput';
import { useSplitPaneFocus } from '../components/useSplitPaneFocus';
import { t } from '../localization';
import { AdapterCapabilityDetails } from './AdapterCapabilityDetails';
import { loadAdapterOperations } from './adapterOperations';
import { ConnectionsError } from './connections';
import './connections.css';
import './splitLayout.css';
import './adapterOperations.css';

/** Sort the reference picker without changing provider registration or execution order. */
function orderAdapters(adapters: AdapterOperationsResponse[]): AdapterOperationsResponse[] {
  return [...adapters].sort((left, right) =>
    Number(left.adapterKey === 'simulator') - Number(right.adapterKey === 'simulator') ||
    left.displayName.localeCompare(right.displayName, undefined, { sensitivity: 'base' }) ||
    left.adapterKey.localeCompare(right.adapterKey));
}

/** Static adapter reference. No saved Connection, Asset or provider data is loaded by this screen. */
export function AdaptersPanel() {
  const [profiles, setProfiles] = useState<AdapterOperationsResponse[]>([]);
  const [adapterKey, setAdapterKey] = useState('');
  const [query, setQuery] = useState('');
  const [selection, setSelection] = useState<string | null>(null);
  const [detailOpen, setDetailOpen] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const abort = new AbortController();
    setLoading(true); setError(null);
    void loadAdapterOperations(abort.signal).then(adapters => {
      if (abort.signal.aborted) return;
      const ordered = orderAdapters(adapters);
      setProfiles(ordered);
      setAdapterKey(current => current || ordered.find(item => item.adapterKey.toLowerCase() === 'foresite')?.adapterKey || ordered[0]?.adapterKey || '');
    }).catch((caught: unknown) => {
      if (!abort.signal.aborted) {
        setProfiles([]);
        setError(caught instanceof ConnectionsError && caught.kind === 'accessDenied' ? t('adapterReference.denied') : t('adapterReference.failed'));
      }
    }).finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [attempt]);

  const adapter = profiles.find(profile => profile.adapterKey === adapterKey);
  const capabilities = (adapter?.capabilities ?? []).filter(capability => {
    const variants = adapter?.operations?.filter(operation => capability.operationKeys?.includes(operation.key ?? '')) ?? [];
    return [capability.displayName, capability.description, ...variants.flatMap(operation => [operation.displayName, operation.quantity])]
      .some(text => text?.toLowerCase().includes(query.trim().toLowerCase()));
  });
  // Keep the user's explicit capability choice through search/refresh; show the first
  // available capability only while that choice is filtered out or not yet made.
  const selected = capabilities.find(capability => capability.key === selection) ?? capabilities[0];
  const selectedKey = selected?.key ?? null;
  // On narrow screens, start on the list even though the desktop inspector is prepopulated.
  // Only an explicit row click opens the mobile detail pane (and moves keyboard focus).
  const splitFocus = useSplitPaneFocus(detailOpen && !loading && !error ? selectedKey : null);
  const operations = adapter?.operations?.filter(operation => selected?.operationKeys?.includes(operation.key ?? '')) ?? [];

  return <div {...splitFocus} className="split-layout adapter-operations" data-has-selection={!!selected && detailOpen && !loading && !error}>
    <div className="panel operation-controls">
      <SelectField label={t('adapterReference.adapter')} value={adapterKey} disabled={loading || !!error || profiles.length === 0} onChange={event => {
        setAdapterKey(event.target.value); setSelection(null); setDetailOpen(false); setQuery('');
      }}>{adapterKey && !adapter && <option value={adapterKey}>{t('adapterReference.missingOption', { adapter: adapterKey })}</option>}{profiles.map(profile => <option key={profile.adapterKey} value={profile.adapterKey ?? ''}>{profile.displayName}</option>)}</SelectField>
      <ActionButton label={t('adapterReference.refresh')} icon={RefreshCw} iconOnly onClick={() => setAttempt(value => value + 1)} busy={loading} />
    </div>
    {!loading && !error && adapterKey && !adapter && <Notice>{t('adapterReference.missing')}</Notice>}
    {error && <Notice tone="error">{error}</Notice>}
    {loading ? <PanelLoading label={t('adapterReference.loading')} /> : !error && <div className="split-panes">
      <section className="panel split-browse" aria-label={t('adapterReference.capabilities')}>
        <PanelHeader title={t('adapterReference.capabilities')} count={capabilities.length} />
        <div className="operation-search"><SearchInput aria-label={t('adapterReference.search')} placeholder={t('adapterReference.search')} value={query}
          onChange={event => { setQuery(event.target.value); setDetailOpen(false); }} /></div>
        {capabilities.length === 0 && <p className="empty-state">{query.trim() ? t('adapterReference.noMatches') : t('adapterReference.empty')}</p>}
        <ul className="split-results">{capabilities.map(capability => <li key={capability.key} data-selected={capability.key === selectedKey}>
          <button type="button" className="connection-row" aria-current={capability.key === selectedKey}
            data-split-focus-key={capability.key} onClick={() => { setSelection(capability.key ?? null); setDetailOpen(true); }}>
            <strong>{capability.displayName}</strong><small>{t('adapterReference.version', { version: capability.contractVersion ?? '' })}</small>
          </button></li>)}</ul>
      </section>
      <section className="panel split-selected" tabIndex={-1} aria-label={t('adapterReference.selected')}>
        {selected ? <>
          <ActionButton className="split-back" label={t('adapterReference.back')} onClick={() => setDetailOpen(false)} />
          <PanelHeader title={selected.displayName ?? t('adapterReference.capability')} />
          <AdapterCapabilityDetails key={adapterKey + '/' + selected.key} capability={selected} operations={operations} />
        </> : <p className="empty-state">{t('adapterReference.select')}</p>}
      </section>
    </div>}
  </div>;
}
